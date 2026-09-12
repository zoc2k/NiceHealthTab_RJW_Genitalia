using System.Collections.Generic;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Which related mods are active.
    ///
    /// The only mods this patch **requires** are Nice Health Tab and RimJobWorld. Every
    /// other mod adds features when present; without it only those features are missing.
    /// Settings that rely on a missing mod are disabled in the UI so they cannot be turned
    /// on at all.
    ///
    /// The check is the same one Sized Apparel uses (HarmonyPatches.cs:16-24):
    ///     ModLister.AnyFromListActive(new[] { "packageId" })
    /// It is a vanilla API, so no assembly is referenced. packageId is case-insensitive.
    ///
    /// Computed once into static fields - the mod list cannot change while the game runs.
    /// </summary>
    internal static class ModDeps
    {
        // Copied verbatim from each mod's About/About.xml packageId.
        private const string RjwId = "rim.job.world";
        private const string NhtId = "Andromeda.NiceHealthTab";
        private const string MenstruationId = "rjw.menstruation";
        private const string FluidsId = "eltoro.rjw.menstruation.fluids";
        private const string SizedApparelId = "OTYOTY.SizedApparel";
        private const string BallsId = "TeheeItsMe525.RJWGenderOrgansMod";
        private const string LicentiaId = "LustLicentiaSerums.RJWLabs";

        /// <summary>Nice Health Tab - without it this patch has nothing to do.</summary>
        internal static readonly bool NiceHealthTab = Active(NhtId);

        /// <summary>RimJobWorld - without it our part Defs are not even loaded (MayRequire).</summary>
        internal static readonly bool Rjw = Active(RjwId);

        /// <summary>RJW Menstruation - source of the per-pawn areola pigmentation value.</summary>
        internal static readonly bool Menstruation = Active(MenstruationId);

        /// <summary>RJW Menstruation - Fluids - source of womb inflation (the vaginal
        /// cumflation hediff).</summary>
        internal static readonly bool MenstruationFluids = Active(FluidsId);

        /// <summary>Sized Apparel - source of body part variations such as inverted nipples.</summary>
        internal static readonly bool SizedApparel = Active(SizedApparelId);

        /// <summary>RJW Now with balls! - source of the gonad body part.</summary>
        internal static readonly bool Balls = Active(BallsId);

        /// <summary>Licentia Serums - serums that swap the breast hediff, which indirectly
        /// changes the nipple colour.</summary>
        internal static readonly bool Licentia = Active(LicentiaId);

        private static bool Active(string packageId)
        {
            return ModLister.AnyFromListActive(new List<string> { packageId });
        }
    }
}
