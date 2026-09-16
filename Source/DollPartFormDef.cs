using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// "When this doll (body type) has this form on this part, draw it at this position, size
    /// and texture."
    ///
    /// A single BodyPartDef (Genitals / Chest / Anus / Gonads) takes different anatomical forms
    /// from pawn to pawn: the Genitals part can be a penis or a vagina, and the Gonads part can
    /// be testicles (outside the body) or ovaries (inside the pelvis). Each form needs its own
    /// art and its own place on the doll, which is what this Def holds.
    ///
    /// The match looks first at **the hediffs a pawn actually has**, not at its gender, so
    /// transgender and futa pawns are shown correctly. (Only the chest goes by gender, because
    /// RJW gives every pawn the same Breasts hediff.)
    /// </summary>
    public class DollPartFormDef : Def
    {
        // --- Where it applies ----------------------------------------------------
        /// <summary>Target doll: a Nice Health Tab Doll defName (Male / Female / Fat / Hulk / Kid).</summary>
        public string dollName;

        /// <summary>Target BodyPartDef defName (Genitals / Chest / Anus / Gonads).</summary>
        public string bodyPart;

        /// <summary>Form name, also used in texture file names (Penis / Vagina / Testicles /
        /// Ovaries / Breasts / Chest / Anus).</summary>
        public string form;

        /// <summary>Priority when several forms match one part. Higher goes first.</summary>
        public int priority;

        // --- What it matches on (all optional; only those given are ANDed) -------
        /// <summary>An RJW GenitalFamily name (Penis / Vagina / Breasts / Anus).</summary>
        public string genitalFamily;

        /// <summary>Matches when the hediff defName contains this string. Used to tell testicles
        /// from ovaries.</summary>
        public string hediffNameContains;

        /// <summary>
        /// A form whose tier is read from somewhere other than a severity.
        /// <c>"Fluid"</c> / <c>"Implanted"</c> / <c>"Belly"</c>.
        ///
        /// When this is set, the hediff rules (<see cref="hediffNameContains"/> and friends) are
        /// not consulted; <see cref="FormStateReader"/> is asked instead, and the tier comes from
        /// the value it reads rather than from a hediff severity.
        ///
        /// Menstruation and fluid live in comp fields of rjw_menstruation, and the implantation
        /// count is <c>babies</c> on the RJW pregnancy hediff - none of the three can be read as
        /// a severity. Without the relevant mod the form never matches and is not drawn.
        /// </summary>
        public string stateSource;

        /// <summary>Only when the hediff defName is exactly one of these.</summary>
        public List<string> hediffs;

        /// <summary>
        /// The hediffs that swell the belly, for <c>stateSource</c> <c>"Belly"</c>: pregnancies,
        /// eggs and cum inflation. The belly value is the sum over the pawn's hediffs, the way
        /// Sized Apparel sums its Belly hediffs (see <see cref="FormStateReader.TryBelly"/>).
        /// </summary>
        public List<BellyHediff> bellyHediffs;

        private Dictionary<string, BellyHediff> bellyByName;

        /// <summary>The <see cref="bellyHediffs"/> entry for a hediff, or null.</summary>
        internal BellyHediff BellyEntryFor(Hediff hediff)
        {
            if (hediff == null || hediff.def == null || bellyHediffs == null)
            {
                return null;
            }
            if (bellyByName == null)
            {
                Dictionary<string, BellyHediff> map = new Dictionary<string, BellyHediff>();
                for (int i = 0; i < bellyHediffs.Count; i++)
                {
                    BellyHediff e = bellyHediffs[i];
                    if (e != null && !e.hediff.NullOrEmpty())
                    {
                        map[e.hediff] = e;
                    }
                }
                bellyByName = map;
            }
            BellyHediff found;
            return bellyByName.TryGetValue(hediff.def.defName, out found) ? found : null;
        }

        /// <summary>
        /// Skip this form while a fetus is showing on the pawn.
        ///
        /// The womb fluid and inflation layers use it. RJW Menstruation's own womb window does
        /// the same: once a pregnancy hediff is there it draws the cum layer only while
        /// gestation progress is below 0.2 (the implantation stage) and leaves the womb clean
        /// from there on (<c>Dialog_WombStatus</c>). The amount is still tracked by that mod; it
        /// is simply not drawn over a womb that shows a fetus.
        /// </summary>
        public bool hideWhileFetus;

        /// <summary>Match by the pawn's gender (Male / Female). Used for the chest.</summary>
        public string gender;

        /// <summary>Read the size only from RJW sex part hediffs (derived from
        /// rjw.HediffDef_SexPart). Keeps forms with no match rule (chest, anus) from mistaking a
        /// wound or a scar for a size tier.</summary>
        public bool sexPartOnly;

        /// <summary>A Sized Apparel (SAR) part variation name, e.g. InvertedNipple. Compared
        /// against <c>SizedApparel.SizedApparelBodyPartDetail.variation</c>, which SAR attaches to
        /// the hediff. Without SAR this condition never matches, so the default form is used.</summary>
        public string variation;

        /// <summary>Use this form even when no hediff matches (the final fallback).</summary>
        public bool fallback;

        // --- Without 'RJW Now with balls!' ---------------------------------------
        // Without that mod there are no testicle or ovary hediffs at all, so the match rule
        // above (hediffNameContains) can never hit and the gonads vanish from the doll. Only
        // then do we match on the genital family instead: we cannot know what is inside the
        // body, so we draw the default anatomy - "a penis means testicles too".
        //
        // The swap happens once at startup in <see cref="ApplyBallsFallback"/>; from there the
        // pipeline runs exactly as usual.

        /// <summary>The genital family to match instead without the balls mod (Penis / Vagina).
        /// Empty means no fallback.</summary>
        public string noBallsGenitalFamily;

        /// <summary>The size tier to pin in that case. Negative keeps the usual severity rule.</summary>
        public int noBallsFixedTier = -1;

        /// <summary>Pins the size tier to this value instead of reading a severity. Negative is
        /// unused.</summary>
        [Unsaved(false)]
        public int fixedTier = -1;

        /// <summary>
        /// Without the balls mod, moves the fallback rule into the real match rule's place.
        /// Does nothing when the mod is present. Called once at startup.
        /// </summary>
        public void ApplyBallsFallback(bool ballsPresent)
        {
            if (ballsPresent || noBallsGenitalFamily.NullOrEmpty())
            {
                return;
            }
            hediffNameContains = null;      // Stop looking for a hediff that cannot exist
            genitalFamily = noBallsGenitalFamily;
            fixedTier = noBallsFixedTier;
        }

        // --- How it is drawn -----------------------------------------------------
        /// <summary>Doll coordinates, measured to the texture's centre.</summary>
        public Vector2 position;

        /// <summary>Texture scale. Negative mirrors it horizontally (a Nice Health Tab
        /// convention).</summary>
        public float scale = 1f;

        /// <summary>
        /// Click hit area. Nice Health Tab computes the hitbox from the **whole texture rect**:
        ///     hit width = canvas width x scale x (2 * hitboxScale.x - 1)
        /// Every form uses the same 256x256 canvas, so forms with a small glyph must lower this
        /// value or the hit area would swallow the empty margin. The value is computed from the
        /// glyph bbox automatically
        /// (docs/tools/gen_defs.py).
        /// </summary>
        public Vector2 hitboxScale = new Vector2(0.8f, 0.8f);

        /// <summary>
        /// Position inside the RJW panel (our strip, opening where the hand and foot strip does).
        /// The panel coordinate system is based on <see cref="PanelRenderer"/>'s BoundingBox and
        /// has nothing to do with doll coordinates. gen_defs.py measures the glyphs and fills the
        /// value in.
        /// </summary>
        public Vector2 panelPosition;

        /// <summary>Scale inside the panel. 0 means this form is not drawn in the panel.</summary>
        public float panelScale;

        /// <summary>
        /// The area every tier's glyph takes up on the doll (doll coordinates, top left / bottom
        /// right). Texture pixels cannot be read in game, so gen_defs.py measures it and fills it
        /// in. The RJW panel button uses this area to frame its crotch close-up.
        /// Both being equal (both zero) means nothing was measured.
        /// </summary>
        public Vector2 glyphMin;

        /// <summary>The counterpart of <see cref="glyphMin"/>.</summary>
        public Vector2 glyphMax;

        /// <summary>Whether a measured glyph area exists.</summary>
        public bool HasGlyphBounds
        {
            get { return glyphMax.x > glyphMin.x && glyphMax.y > glyphMin.y; }
        }

        /// <summary>Texture path prefix. The actual files are "<c>prefix</c>_<c>tier</c>".</summary>
        public string texturePrefix;

        /// <summary>Number of size tiers. Textures must run _0 to _(sizeSteps-1).</summary>
        public int sizeSteps = 1;

        /// <summary>
        /// Suffix of the multiplet pregnancy art, e.g. <c>_Multiplet</c> gives
        /// "<c>prefix</c>_<c>tier</c>_Multiplet". Empty means the form has no multiplet art. With
        /// two or more babies we use that picture for the same tier, and fall back to the single
        /// picture when that tier has none. This is the same rule as RJW Menstruation's twin art
        /// (<c>MenstruationUtility.GetPregnancyIcon</c> -> <c>TryGetTwinsIcon</c>: with a baby
        /// count above 1 it looks for <c>_Multiplet_</c> art and falls back to the single one).
        /// </summary>
        public string multipletSuffix;

        /// <summary>
        /// Lower severity bound of each tier, ascending; the count must equal sizeSteps.
        /// The pawn's tier is the largest i whose thresholds[i] the severity reaches.
        /// </summary>
        public List<float> sizeThresholds = new List<float>();

        // --- Kinds (penis types) -------------------------------------------------
        /// <summary>
        /// Art sets for other kinds of this form, such as <c>HorsePenis</c> or the <c>Uncut</c>
        /// variation of <c>Penis</c>. gen_defs.py fills this in with **only the tiers that have
        /// art**: an empty PNG cannot be told apart in game (textures are unreadable), and a blank
        /// tier must fall back to the default art instead of drawing nothing.
        ///
        /// Files live next to the default set. With <c>dir</c> the folder of
        /// <see cref="texturePrefix"/> and <c>own</c> its file stem (e.g. <c>Penis</c>):
        ///     own name     <c>dir/own_{tier}_{variation}</c>
        ///     other kinds  <c>dir/{name}/{name}_{tier}[_{variation}]</c>
        ///
        /// The lookup follows Sized Apparel
        /// (<c>SizedApparelUtility.CheckBodyPartGraphicExists</c>): the kind with its variation,
        /// the kind without it, the default name with the variation, then the default art - all at
        /// the same tier.
        /// </summary>
        public List<TextureVariant> textureVariants;

        /// <summary>
        /// Where the kind comes from. Empty: the hediff that matched this form (the penis
        /// itself). <c>Penis</c>: the pawn's penis hediff. Sized Apparel picks balls art the same
        /// way - by the penis hediff's name and variation (<c>Penis/Balls/{penis}_{tier}</c>).
        /// </summary>
        public string variantFrom;

        /// <summary>
        /// Kinds that have **none of this part**: while the pawn's kind (see
        /// <see cref="variantFrom"/>) is one of these, the part is not drawn. gen_defs.py lists
        /// every kind whose folder exists but holds only empty pictures - for testicles that is
        /// how the artist marks a penis kind without balls.
        /// </summary>
        public List<string> hiddenKinds;

        /// <summary>
        /// With <see cref="hiddenKinds"/>: keep the part in the organ view and hide it only in the
        /// RJW panel. The organ-layer testicles use it, so injuries and surgery on the gonads stay
        /// reachable from the doll; the surface testicles are the ones that disappear.
        /// </summary>
        public bool hiddenKindsKeepOrgan;

        /// <summary>
        /// The kind art of this form is drawn **on the kind's penis canvas**, tier for tier with
        /// the penis (testicles: the artist draws the balls over that kind's penis template). Such
        /// art is placed with the doll's penis form (position, scale, panel placement) and picked
        /// by the penis size tier - the way Sized Apparel draws balls with the penis severity
        /// (<c>SizedApparelComp</c> hands the balls addon the penis hediff's severity).
        /// </summary>
        public bool variantOnPenisCanvas;

        /// <summary>Resolved kind art: key -> one texture per tier, null where the tier has none.</summary>
        [Unsaved(false)]
        public Dictionary<string, Texture2D[]> variantTiers;

        /// <summary>Per-kind geometry (hitbox, glyph area, panel placement): kind -> its entry.</summary>
        [Unsaved(false)]
        private Dictionary<string, TextureVariant> kindInfo;

        /// <summary>The geometry entry of a kind, or null when the kind has no art of its own.</summary>
        public TextureVariant KindInfo(string kind)
        {
            TextureVariant info;
            return (kind != null && kindInfo != null && kindInfo.TryGetValue(kind, out info)) ? info : null;
        }

        /// <summary>Whether this form has any kind art at all.</summary>
        public bool HasKinds
        {
            get { return textureVariants != null && textureVariants.Count > 0; }
        }

        /// <summary>
        /// Click hit area for a kind. A long kind (a horse penis, say) is measured on its own so it
        /// does not widen the hit area of every other pawn's penis.
        /// </summary>
        public Vector2 HitboxFor(string kind)
        {
            TextureVariant info = KindInfo(kind);
            return (info != null && info.hitboxScale != Vector2.zero) ? info.hitboxScale : hitboxScale;
        }

        /// <summary>The glyph area on the doll for a kind (the crotch button frames it).</summary>
        public bool GlyphBoundsFor(string kind, out Vector2 min, out Vector2 max)
        {
            TextureVariant info = KindInfo(kind);
            if (info != null && info.glyphMax.x > info.glyphMin.x && info.glyphMax.y > info.glyphMin.y)
            {
                min = info.glyphMin;
                max = info.glyphMax;
                return true;
            }
            min = glyphMin;
            max = glyphMax;
            return HasGlyphBounds;
        }

        /// <summary>
        /// Whether this pawn's kind is one of <see cref="hiddenKinds"/>. Only kinds read from the
        /// pawn's penis are known before a form is picked, so that is the only source checked.
        /// </summary>
        public bool HidesKindOf(Pawn pawn)
        {
            if (hiddenKinds == null || hiddenKinds.Count == 0 || !VariantFromPenis)
            {
                return false;
            }
            Hediff penis = PenisOf(pawn);
            return penis != null && penis.def != null && hiddenKinds.Contains(penis.def.defName);
        }

        /// <summary>Whether the kind comes from the pawn's penis rather than the matched hediff.</summary>
        public bool VariantFromPenis
        {
            get { return string.Equals(variantFrom, "Penis", StringComparison.OrdinalIgnoreCase); }
        }

        [Unsaved(false)]
        public Texture2D[] tiers;

        /// <summary>Multiplet art per tier. Null without <see cref="multipletSuffix"/>, and null
        /// in slots that have no picture.</summary>
        [Unsaved(false)]
        public Texture2D[] multipletTiers;

        public void ResolveTextures()
        {
            if (sizeSteps < 1)
            {
                sizeSteps = 1;
            }
            tiers = new Texture2D[sizeSteps];
            multipletTiers = null;
            variantTiers = null;
            if (texturePrefix.NullOrEmpty())
            {
                return;
            }
            ResolveVariants();

            // Multiplet art does not fill gaps from neighbouring tiers - a gap simply means the
            // single picture of that tier.
            if (!multipletSuffix.NullOrEmpty())
            {
                multipletTiers = new Texture2D[sizeSteps];
                for (int i = 0; i < sizeSteps; i++)
                {
                    multipletTiers[i] = ContentFinder<Texture2D>.Get(
                        texturePrefix + "_" + i + multipletSuffix, false);
                }
            }

            int missing = 0;
            for (int i = 0; i < sizeSteps; i++)
            {
                tiers[i] = ContentFinder<Texture2D>.Get(texturePrefix + "_" + i, false);
                if (tiers[i] == null)
                {
                    missing++;
                }
            }

            // Gaps take the nearest lower tier, so partially drawn sets still work.
            Texture2D last = null;
            for (int i = 0; i < sizeSteps; i++)
            {
                if (tiers[i] != null)
                {
                    last = tiers[i];
                }
                else
                {
                    tiers[i] = last;
                }
            }
            for (int i = sizeSteps - 1; i >= 0; i--)
            {
                if (tiers[i] == null && i + 1 < sizeSteps)
                {
                    tiers[i] = tiers[i + 1];
                }
            }

            if (missing == sizeSteps)
            {
                Log.Warning("[NHT RJW Genitalia] " + defName + ": no texture found under '"
                            + texturePrefix + "_0.." + (sizeSteps - 1) + "'.");
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string e in base.ConfigErrors())
            {
                yield return e;
            }
            if (dollName.NullOrEmpty())
            {
                yield return "dollName is required";
            }
            if (bodyPart.NullOrEmpty())
            {
                yield return "bodyPart is required";
            }
            if (sizeThresholds != null && sizeThresholds.Count != 0
                && sizeThresholds.Count != sizeSteps)
            {
                yield return "sizeThresholds has " + sizeThresholds.Count
                             + " entries but sizeSteps is " + sizeSteps;
            }
        }

        private void ResolveVariants()
        {
            if (textureVariants == null || textureVariants.Count == 0)
            {
                return;
            }
            int slash = texturePrefix.LastIndexOf('/');
            string dir = (slash < 0) ? "" : texturePrefix.Substring(0, slash + 1);
            string own = texturePrefix.Substring(slash + 1);

            variantTiers = new Dictionary<string, Texture2D[]>();
            kindInfo = new Dictionary<string, TextureVariant>();
            for (int v = 0; v < textureVariants.Count; v++)
            {
                TextureVariant tv = textureVariants[v];
                if (tv == null || tv.name.NullOrEmpty() || tv.tiers == null)
                {
                    continue;
                }
                if (!kindInfo.ContainsKey(tv.name))
                {
                    kindInfo[tv.name] = tv;     // geometry sits on the kind's first entry
                }
                string stem = (tv.name == own ? dir : dir + tv.name + "/") + tv.name;
                string suffix = tv.variation.NullOrEmpty() ? "" : "_" + tv.variation;
                Texture2D[] set = new Texture2D[sizeSteps];
                bool any = false;
                for (int k = 0; k < tv.tiers.Count; k++)
                {
                    int i = tv.tiers[k];
                    if (i < 0 || i >= sizeSteps)
                    {
                        continue;
                    }
                    set[i] = ContentFinder<Texture2D>.Get(stem + "_" + i + suffix, false);
                    any |= set[i] != null;
                }
                if (any)
                {
                    variantTiers[VariantKey(tv.name, tv.variation)] = set;
                }
            }
            if (variantTiers.Count == 0)
            {
                variantTiers = null;
            }
        }

        private static string VariantKey(string name, string variation)
        {
            return variation.NullOrEmpty() ? name : name + ":" + variation;
        }

        private bool TryVariant(string name, string variation, int tier, out Texture2D tex)
        {
            tex = null;
            Texture2D[] set;
            if (variantTiers.TryGetValue(VariantKey(name, variation), out set)
                && tier < set.Length && set[tier] != null)
            {
                tex = set[tier];
                return true;
            }
            return false;
        }

        /// <summary>The kind's own art at this tier (with its variation first), not the fallbacks.</summary>
        private bool TryKindArt(string kind, string variation, int tier, out Texture2D tex)
        {
            tex = null;
            if (variantTiers == null || kind.NullOrEmpty())
            {
                return false;
            }
            if (!variation.NullOrEmpty() && TryVariant(kind, variation, tier, out tex))
            {
                return true;
            }
            return TryVariant(kind, null, tier, out tex);
        }

        /// <summary>Whether the kind itself has art at this tier (so its canvas rules apply).</summary>
        public bool KindHasOwnArt(string kind, string variation, int tier)
        {
            if (tiers == null || tiers.Length == 0)
            {
                return false;
            }
            Texture2D unused;
            return TryKindArt(kind, variation, Mathf.Clamp(tier, 0, tiers.Length - 1), out unused);
        }

        /// <summary>
        /// The picture for this tier. With two or more babies, the multiplet picture of that tier
        /// when one exists.
        /// </summary>
        public Texture2D TextureFor(int tier, int babies)
        {
            return TextureFor(tier, babies, null, null);
        }

        /// <summary>
        /// The picture for this tier and kind. <paramref name="kind"/> is a hediff defName such as
        /// <c>HorsePenis</c>, <paramref name="variation"/> a Sized Apparel part variation such as
        /// <c>Uncut</c>; either may be null. Kinds without art at this tier fall back in Sized
        /// Apparel's order, ending at the default art.
        /// </summary>
        public Texture2D TextureFor(int tier, int babies, string kind, string variation)
        {
            if (tiers == null || tiers.Length == 0)
            {
                return null;
            }
            tier = Mathf.Clamp(tier, 0, tiers.Length - 1);
            if (variantTiers != null && !kind.NullOrEmpty())
            {
                Texture2D found;
                if (TryKindArt(kind, variation, tier, out found))
                {
                    return found;
                }
                int slash = texturePrefix.LastIndexOf('/');
                string own = texturePrefix.Substring(slash + 1);
                if (!variation.NullOrEmpty() && kind != own && TryVariant(own, variation, tier, out found))
                {
                    return found;
                }
            }
            if (babies > 1 && multipletTiers != null && tier < multipletTiers.Length
                && multipletTiers[tier] != null)
            {
                return multipletTiers[tier];
            }
            return tiers[tier];
        }

        /// <summary>Turns a severity into a size tier.</summary>
        public int TierFor(float severity)
        {
            if (sizeThresholds == null || sizeThresholds.Count == 0)
            {
                return 0;
            }
            int tier = 0;
            for (int i = 0; i < sizeThresholds.Count && i < sizeSteps; i++)
            {
                if (severity >= sizeThresholds[i])
                {
                    tier = i;
                }
            }
            return Mathf.Clamp(tier, 0, sizeSteps - 1);
        }

        /// <summary>Does this hediff belong to this form?</summary>
        public bool MatchesHediff(Hediff hediff)
        {
            if (hediff == null || hediff.def == null)
            {
                return false;
            }
            if (sexPartOnly && !IsSexPart(hediff.def))
            {
                return false;
            }
            if (!variation.NullOrEmpty())
            {
                // Part variations (inverted nipples and so on) only exist with Sized Apparel.
                // Turned off in the settings, or with that mod missing, a variation form is never
                // picked and the default form is used instead.
                if (!NHTRJWSettings.Current.EffectiveInvertedNipple)
                {
                    return false;
                }
                if (!string.Equals(VariationOf(hediff), variation,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            if (hediffs != null && hediffs.Count > 0 && !hediffs.Contains(hediff.def.defName))
            {
                return false;
            }
            if (!hediffNameContains.NullOrEmpty()
                && hediff.def.defName.IndexOf(hediffNameContains,
                                              StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
            if (!genitalFamily.NullOrEmpty()
                && !string.Equals(GenitalFamilyOf(hediff.def), genitalFamily,
                                  StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        public bool MatchesGender(Pawn pawn)
        {
            if (gender.NullOrEmpty())
            {
                return true;
            }
            if (pawn == null)
            {
                return false;
            }
            return string.Equals(pawn.gender.ToString(), gender, StringComparison.OrdinalIgnoreCase);
        }

        // --- RJW reflection ------------------------------------------------------
        private static readonly Dictionary<HediffDef, string> familyCache =
            new Dictionary<HediffDef, string>();

        private const string SexPartDefTypeName = "rjw.HediffDef_SexPart";
        private const string SarDetailTypeName = "SizedApparel.SizedApparelBodyPartDetail";

        private static Type sarDetailType;
        private static FieldInfo sarVariationField;
        private static bool sarResolved;

        /// <summary>
        /// The part variation name Sized Apparel attached to this hediff, or "".
        ///
        /// SAR's <c>SizedApparelBodyPartDetail</c> is a HediffComp: when the part is created it
        /// rolls one of the candidates in <c>SizedApparelBodyPartVariationDef</c>, stores it in
        /// <c>variation</c> and saves it (Variation/VariationHediffComp.cs). The breast candidates
        /// are default and InvertedNipple
        /// (Defs/BodyPartDetailDefs/BodypartDetail_Breasts.xml); the default normalises to null.
        ///
        /// The SAR assembly is never hard-referenced - without it this is simply "".
        /// </summary>
        public static string VariationOf(Hediff hediff)
        {
            if (!sarResolved)
            {
                sarResolved = true;
                sarDetailType = GenTypes.GetTypeInAnyAssembly(SarDetailTypeName);
                if (sarDetailType != null)
                {
                    sarVariationField = sarDetailType.GetField(
                        "variation", BindingFlags.Instance | BindingFlags.Public);
                }
            }
            if (sarDetailType == null || sarVariationField == null)
            {
                return "";
            }

            HediffWithComps withComps = hediff as HediffWithComps;
            if (withComps == null || withComps.comps == null)
            {
                return "";
            }
            for (int i = 0; i < withComps.comps.Count; i++)
            {
                HediffComp comp = withComps.comps[i];
                if (comp == null || !sarDetailType.IsInstanceOfType(comp))
                {
                    continue;
                }
                try
                {
                    return sarVariationField.GetValue(comp) as string ?? "";
                }
                catch
                {
                    return "";
                }
            }
            return "";
        }


        private static readonly Dictionary<HediffDef, bool> sexPartCache =
            new Dictionary<HediffDef, bool>();

        /// <summary>
        /// The pawn's penis hediff: the first sex part whose genital family is Penis, as Sized
        /// Apparel takes it (<c>penisHediffs[0]</c>). Null when there is none.
        /// </summary>
        public static Hediff PenisOf(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null)
            {
                return null;
            }
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                Hediff h = all[i];
                if (h != null && h.def != null && IsSexPart(h.def)
                    && string.Equals(GenitalFamilyOf(h.def), "Penis", StringComparison.OrdinalIgnoreCase))
                {
                    return h;
                }
            }
            return null;
        }

        /// <summary>Is this HediffDef rjw.HediffDef_SexPart, or derived from it?</summary>
        public static bool IsSexPart(HediffDef def)
        {
            if (def == null)
            {
                return false;
            }
            bool cached;
            if (sexPartCache.TryGetValue(def, out cached))
            {
                return cached;
            }
            bool result = false;
            for (Type t = def.GetType(); t != null; t = t.BaseType)
            {
                if (t.FullName == SexPartDefTypeName)
                {
                    result = true;
                    break;
                }
            }
            sexPartCache[def] = result;
            return result;
        }

        /// <summary>Reads rjw.HediffDef_SexPart.genitalFamily through reflection, or "".</summary>
        public static string GenitalFamilyOf(HediffDef def)
        {
            string cached;
            if (familyCache.TryGetValue(def, out cached))
            {
                return cached;
            }
            string result = "";
            try
            {
                FieldInfo f = def.GetType().GetField("genitalFamily",
                                                     BindingFlags.Instance | BindingFlags.Public);
                if (f != null)
                {
                    object v = f.GetValue(def);
                    if (v != null)
                    {
                        result = v.ToString();
                    }
                }
            }
            catch
            {
                result = "";
            }
            familyCache[def] = result;
            return result;
        }
    }

    /// <summary>One kind's art set on a form: see <see cref="DollPartFormDef.textureVariants"/>.</summary>
    public class TextureVariant
    {
        /// <summary>The kind: a hediff defName such as <c>HorsePenis</c>.</summary>
        public string name;

        /// <summary>An optional Sized Apparel part variation such as <c>Uncut</c>.</summary>
        public string variation;

        /// <summary>The tiers that have art. The others fall back.</summary>
        public List<int> tiers;

        // --- Geometry measured from this kind's own art (gen_defs.py) --------------
        // Only the kind's first entry carries it. Zero means "use the form's value".

        /// <summary>Click hit area for this kind.</summary>
        public Vector2 hitboxScale;

        /// <summary>Glyph area on the doll for this kind (doll coordinates).</summary>
        public Vector2 glyphMin;

        /// <summary>The counterpart of <see cref="glyphMin"/>.</summary>
        public Vector2 glyphMax;
    }

    /// <summary>One hediff that swells the belly: see <see cref="DollPartFormDef.bellyHediffs"/>.</summary>
    public class BellyHediff
    {
        /// <summary>The hediff defName.</summary>
        public string hediff;

        /// <summary>Its severity counts this much (Sized Apparel's severityScale).</summary>
        public float scale = 1f;

        /// <summary>Labour: counts as a full 1 whatever its severity, as in Sized Apparel.</summary>
        public bool labor;
    }
}
