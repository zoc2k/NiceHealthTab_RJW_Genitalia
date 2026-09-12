using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    public class NHTRJWSettings : ModSettings
    {
        /// <summary>
        /// Whether to draw this patch's markers on the child body type (NHT's Kid doll).
        /// Off by default.
        ///
        /// This used to be a "minimum age for sensitive parts" slider (20 by default). On
        /// request it became a toggle that goes by the **doll**, not by age. The old age value
        /// left in the settings file is never read, so it is simply ignored.
        /// </summary>
        public bool showKidGenitals;

        /// <summary>Whether to leave plain size hediffs out of the doll condition check.</summary>
        public bool hideSizeOnlyHediffs = true;

        // --- Nipples --------------------------------------------------------------
        // This used to be a choice between four presets - RJW Advanced / Classic / NHT Minor
        // / NHT Classic. Each preset mixed features that needed different mods, so without one
        // of those mods the whole preset misbehaved.
        // Now there is one switch per feature.

        /// <summary>Whether to draw the nipple layer. Off shows breasts only.</summary>
        public bool showNipples = true;

        /// <summary>Whether to pull the nipple colour from the pawn's skin. Off pins it to
        /// the reference colour.</summary>
        public bool useSkinColor = true;

        /// <summary>Whether to follow areola pigmentation. Needs RJW Menstruation.</summary>
        public bool usePigmentation = true;

        /// <summary>Whether inverted nipples get their own art. Needs Sized Apparel.</summary>
        public bool useInvertedNipple = true;

        /// <summary>Whether to draw the nipples in black and white.</summary>
        public bool monochromeNipples;

        // --- Chest ----------------------------------------------------------------
        /// <summary>
        /// Whether to draw the chest (breasts plus nipples) on pawns whose gender is male.
        ///
        /// RJW gives every pawn a Breasts hediff, so left alone the marker shows on male pawns
        /// too. Off by default - most playthroughs do not want that picture. Only the marker is
        /// hidden; the body part and its hediffs stay.
        /// </summary>
        public bool showMaleChest;

        /// <summary>
        /// The RJW part panel. Adds one more button next to the hand and foot strip; pressing
        /// it lays our parts out enlarged in the same place.
        /// </summary>
        public bool showRjwPanel = true;

        // --- Gonads ---------------------------------------------------------------
        /// <summary>
        /// Whether to draw testicles on a pawn that has **both** a penis and a vagina (futa).
        ///
        /// Since the ovaries got their own slot, a futa shows both ovaries and testicles in
        /// the organ view. Some playthroughs do not want that, hence this switch. Off by
        /// default - then only the ovaries remain (they ignore this switch).
        ///
        /// It applies to both testicle slots: the Gonads organ and the OuterGonads surface copy.
        ///
        /// The **only** row on the gonads tab. The old "show gonads" switch (showGonads) was
        /// dropped on request - gonads are always drawn now. The old saved value is never read,
        /// so it is simply ignored.
        /// </summary>
        public bool showFutaTesticles;

        // --- Effective values, filtered for missing mods ---------------------------
        // Read through these rather than using the raw setting. Removing a mod leaves the
        // saved setting in place (so it comes back with the mod), which means the reading side
        // has to filter every time.
        public bool EffectivePigmentation
        {
            get { return usePigmentation && ModDeps.Menstruation; }
        }

        public bool EffectiveInvertedNipple
        {
            get { return useInvertedNipple && ModDeps.SizedApparel; }
        }

        private int legacyNippleStyle = -1;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref showKidGenitals, "showKidGenitals", false);
            Scribe_Values.Look(ref hideSizeOnlyHediffs, "hideSizeOnlyHediffs", true);

            Scribe_Values.Look(ref showNipples, "showNipples", true);
            Scribe_Values.Look(ref useSkinColor, "useSkinColor", true);
            Scribe_Values.Look(ref usePigmentation, "usePigmentation", true);
            Scribe_Values.Look(ref useInvertedNipple, "useInvertedNipple", true);
            Scribe_Values.Look(ref monochromeNipples, "monochromeNipples", false);
            Scribe_Values.Look(ref showMaleChest, "showMaleChest", false);
            Scribe_Values.Look(ref showFutaTesticles, "showFutaTesticles", false);
            Scribe_Values.Look(ref showRjwPanel, "showRjwPanel", true);

            // One-time migration for users of the old four-preset setting.
            //   0 RJWAdvanced / 1 RJWClassic / 2 NHTMinor / 3 NHTClassic
            Scribe_Values.Look(ref legacyNippleStyle, "nippleStyle", -1);
            if (Scribe.mode == LoadSaveMode.LoadingVars && legacyNippleStyle >= 0)
            {
                showNipples = legacyNippleStyle != 3;
                useSkinColor = legacyNippleStyle != 1;
                monochromeNipples = legacyNippleStyle == 2;
                legacyNippleStyle = -1;
            }
        }

        public static NHTRJWSettings Current
        {
            get
            {
                NHTRJWSettings s = NHTRJWMod.Settings;
                return s ?? (NHTRJWMod.Fallback ?? (NHTRJWMod.Fallback = new NHTRJWSettings()));
            }
        }
    }

    public class NHTRJWMod : Mod
    {
        public static NHTRJWSettings Settings;

        /// <summary>Fallback for access before the Mod instance exists.</summary>
        public static NHTRJWSettings Fallback;

        private readonly SettingsTabs tabs;

        public NHTRJWMod(ModContentPack content)
            : base(content)
        {
            Settings = GetSettings<NHTRJWSettings>();
            tabs = new SettingsTabs(new List<SettingsPage>
            {
                new Page_General(),
                new Page_Nipples(),
                new Page_Gonads(),
            });
        }

        public override string SettingsCategory()
        {
            return "NHTRJW_ModTitle".Translate();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            tabs.Draw(inRect, Settings ?? NHTRJWSettings.Current);
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            if (Settings != null)
            {
                SizeHediffFilter.ApplySetting(Settings.hideSizeOnlyHediffs);
            }
        }
    }
}
