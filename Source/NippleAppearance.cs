using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Works out a pawn's nipple colour and puts it on the doll.
    ///
    /// Where the values come from - only what was confirmed by reading the actual mod sources.
    ///
    ///  RJW (rim.job.world)
    ///     Has no nipple "colour" data at all, only the breast hediff severity (its size).
    ///
    ///  RJW Menstruation (rjw_menstruation)
    ///     This is where the colour is actually computed.
    ///     <c>RJW_Menstruation.HediffComp_Breast</c> sits on the breast hediff and keeps
    ///     (1.6/source/.../HediffComps/HediffComp_Breast.cs):
    ///
    ///         cachedColor = Colors.CMYKLerp(SafeSkinColor(pawn), Props.BlackNippleColor, Alpha)
    ///         Alpha       = baseAlpha + nippleProgress * 0.2
    ///
    ///     - <c>baseAlpha</c> is rolled per pawn and saved with the game.
    ///     - <c>nippleProgress</c> rises and falls with pregnancy and nursing.
    ///     - <c>Props.BlackNippleColor</c> differs per breast HediffDef
    ///       (Patches/Hediffs_PrivateParts_Breasts.xml - natural breasts (55,20,0),
    ///        artificial ones (Hydraulic/Bionic/Archotech) (255,255,255)).
    ///     We borrow <c>Alpha</c> (the per-pawn depth) and <c>Props.BlackNippleColor</c> (the
    ///     direction it deepens towards). Only the starting point differs: a reference colour
    ///     mixed with <c>SarNipple</c> rather than plain skin.
    ///
    ///         base   = CMYKLerp(skin, SarNipple, 0.5)          (useSkinColor off: SarNipple)
    ///         colour = CMYKLerp(base, BlackNippleColor, Alpha)   (usePigmentation off: base)
    ///
    ///     This used to be <c>CMYKLerp(skin, SarNipple, Alpha)</c>. A rising depth only moved
    ///     the colour **towards pink** instead of deepening it, and on dark skin it even got
    ///     lighter (reported as "the changed nipple colour does not show on the doll").
    ///
    ///  Licentia Serums (licentia-serums)
    ///     Has no code that touches nipple colour directly. Instead
    ///     <c>Comp_MutagenicSerumSpecial</c> swaps the breast HediffDef itself
    ///     (Mutator.cs:86 - SlimeBreasts, for example). A different Def brings a different
    ///     <c>Props.BlackNippleColor</c> and comp instance with it, so reading the comp picks
    ///     the serum's effect up on its own.
    ///
    ///  Sized Apparel (sized-apparel-zero plus its race and body add-ons)
    ///     The place that actually "draws" nipples. The reference colour (<c>SarNipple</c>)
    ///     was measured from its textures.
    ///
    /// Without Menstruation there is no per-pawn depth data; then nothing deepens and the base
    /// colour is used.
    /// </summary>
    internal static class NippleAppearance
    {
        private const string BreastCompTypeName = "RJW_Menstruation.HediffComp_Breast";
        private const string BreastPropsTypeName = "RJW_Menstruation.CompProperties_Breast";

        /// <summary>
        /// How much skin and SarNipple are mixed into the base colour: half and half.
        /// Per-pawn variation comes from pigmentation (Menstruation's Alpha), not from here -
        /// this value is fixed so we do not invent data that does not exist.
        /// </summary>
        private const float FallbackAlpha = 0.5f;

        /// <summary>
        /// The reference nipple colour, measured from Sized Apparel family textures.
        ///
        /// SAR itself (sized-apparel-zero) has greyscale breast textures, so no nipple colour.
        /// The colour lives in its race and body add-ons. We took 16 textures with drawn nipples
        /// from the installed SAR family mods, kept only the areas clearly more saturated than
        /// the surrounding skin (the areola and nipple) and took the median.
        ///     rgb(235, 159, 151)
        /// e.g. Anty (236,175,163) / Snow-Rabbit (229,160,153) / Miho (222,147,140)
        /// </summary>
        private static readonly Color SarNipple =
            new Color(235f / 255f, 159f / 255f, 151f / 255f);

        private static Type breastCompType;
        private static PropertyInfo alphaProperty;
        private static bool resolved;

        /// <summary>Nipple colour of the pawn being drawn. Filled in by <see cref="Prepare"/>.</summary>
        internal static Color CurrentColor = Color.white;

        // --- Frame stamp ---------------------------------------------------------
        // We draw this layer ourselves (TintedPartRenderer). Whether to draw it lives in a
        // static field, so if the preparing side is skipped even once, the previous pawn's
        // value stays behind and keeps being drawn **for every pawn**. That actually happened:
        // when DollStateApplier switched itself off after an exception, the womb fluid stuck
        // around forever.
        //
        // So we record the frame it was prepared in and treat it as valid only in that frame.
        // Even if something forgets to reset it, it cannot survive past one frame.
        private static bool visible;
        private static int preparedFrame = -1;

        /// <summary>Whether to draw this layer in the current frame.</summary>
        internal static bool CurrentVisible
        {
            get { return visible && preparedFrame == Time.frameCount; }
        }

        /// <summary>
        /// Settles the pawn's nipple colour right before one health card is drawn.
        ///
        /// One settings switch, one step. Switches whose mod is missing already come back off
        /// through <c>Effective*</c>, so they are not checked again here.
        ///
        ///   showNipples       off -> nothing is drawn
        ///   useSkinColor      on  -> skin and SarNipple, half and half, as the base colour
        ///                     off -> SarNipple is the base colour
        ///   usePigmentation   on  -> from the base towards the dark colour by the pawn's own
        ///                            depth (Menstruation)
        ///                     off -> the base colour as it is
        ///   monochromeNipples on  -> keep only the brightness at the end
        /// </summary>
        internal static void Prepare(Pawn pawn, Hediff breastHediff)
        {
            NHTRJWSettings s = NHTRJWSettings.Current;
            if (!s.showNipples || breastHediff == null)
            {
                Clear();
                return;
            }

            visible = true;
            preparedFrame = Time.frameCount;

            // 1) The base colour: the nipple without any pigmentation
            Color c = s.useSkinColor
                ? CMYKLerp(SkinColorOf(pawn), SarNipple, FallbackAlpha)
                : SarNipple;

            // 2) Pigmentation: from the base towards Menstruation's dark colour by the pawn's
            //    own depth. Same direction as Menstruation (Props.BlackNippleColor), so the
            //    deepening through pregnancy and nursing shows as it should. For artificial
            //    breasts that colour is white, so they get lighter - same as on their side.
            float alpha;
            Color dark;
            if (s.EffectivePigmentation && TryReadMenstruationAlpha(breastHediff, out alpha, out dark))
            {
                c = CMYKLerp(c, dark, Mathf.Clamp01(alpha));
            }

            CurrentColor = s.monochromeNipples ? ToGray(c) : c;
        }

        internal static void Clear()
        {
            visible = false;
            preparedFrame = -1;
            CurrentColor = Color.white;
        }

        /// <summary>Menstruation's CompProperties_Breast.DefaultBlacknippleColor (55, 20, 0).</summary>
        private static readonly Color DefaultDarkNipple = new Color(55f / 255f, 20f / 255f, 0f);

        private static PropertyInfo darkColorProperty;

        /// <summary>
        /// The per-pawn nipple depth (0..1) Menstruation keeps, and the colour it deepens
        /// towards. False when absent.
        /// <c>HediffComp_Breast.Alpha</c> = baseAlpha + pregnancy/nursing progress x delta.
        /// The colour is on the comp's <c>props</c>
        /// (<c>CompProperties_Breast.BlackNippleColor</c>) and differs per breast HediffDef;
        /// if it cannot be read we use their default.
        /// </summary>
        private static bool TryReadMenstruationAlpha(Hediff hediff, out float alpha, out Color dark)
        {
            alpha = FallbackAlpha;
            dark = DefaultDarkNipple;
            if (!resolved)
            {
                resolved = true;
                breastCompType = GenTypes.GetTypeInAnyAssembly(BreastCompTypeName);
                if (breastCompType != null)
                {
                    alphaProperty = breastCompType.GetProperty(
                        "Alpha", BindingFlags.Instance | BindingFlags.Public);
                }
                Type propsType = GenTypes.GetTypeInAnyAssembly(BreastPropsTypeName);
                if (propsType != null)
                {
                    darkColorProperty = propsType.GetProperty(
                        "BlackNippleColor", BindingFlags.Instance | BindingFlags.Public);
                    if (darkColorProperty != null && darkColorProperty.PropertyType != typeof(Color))
                    {
                        darkColorProperty = null;
                    }
                }
            }
            if (breastCompType == null || alphaProperty == null)
            {
                return false;
            }

            HediffWithComps withComps = hediff as HediffWithComps;
            if (withComps == null || withComps.comps == null)
            {
                return false;
            }
            for (int i = 0; i < withComps.comps.Count; i++)
            {
                HediffComp comp = withComps.comps[i];
                if (comp == null || !breastCompType.IsInstanceOfType(comp))
                {
                    continue;
                }
                try
                {
                    object v = alphaProperty.GetValue(comp, null);
                    if (!(v is float))
                    {
                        return false;
                    }
                    alpha = (float)v;
                    // Their own comment: props can be null when RJW moves the chest around,
                    // on animals for instance.
                    if (darkColorProperty != null && comp.props != null
                        && darkColorProperty.DeclaringType.IsInstanceOfType(comp.props))
                    {
                        dark = (Color)darkColorProperty.GetValue(comp.props, null);
                    }
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        /// <summary>The same handling as Menstruation's <c>Utility.SafeSkinColor</c>.</summary>
        private static Color SkinColorOf(Pawn pawn)
        {
            try
            {
                if (pawn != null && pawn.story != null)
                {
                    return pawn.story.SkinColor;
                }
            }
            catch (NullReferenceException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            return Color.white;
        }

        // --- A port of Menstruation's Colors.CMYKLerp ----------------------------
        // Converts to CMYK and interpolates per channel. Unlike RGB interpolation it does not
        // go muddy on the way down, so skin -> deep nipple colour reads naturally.
        private static Color CMYKLerp(Color a, Color b, float t)
        {
            float ac, am, ay, ak, bc, bm, by, bk;
            RGBtoCMYK(a, out ac, out am, out ay, out ak);
            RGBtoCMYK(b, out bc, out bm, out by, out bk);
            return CMYKtoRGB(Mathf.Lerp(ac, bc, t), Mathf.Lerp(am, bm, t),
                             Mathf.Lerp(ay, by, t), Mathf.Lerp(ak, bk, t));
        }

        private static void RGBtoCMYK(Color rgb, out float c, out float m, out float y, out float k)
        {
            k = 1f - Math.Max(rgb.r, Math.Max(rgb.g, rgb.b));
            float d = 1f - k;
            if (d <= 0f)
            {
                // Pure black. Avoids the division by zero where the original yields NaN.
                c = 0f;
                m = 0f;
                y = 0f;
                return;
            }
            c = (1f - rgb.r - k) / d;
            m = (1f - rgb.g - k) / d;
            y = (1f - rgb.b - k) / d;
        }

        private static Color CMYKtoRGB(float c, float m, float y, float k)
        {
            return new Color((1f - c) * (1f - k), (1f - m) * (1f - k), (1f - y) * (1f - k));
        }

        /// <summary>Keeps only the brightness of the colour, to suit NHT's greyscale doll.</summary>
        private static Color ToGray(Color c)
        {
            float l = c.grayscale;
            return new Color(l, l, l, c.a);
        }
    }
}
