using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// General - linked-mod status, the child body type toggle and the size hediff filter.
    /// Always present as long as this patch is active.
    /// </summary>
    internal sealed class Page_General : SettingsPage
    {
        internal override string Label
        {
            get { return "NHTRJW_Tab_General".Translate(); }
        }

        internal override void Draw(ref float y, Rect area, NHTRJWSettings s)
        {
            // --- Linked mods -------------------------------------------------------
            // So that one glance here tells you why a tab is missing or a row is locked.
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_DepsHeading".Translate());
            // A tooltip per row: what that mod adds to this patch.
            SettingsUI.StatusRow(ref y, area, "Nice Health Tab", ModDeps.NiceHealthTab, true,
                                 "NHTRJW_DepTip_NHT".Translate());
            SettingsUI.StatusRow(ref y, area, "RimJobWorld", ModDeps.Rjw, true,
                                 "NHTRJW_DepTip_RJW".Translate());
            SettingsUI.StatusRow(ref y, area, "RJW Menstruation", ModDeps.Menstruation, false,
                                 "NHTRJW_DepTip_Menstruation".Translate());
            SettingsUI.StatusRow(ref y, area, "RJW Menstruation - Fluids",
                                 ModDeps.MenstruationFluids, false,
                                 "NHTRJW_DepTip_Fluids".Translate());
            SettingsUI.StatusRow(ref y, area, "Sized Apparel", ModDeps.SizedApparel, false,
                                 "NHTRJW_DepTip_SizedApparel".Translate());
            SettingsUI.StatusRow(ref y, area, "RJW Now with balls!", ModDeps.Balls, false,
                                 "NHTRJW_DepTip_Balls".Translate());
            SettingsUI.Gap(ref y, 8f);
            SettingsUI.Info(ref y, area, "NHTRJW_DepsDesc".Translate());
            SettingsUI.Gap(ref y);

            // --- Child body type ---------------------------------------------------
            // Replaces the old minimum-age slider.
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_KidHeading".Translate());
            SettingsUI.Checkbox(ref y, area, "NHTRJW_ShowKidGenitals".Translate(),
                                ref s.showKidGenitals, "NHTRJW_ShowKidGenitalsDesc".Translate());
            SettingsUI.Info(ref y, area, "NHTRJW_ShowKidGenitalsDesc".Translate());
            SettingsUI.Gap(ref y);

            // --- RJW part panel ----------------------------------------------------
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_PanelHeading".Translate());
            SettingsUI.Checkbox(ref y, area, "NHTRJW_ShowPanel".Translate(),
                                ref s.showRjwPanel, "NHTRJW_ShowPanelDesc".Translate());
            SettingsUI.Info(ref y, area, "NHTRJW_ShowPanelDesc".Translate());
            SettingsUI.Gap(ref y);

            // --- Dolls other mods build --------------------------------------------
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_LendHeading".Translate());
            SettingsUI.Checkbox(ref y, area, "NHTRJW_LendParts".Translate(),
                                ref s.lendPartsToOtherDolls, "NHTRJW_LendPartsDesc".Translate());
            SettingsUI.Info(ref y, area, "NHTRJW_LendPartsDesc".Translate());
            SettingsUI.Gap(ref y);

            // --- Size hediffs ------------------------------------------------------
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_SizeHediffHeading".Translate());
            bool before = s.hideSizeOnlyHediffs;
            SettingsUI.Checkbox(ref y, area, "NHTRJW_HideSizeHediffLabel".Translate(),
                                ref s.hideSizeOnlyHediffs);
            if (before != s.hideSizeOnlyHediffs)
            {
                SizeHediffFilter.ApplySetting(s.hideSizeOnlyHediffs);
            }
            SettingsUI.Info(ref y, area, "NHTRJW_HideSizeHediffDesc".Translate());
        }
    }

    /// <summary>
    /// Chest and nipples - one switch per feature.
    /// Each row needs a different mod; rows that rely on a missing mod are disabled.
    /// </summary>
    internal sealed class Page_Nipples : SettingsPage
    {
        internal override string Label
        {
            get { return "NHTRJW_Tab_Nipples".Translate(); }
        }

        internal override void Draw(ref float y, Rect area, NHTRJWSettings s)
        {
            // --- Chest --------------------------------------------------------------
            // Comes before the nipples: turning the chest off takes the nipples with it.
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_ChestHeading".Translate());
            SettingsUI.Checkbox(ref y, area, "NHTRJW_ShowMaleChest".Translate(),
                                ref s.showMaleChest, "NHTRJW_ShowMaleChestDesc".Translate());
            SettingsUI.Info(ref y, area, "NHTRJW_ShowMaleChestDesc".Translate());
            SettingsUI.Gap(ref y);

            SettingsUI.SectionHeader(ref y, area, "NHTRJW_NippleHeading".Translate());

            SettingsUI.Checkbox(ref y, area, "NHTRJW_ShowNipples".Translate(),
                                ref s.showNipples, "NHTRJW_ShowNipplesDesc".Translate());

            // With the nipples off entirely, the rows below mean nothing.
            bool on = s.showNipples;
            const float Indent = 24f;

            SettingsUI.GatedCheckbox(ref y, area, "NHTRJW_UseSkinColor".Translate(),
                                     ref s.useSkinColor, on, null,
                                     "NHTRJW_UseSkinColorDesc".Translate(), Indent);

            SettingsUI.GatedCheckbox(ref y, area, "NHTRJW_UsePigmentation".Translate(),
                                     ref s.usePigmentation,
                                     on && ModDeps.Menstruation,
                                     ModDeps.Menstruation
                                         ? null
                                         : "NHTRJW_NeedsMod".Translate("RJW Menstruation").ToString(),
                                     "NHTRJW_UsePigmentationDesc".Translate(), Indent);

            SettingsUI.GatedCheckbox(ref y, area, "NHTRJW_UseInvertedNipple".Translate(),
                                     ref s.useInvertedNipple,
                                     on && ModDeps.SizedApparel,
                                     ModDeps.SizedApparel
                                         ? null
                                         : "NHTRJW_NeedsMod".Translate("Sized Apparel").ToString(),
                                     "NHTRJW_UseInvertedNippleDesc".Translate(), Indent);

            SettingsUI.GatedCheckbox(ref y, area, "NHTRJW_Monochrome".Translate(),
                                     ref s.monochromeNipples, on, null,
                                     "NHTRJW_MonochromeDesc".Translate(), Indent);

            SettingsUI.Gap(ref y, 6f);
            SettingsUI.Info(ref y, area, "NHTRJW_NippleDesc".Translate());
        }
    }

    /// <summary>
    /// Gonads - a single row, whether to draw testicles on futanari.
    /// The gonads themselves are always drawn (without the balls mod a reference picture is
    /// used instead).
    /// </summary>
    internal sealed class Page_Gonads : SettingsPage
    {
        internal override string Label
        {
            get { return "NHTRJW_Tab_Gonads".Translate(); }
        }

        // Even without the balls mod we draw reference art (Ovaries_5 for ovaries, penis
        // size for testicles), so this row is always usable.
        internal override bool Enabled
        {
            get { return true; }
        }

        internal override void Draw(ref float y, Rect area, NHTRJWSettings s)
        {
            // Only one row. The old "show gonads" switch was dropped on request.
            SettingsUI.SectionHeader(ref y, area, "NHTRJW_GonadsHeading".Translate());
            SettingsUI.Checkbox(ref y, area, "NHTRJW_ShowFutaTesticles".Translate(),
                                ref s.showFutaTesticles,
                                "NHTRJW_ShowFutaTesticlesDesc".Translate());
            SettingsUI.Info(ref y, area, "NHTRJW_ShowFutaTesticlesDesc".Translate());
        }
    }
}
