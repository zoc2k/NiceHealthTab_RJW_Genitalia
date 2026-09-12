using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Leaves RJW's "plain size hediffs" out of Nice Health Tab's doll condition check.
    ///
    /// Why this is needed
    ///   NHT's <c>DollCondition.GetPartColor</c> calls a part <c>NonOk</c> as soon as it has
    ///   any visible hediff, and paints the part with that hediff's LabelColor. RJW stores
    ///   the "size" of genitals, breasts and gonads as the severity of an always-present
    ///   visible hediff, so a perfectly healthy pawn's parts would stay lit up forever.
    ///
    /// How
    ///   We add them to <c>HediffCache.IgnoreHediffs</c>, the set NHT opened up for add-ons
    ///   (the very set NHT's own <c>SpecialHediffDataDef</c> fills from XML). Hediffs in that
    ///   set drop out of the doll's condition and colour maths only; the list on the right
    ///   reads <c>pawn.health.hediffSet</c> directly, so size information still shows.
    ///
    /// What goes in - every HediffDef that satisfies all of
    ///   - its type is <c>rjw.HediffDef_SexPart</c> (or derived from it)
    ///   - <c>isBad == false</c>                       (pathological ones keep showing)
    ///   - <c>countsAsAddedPartOrImplant == false</c>  (implants such as bionic genitals
    ///                                                  keep showing)
    /// </summary>
    internal static class SizeHediffFilter
    {
        private const string SexPartDefTypeName = "rjw.HediffDef_SexPart";

        private static HashSet<HediffDef> ignoreSet;
        private static readonly List<HediffDef> targets = new List<HediffDef>();
        private static bool applied;

        /// <summary>Collects the target hediffs. Returns how many were found.</summary>
        public static int Init()
        {
            targets.Clear();
            ignoreSet = null;
            applied = false;

            Type cacheType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.HediffCache");
            if (cacheType == null)
            {
                return 0;
            }

            FieldInfo field = cacheType.GetField("IgnoreHediffs",
                                                 BindingFlags.Static | BindingFlags.Public);
            if (field == null)
            {
                return 0;
            }

            ignoreSet = field.GetValue(null) as HashSet<HediffDef>;
            if (ignoreSet == null)
            {
                return 0;
            }

            foreach (HediffDef def in DefDatabase<HediffDef>.AllDefsListForReading)
            {
                if (IsSizeOnlySexPart(def))
                {
                    targets.Add(def);
                }
            }
            return targets.Count;
        }

        public static void ApplySetting(bool hide)
        {
            if (ignoreSet == null || hide == applied)
            {
                return;
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (hide)
                {
                    ignoreSet.Add(targets[i]);
                }
                else
                {
                    ignoreSet.Remove(targets[i]);
                }
            }
            applied = hide;
        }

        private static bool IsSizeOnlySexPart(HediffDef def)
        {
            if (def == null || def.isBad || def.countsAsAddedPartOrImplant)
            {
                return false;
            }
            for (Type t = def.GetType(); t != null; t = t.BaseType)
            {
                if (t.FullName == SexPartDefTypeName)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
