using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>Runtime information about one part we put on the doll.</summary>
    internal class BoundPart
    {
        public Def def;                     // NiceHealthTab.DollBodyPart or DollOrgan
        public string slot;                 // Our part slot name (Outer* copies included)
        public string partDefName;          // Genitals / Chest / Anus / Gonads
        public string dollName;             // Male / Female / Fat / Hulk / Kid
        public int realIndex = -1;          // Real index in Human BodyDef.AllParts

        // The original values from the Def XML; restored when no form matches.
        public Texture2D defaultTex;
        public Vector2 defaultPosition;
        public float defaultWidth = 1f;
        public float defaultHeight = 1f;
        public Vector2 defaultHitbox = new Vector2(0.8f, 0.8f);

        /// <summary>The forms for this (doll x part), by descending priority.</summary>
        public List<DollPartFormDef> forms;

        /// <summary>The form chosen for the current pawn. The RJW panel reads its panel
        /// coordinates from here.</summary>
        public DollPartFormDef currentForm;

        /// <summary>
        /// The form whose position, scale and panel placement the part uses this frame. Usually
        /// <see cref="currentForm"/>; for kind testicle art drawn on the penis canvas it is the
        /// doll's penis form. Null when the part is hidden.
        /// </summary>
        public DollPartFormDef placementForm;

        /// <summary>Hide the whole part when no form matches, instead of restoring the default
        /// texture. Used by slots where some pawns simply do not have the part, such as the
        /// vulva.</summary>
    }

    /// <summary>
    /// Nice Health Tab's doll part Defs point at a part through an "integer index" into
    /// <c>BodyDef.AllParts</c>, not through a defName.
    /// Once at game start we look up the real indices and write them into our Defs; after that,
    /// every time the health tab is drawn we apply the current pawn's state (the doll gate and
    /// the size-based textures).
    ///
    /// Design rules
    ///  - The NiceHealthTab / RimJobWorld / balls assemblies are never hard-referenced (all
    ///    reflection). With any of them missing we quietly do nothing, and no TypeLoadException.
    ///  - Harmony is used for a single prefix on vanilla
    ///    <c>HealthCardUtility.DrawPawnHealthCard</c>. No transpilers, and it does not interrupt
    ///    the original flow (it returns void).
    ///  - On failure everything stays on the static indices written in the XML. No red errors.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Bootstrap
    {
        internal const string Prefix = "[NHT RJW Genitalia] ";
        internal const string HarmonyId = "zoc2k.nicehealthtab.rjwgenitalia";

        /// <summary>defName prefix of the doll Defs we ship. Shape:
        /// NHTRJW_&lt;part&gt;_&lt;doll&gt;</summary>
        private const string DefNamePrefix = "NHTRJW_";

        private const string BallsPackageId = "teheeitsme525.rjwgenderorgansmod";
        private const string GonadsPartDefName = "Gonads";

        /// <summary>The nipple slot. Same body part as the chest, but its own layer.</summary>
        internal const string NippleSlot = "Nipples";

        /// <summary>The chest slot. The slot name is also the BodyPartDef name.</summary>
        internal const string ChestSlot = "Chest";

        /// <summary>The anus slot. The slot name is also the BodyPartDef name.</summary>
        internal const string AnusSlot = "Anus";

        /// <summary>
        /// The surface copy of the anus, so it is **always** visible in the normal view - the
        /// organ (Anus) is only drawn there when injured, and only while NHT's "show organs" is
        /// on. It points at the same body part as Anus.
        /// </summary>
        internal const string OuterAnusSlot = "OuterAnus";

        /// <summary>The internal genitals slot. The slot name is also the BodyPartDef name.</summary>
        internal const string GenitalsSlot = "Genitals";

        /// <summary>The testicle slot proper, drawn in the organ view.</summary>
        internal const string GonadsSlot = "Gonads";

        /// <summary>
        /// The ovary slot. It points at the same part as <see cref="GonadsSlot"/>.
        ///
        /// While the ovaries shared a slot with the testicles, **a pawn with both lost one of
        /// them**, because form selection keeps only the highest-priority match. Flipping the
        /// priority only changed which one was lost, so the slots were split - the same move that
        /// splits penis and vagina across OuterGenitals / Genitals.
        /// </summary>
        internal const string OvariesSlot = "Ovaries";

        /// <summary>
        /// The womb slot. It points at the same part as <see cref="GenitalsSlot"/>, because RJW
        /// has no womb part and rjw_menstruation keeps the womb state (menstruation, cum,
        /// pregnancy) on a comp of the vagina hediff. It is a display layer over the vagina.
        /// </summary>
        internal const string WombSlot = "Womb";

        /// <summary>Fluid inside the womb, a layer over the womb base. Its part is Genitals.</summary>
        internal const string WombFluidSlot = "WombFluid";

        /// <summary>
        /// Name of the bucket that holds hediffs with no part. It uses a character that cannot
        /// appear in a part name, so it can never clash with a real one.
        /// </summary>
        internal const string WholeBodyKey = "*";

        /// <summary>
        /// Buckets a slot has to look at **besides its own part**.
        /// The womb state does not attach to Genitals: pregnancy goes on the Torso and inflation
        /// attaches without a part. Drawing the womb means looking at those.
        /// </summary>
        internal static readonly Dictionary<string, string[]> SlotExtraHediffParts =
            new Dictionary<string, string[]>
            {
                { WombSlot, new[] { "Torso", WholeBodyKey } },
                { WombFluidSlot, new[] { "Torso", WholeBodyKey } },
            };

        /// <summary>
        /// The outer genitals slot. Same part as the genitals, but drawn on the body layer.
        /// The penis and the vulva live here - both are forms visible outside the body.
        /// </summary>
        internal const string OuterGenitalsSlot = "OuterGenitals";

        /// <summary>
        /// The gonad slot visible on the body surface (testicles). It points at the same part as
        /// <see cref="GonadsSlot"/> and pairs with the organ-layer one. The ovaries are inside the
        /// body, so they do not appear here.
        /// </summary>
        internal const string OuterGonadsSlot = "OuterGonads";

        /// <summary>
        /// Our part slot -> RimWorld BodyPartDef name.
        /// The nipples are only a layer over the breasts; the body part they point at is the
        /// chest. Slots absent from this table use their own name as the BodyPartDef name.
        /// </summary>
        private static readonly Dictionary<string, string> SlotToBodyPart =
            new Dictionary<string, string>
            {
                { NippleSlot, "Chest" },
                { OuterGenitalsSlot, "Genitals" },
                { OuterAnusSlot, "Anus" },
                { WombSlot, "Genitals" },
                { WombFluidSlot, "Genitals" },
                // The gonads are not here - which part they use is decided at runtime by whether
                // that part exists. See BodyPartOfSlot.
            };

        /// <summary>
        /// The part name the gonad markers really point at.
        ///
        /// The Gonads part is created by 'RJW Now with balls!'. Without that mod the part does
        /// not exist, the index becomes -1 and the ovaries and testicles vanish from the doll.
        /// In that case we attach them to the genitals instead: the markers still show, and
        /// clicking opens the genitals entry. With no part of their own but a picture to show,
        /// that is the right behaviour.
        ///
        /// That makes their bodyPartId equal to the genitals one, but NHT does not let parts with
        /// the same index fight over clicks (the vulva and the womb already work this way).
        /// </summary>
        private static string gonadsBodyPart = GonadsPartDefName;

        private static string BodyPartOfSlot(string slot)
        {
            if (slot == GonadsSlot || slot == OuterGonadsSlot || slot == OvariesSlot)
            {
                return gonadsBodyPart;
            }
            string mapped;
            return SlotToBodyPart.TryGetValue(slot, out mapped) ? mapped : slot;
        }
        private const string GonadsGroupDefName = "GonadsBPG";
        private const string GonadsEnglishLabel = "gonads";

        // Labels come from our Languages/<language>/Keyed. No language is hard-coded in the
        // source, so adding a translation is a matter of dropping in a Keyed XML.
        private const string GonadsLabelKey = "NHTRJW_GonadsLabel";
        private const string GonadsGroupLabelKey = "NHTRJW_GonadsGroupLabel";

        internal static readonly List<BoundPart> BoundParts = new List<BoundPart>();
        internal static FieldInfo BodyPartIdField;
        internal static FieldInfo TexField;
        internal static FieldInfo PositionField;
        internal static FieldInfo WidthField;
        internal static FieldInfo HeightField;
        internal static FieldInfo HitboxField;
        internal static bool Ready;

        static Bootstrap()
        {
            try
            {
                Run();
            }
            catch (Exception ex)
            {
                Ready = false;
                // Even if this dies the game must keep working; it falls back to the static
                // indices in the XML.
                Log.Message(Prefix + "bootstrap skipped (" + ex.GetType().Name + ": " + ex.Message
                            + "). Falling back to the static body part indexes in XML.");
            }
        }

        private static void Run()
        {
            // --- 1. Check Nice Health Tab is there, and let it initialise first -----
            // NHT's Assets static constructor registers Def -> Doll and runs
            // BodyPartIndexesRemap.Initialize() (which calls Clear() inside). We must touch things
            // only after that, so rather than trusting the StaticConstructorOnStartup order we run
            // it explicitly first.
            Type assetsType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.Assets");
            if (assetsType == null)
            {
                Log.Message(Prefix + "Nice Health Tab not present - nothing to do.");
                return;
            }
            RuntimeHelpers.RunClassConstructor(assetsType.TypeHandle);

            Type partDefType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollBodyPartDef");
            BodyPartIdField = (partDefType == null)
                ? null
                : partDefType.GetField("bodyPartId", BindingFlags.Instance | BindingFlags.Public);
            TexField = Field(partDefType, "tex");
            PositionField = Field(partDefType, "position");
            WidthField = Field(partDefType, "width");
            HeightField = Field(partDefType, "height");
            HitboxField = Field(partDefType, "hitboxScale");
            if (BodyPartIdField == null || BodyPartIdField.FieldType != typeof(int))
            {
                Log.Message(Prefix + "unexpected Nice Health Tab layout - keeping the static indexes.");
                return;
            }

            BodyDef human = DefDatabase<BodyDef>.GetNamedSilentFail("Human");
            if (human == null)
            {
                return;
            }

            // --- 1-b. Does the gonad part exist? -----------------------------------
            // If not (no balls mod), attach the gonad markers to the genitals part.
            if (DefDatabase<BodyPartDef>.GetNamedSilentFail(GonadsPartDefName) == null)
            {
                gonadsBodyPart = GenitalsSlot;
            }

            // --- 2. Prepare the form Defs -------------------------------------------
            // (doll x part) -> list of forms, sorted by descending priority.
            Dictionary<string, List<DollPartFormDef>> formsByKey =
                new Dictionary<string, List<DollPartFormDef>>();
            int formCount = 0;
            foreach (DollPartFormDef fd in DefDatabase<DollPartFormDef>.AllDefsListForReading)
            {
                if (fd.dollName.NullOrEmpty() || fd.bodyPart.NullOrEmpty())
                {
                    continue;
                }
                // Without the balls mod there are no testicle or ovary hediffs, so we move the
                // fallback match rule into the real rule's place, once, here.
                fd.ApplyBallsFallback(ModDeps.Balls);
                fd.ResolveTextures();
                string key = fd.dollName + "/" + fd.bodyPart;
                List<DollPartFormDef> list;
                if (!formsByKey.TryGetValue(key, out list))
                {
                    list = new List<DollPartFormDef>();
                    formsByKey[key] = list;
                }
                list.Add(fd);
                formCount++;
            }
            foreach (List<DollPartFormDef> list in formsByKey.Values)
            {
                list.Sort((a, b) => b.priority.CompareTo(a.priority));
            }

            // --- 3. Fix our Defs' bodyPartId to the real Human index -----------------
            // Missing parts get -1; the NHT renderer skips a negative index quietly.
            Dictionary<string, int> canonical = new Dictionary<string, int>();
            BoundParts.Clear();
            int bound = 0;
            int unbound = 0;

            foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(partDefType))
            {
                string slot = OurPartName(def);
                if (slot == null)
                {
                    continue;
                }
                string part = BodyPartOfSlot(slot);

                int index;
                if (!canonical.TryGetValue(part, out index))
                {
                    index = IndexOfPart(human, part);
                    canonical[part] = index;
                }

                BodyPartIdField.SetValue(def, index);

                string doll = OurDollName(def);
                List<DollPartFormDef> forms;
                formsByKey.TryGetValue(doll + "/" + slot, out forms);

                // Layers whose colour comes from somewhere other than the health status are
                // drawn by us.
                if (slot == NippleSlot)
                {
                    TintedPartRenderer.Register(def, new TintedPartRenderer.Layer
                    {
                        Color = () => NippleAppearance.CurrentColor,
                        Visible = () => NippleAppearance.CurrentVisible,
                        OrganView = false,      // Nipples are drawn in the normal doll view
                    });
                }
                else if (slot == AnusSlot)
                {
                    // The anus is drawn inside a window; the window (background plus outline)
                    // is laid first.
                    TintedPartRenderer.Register(def, new TintedPartRenderer.Layer
                    {
                        AnusWindow = true,
                    });
                }
                else if (slot == OuterAnusSlot)
                {
                    // The anus in the normal view. Dropped in the armour view, which has no
                    // window.
                    TintedPartRenderer.Register(def, new TintedPartRenderer.Layer
                    {
                        AnusGlyph = true,
                    });
                }
                else if (slot == WombFluidSlot)
                {
                    TintedPartRenderer.Register(def, new TintedPartRenderer.Layer
                    {
                        Color = () => WombFluidAppearance.CurrentColor,
                        Visible = () => WombFluidAppearance.CurrentVisible,
                        OrganView = true,       // The womb appears in the organ view only
                    });
                }

                BoundParts.Add(new BoundPart
                {
                    def = def,
                    slot = slot,
                    partDefName = part,
                    dollName = doll,
                    realIndex = index,
                    defaultTex = (TexField == null) ? null : TexField.GetValue(def) as Texture2D,
                    defaultPosition = (PositionField == null)
                        ? Vector2.zero : (Vector2)PositionField.GetValue(def),
                    defaultWidth = (WidthField == null) ? 1f : (float)WidthField.GetValue(def),
                    defaultHitbox = (HitboxField == null)
                        ? new Vector2(0.8f, 0.8f) : (Vector2)HitboxField.GetValue(def),
                    defaultHeight = (HeightField == null) ? 1f : (float)HeightField.GetValue(def),
                    forms = forms,
                });

                if (index >= 0)
                {
                    bound++;
                }
                else
                {
                    unbound++;
                }
            }

            if (bound == 0 && unbound == 0)
            {
                // None of our Defs loaded, which means RimJobWorld is absent (MayRequire).
                return;
            }

            // --- 4. Add entries to the remaps of other BodyDefs ---------------------
            // ModCompat races such as Ratkin / Kurin / ABF Synstruct, and any override the user
            // built in NHT's settings editor, also get our part mappings.
            int remapped = PatchRemaps(canonical);

            // --- 5. Inject gonad labels from our Keyed translation, balls mod only ---
            LocalizeGonadsIfNeeded();

            // --- 6. Leave plain size hediffs out of the doll condition check ---------
            int filtered = SizeHediffFilter.Init();
            SizeHediffFilter.ApplySetting(NHTRJWSettings.Current.hideSizeOnlyHediffs);

            // --- 7. Install the hook that applies each pawn's state -----------------
            Ready = true;
            InstallHook();

            // --- 8. Render hook for the tinted layers (nipples, womb fluid) ---------
            // Wrapped on its own so the rest keeps working if it fails.
            try
            {
                TintedPartRenderer.Install(partDefType);
            }
            catch (Exception ex)
            {
                Log.Message(Prefix + "nipple layer not installed ("
                            + ex.GetType().Name + ": " + ex.Message + ").");
            }

            // --- 9. The Cumpilation compatibility guard -----------------------------
            // Stops someone else's code attaching a hediff to missing genitals. See the comments
            // in CumpilationCompat for details. Quietly false without that mod.
            bool cumGuard = false;
            try
            {
                cumGuard = CumpilationCompat.Install();
            }
            catch (Exception ex)
            {
                Log.Message(Prefix + "Cumpilation guard not installed ("
                            + ex.GetType().Name + ": " + ex.Message + ").");
            }

            // --- 9-b. Split harvesting into testicles / ovaries ---------------------
            // The balls mod's harvesting surgery cannot choose what to remove. See the comments
            // in GonadSurgery for details. Quietly false when that mod is absent or shaped
            // differently.
            bool surgery = false;
            try
            {
                surgery = GonadSurgery.Install();
            }
            catch (Exception ex)
            {
                Log.Message(Prefix + "gonad surgery split not installed ("
                            + ex.GetType().Name + ": " + ex.Message + ").");
            }

            // --- 10. The RJW part panel (a third strip, like the hand and foot one) --
            bool panel = false;
            try
            {
                panel = PanelRenderer.Install(partDefType);
            }
            catch (Exception ex)
            {
                Log.Message(Prefix + "RJW panel not installed ("
                            + ex.GetType().Name + ": " + ex.Message + ").");
            }

            // Log the assembly version so one log line tells you whether an old build is
            // deployed. (RimWorld loads mod assemblies from a byte array, so the file path and
            // timestamp are not available.)
            string version = typeof(Bootstrap).Assembly.GetName().Version.ToString(3);

            Log.Message(Prefix + "v" + version + " - bound " + bound + " doll part def(s)"
                        + (unbound > 0 ? (", " + unbound + " left unbound (part absent)") : "")
                        + (remapped > 0 ? (", " + remapped + " cross-body remap entrie(s) added") : "")
                        + (filtered > 0 ? (", " + filtered + " size-only hediff(s) filtered") : "")
                        + ", " + formCount + " part form(s)"
                        + (cumGuard ? ", Cumpilation guard active" : "")
                        + (surgery ? ", gonad surgery split" : "")
                        + (panel ? ", RJW panel active" : "")
                        + ".");
        }

        /// <summary>
        /// Puts a prefix on vanilla <c>HealthCardUtility.DrawPawnHealthCard</c>.
        /// Nice Health Tab prefixes the same method and returns false to draw its own UI, so ours
        /// has to run before it - hence Priority.First.
        /// Ours returns void, so it does not interfere with the original flow.
        /// </summary>
        private static void InstallHook()
        {
            MethodInfo target = AccessTools.Method(typeof(HealthCardUtility), "DrawPawnHealthCard");
            if (target == null)
            {
                Log.Message(Prefix + "HealthCardUtility.DrawPawnHealthCard not found - "
                            + "per-pawn features disabled.");
                return;
            }

            HarmonyMethod prefix = new HarmonyMethod(
                typeof(DollStateApplier).GetMethod("Prefix",
                                                   BindingFlags.Static | BindingFlags.Public))
            {
                priority = Priority.First,
            };
            new Harmony(HarmonyId).Patch(target, prefix);
        }

        /// <summary>
        /// For one of our Defs, the target BodyPartDef's defName; otherwise null.
        /// "NHTRJW_Genitals_Male" -> "Genitals"
        /// </summary>
        private static string OurPartName(Def def)
        {
            if (def == null)
            {
                return null;
            }
            string name = def.defName;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(DefNamePrefix, StringComparison.Ordinal))
            {
                return null;
            }
            string[] parts = name.Split('_');
            if (parts.Length < 3 || parts[1].Length == 0)
            {
                return null;
            }
            return parts[1];
        }

        /// <summary>"NHTRJW_Genitals_Male" -> "Male". Null when the shape does not match.</summary>
        private static string OurDollName(Def def)
        {
            string[] parts = def.defName.Split('_');
            return (parts.Length >= 3) ? parts[2] : null;
        }

        private static FieldInfo Field(Type t, string name)
        {
            return (t == null) ? null : t.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        }

        /// <summary>First index of that BodyPartDef in BodyDef.AllParts, or -1.</summary>
        private static int IndexOfPart(BodyDef body, string partDefName)
        {
            List<BodyPartRecord> all = body.AllParts;
            if (all == null)
            {
                return -1;
            }
            for (int i = 0; i < all.Count; i++)
            {
                BodyPartRecord rec = all[i];
                if (rec != null && rec.def != null && rec.def.defName == partDefName)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Walks the static dictionaries of NiceHealthTab.BodyPartIndexesRemap, finds the real
        /// index of our parts in each BodyDef and adds a "canonical index -> real index" mapping.
        /// A BodyDef without that part gets no mapping, so Get() returns -1 and nothing shows.
        /// </summary>
        private static int PatchRemaps(Dictionary<string, int> canonical)
        {
            Type remapType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.BodyPartIndexesRemap");
            if (remapType == null)
            {
                return 0;
            }

            MethodInfo setter = remapType.GetMethod(
                "Set",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(int), typeof(int), typeof(bool) },
                null);
            if (setter == null)
            {
                return 0;
            }

            string[] dictFields = { "BodyPartsRemaper", "BodyPartsOverride" };
            int added = 0;

            foreach (string fieldName in dictFields)
            {
                FieldInfo field = remapType.GetField(fieldName, BindingFlags.Static | BindingFlags.Public);
                if (field == null)
                {
                    continue;
                }

                IDictionary dict = field.GetValue(null) as IDictionary;
                if (dict == null)
                {
                    continue;
                }

                foreach (DictionaryEntry entry in dict)
                {
                    string bodyDefName = entry.Key as string;
                    // "*" is the identity mapping (BodyPartIndexesRemapDefaultHuman): nothing to
                    // change, and no way to change it.
                    if (entry.Value == null || string.IsNullOrEmpty(bodyDefName) || bodyDefName == "*")
                    {
                        continue;
                    }

                    BodyDef body = DefDatabase<BodyDef>.GetNamedSilentFail(bodyDefName);
                    if (body == null)
                    {
                        continue;
                    }

                    foreach (KeyValuePair<string, int> pair in canonical)
                    {
                        if (pair.Value < 0)
                        {
                            continue;   // A part absent from Human has no canonical index
                        }
                        int actual = IndexOfPart(body, pair.Key);
                        if (actual < 0)
                        {
                            continue;   // This race lacks the part -> no mapping
                        }
                        setter.Invoke(entry.Value, new object[] { pair.Value, actual, false });
                        added++;
                    }
                }
            }

            return added;
        }

        /// <summary>
        /// Fills the gonad labels from our Keyed translation, but only when the balls mod is
        /// really loaded. That mod ships English only, and DefInjected against a Def that does not
        /// exist raises translation warnings
        /// (LoadedLanguage.InjectIntoData_AfterImpliedDefs), so this is handled here,
        /// conditionally, rather than in XML.
        /// </summary>
        private static void LocalizeGonadsIfNeeded()
        {
            if (ModLister.GetActiveModWithIdentifier(BallsPackageId, true) == null)
            {
                return;
            }

            ApplyLabel(DefDatabase<BodyPartDef>.GetNamedSilentFail(GonadsPartDefName), GonadsLabelKey);
            ApplyLabel(DefDatabase<BodyPartGroupDef>.GetNamedSilentFail(GonadsGroupDefName), GonadsGroupLabelKey);
        }

        /// <summary>
        /// Overwrites only while the label is still the English original, to respect other
        /// translation mods. Does nothing when the active language lacks the key.
        /// </summary>
        private static void ApplyLabel(Def def, string key)
        {
            if (def == null || def.label == null)
            {
                return;
            }
            if (!string.Equals(def.label, GonadsEnglishLabel, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!key.CanTranslate())
            {
                return;
            }

            string translated = key.Translate().ToString();
            if (translated.NullOrEmpty() || translated == def.label)
            {
                return;
            }

            def.label = translated;

            // Def.LabelCap is cached in cachedLabelCap, so an existing cache must be invalidated.
            FieldInfo cache = typeof(Def).GetField("cachedLabelCap",
                                                   BindingFlags.Instance | BindingFlags.NonPublic);
            if (cache != null)
            {
                cache.SetValue(def, Activator.CreateInstance(cache.FieldType));
            }
        }
    }
}
