using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// We draw only the layers whose colour does not come from the health status.
    ///
    /// Why draw them ourselves: Nice Health Tab tints part textures through <c>GUI.color</c>,
    /// and that colour comes from the part's health status (it **overwrites** ours with
    /// <c>GUI.color = partVisual.Color</c>, so setting a colour beforehand does not work).
    /// Two of our layers carry information in the colour itself.
    ///
    ///   nipples      the nipple colour the pawn actually has
    ///   womb fluid   menstrual blood or cum - <c>GetCumMixtureColor</c> decides
    ///
    /// Where we hook in: a prefix on NHT's part drawing method which, **only for a registered
    /// Def**, draws the part itself and returns false to skip the original. Other parts are
    /// left alone. Click handling, tooltips and the bandage overlay are skipped with it, but
    /// nothing is lost: for both layers the part right underneath (chest / womb) already
    /// carries those.
    ///
    /// NHT has two renderers, chosen by its "legacy tab" setting (UseLegacyTab).
    ///     DollPartRenderer.DrawPart(DollRenderContext, DollBodyPartDef, Doll, Rect, float, bool, Vector2)
    ///     DollDrawer.DrawPart(BodyPartIndexesRemap, DollBodyPartDef, Pawn, Doll, Rect, float, bool, Vector2)
    /// Their argument lists differ, so we walk <c>__args</c> by type and pick out only what we
    /// need. That way a change in NHT's argument order does not break us.
    /// </summary>
    internal static class TintedPartRenderer
    {
        /// <summary>One registered layer. Colour and visibility are asked for on every draw.</summary>
        internal sealed class Layer
        {
            /// <summary>The colour to use this frame.</summary>
            internal Func<Color> Color;

            /// <summary>Whether to draw it this frame.</summary>
            internal Func<bool> Visible;

            /// <summary>
            /// True draws it **in the organ view only** (FilterMode != None).
            /// False draws it in the normal doll view only.
            ///
            /// The original DrawPart makes this decision inside itself; since we intercept
            /// before it, we have to make the same decision here.
            /// </summary>
            internal bool OrganView;

            /// <summary>
            /// The anus window layer. It does not tint anything; it **lays the window
            /// underneath**. After drawing the window, if there is an anus we hand over to the
            /// original DrawPart so NHT draws the anus on top (status colour and clicking
            /// included). With no anus we skip the original, leaving only the window.
            /// </summary>
            internal bool AnusWindow;

            /// <summary>
            /// The anus in the normal view (the surface copy). NHT draws it as usual; we only
            /// drop it in the armour view, which has no anus window - an anus floating without
            /// its window looks wrong.
            /// </summary>
            internal bool AnusGlyph;
        }

        private static readonly Dictionary<Def, Layer> Layers = new Dictionary<Def, Layer>();

        private static Type dollType;
        private static Type contextType;
        private static FieldInfo boundingBoxField;
        private static FieldInfo armorModeField;
        private static Type organType;
        private static FieldInfo filterModeField;
        private static MethodInfo getRectMethod;
        private static bool ready;

        private static readonly Rect FullUv = new Rect(0f, 0f, 1f, 1f);

        internal static void Register(Def def, Layer layer)
        {
            if (def != null && layer != null)
            {
                Layers[def] = layer;
            }
        }

        /// <summary>Puts a prefix on both NHT renderers. The rest of the mod must survive a
        /// failure here.</summary>
        internal static void Install(Type partDefType)
        {
            if (Layers.Count == 0)
            {
                return;
            }

            dollType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.Doll");
            contextType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollRenderContext");
            boundingBoxField = (dollType == null)
                ? null
                : dollType.GetField("BoundingBox", BindingFlags.Instance | BindingFlags.Public);
            organType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollOrgan");
            armorModeField = (contextType == null)
                ? null
                : contextType.GetField("ArmorMode", BindingFlags.Instance | BindingFlags.Public);
            // DollFilterMode: None=0 / Bones=1 / Organs=2
            filterModeField = (contextType == null)
                ? null
                : contextType.GetField("FilterMode", BindingFlags.Instance | BindingFlags.Public);
            getRectMethod = (partDefType == null)
                ? null
                : partDefType.GetMethod("GetRect",
                                        BindingFlags.Instance | BindingFlags.Public,
                                        null,
                                        new[] { typeof(Rect), typeof(Rect), typeof(Vector2) },
                                        null);

            if (dollType == null || boundingBoxField == null || getRectMethod == null)
            {
                Log.Message(Bootstrap.Prefix + "tinted layers disabled - "
                            + "unexpected Nice Health Tab layout.");
                return;
            }

            MethodInfo prefix = typeof(TintedPartRenderer).GetMethod(
                "DrawPartPrefix", BindingFlags.Static | BindingFlags.Public);
            Harmony harmony = new Harmony(Bootstrap.HarmonyId);
            int patched = 0;
            patched += PatchDrawPart(harmony, prefix, "NiceHealthTab.DollPartRenderer");
            patched += PatchDrawPart(harmony, prefix, "NiceHealthTab.DollDrawer");

            if (patched == 0)
            {
                Log.Message(Bootstrap.Prefix + "tinted layers disabled - no doll renderer found.");
                return;
            }
            ready = true;
        }

        private static int PatchDrawPart(Harmony harmony, MethodInfo prefix, string typeName)
        {
            try
            {
                Type t = GenTypes.GetTypeInAnyAssembly(typeName);
                if (t == null)
                {
                    return 0;
                }
                MethodInfo target = AccessTools.Method(t, "DrawPart");
                if (target == null)
                {
                    return 0;
                }
                harmony.Patch(target, new HarmonyMethod(prefix));
                return 1;
            }
            catch (Exception ex)
            {
                Log.Message(Bootstrap.Prefix + "could not hook " + typeName + ".DrawPart ("
                            + ex.GetType().Name + ").");
                return 0;
            }
        }

        /// <summary>
        /// For a registered Def: draw it ourselves and skip the original (false).
        /// For anything else: do nothing (true).
        /// </summary>
        public static bool DrawPartPrefix(object[] __args)
        {
            if (!ready || __args == null)
            {
                return true;
            }

            Def part = null;
            Def anyPart = null;     // The part Def being drawn, ours or not
            Layer layer = null;
            object doll = null;
            Rect rectDoll = default(Rect);
            Vector2 offset = Vector2.zero;
            bool haveRect = false;
            bool haveOffset = false;
            bool armorMode = false;
            bool filtered = false;
            int filterMode = 0;     // DollFilterMode: None=0 / Bones=1 / Organs=2

            for (int i = 0; i < __args.Length; i++)
            {
                object a = __args[i];
                if (a == null)
                {
                    continue;
                }
                // Doll derives from Def too, so it must be filtered before the Def check.
                if (doll == null && dollType.IsInstanceOfType(a))
                {
                    doll = a;
                    continue;
                }
                Def d = a as Def;
                if (d != null)
                {
                    anyPart = d;
                    Layer found;
                    if (Layers.TryGetValue(d, out found))
                    {
                        part = d;
                        layer = found;
                    }
                    continue;
                }
                if (!haveRect && a is Rect)
                {
                    rectDoll = (Rect)a;
                    haveRect = true;
                    continue;
                }
                if (!haveOffset && a is Vector2)
                {
                    offset = (Vector2)a;
                    haveOffset = true;
                    continue;
                }
                if (contextType != null && contextType.IsInstanceOfType(a))
                {
                    if (armorModeField != null)
                    {
                        armorMode = (bool)armorModeField.GetValue(a);
                    }
                    if (filterModeField != null)
                    {
                        object fm = filterModeField.GetValue(a);
                        filterMode = (fm == null) ? 0 : Convert.ToInt32(fm);
                        filtered = filterMode != 0;
                    }
                }
            }

            // The crotch close-up on the RJW panel button keeps outer parts only: no organs
            // (NHT's own included) and no anus (neither the window nor the surface copy). The
            // head is dropped as well - hair and gene overlays are not clipped to the button
            // frame and would print outside it.
            if (PanelRenderer.Cropping && anyPart != null
                && ((organType != null && organType.IsInstanceOfType(anyPart))
                    || (layer != null && (layer.AnusWindow || layer.AnusGlyph))
                    || PanelRenderer.IsHeadPart(doll, anyPart)))
            {
                return false;
            }
            if (part == null)
            {
                return true;        // Not one of our Defs - leave it alone
            }
            if (layer.AnusWindow)
            {
                return AnusWindowPrefix(armorMode, filterMode);
            }
            if (layer.AnusGlyph)
            {
                return !armorMode;
            }
            // One of our Defs, but not drawn this time. Skip the original too.
            if (armorMode || doll == null || !haveRect
                || (layer.OrganView ? !filtered : filtered)
                || !layer.Visible())
            {
                return false;
            }

            try
            {
                Draw(part, doll, rectDoll, offset, layer.Color());
            }
            catch (Exception ex)
            {
                ready = false;      // Runs every frame; one failure switches it off.
                GUI.color = Color.white;
                Log.Warning(Bootstrap.Prefix + "tinted layers disabled after an error: " + ex);
            }
            return false;
        }

        /// <summary>
        /// The anus organ turn. The window itself is laid before the doll is drawn
        /// (PanelRenderer.DrawMainDollPrefix), so here we only decide whether the organ glyph is
        /// handed over to the original.
        /// </summary>
        private static bool AnusWindowPrefix(bool armorMode, int filterMode)
        {
            if (PanelRenderer.Drawing || !AnusWindow.Visible)
            {
                return true;
            }
            // In the normal view the anus is drawn by the surface copy (OuterAnus). Drawing the
            // organ one as well would stack two glyphs in the same spot when it is injured.
            if (filterMode == 0)
            {
                return false;
            }
            // With no anus, skip the original - the default art must not read as a real anus.
            return AnusWindow.ShowGlyph;
        }

        private static void Draw(Def part, object doll, Rect rectDoll, Vector2 offset, Color tint)
        {
            Texture2D tex = (Bootstrap.TexField == null)
                ? null
                : Bootstrap.TexField.GetValue(part) as Texture2D;
            if (tex == null)
            {
                return;
            }

            Rect bbox = (Rect)boundingBoxField.GetValue(doll);
            Rect r = (Rect)getRectMethod.Invoke(part, new object[] { bbox, rectDoll, offset });

            Color before = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(r, tex, FullUv);
            GUI.color = before;
        }
    }
}
