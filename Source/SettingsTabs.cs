using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// One tab page of the settings window.
    ///
    /// When <see cref="Enabled"/> is false the tab does not appear at all. If a whole feature
    /// only makes sense with a given mod, locking the page is better than disabling each of
    /// its rows one by one. As features grow, only pages need to be added.
    ///
    /// The structure follows SettingsWindowBase / TabManager from
    /// refs/rimjobworld-onahole-extension. That one saves values per page through [Expose]
    /// reflection; this mod has few settings, so every value lives in one place,
    /// <see cref="NHTRJWSettings"/>.
    /// </summary>
    internal abstract class SettingsPage
    {
        /// <summary>The name shown on the tab.</summary>
        internal abstract string Label { get; }

        /// <summary>Whether this page can be shown. False when a required mod is missing.</summary>
        internal virtual bool Enabled
        {
            get { return true; }
        }

        /// <summary>
        /// Draws the contents, pushing <paramref name="y"/> down as it goes; the final y is
        /// the scroll height.
        /// </summary>
        internal abstract void Draw(ref float y, Rect area, NHTRJWSettings s);
    }

    /// <summary>
    /// The tab strip. Draws a tab only for enabled pages and puts the chosen page's contents
    /// into a scroll view.
    ///
    /// The tabs are rebuilt when a page's <c>Enabled</c> or the language changes. The mod list
    /// cannot change while the game runs, but the language can change with the settings window
    /// still open.
    /// </summary>
    internal sealed class SettingsTabs
    {
        private readonly List<SettingsPage> pages;
        private readonly List<TabRecord> tabs = new List<TabRecord>();
        private readonly List<SettingsPage> shown = new List<SettingsPage>();

        private int selected;
        private LoadedLanguage builtFor;
        private bool[] builtEnabled;

        private Vector2 scroll = Vector2.zero;
        private float contentHeight = 400f;

        internal SettingsTabs(List<SettingsPage> pages)
        {
            this.pages = pages;
        }

        internal void Draw(Rect inRect, NHTRJWSettings s)
        {
            RebuildIfNeeded();
            if (shown.Count == 0)
            {
                Widgets.Label(inRect, "NHTRJW_NoPages".Translate());
                return;
            }
            if (selected >= shown.Count)
            {
                selected = 0;
            }
            for (int i = 0; i < tabs.Count; i++)
            {
                tabs[i].selected = i == selected;
            }

            // The tab strip is drawn over the window's top margin. It needs to sit a little
            // lower so it does not overlap the title.
            const float TabHeight = 30f;
            Rect tabRect = new Rect(inRect.x, inRect.y + TabHeight, inRect.width, TabHeight);
            TabDrawer.DrawTabs(tabRect, tabs);

            Rect body = new Rect(inRect.x, tabRect.yMax + 8f,
                                 inRect.width, inRect.height - TabHeight * 2f - 8f);

            Rect view = new Rect(0f, 0f, body.width - 20f, contentHeight);
            Widgets.BeginScrollView(body, ref scroll, view);
            try
            {
                Rect area = new Rect(4f, 0f, view.width - 8f, view.height);
                float y = 4f;
                shown[selected].Draw(ref y, area, s);
                contentHeight = Mathf.Max(y + 12f, 40f);
            }
            catch (Exception ex)
            {
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                Log.ErrorOnce(Bootstrap.Prefix + "settings page '" + shown[selected].Label
                              + "' failed: " + ex, 0x4E48_5401);
            }
            finally
            {
                // Always keep the pair balanced. If it slips, UI outside this window breaks too.
                Widgets.EndScrollView();
            }
        }

        private void RebuildIfNeeded()
        {
            bool stale = builtEnabled == null
                         || builtEnabled.Length != pages.Count
                         || builtFor != LanguageDatabase.activeLanguage;
            if (!stale)
            {
                for (int i = 0; i < pages.Count; i++)
                {
                    if (builtEnabled[i] != pages[i].Enabled)
                    {
                        stale = true;
                        break;
                    }
                }
            }
            if (!stale)
            {
                return;
            }

            builtEnabled = new bool[pages.Count];
            for (int i = 0; i < pages.Count; i++)
            {
                builtEnabled[i] = pages[i].Enabled;
            }
            builtFor = LanguageDatabase.activeLanguage;

            tabs.Clear();
            shown.Clear();
            foreach (SettingsPage page in pages)
            {
                if (!page.Enabled)
                {
                    continue;
                }
                int index = shown.Count;       // Capture by value; never capture the loop variable.
                shown.Add(page);
                tabs.Add(new TabRecord(page.Label, delegate { selected = index; }, false));
            }
        }
    }
}
