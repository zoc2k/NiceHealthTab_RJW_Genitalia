using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Cumpilation compatibility: stops the red error raised by attaching a hediff to missing
    /// genitals.
    ///
    /// What goes wrong
    ///   <c>Cumpilation.Cumflation.CumflationUtility.GetOrCreateCumflationHediff</c>
    ///   **creates and attaches** a cumflation hediff on the spot when the pawn has none. The
    ///   part it attaches to is always the genitals
    ///   (<c>rjw.Genital_Helper.get_genitalsBPR</c>). If that part is missing,
    ///   <c>Verse.HediffSet.AddDirect</c> refuses and leaves a red line in the log.
    ///
    ///       Tried to add health diff to missing part BodyPartRecord(Genitals parts.Count=0)
    ///
    ///   Two places call that method, so it fires even where it looks like a mere check.
    ///     - <c>Recipe_ExtractCum.AvailableOnNow</c>, every time a surgery menu is built.
    ///       Both NHT's <c>DollOperations.OpenFloatMenuForPart</c> and vanilla
    ///       <c>HealthCardUtility</c> ask every recipe once whether it is available, so the
    ///       error appeared **whichever part you clicked**.
    ///     - <c>ThinkNode_ConditionalCumflationSeverity.Satisfied</c>, every time a pawn picks
    ///       its next job. That one piles up just from leaving the game running.
    ///
    ///   It reproduces without this mod too, but since this patch adds one more thing to click
    ///   on the doll, we block it here.
    ///
    /// What it does
    ///   Rather than guarding each caller, it guards **the one cause**. Only when the error
    ///   would really happen do we skip the original and return the same hediff it would have
    ///   made - we only leave out the attaching.
    ///
    ///       Hediff h = HediffMaker.MakeHediff(Cumpilation_Cumflation, pawn, genitalsPart);
    ///       h.Severity = 0f;
    ///       // health.AddHediff(...) is never called
    ///
    ///   **Behaviour is unchanged and only the error disappears**, because the original also
    ///   returns the same severity-0 object after the add fails (AddDirect just logs and
    ///   returns). What both callers see is exactly what they see today.
    ///     - the recipe: <c>!( ... .Severity &gt; 0f)</c> -> false. A pawn with no genitals not
    ///       being offered "extract cum" is the correct outcome anyway.
    ///     - the think node: <c>Severity &gt; AutoDeflateMinSeverity</c> -> false.
    ///   Not returning null matters: the think node checks for null, but the recipe reads
    ///   <c>.Severity</c> straight away.
    ///
    ///   We skip the original only when all three hold:
    ///     1. the pawn has no Cumflation hediff yet (otherwise the original just returns it)
    ///     2. the genitals part can be found (without it the hediff goes on the whole body and
    ///        raises no error)
    ///     3. that part is missing
    ///
    /// Without Cumpilation this does nothing, and its assembly is never hard-referenced.
    /// If they fix <c>GetOrCreateCumflationHediff</c>, this guard quietly becomes harmless.
    /// </summary>
    internal static class CumpilationCompat
    {
        private const string UtilityTypeName = "Cumpilation.Cumflation.CumflationUtility";
        private const string HelperTypeName = "rjw.Genital_Helper";
        private const string CumflationDefName = "Cumpilation_Cumflation";

        private static MethodInfo genitalsBpr;
        private static HediffDef cumflation;
        private static bool ready;

        /// <summary>True when patched. Quietly false when Cumpilation is absent.</summary>
        internal static bool Install()
        {
            Type utilityType = GenTypes.GetTypeInAnyAssembly(UtilityTypeName);
            if (utilityType == null)
            {
                return false;               // No Cumpilation - nothing to do
            }

            MethodInfo target = AccessTools.Method(
                utilityType, "GetOrCreateCumflationHediff", new[] { typeof(Pawn) });

            // Always check DeclaringType and the return type: AccessTools searches base types
            // too, and our prefix takes __result as a Hediff. If their signature changes we back
            // out instead of patching.
            if (target == null || target.DeclaringType != utilityType
                || target.ReturnType != typeof(Hediff))
            {
                Log.Message(Bootstrap.Prefix + "Cumpilation is present but "
                            + "CumflationUtility.GetOrCreateCumflationHediff no longer matches"
                            + " - not patching.");
                return false;
            }

            Type helperType = GenTypes.GetTypeInAnyAssembly(HelperTypeName);
            genitalsBpr = (helperType == null)
                ? null
                : AccessTools.Method(helperType, "get_genitalsBPR", new[] { typeof(Pawn) });
            if (genitalsBpr == null || genitalsBpr.ReturnType != typeof(BodyPartRecord))
            {
                Log.Message(Bootstrap.Prefix + "Cumpilation is present but "
                            + "rjw.Genital_Helper.get_genitalsBPR is not available - not patching.");
                return false;
            }

            // We must return the same hediff the original would make, so without this the
            // guard cannot be installed at all.
            cumflation = DefDatabase<HediffDef>.GetNamedSilentFail(CumflationDefName);
            if (cumflation == null)
            {
                Log.Message(Bootstrap.Prefix + "Cumpilation is present but HediffDef '"
                            + CumflationDefName + "' was not found - not patching.");
                return false;
            }

            MethodInfo prefix = typeof(CumpilationCompat).GetMethod(
                "GetOrCreatePrefix", BindingFlags.Static | BindingFlags.Public);
            new Harmony(Bootstrap.HarmonyId).Patch(target, new HarmonyMethod(prefix));
            ready = true;
            return true;
        }

        /// <summary>
        /// When the genitals part is missing, do not attach. Hand back the same severity-0
        /// hediff the original would have returned (never null).
        /// </summary>
        /// <remarks>
        /// The parameter name <c>inflated</c> must match the original - Harmony binds by name.
        /// </remarks>
        public static bool GetOrCreatePrefix(Pawn inflated, ref Hediff __result)
        {
            if (!ready)
            {
                return true;
            }
            try
            {
                if (inflated == null || inflated.health == null
                    || inflated.health.hediffSet == null)
                {
                    return true;
                }

                // 1. If one exists the original does not create anything, so no error can
                //    happen - leave it to them.
                if (inflated.health.hediffSet.GetFirstHediffOfDef(cumflation) != null)
                {
                    return true;
                }

                // 2. Find the part **the same way they do**; a different lookup would make the
                //    guard disagree with them.
                BodyPartRecord part =
                    genitalsBpr.Invoke(null, new object[] { inflated }) as BodyPartRecord;
                if (part == null)
                {
                    return true;
                }

                // 3. Use **the same test** AddDirect uses; it also covers a missing parent part.
                if (inflated.health.hediffSet.GetNotMissingParts().Contains(part))
                {
                    return true;
                }

                // From here on is the path where the original logs the red error. We build the
                // hediff exactly as they do and only leave out the attaching. Nothing is saved,
                // so the pawn's state does not change.
                Hediff made = HediffMaker.MakeHediff(cumflation, inflated, part);
                made.Severity = 0f;
                __result = made;
                return false;
            }
            catch (Exception ex)
            {
                // This runs often. After one failure, switch off and fall back to the original.
                ready = false;
                Log.Warning(Bootstrap.Prefix + "Cumpilation guard disabled after an error: " + ex);
                return true;
            }
        }
    }
}
