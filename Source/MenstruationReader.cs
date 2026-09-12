using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Reads the womb fluid amount from rjw_menstruation. Attached **through reflection only**
    /// - a hard reference to that mod throws TypeLoadException when it is absent.
    ///
    /// Why this is needed
    ///   Of the womb pictures, pregnancy, implantation and inflation all come from hediffs
    ///   (a severity or a babies count). The **fluid amount is not a hediff** at all - it is a
    ///   property on a comp.
    ///
    /// What we read (names checked directly in the 1.6 assembly)
    ///   <c>TotalCumPercent</c>  a float property: fluid as a **fraction (0..1)** of womb
    ///                           capacity
    ///
    ///   That it is a fraction is clear from their <c>MenstruationUtility.GetCumIcon</c>
    ///   thresholds: 0.001 / 0.01 / 0.05 / 0.11 and so on. It is not a percentage.
    ///
    /// Menstrual blood is in there too
    ///   Their <c>BleedOut()</c> pours menstrual blood into the **same container** as cum.
    ///
    ///       CumIn(Pawn, bledAmount, Translations.Menstrual_Blood, -5.0f, Utility.BloodDef(Pawn));
    ///       blood.Color = BloodColor;
    ///
    ///   <c>CumIn</c> simply Adds to <c>cums</c>, so menstrual blood counts towards
    ///   <c>TotalCumPercent</c> and turns <c>GetCumMixtureColor</c> red. We therefore cover
    ///   both with **one greyscale fluid set plus runtime tinting**, and keep no separate
    ///   menstruation textures.
    ///
    /// The comp sits on the **vagina hediff** (<c>Hediffs_PrivateParts.xml</c> adds it to
    /// <c>rjw.HediffDef_SexPart[defName="Vagina"]</c>), which is why these methods take a
    /// vagina hediff.
    ///
    /// Without the mod, <see cref="Available"/> is false and every lookup returns false.
    /// </summary>
    internal static class MenstruationReader
    {
        private const string CompTypeName = "RJW_Menstruation.HediffComp_Menstruation";

        /// <summary>
        /// The smallest fraction that still counts as "there is fluid". Below this their
        /// <c>GetCumIcon</c> uses <c>Womb/Empty</c> (fully transparent); we use the same bound.
        /// </summary>
        private const float FluidFloor = 0.001f;

        private static bool resolved;
        private static Type compType;
        private static PropertyInfo totalCumPercentProp;
        private static PropertyInfo cumColorProp;

        /// <summary>True when rjw_menstruation is present and its members were found.</summary>
        internal static bool Available
        {
            get
            {
                Resolve();
                return compType != null;
            }
        }

        private static void Resolve()
        {
            if (resolved)
            {
                return;
            }
            resolved = true;

            Type t = GenTypes.GetTypeInAnyAssembly(CompTypeName);
            if (t == null)
            {
                return;         // The mod is absent. Do nothing, quietly.
            }

            PropertyInfo cum = t.GetProperty("TotalCumPercent",
                                             BindingFlags.Public | BindingFlags.Instance);
            // The colour is optional - if it cannot be read, the texture stays as drawn.
            cumColorProp = t.GetProperty("GetCumMixtureColor",
                                         BindingFlags.Public | BindingFlags.Instance);
            if (cumColorProp != null && cumColorProp.PropertyType != typeof(UnityEngine.Color))
            {
                cumColorProp = null;
            }
            if (cum == null || cum.PropertyType != typeof(float))
            {
                // Their layout changed. Rather than guess, switch the whole thing off.
                Log.Warning(Bootstrap.Prefix
                            + "rjw_menstruation found but TotalCumPercent is unfamiliar; "
                            + "womb fluid graphics disabled.");
                return;
            }

            compType = t;
            totalCumPercentProp = cum;
        }

        /// <summary>The menstruation comp on this hediff, or null.</summary>
        private static object CompOn(Hediff hediff)
        {
            Resolve();
            HediffWithComps withComps = hediff as HediffWithComps;
            if (compType == null || withComps == null || withComps.comps == null)
            {
                return null;
            }
            List<HediffComp> comps = withComps.comps;
            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i] != null && compType.IsInstanceOfType(comps[i]))
                {
                    return comps[i];
                }
            }
            return null;
        }

        /// <summary>Is there fluid in the womb? If so, gives the fraction of capacity.</summary>
        internal static bool TryFluid(Hediff vagina, out float percent)
        {
            percent = 0f;
            object comp = CompOn(vagina);
            if (comp == null)
            {
                return false;
            }
            try
            {
                percent = (float)totalCumPercentProp.GetValue(comp, null);
            }
            catch (Exception ex)
            {
                // One failure switches it off for good: this runs every frame, so logging
                // would flood.
                compType = null;
                Log.Warning(Bootstrap.Prefix + "womb fluid read failed, disabled: " + ex);
                return false;
            }
            return percent >= FluidFloor;
        }

        private static bool eggResolved;
        private static MethodInfo eggIconMethod;

        // Their ovary branch can throw through no fault of ours (see TryEggIcon), so it is put
        // to sleep for a while instead of being switched off for good.
        private const float OvaryRetrySeconds = 10f;
        private static bool ovaryBroken;
        private static float ovaryRetryAt;
        private static bool ovaryWarned;
        private static bool eggWarned;

        /// <summary>
        /// The ovulation / fertilization / implantation picture - **the very picture**
        /// menstruation draws in its own womb window.
        ///
        /// We simply call <c>MenstruationUtility.GetEggIcon(comp, includeOvary: true)</c>
        /// (a public static confirmed in the 1.6 assembly). Ovaries in the follicular and
        /// ovulation phases, egg / fertilizing / fertilized in the luteal phase, implantation
        /// in early pregnancy - they decide every stage. When there is nothing to show they
        /// return the transparent <c>Womb/Empty</c>.
        ///
        /// Why <c>includeOvary</c> is retried rather than trusted: with it true, their
        /// <c>GetOvaryIcon</c> reads the pawn's current sex job through
        /// <c>JobDriver_Sex.Sexprops.sexType</c>. For some interaction defs RJW throws a
        /// NullReferenceException in <c>SexProps.get_interaction</c> while building
        /// <c>SexInteraction</c>, which surfaces here as a TargetInvocationException. That is a
        /// transient state of another mod, not a broken lookup on our side, so we fall back to
        /// <c>includeOvary: false</c> for the moment (every other stage, implantation included,
        /// still draws) and try the ovary branch again a few seconds later.
        /// </summary>
        internal static bool TryEggIcon(Pawn pawn, out UnityEngine.Texture2D icon)
        {
            icon = null;
            if (!eggResolved)
            {
                eggResolved = true;
                Resolve();
                Type util = GenTypes.GetTypeInAnyAssembly("RJW_Menstruation.MenstruationUtility");
                if (util != null && compType != null)
                {
                    eggIconMethod = util.GetMethod("GetEggIcon", BindingFlags.Public | BindingFlags.Static,
                                                   null, new[] { compType, typeof(bool) }, null);
                    if (eggIconMethod != null && eggIconMethod.ReturnType != typeof(UnityEngine.Texture2D))
                    {
                        eggIconMethod = null;
                    }
                }
            }
            if (eggIconMethod == null || pawn == null || pawn.health == null
                || pawn.health.hediffSet == null)
            {
                return false;
            }
            List<Hediff> all = pawn.health.hediffSet.hediffs;
            for (int i = 0; i < all.Count; i++)
            {
                object comp = CompOn(all[i]);
                if (comp == null)
                {
                    continue;
                }
                bool withOvary = !ovaryBroken || UnityEngine.Time.realtimeSinceStartup >= ovaryRetryAt;
                if (TryIcon(comp, withOvary, out icon))
                {
                    ovaryBroken = false;
                    return icon != null;
                }
                if (!withOvary)
                {
                    return false;       // Already the plain branch; nothing left to fall back to.
                }

                // Their ovary branch threw. Rest it for a while and draw the rest meanwhile.
                ovaryBroken = true;
                ovaryRetryAt = UnityEngine.Time.realtimeSinceStartup + OvaryRetrySeconds;
                return TryIcon(comp, false, out icon) && icon != null;
            }
            return false;
        }

        /// <summary>
        /// One call into their GetEggIcon. False when it threw; the icon is then null.
        /// Each branch logs at most once - this runs every frame.
        /// </summary>
        private static bool TryIcon(object comp, bool includeOvary, out UnityEngine.Texture2D icon)
        {
            icon = null;
            try
            {
                icon = eggIconMethod.Invoke(null, new object[] { comp, includeOvary })
                       as UnityEngine.Texture2D;
                return true;
            }
            catch (Exception ex)
            {
                if (includeOvary)
                {
                    if (!ovaryWarned)
                    {
                        ovaryWarned = true;
                        Log.Warning(Bootstrap.Prefix + "ovulation picture skipped for now - RJW "
                                    + "Menstruation threw while reading the pawn's sex job. The "
                                    + "other stages still draw and the ovary branch is retried "
                                    + "every " + OvaryRetrySeconds + "s: " + ex);
                    }
                }
                else if (!eggWarned)
                {
                    eggWarned = true;
                    Log.Warning(Bootstrap.Prefix + "egg icon read failed: " + ex);
                }
                return false;
            }
        }

        /// <summary>
        /// The mixed colour of the fluid in the womb; it turns red when menstrual blood is in
        /// there. This is the colour their <c>Dialog_WombStatus.DrawWomb</c> multiplies over
        /// the fluid texture.
        /// </summary>
        internal static bool TryFluidColor(Hediff vagina, out UnityEngine.Color color)
        {
            color = UnityEngine.Color.white;
            object comp = CompOn(vagina);
            if (comp == null || cumColorProp == null)
            {
                return false;
            }
            try
            {
                color = (UnityEngine.Color)cumColorProp.GetValue(comp, null);
                return true;
            }
            catch (Exception ex)
            {
                cumColorProp = null;
                Log.Warning(Bootstrap.Prefix + "womb fluid color read failed: " + ex);
                return false;
            }
        }
    }
}
