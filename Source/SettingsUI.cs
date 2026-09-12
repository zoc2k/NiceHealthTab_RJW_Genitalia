using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// Drawing helpers for the settings pages.
    ///
    /// Every helper takes the current y, draws one row and pushes y down. We compute Rects
    /// ourselves instead of using <c>Listing_Standard</c>: inside a scroll view the Listing's
    /// own height maths went wrong and cut the whole lower part off. Holding the row height
    /// ourselves avoids that.
    ///
    /// The layout follows SettingsUI from refs/rimjobworld-onahole-extension.
    /// </summary>
    internal static class SettingsUI
    {
        /// <summary>Base height of every row.</summary>
        internal const float Line = 30f;

        private static readonly Color SeparatorColor = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color InfoColor = new Color(1f, 1f, 1f, 0.6f);
        private static readonly Color DisabledColor = new Color(0.62f, 0.62f, 0.65f);
        private static readonly Color OkColor = new Color(0.55f, 0.85f, 0.6f);
        private static readonly Color MissingColor = new Color(0.85f, 0.55f, 0.55f);

        /// <summary>Section heading plus an underline.</summary>
        internal static void SectionHeader(ref float y, Rect area, string label)
        {
            Text.Font = GameFont.Medium;
            float h = Text.CalcHeight(label, area.width);
            Widgets.Label(new Rect(area.x, y, area.width, h), label);
            Text.Font = GameFont.Small;
            y += h + 2f;
            Separator(area.x, y, area.width);
            y += 8f;
        }

        internal static void Separator(float x, float y, float width)
        {
            GUI.color = SeparatorColor;
            Widgets.DrawLineHorizontal(x, y, width);
            GUI.color = Color.white;
        }

        /// <summary>A dimmed explanatory paragraph, wrapped to the width.</summary>
        internal static void Info(ref float y, Rect area, string text)
        {
            GUI.color = InfoColor;
            Text.Font = GameFont.Tiny;
            float h = Text.CalcHeight(text, area.width);
            Widgets.Label(new Rect(area.x, y, area.width, h), text);
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            y += h + 6f;
        }

        /// <summary>One linked-mod row: green when present; red when a missing mod is
        /// required, grey when it is optional.</summary>
        /// <param name="tooltip">Hover text: what that mod adds to this patch.</param>
        internal static void StatusRow(ref float y, Rect area, string name, bool present, bool required,
                                       string tooltip = null)
        {
            string state = present ? "NHTRJW_DepFound".Translate() : "NHTRJW_DepMissing".Translate();
            string note = required ? "NHTRJW_DepRequired".Translate() : "NHTRJW_DepOptional".Translate();

            // Rows are spaced Line x 0.8, so the hit area is only that tall. A full row
            // height would overlap the next row and show the wrong mod's tooltip.
            Rect hover = new Rect(area.x, y, area.width, Line * 0.8f);
            if (tooltip != null && Mouse.IsOver(hover))
            {
                Widgets.DrawHighlight(hover);
                TooltipHandler.TipRegion(hover, tooltip);
            }

            GUI.color = present ? OkColor : (required ? MissingColor : DisabledColor);
            Widgets.Label(new Rect(area.x + 6f, y, area.width - 6f, Line),
                          name + " - " + state + " (" + note + ")");
            GUI.color = Color.white;
            y += Line * 0.8f;
        }

        /// <summary>A plain checkbox row.</summary>
        internal static void Checkbox(ref float y, Rect area, string label, ref bool value,
                                      string tooltip = null, float indent = 0f)
        {
            Rect row = new Rect(area.x + indent, y, area.width - indent, Line);
            if (tooltip != null && Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
                TooltipHandler.TipRegion(row, tooltip);
            }
            Widgets.CheckboxLabeled(row, label, ref value);
            y += Line;
        }

        /// <summary>
        /// A conditional checkbox row.
        ///
        /// When <paramref name="available"/> is false the row is greyed out and cannot be
        /// clicked. The saved value is left alone - it must come back if the mod returns.
        /// </summary>
        internal static void GatedCheckbox(ref float y, Rect area, string label, ref bool value,
                                           bool available, string missingNote, string tooltip,
                                           float indent = 0f)
        {
            Rect row = new Rect(area.x + indent, y, area.width - indent, Line);
            string text = label;
            string tip = tooltip;
            if (!available && missingNote != null)
            {
                text = label + "   (" + missingNote + ")";
                tip = tooltip + "\n\n" + missingNote;
            }

            if (Mouse.IsOver(row))
            {
                Widgets.DrawHighlight(row);
                if (!tip.NullOrEmpty())
                {
                    TooltipHandler.TipRegion(row, tip);
                }
            }

            if (available)
            {
                Widgets.CheckboxLabeled(row, text, ref value);
            }
            else
            {
                // Pass a copy. The disabled flag already blocks input, but keeping it away
                // from the original makes sure the setting cannot change even by accident.
                bool copy = value;
                GUI.color = DisabledColor;
                Widgets.CheckboxLabeled(row, text, ref copy, true);
                GUI.color = Color.white;
            }
            y += Line;
        }

        /// <summary>A label row plus a slider row. Returns the rounded integer.</summary>
        internal static int IntSlider(ref float y, Rect area, string label, int value, int min, int max)
        {
            Widgets.Label(new Rect(area.x, y, area.width, Line), label);
            y += Line;
            float v = Widgets.HorizontalSlider(new Rect(area.x, y, area.width, 22f),
                                               value, min, max);
            y += 26f;
            return Mathf.RoundToInt(v);
        }

        internal static bool Button(ref float y, Rect area, string label, float width = 260f)
        {
            bool hit = Widgets.ButtonText(new Rect(area.x, y, Mathf.Min(width, area.width), Line), label);
            y += Line + 4f;
            return hit;
        }

        internal static void Gap(ref float y, float height = 10f)
        {
            y += height;
        }
    }
}
