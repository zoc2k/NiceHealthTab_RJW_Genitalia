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
        /// <c>"Bleeding"</c> / <c>"Fluid"</c> / <c>"Implanted"</c>.
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
            if (texturePrefix.NullOrEmpty())
            {
                return;
            }

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

        /// <summary>
        /// The picture for this tier. With two or more babies, the multiplet picture of that tier
        /// when one exists.
        /// </summary>
        public Texture2D TextureFor(int tier, int babies)
        {
            if (tiers == null || tiers.Length == 0)
            {
                return null;
            }
            tier = Mathf.Clamp(tier, 0, tiers.Length - 1);
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
}
