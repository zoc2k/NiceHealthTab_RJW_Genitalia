using System;
using System.Collections;
using System.Reflection;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// The route for reading a form's tier from somewhere other than a hediff's severity.
    ///
    /// Each womb state takes its value from a different place.
    ///
    ///   pregnancy    severity of RJW_pregnancy      <- the plain hediff rules are enough
    ///   inflation    severity of the Cumflation hediff  <- likewise
    ///   fluid        TotalCumPercent on a comp      <- <see cref="MenstruationReader"/>
    ///                (menstrual blood goes into the same container, so this one layer
    ///                 stands for menstruation as well)
    ///   implanted    the babies count on the pregnancy hediff  <- here
    ///
    /// A form Def's <c>stateSource</c> picks which of these to use. Every lookup is
    /// reflection, and returns false quietly when the mod is missing (that form is then not
    /// drawn).
    /// </summary>
    internal static class FormStateReader
    {
        internal const string Fluid = "Fluid";
        internal const string Implanted = "Implanted";

        /// <summary>
        /// Upper bound of gestation that still counts as implantation. rjw_menstruation uses
        /// the same value - <c>GetPregnancyIcon</c> picks <c>Womb_Implanted</c> instead of the
        /// fetus art while <c>gestationProgress &lt; 0.2</c>, when no fetus is visible yet.
        /// </summary>
        private const float ImplantedUntil = 0.2f;

        private const string PregnancyTypeName = "rjw.Hediff_BasePregnancy";

        private static bool resolved;
        private static Type pregnancyType;
        private static FieldInfo babiesField;

        /// <summary>
        /// Reads the value a Def's <c>stateSource</c> points at.
        /// Returns false when the pawn is not in that state - then the form is not picked.
        /// </summary>
        internal static bool TryRead(string source, Hediff hediff, out float value)
        {
            value = 0f;
            if (source == null || hediff == null)
            {
                return false;
            }
            if (source == Fluid)
            {
                return MenstruationReader.TryFluid(hediff, out value);
            }
            if (source == Implanted)
            {
                return TryImplanted(hediff, out value);
            }
            return false;
        }

        /// <summary>
        /// Is this right after implantation? If so, gives <b>egg count - 1</b> (0 = one, 4 = five).
        ///
        /// The tier is a count rather than a severity, so this form's sizeThresholds must be
        /// 0 1 2 3 4 (gen_defs.COUNT5).
        /// </summary>
        private static bool TryImplanted(Hediff hediff, out float value)
        {
            value = 0f;
            Resolve();
            if (pregnancyType == null || !pregnancyType.IsInstanceOfType(hediff))
            {
                return false;
            }
            if (hediff.Severity >= ImplantedUntil)
            {
                return false;       // The fetus is showing now; the fetus art takes over.
            }
            int count;
            if (!TryBabies(hediff, out count))
            {
                return false;
            }
            value = count - 1;
            return true;
        }

        /// <summary>
        /// Is a fetus showing on this pawn? Forms marked
        /// <see cref="DollPartFormDef.hideWhileFetus"/> (the womb fluid and inflation layers) are
        /// skipped while one is.
        ///
        /// This is the rule RJW Menstruation's own womb window uses. In
        /// <c>Dialog_WombStatus</c>, once a pregnancy hediff is there it draws the cum layer only
        /// while gestation progress is below <see cref="ImplantedUntil"/> (the implantation
        /// stage) and uses <c>Womb/Empty</c> from there on. The amounts are still tracked by
        /// those mods; they are simply not drawn over a womb that shows a fetus.
        ///
        /// The pregnancy severity is the gestation progress (0.001 -> 1.0), the same value
        /// <see cref="TryImplanted"/> reads.
        /// </summary>
        internal static bool FetusShowing(Pawn pawn)
        {
            Resolve();
            if (pregnancyType == null || pawn == null || pawn.health == null
                || pawn.health.hediffSet == null)
            {
                return false;
            }
            System.Collections.Generic.List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                Hediff h = all[i];
                if (h != null && pregnancyType.IsInstanceOfType(h) && h.Severity >= ImplantedUntil)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The baby count on a pregnancy hediff. 1 if it is not one, or cannot be read.
        ///
        /// The fetus art uses this to pick the multiplet picture (DESIGN.md 67). RJW
        /// Menstruation picks its twin art from the same field - <c>GetPregnancyIcon</c>'s
        /// <c>babycount = preg.babies?.Count ?? 1</c>.
        /// </summary>
        internal static int BabyCount(Hediff hediff)
        {
            int count;
            return TryBabies(hediff, out count) ? count : 1;
        }

        private static bool TryBabies(Hediff hediff, out int count)
        {
            count = 1;
            Resolve();
            if (hediff == null || pregnancyType == null || !pregnancyType.IsInstanceOfType(hediff))
            {
                return false;
            }
            try
            {
                ICollection babies = babiesField.GetValue(hediff) as ICollection;
                count = (babies == null) ? 1 : babies.Count;
                if (count < 1)
                {
                    count = 1;      // May not be filled in yet; treat it as one.
                }
                return true;
            }
            catch (Exception ex)
            {
                pregnancyType = null;
                count = 1;
                Log.Warning(Bootstrap.Prefix + "pregnancy baby count read failed, disabled: " + ex);
                return false;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            Type t = GenTypes.GetTypeInAnyAssembly(PregnancyTypeName);
            if (t == null)
            {
                return;             // No RJW. This whole mod then does nothing.
            }
            FieldInfo f = t.GetField("babies", BindingFlags.Public | BindingFlags.Instance);
            if (f == null)
            {
                Log.Warning(Bootstrap.Prefix
                            + "rjw.Hediff_BasePregnancy has no 'babies' field; "
                            + "implanted and multiplet graphics disabled.");
                return;
            }
            pregnancyType = t;
            babiesField = f;
        }
    }
}
