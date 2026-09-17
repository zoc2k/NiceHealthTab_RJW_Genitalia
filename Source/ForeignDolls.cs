using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Lends our parts to dolls other mods build at runtime.
    ///
    /// Our parts join a doll by name at startup: NHT's <c>DollBodyPart.PrepareBodyPart</c> looks up
    /// the Doll Def named in <c>dollName</c> and adds itself to it. A doll that is **not a Def** -
    /// one a framework builds while the game runs - therefore holds none of our parts, and the RJW
    /// art is missing for every pawn drawn with it.
    ///
    /// "Nice Health Tab - Anatomy Editor" is such a framework: for a race profile it builds its own
    /// <c>HumanlikeDoll</c> and swaps it into the render context (its hooks on
    /// <c>DollRenderContext.GetMainDoll</c> / <c>ResolveImplants</c> / <c>DollDrawer.DrawDoll</c>).
    /// The good news is what those profiles are made of: the body is a **copy of one of NHT's own
    /// dolls** - the same outline texture and the same part positions - with the race's extras
    /// (tail, ears, wings, hair) added on top. So the coordinate system is the same as ours, and
    /// lending our parts to that doll puts them exactly where they belong.
    ///
    /// What we do, on the first frame such a doll is seen:
    ///
    ///   1. work out **which** of NHT's dolls it was built from, by its outline texture;
    ///   2. add the parts bound to that doll to its part list, and sort by layer as NHT does;
    ///   3. remember the doll name as an alias of that doll, so the RJW panel and the crotch
    ///      button find our placements for it too.
    ///
    /// A doll built from an outline we do not know gets nothing - guessing a placement would put
    /// the genitals somewhere random.
    ///
    /// The framework may keep its own draw order for its parts (the Anatomy Editor does, in a
    /// dictionary keyed by part) and read it for every part in the list, so parts of ours it has
    /// never seen would throw there. We therefore extend that table as well, by reflection, and
    /// lend nothing when we cannot.
    ///
    /// Everything here is reflection: no other mod's assembly is referenced.
    /// </summary>
    internal static class ForeignDolls
    {
        private const string ContextTypeName = "NiceHealthTab.DollRenderContext";
        private const string DollTypeName = "NiceHealthTab.Doll";
        private const string HumanDollTypeName = "NiceHealthTab.HumanlikeDoll";
        private const string LayeredTypeName = "NiceHealthTab.DollExtraLayered";
        private const string PartDefTypeName = "NiceHealthTab.DollBodyPartDef";

        // The Anatomy Editor's own bookkeeping, so its sorting does not trip over our parts.
        private const string RegistryTypeName = "NHTAnatomyEditor.RuntimeRegistry";
        private const string DrawOrderFieldName = "drawOrder";

        private static FieldInfo mainDollField;
        private static FieldInfo remapField;
        private static FieldInfo boundingBoxField;
        private static FieldInfo layerField;
        private static Type layeredType;
        private static Type dollTypeCached;
        private static FieldInfo profileBodyField;
        private static PropertyInfo layerProperty;
        private static FieldInfo imagePathField;
        private static FieldInfo positionField;
        private static FieldInfo widthField;

        /// <summary>
        /// How far a runtime doll laid the parts it copied from ours. Our parts are moved by the
        /// same amount on that doll. See <see cref="MeasureOffset"/>.
        /// </summary>
        private static readonly Dictionary<string, Vector2> offsetOf =
            new Dictionary<string, Vector2>();
        private static Pawn lastPawn;
        private static object lastDoll;

        /// <summary>The layer each of our parts has in XML, before any shift.</summary>
        private static readonly Dictionary<Def, int> baseLayer = new Dictionary<Def, int>();

        /// <summary>How far our layers are currently shifted up. See <see cref="LiftAbove"/>.</summary>
        private static int layerShift;

        /// <summary>The anus placement worked out for a doll of another mod, by doll.</summary>
        private static readonly Dictionary<object, KeyValuePair<Vector2, float>> anusPlace =
            new Dictionary<object, KeyValuePair<Vector2, float>>();
        private static FieldInfo pawnField;
        private static FieldInfo partsField;
        private static FieldInfo outlinePathField;
        private static MethodInfo sortMethod;
        private static MethodInfo registryFor;
        private static FieldInfo drawOrderField;
        private static bool ready;

        /// <summary>Outline texture path -> the NHT doll we have parts for.</summary>
        private static readonly Dictionary<string, string> byOutline =
            new Dictionary<string, string>();

        /// <summary>Foreign doll defName -> the doll of ours it borrows from.</summary>
        private static readonly Dictionary<string, string> alias =
            new Dictionary<string, string>();

        /// <summary>Dolls already dealt with, lent to or not.</summary>
        private static readonly HashSet<object> seen = new HashSet<object>();

        /// <summary>
        /// Hooks the moment a render context has settled on its doll. Returns true when installed.
        /// </summary>
        internal static bool Install()
        {
            Type contextType = GenTypes.GetTypeInAnyAssembly(ContextTypeName);
            Type dollType = GenTypes.GetTypeInAnyAssembly(DollTypeName);
            Type humanDollType = GenTypes.GetTypeInAnyAssembly(HumanDollTypeName);
            if (contextType == null || dollType == null)
            {
                return false;
            }
            dollTypeCached = dollType;
            layeredType = GenTypes.GetTypeInAnyAssembly(LayeredTypeName);
            Type partDefType = GenTypes.GetTypeInAnyAssembly(PartDefTypeName);
            layerField = (partDefType == null)
                ? null
                : partDefType.GetField("layer", BindingFlags.Instance | BindingFlags.Public);
            boundingBoxField = dollType.GetField("BoundingBox",
                                                 BindingFlags.Instance | BindingFlags.Public);
            if (partDefType != null)
            {
                layerProperty = partDefType.GetProperty("Layer",
                                                        BindingFlags.Instance | BindingFlags.Public);
                imagePathField = partDefType.GetField("imagePath",
                                                      BindingFlags.Instance | BindingFlags.Public);
                positionField = partDefType.GetField("position",
                                                     BindingFlags.Instance | BindingFlags.Public);
                widthField = partDefType.GetField("width",
                                                  BindingFlags.Instance | BindingFlags.Public);
            }
            mainDollField = contextType.GetField("MainDoll", BindingFlags.Instance | BindingFlags.Public);
            remapField = contextType.GetField("Remap", BindingFlags.Instance | BindingFlags.Public);
            pawnField = contextType.GetField("Pawn", BindingFlags.Instance | BindingFlags.Public);
            partsField = dollType.GetField("Parts", BindingFlags.Instance | BindingFlags.Public);
            outlinePathField = dollType.GetField("outlinePath", BindingFlags.Instance | BindingFlags.Public);
            sortMethod = dollType.GetMethod("Sort", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo resolve = AccessTools.Method(contextType, "ResolveImplants");
            if (mainDollField == null || pawnField == null || partsField == null
                || outlinePathField == null || sortMethod == null || resolve == null)
            {
                return false;
            }

            // Which of NHT's dolls carries which outline. Only dolls we have parts for count.
            foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(dollType))
            {
                if (humanDollType != null && !humanDollType.IsInstanceOfType(def))
                {
                    continue;
                }
                string outline = outlinePathField.GetValue(def) as string;
                if (!outline.NullOrEmpty() && Bootstrap.HasOwnPartsFor(def.defName))
                {
                    byOutline[outline] = def.defName;
                }
            }
            if (byOutline.Count == 0)
            {
                return false;
            }

            // The layer each part started with, so a shift is always measured from the same base.
            if (layerField != null)
            {
                for (int i = 0; i < Bootstrap.BoundParts.Count; i++)
                {
                    Def def = Bootstrap.BoundParts[i].def;
                    if (!baseLayer.ContainsKey(def))
                    {
                        baseLayer[def] = (int)layerField.GetValue(def);
                    }
                }
            }

            Type registry = GenTypes.GetTypeInAnyAssembly(RegistryTypeName);
            registryFor = (registry == null)
                ? null
                : registry.GetMethod("For", BindingFlags.Static | BindingFlags.Public);
            if (registryFor != null && registryFor.ReturnType != null)
            {
                drawOrderField = registryFor.ReturnType.GetField(
                    DrawOrderFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                profileBodyField = registryFor.ReturnType.GetField(
                    "body", BindingFlags.Instance | BindingFlags.Public);
            }

            try
            {
                new Harmony(Bootstrap.HarmonyId).Patch(
                    resolve,
                    new HarmonyMethod(typeof(ForeignDolls).GetMethod(
                        "ResolvePrefix", BindingFlags.NonPublic | BindingFlags.Static)));
                ready = true;
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "could not hook the render context: " + ex);
            }
            return ready;
        }

        /// <summary>
        /// How far this doll laid the parts it copied from ours.
        ///
        /// Both dolls draw the same textures, so a part of theirs and a part of ours that share an
        /// <c>imagePath</c> (and the same mirroring) are the same piece of body; the difference of
        /// their positions is the offset. The median of those differences is taken, so an odd part
        /// the author moved on its own does not drag the rest along. Zero unless at least three
        /// parts agree.
        /// </summary>
        private static Vector2 MeasureOffset(IList parts, string source)
        {
            if (imagePathField == null || positionField == null || widthField == null
                || dollTypeCached == null)
            {
                return Vector2.zero;
            }
            // Where our doll has each piece.
            Dictionary<string, Vector2> mine = new Dictionary<string, Vector2>();
            foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(dollTypeCached))
            {
                if (def.defName != source)
                {
                    continue;
                }
                IList sourceParts = partsField.GetValue(def) as IList;
                if (sourceParts == null)
                {
                    break;
                }
                for (int i = 0; i < sourceParts.Count; i++)
                {
                    string key = KeyOf(sourceParts[i] as Def);
                    if (key != null && !mine.ContainsKey(key))
                    {
                        mine[key] = (Vector2)positionField.GetValue(sourceParts[i]);
                    }
                }
                break;
            }
            if (mine.Count == 0)
            {
                return Vector2.zero;
            }

            List<float> dx = new List<float>();
            List<float> dy = new List<float>();
            for (int i = 0; i < parts.Count; i++)
            {
                Def part = parts[i] as Def;
                string key = KeyOf(part);
                Vector2 at;
                if (key == null || baseLayer.ContainsKey(part) || !mine.TryGetValue(key, out at))
                {
                    continue;
                }
                Vector2 theirs = (Vector2)positionField.GetValue(part);
                dx.Add(theirs.x - at.x);
                dy.Add(theirs.y - at.y);
            }
            if (dx.Count < 3)
            {
                return Vector2.zero;
            }
            Vector2 offset = new Vector2(Median(dx), Median(dy));
            if (Mathf.Abs(offset.x) < 0.01f && Mathf.Abs(offset.y) < 0.01f)
            {
                return Vector2.zero;
            }
            Log.Message(Bootstrap.Prefix + "that doll lays the shared parts at "
                        + offset.ToString("0.##") + " from ours (" + dx.Count
                        + " parts compared); our parts follow.");
            return offset;
        }

        /// <summary>
        /// Puts a doll's parts in layer order **without disturbing the order of parts that share a
        /// layer** (an insertion sort; the lists are short).
        ///
        /// NHT's own <c>Doll.Sort</c> is a List.Sort, which is not stable: re-sorting a doll would
        /// shuffle the parts of every shared layer. On a doll another mod built that order is its
        /// own business - the Anatomy Editor, for instance, has twelve hairstyles and both ears on
        /// the same layers, and a shuffle there can leave one of them behind the head.
        /// </summary>
        private static void SortByLayer(IList parts)
        {
            if (parts == null)
            {
                return;
            }
            if (layerProperty == null)
            {
                return;             // Without the layer we cannot sort at all
            }
            for (int i = 1; i < parts.Count; i++)
            {
                object item = parts[i];
                int layer = LayerOf(item);
                int j = i - 1;
                while (j >= 0 && LayerOf(parts[j]) > layer)
                {
                    parts[j + 1] = parts[j];
                    j--;
                }
                parts[j + 1] = item;
            }
        }

        private static int LayerOf(object part)
        {
            try
            {
                return (int)layerProperty.GetValue(part, null);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Writes down what a doll of another mod ended up with, for a bug report. Dev mode only.
        /// </summary>
        private static void Describe(Def doll, IList parts)
        {
            try
            {
                Rect bb = (boundingBoxField == null)
                    ? default(Rect)
                    : (Rect)boundingBoxField.GetValue(doll);
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append(Bootstrap.Prefix).Append("doll ").Append(doll.defName)
                  .Append(": box ").Append(bb.ToString())
                  .Append(", our parts shifted by ").Append(offsetOf[doll.defName].ToString("0.##"))
                  .Append(", layers lifted by ").Append(layerShift)
                  .Append(", parts now ").Append(parts.Count);
                List<BoundPart> ours = Bootstrap.BoundParts;
                for (int i = 0; i < ours.Count; i++)
                {
                    BoundPart p = ours[i];
                    if (p.dollName != SourceName(doll.defName) || positionField == null)
                    {
                        continue;
                    }
                    sb.AppendLine().Append("    ").Append(p.slot).Append(": id ")
                      .Append((int)Bootstrap.BodyPartIdField.GetValue(p.def)).Append(", at ")
                      .Append(((Vector2)positionField.GetValue(p.def)).ToString("0.#"))
                      .Append(", scale ")
                      .Append(((float)widthField.GetValue(p.def)).ToString("0.###"))
                      .Append(", layer ").Append(LayerOf(p.def));
                }
                Log.Message(sb.ToString());
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "could not describe the doll: " + ex);
            }
        }

        /// <summary>A part's picture and which way round it is drawn - what makes two parts the
        /// same piece of body across dolls.</summary>
        private static string KeyOf(Def part)
        {
            if (part == null)
            {
                return null;
            }
            string image = imagePathField.GetValue(part) as string;
            if (image.NullOrEmpty())
            {
                return null;
            }
            return ((float)widthField.GetValue(part) < 0f ? "-" : "+") + image;
        }

        private static float Median(List<float> values)
        {
            values.Sort();
            int n = values.Count;
            return (n % 2 == 1) ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) * 0.5f;
        }

        /// <summary>
        /// How far our parts move on the doll this pawn is drawn with. Zero on our own dolls and on
        /// a doll that lays the shared parts where we do.
        /// </summary>
        internal static Vector2 OffsetFor(Pawn pawn)
        {
            try
            {
                if (!ready || offsetOf.Count == 0)
                {
                    return Vector2.zero;
                }
                Def doll = DollOf(pawn) as Def;
                Vector2 offset;
                return (doll != null && offsetOf.TryGetValue(doll.defName, out offset))
                    ? offset
                    : Vector2.zero;
            }
            catch (Exception ex)
            {
                Disable("reading the body offset failed", ex);
                return Vector2.zero;
            }
        }

        /// <summary>Switches this whole bridge off after a failure; the mod keeps working on our
        /// own dolls.</summary>
        private static void Disable(string what, Exception ex)
        {
            ready = false;
            Log.Warning(Bootstrap.Prefix + what + "; lending parts to other mods' dolls is off "
                        + "for this session: " + ex);
        }

        /// <summary>
        /// The highest layer among the doll's **surface** parts (not organs, bones or eyes, and not
        /// ours). Those are the ones that can cover the chest and the belly.
        /// </summary>
        private static int TopSurfaceLayer(IList parts)
        {
            int top = 0;
            if (layerField == null)
            {
                return top;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                Def part = parts[i] as Def;
                if (part == null || baseLayer.ContainsKey(part)
                    || (layeredType != null && layeredType.IsInstanceOfType(part)))
                {
                    continue;
                }
                int layer = (int)layerField.GetValue(part);
                if (layer > top)
                {
                    top = layer;
                }
            }
            return top;
        }

        /// <summary>
        /// Lifts our layers until the lowest of our surface parts (the belly) is above
        /// <paramref name="top"/>, and re-sorts every doll that holds them.
        ///
        /// Layers live on the Def, so one doll cannot have its own - a lift therefore applies
        /// everywhere. That is harmless: it keeps our parts in the same order among themselves and
        /// above the surface parts of every doll we have met, which is what the chest and the belly
        /// need (they must never end up under an arm).
        /// </summary>
        private static void LiftAbove(int top)
        {
            if (layerField == null || baseLayer.Count == 0)
            {
                return;
            }
            int lowest = int.MaxValue;
            List<BoundPart> ours = Bootstrap.BoundParts;
            for (int i = 0; i < ours.Count; i++)
            {
                Def def = ours[i].def;
                int b;
                if ((layeredType != null && layeredType.IsInstanceOfType(def))
                    || !baseLayer.TryGetValue(def, out b))
                {
                    continue;       // Organs are not what an arm covers
                }
                if (b < lowest)
                {
                    lowest = b;
                }
            }
            if (lowest == int.MaxValue || lowest + layerShift > top)
            {
                return;             // Already above it
            }
            layerShift = top + 1 - lowest;
            for (int i = 0; i < ours.Count; i++)
            {
                Def def = ours[i].def;
                int b;
                if (baseLayer.TryGetValue(def, out b))
                {
                    layerField.SetValue(def, b + layerShift);
                }
            }
            ResortAll();
            Log.Message(Bootstrap.Prefix + "our doll layers lifted by " + layerShift
                        + " so the chest and the belly stay above every doll's arms.");
        }

        /// <summary>Sorts every doll that holds our parts again, after a layer lift.</summary>
        private static void ResortAll()
        {
            HashSet<string> names = new HashSet<string>();
            List<BoundPart> ours = Bootstrap.BoundParts;
            for (int i = 0; i < ours.Count; i++)
            {
                names.Add(ours[i].dollName);
            }
            if (dollTypeCached != null)
            {
                foreach (Def def in GenDefDatabase.GetAllDefsInDatabaseForDef(dollTypeCached))
                {
                    if (names.Contains(def.defName))
                    {
                        SortByLayer(partsField.GetValue(def) as IList);
                    }
                }
            }
            foreach (object doll in seen)
            {
                Def def = doll as Def;
                if (def != null && alias.ContainsKey(def.defName))
                {
                    SortByLayer(partsField.GetValue(doll) as IList);
                }
            }
        }

        /// <summary>
        /// The anus placement for a pawn drawn with another mod's doll, worked out from that doll's
        /// bounding box (<see cref="PanelRenderer.AnusPlacement"/>). False for our own dolls, where
        /// the value in the Def is already right.
        /// </summary>
        internal static bool TryAnusPlacement(Pawn pawn, DollPartFormDef form,
                                              out Vector2 position, out float scale)
        {
            position = Vector2.zero;
            scale = 0f;
            if (!ready || form == null || boundingBoxField == null)
            {
                return false;
            }
            try
            {
                return AnusPlacement(pawn, form, out position, out scale);
            }
            catch (Exception ex)
            {
                Disable("working out the anus placement failed", ex);
                position = Vector2.zero;
                scale = 0f;
                return false;
            }
        }

        private static bool AnusPlacement(Pawn pawn, DollPartFormDef form,
                                          out Vector2 position, out float scale)
        {
            position = Vector2.zero;
            scale = 0f;
            object doll = DollOf(pawn);
            Def def = doll as Def;
            if (def == null || !alias.ContainsKey(def.defName))
            {
                return false;
            }
            KeyValuePair<Vector2, float> found;
            if (anusPlace.TryGetValue(doll, out found))
            {
                position = found.Key;
                scale = found.Value;
                return scale > 0f;
            }
            DollPartFormDef source = PanelSource(form);
            Rect bb = (Rect)boundingBoxField.GetValue(doll);
            bool ok = source != null
                      && PanelRenderer.AnusPlacement(bb, source.panelPosition, source.panelScale,
                                                     out position, out scale);
            anusPlace[doll] = new KeyValuePair<Vector2, float>(position, scale);
            return ok;
        }

        /// <summary>
        /// The form that carries the panel placement of the anus window for this doll.
        ///
        /// The window itself belongs to the anus **organ** slot, so the surface copy's own Def has
        /// no panel placement (gen_defs gives a panel box to the organ slot only). Both are drawn
        /// in the same window, so the organ slot's form answers for both.
        /// </summary>
        private static DollPartFormDef PanelSource(DollPartFormDef form)
        {
            if (form.panelScale > 0f)
            {
                return form;
            }
            List<BoundPart> ours = Bootstrap.BoundParts;
            for (int i = 0; i < ours.Count; i++)
            {
                BoundPart p = ours[i];
                if (p.slot != Bootstrap.AnusSlot || p.dollName != form.dollName || p.forms == null)
                {
                    continue;
                }
                for (int k = 0; k < p.forms.Count; k++)
                {
                    if (p.forms[k].panelScale > 0f)
                    {
                        return p.forms[k];
                    }
                }
            }
            return null;
        }

        /// <summary>The doll this pawn is drawn with, as far as we have seen.</summary>
        /// <summary>
        /// The doll this pawn is drawn with, as noted when its context last settled. **Only** the
        /// noted one: asking the other mod during a draw would run a lot of its code (it builds
        /// profiles and loads textures on demand) in the middle of ours.
        /// </summary>
        private static object DollOf(Pawn pawn)
        {
            return (pawn != null && ReferenceEquals(pawn, lastPawn)) ? lastDoll : null;
        }

        /// <summary>The doll of ours this doll name borrows from, or the name itself.</summary>
        internal static string SourceName(string dollName)
        {
            string source;
            return (dollName != null && alias.TryGetValue(dollName, out source)) ? source : dollName;
        }

        /// <summary>
        /// Runs before NHT resolves the implants of a context, by which point every mod that wants
        /// its own doll has put it in place.
        /// </summary>
        private static bool ResolvePrefix(object __instance)
        {
            if (ready)
            {
                try
                {
                    object doll = mainDollField.GetValue(__instance);
                    Pawn pawn = pawnField.GetValue(__instance) as Pawn;
                    lastPawn = pawn;
                    lastDoll = doll;
                    // The body part table follows whichever pawn is about to be drawn, not only
                    // the one whose health card we prepared, and the remap comes from the context
                    // rather than from a lookup of our own (DollIndexRemap).
                    DollIndexRemap.Note(pawn);
                    if (remapField != null)
                    {
                        DollIndexRemap.NoteRemap(remapField.GetValue(__instance));
                    }
                    Ensure(doll, pawn);
                }
                catch (Exception ex)
                {
                    ready = false;
                    Log.Warning(Bootstrap.Prefix + "lending parts to another mod's doll failed, "
                                + "disabled: " + ex);
                }
            }
            return true;
        }

        private static void Ensure(object doll, Pawn pawn)
        {
            Def def = doll as Def;
            if (def == null || seen.Contains(doll) || Bootstrap.HasOwnPartsFor(def.defName))
            {
                return;         // Not a doll, dealt with already, or one of ours
            }
            if (!NHTRJWSettings.Current.lendPartsToOtherDolls)
            {
                return;         // Switched off: that doll is left exactly as its mod draws it
            }
            seen.Add(doll);

            string outline = outlinePathField.GetValue(doll) as string;
            string source;
            if (outline.NullOrEmpty() || !byOutline.TryGetValue(outline, out source))
            {
                Log.Message(Bootstrap.Prefix + "doll '" + def.defName + "' is not built on one of "
                            + "Nice Health Tab's own dolls (outline '" + (outline ?? "") + "'), so "
                            + "the RJW parts are left off it.");
                return;
            }

            IList parts = partsField.GetValue(doll) as IList;
            if (parts == null)
            {
                return;
            }
            // A framework that keeps its own draw order reads it for **every** part in the list
            // and throws on one it has not seen, so we either extend that table or lend nothing at
            // all. If the framework is here, not being able to reach its table is reason enough to
            // stand back.
            IDictionary order = null;
            if (registryFor != null)
            {
                object profile = (pawn == null)
                    ? null
                    : registryFor.Invoke(null, new object[] { pawn });
                order = (profile == null || drawOrderField == null)
                    ? null
                    : drawOrderField.GetValue(profile) as IDictionary;
                if (order == null)
                {
                    Log.Message(Bootstrap.Prefix + "doll '" + def.defName + "' belongs to a "
                                + "framework whose draw order we cannot extend, so the RJW parts "
                                + "are left off it.");
                    return;
                }
            }

            int next = 0;
            if (order != null)
            {
                foreach (DictionaryEntry e in order)
                {
                    int v = (int)e.Value;
                    if (v >= next)
                    {
                        next = v + 1;
                    }
                }
            }

            // Our surface parts have to sit above this doll's arms. A profile may lay its copies
            // of NHT's parts on layers of its own (the Miho profile puts the arms on 23, where NHT
            // has 3), so the layer is worked out from the doll in front of us rather than assumed.
            LiftAbove(TopSurfaceLayer(parts));

            // A profile may lay the parts it copied somewhere else than we do - the Milira one
            // puts the whole body 44.8 units lower than Nice Health Tab's female doll. Our parts
            // have to move with it, so the offset is measured from the parts both dolls share.
            offsetOf[def.defName] = MeasureOffset(parts, source);

            int added = 0;
            List<BoundPart> ours = Bootstrap.BoundParts;
            for (int i = 0; i < ours.Count; i++)
            {
                BoundPart p = ours[i];
                if (p.dollName != source || parts.Contains(p.def))
                {
                    continue;
                }
                parts.Add(p.def);
                if (order != null)
                {
                    order[p.def] = next++;
                }
                added++;
            }
            if (added > 0)
            {
                SortByLayer(parts);
            }
            alias[def.defName] = source;
            Log.Message(Bootstrap.Prefix + "lent " + added + " part(s) of the " + source
                        + " doll to '" + def.defName + "'.");
            if (Prefs.DevMode)
            {
                Describe(def, parts);
            }
        }

    }
}
