using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Colour of the womb fluid layer.
    ///
    /// Why a colour is needed: there is only **one set** of fluid textures and it stands for
    /// both menstrual blood and cum. That is how rjw_menstruation does it too -
    /// <c>BleedOut()</c> pours menstrual blood through <c>CumIn</c> into the same container
    /// (<c>cums</c>) as cum, so the amount is counted together in <c>TotalCumPercent</c> and
    /// only the colour differs, through <c>GetCumMixtureColor</c>.
    ///
    /// Our textures are drawn in the same cream tone as their <c>Womb_Cum_NN</c> (a chroma
    /// spread of about 82). They multiply <c>cumcolor</c> over that same tone, so
    /// **multiplying over cream**, not over greyscale, is the convention of this family.
    ///
    /// <see cref="DollStateApplier"/> settles it once through <see cref="Prepare"/> right
    /// before drawing, and <see cref="TintedPartRenderer"/> picks it up from there.
    /// </summary>
    internal static class WombFluidAppearance
    {
        // --- Frame stamp ---------------------------------------------------------
        // We draw this layer ourselves (TintedPartRenderer). Whether to draw it lives in a
        // static field, so if the preparing side is skipped even once, the previous pawn's
        // value stays behind and keeps being drawn **for every pawn**. That actually
        // happened: when DollStateApplier switched itself off after an exception, the womb
        // fluid stuck around forever.
        //
        // So we record the frame it was prepared in and treat it as valid only in that
        // frame. Even if something forgets to reset it, it cannot survive past one frame.
        private static bool visible;
        private static int preparedFrame = -1;

        /// <summary>Whether to draw this layer in the current frame.</summary>
        internal static bool CurrentVisible
        {
            get { return visible && preparedFrame == Time.frameCount; }
        }

        /// <summary>The colour to multiply over the fluid layer this frame.</summary>
        internal static Color CurrentColor = Color.white;

        internal static void Clear()
        {
            visible = false;
            preparedFrame = -1;
            CurrentColor = Color.white;
        }

        /// <summary>
        /// Works out the fluid colour.
        ///
        /// The comp that carries the colour sits on the **vagina hediff**, but the hediff
        /// that picked the form can be a different one - inflation is picked by the
        /// Cumflation hediff, which has no comp. So we walk the list for one that has it.
        /// </summary>
        internal static void Prepare(List<Hediff> hediffs)
        {
            visible = true;
            preparedFrame = Time.frameCount;
            // If the colour cannot be read, leave the texture as drawn - that tone is cum.
            CurrentColor = Color.white;
            if (hediffs == null)
            {
                return;
            }
            for (int i = 0; i < hediffs.Count; i++)
            {
                Color color;
                if (MenstruationReader.TryFluidColor(hediffs[i], out color))
                {
                    CurrentColor = color;
                    return;
                }
            }
        }
    }
}
