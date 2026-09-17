using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// The RJW part panel: a vertical panel that opens over the doll.
    ///
    /// Why it exists
    ///   Markers on the main doll have to sit at anatomically correct spots, which keeps them
    ///   small. The anus in particular is 19px on screen, too small to read its state. This panel
    ///   gives up the anatomical positions and lays the parts out one under another, leaving room
    ///   on the right for labels and gauges so states such as pregnancy or the cycle can be shown
    ///   alongside later.
    ///
    /// Where it attaches
    ///   NHT's hand and foot button row is **left alone**. An earlier version squeezed a third
    ///   button into that row, which pushed the hand and foot buttons left and covered the vanilla
    ///   toggle button so it could not be clicked. Now the button sits on its own at the lower
    ///   right of the doll column, and the panel opens over the doll.
    ///
    /// How it draws
    ///   **No new part Defs are created.** We borrow the very Defs the main doll uses, change
    ///   only position / width / height to panel coordinates right before drawing, and put them
    ///   back afterwards. Textures, tiers, status colours and click targets therefore match the
    ///   main doll automatically. The panel coordinates are the values gen_defs.py measured from
    ///   the glyphs and stored on DollPartFormDef.
    ///
    ///   The main doll is drawn **before** this panel (DrawMainDoll -> DrawButtons ->
    ///   DrawOverlay), so restoring the values immediately after drawing keeps the two from
    ///   colliding within one frame.
    ///
    /// The NHT assembly is never hard-referenced. If any piece cannot be found, this switches
    /// itself off quietly.
    /// </summary>
    internal static class PanelRenderer
    {
        // ------------------------------------------------------------------ Layout
        // Every value here was measured from docs/img/guide/panel_ui/grid_edit.png, drawn by hand
        // on a template that reproduces the in-game doll column pixel for pixel (UI scale 1.5,
        // 320x633). The values are written as **template pixels** and divided by TemplateScale
        // when used, to get UI units. Re-measuring on the template means swapping numbers only.

        /// <summary>One template pixel = 1 / 1.5 UI units.</summary>
        private const float TemplateScale = 1.5f;

        /// <summary>
        /// The area NHT gives the doll, in UI units (DollWidget.Draw: the column is 213.4 wide and
        /// the doll takes 90% of the tab's inner height). gen_defs.DOLL_RECT holds the same pair,
        /// and validate.py cross-checks them - the anus window placement is worked out from it.
        /// </summary>
        private static readonly Vector2 DollRectUnits = new Vector2(213.40001f, (430f - 8f) * 0.9f);

        /// <summary>The panel, from the doll column's top left. It fills the column's width.</summary>
        private static readonly Rect PanelPx = new Rect(0f, 103f, 320f, 380f);

        /// <summary>The button that opens and closes the panel, from the doll column's top left:
        /// outside the panel, just above the hand and foot row.</summary>
        private static readonly Rect ButtonPx = new Rect(256f, 491f, 56f, 71f);

        /// <summary>
        /// The main doll's anus window, from the doll column's top left. It is at **the same
        /// place** as the panel's anus window (<see cref="AnusBox"/>), so opening the panel makes
        /// the two coincide exactly (validate.py cross-checks this).
        /// </summary>
        private static readonly Rect AnusWindowPx = new Rect(256f, 404f, 56f, 71f);

        /// <summary>Whether the panel is drawing parts. While it is, the main doll's anus window
        /// is not drawn separately.</summary>
        internal static bool Drawing;

        // The rect the main doll was drawn in this frame; the anus window is placed from it.
        private static Rect lastDollRect;
        private static int lastDollFrame = -1;

        /// <summary>
        /// The panel coordinate system: **panel pixels as they are**, with the origin at the
        /// centre. It must match PANEL_BOX in gen_defs.py. Panel textures drawn at this size line
        /// up without any coordinate maths.
        /// </summary>
        private static readonly Rect PanelBox = new Rect(-160f, -190f, 320f, 380f);

        /// <summary>The genitals box: outline plus a per-body-type background texture.
        /// gen_defs.PANEL_REPRO_BOX.</summary>
        private static readonly Rect ReproBox = new Rect(-152f, -183f, 304f, 278f);

        /// <summary>The anus window: outline plus the buttocks background (Anus_BG).
        /// gen_defs.PANEL_ANUS_BOX.</summary>
        private static readonly Rect AnusBox = new Rect(96f, 111f, 56f, 71f);

        /// <summary>Ovulation / fertilization / implantation, using menstruation's own picture.
        /// No outline.</summary>
        private static readonly Rect EggBox = new Rect(-152f, 111f, 71f, 71f);

        private const string PanelTexRoot = "HediffTab/RJW_Panel/";

        /// <summary>
        /// Draw order. Breasts and nipples are left out - they already show large on the doll
        /// behind. The belly is the backmost layer, then the gonads (womb, ovaries), with the
        /// internal and outer genitals on top.
        /// </summary>
        private static readonly string[] Slots =
        {
            // The belly is drawn first, underneath: it is the body wall, and the womb and its
            // contents belong in front of it. It is laid with the genitals group (same transform),
            // so it lines up anatomically; what reaches past the top of the genitals box is cut
            // off there.
            Bootstrap.BellySlot,
            Bootstrap.OvariesSlot, Bootstrap.GonadsSlot, Bootstrap.GenitalsSlot,
            Bootstrap.OuterGenitalsSlot,
            // The womb and the fluid inside it. The doll's layer order (30 < 31) is kept here
            // too; the womb glyph has a hole for the ovaries, so they show even when drawn over.
            Bootstrap.WombSlot, Bootstrap.WombFluidSlot,
            Bootstrap.AnusSlot,
        };

        private const float DollFraction = 0.9f;    // dollRect = 90% of innerRect's height

        // --- The same trim as NHT's hand and foot panel (DollOverlay.Draw) --------
        //   ColorBGLD ground, 36 of shadow above and below, 64 at the sides, and a half-rhombus
        //   pointing at the button.
        private const float BandShadow = 36f;
        private const float SideShadow = 64f;
        private static readonly Color ShadowDeep = new Color(0f, 0f, 0f, 0.3f);      // MilkyWayAssets.ColorShadowDeep
        private static readonly Color ShadowHalfDeep = new Color(0f, 0f, 0f, 0.45f); // Assets.ColorShadowHalfDeep
        private static Texture2D vGradient;
        private static Texture2D hGradient;
        private static Texture2D halfRomb;

        // --- Panel textures (a missing one drops only that piece) -----------------
        private static Texture2D anusBgTex;

        // --- The button: a crotch close-up of the current pawn --------------------
        // The frame is the glyph area of the **outer genitals visible** on that pawn (penis,
        // vulva, surface testicles). Its height is padded, and a fraction of the doll's height is
        // kept as a minimum so nothing is blown up too far. The width follows the button's aspect.
        private const float CropPad = 1.5f;
        private const float CropMinFraction = 0.1f;     // Relative to the doll BoundingBox height
        private static MethodInfo drawDollMethod;
        private static FieldInfo dollBoundsField;
        private static FieldInfo armorField;

        /// <summary>Whether the crotch is being drawn on the button. While it is, organs and the
        /// anus are skipped, leaving outer parts only.</summary>
        internal static bool Cropping;

        // --- Overlays that leak outside the button -------------------------------
        // NHT draws scars, bandages and the head overlays (hair, genes) through
        // Graphics.DrawTexture with a Material. That call is not clipped by GUI.BeginClip, so the
        // scars and bandages of the enlarged doll print on the screen outside the button. While
        // the button is being drawn we therefore:
        //   scars    - swap HediffCache.HediffDictPermanent for an empty dictionary, so HasScars
        //              is false.
        //   bandages - swap DollRenderContext.PartFrame for a copy whose Tend is 0.
        //   head     - skip the head part entirely (<see cref="IsHeadPart"/>); the head never
        //              falls inside the crotch frame anyway.
        // Why Harmony is not used on HasScars: its body is one line, so Mono may inline it into
        // DrawPart and the patch would never run. Swapping a field has no such worry.
        private static FieldInfo hediffsField;      // DollRenderContext.Hediffs
        private static FieldInfo scarsField;        // HediffCache.HediffDictPermanent
        private static FieldInfo partFrameField;    // DollRenderContext.PartFrame
        private static FieldInfo tendField;         // PartFrameInfo.Tend
        private static FieldInfo headPartField;     // HumanlikeDoll.headPart
        private static object noScars;
        private static IDictionary noTend;
        private static readonly object Zero = 0;
        private static bool overlayGuardsBroken;
        private static readonly Dictionary<string, Texture2D> backgrounds =
            new Dictionary<string, Texture2D>();
        private static FieldInfo pawnField;

        private static Type widgetType;
        private static Type contextType;
        private static Type organType;
        private static FieldInfo ctxField;
        private static FieldInfo overlayModeField;
        private static FieldInfo filterModeField;
        private static FieldInfo blockField;
        private static FieldInfo mainDollField;
        private static FieldInfo ctxArmorField;
        private static bool windowNoticeLogged;
        private static MethodInfo fitMethod;
        private static MethodInfo drawPartMethod;
        private static object panelDoll;
        private static Color bgColor = new Color(0.118f, 0.129f, 0.141f);

        /// <summary>The widget the panel is currently open on. There can be several widgets, so we
        /// hold the instance.</summary>
        private static object openFor;

        private static bool ready;

        // ------------------------------------------------------------------ Install
        internal static bool Install(Type partDefType)
        {
            widgetType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollWidget");
            contextType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollRenderContext");
            organType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollOrgan");
            Type dollType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.Doll");
            Type rendererType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollPartRenderer");
            if (widgetType == null || contextType == null || dollType == null
                || rendererType == null || partDefType == null)
            {
                return false;
            }

            ctxField = AccessTools.Field(widgetType, "ctx");
            overlayModeField = AccessTools.Field(widgetType, "overlayMode");
            filterModeField = AccessTools.Field(contextType, "FilterMode");
            ctxArmorField = AccessTools.Field(contextType, "ArmorMode");
            blockField = AccessTools.Field(contextType, "BlockOverlay");
            mainDollField = AccessTools.Field(contextType, "MainDoll");
            pawnField = AccessTools.Field(contextType, "Pawn");

            // Trim from NHT's hand and foot panel. If not found, only the trim is missing.
            vGradient = StaticTex("MilkyWay.MilkyWayAssets", "VGradientTex");
            hGradient = StaticTex("MilkyWay.MilkyWayAssets", "HGradientTex");
            halfRomb = StaticTex("NiceHealthTab.Assets", "HalfRombIcon");

            // Our own panel textures.
            anusBgTex = ContentFinder<Texture2D>.Get(PanelTexRoot + "Anus/Anus_BG", false);

            // The button's crotch picture borrows NHT's doll drawing as it is - the same route
            // the hand and foot buttons take.
            Type dollRendererType = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.DollRenderer");
            drawDollMethod = (dollRendererType == null) ? null : AccessTools.Method(
                dollRendererType, "DrawDoll",
                new[] { contextType, dollType, typeof(Rect), typeof(bool), typeof(bool) });
            dollBoundsField = AccessTools.Field(dollType, "BoundingBox");
            armorField = AccessTools.Field(contextType, "ArmorMode");
            InstallOverlayGuards();
            fitMethod = AccessTools.Method(dollType, "Fit");
            drawPartMethod = AccessTools.Method(rendererType, "DrawPart");

            if (ctxField == null || overlayModeField == null || filterModeField == null
                || mainDollField == null || fitMethod == null || drawPartMethod == null)
            {
                Log.Message(Bootstrap.Prefix + "RJW panel disabled - unexpected Nice Health Tab layout.");
                return false;
            }

            // The panel's Doll is not created as a Def: a BoundingBox is all it needs, and
            // registering it as a Def would also demand an outlinePath texture.
            try
            {
                panelDoll = Activator.CreateInstance(dollType);
                AccessTools.Field(dollType, "BoundingBox").SetValue(panelDoll, PanelBox);
                FieldInfo sc = AccessTools.Field(dollType, "scale");
                if (sc != null)
                {
                    sc.SetValue(panelDoll, 1f);
                }
            }
            catch (Exception ex)
            {
                Log.Message(Bootstrap.Prefix + "RJW panel disabled - could not build the panel doll ("
                            + ex.GetType().Name + ").");
                return false;
            }

            Type assets = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.Assets");
            FieldInfo bg = (assets == null) ? null : AccessTools.Field(assets, "ColorBGLD");
            if (bg != null && bg.FieldType == typeof(Color))
            {
                bgColor = (Color)bg.GetValue(null);
            }

            Harmony harmony = new Harmony(Bootstrap.HarmonyId);
            int n = 0;
            n += Patch(harmony, "DrawOverlay", "DrawOverlayPostfix", prefix: false);
            n += Patch(harmony, "DrawMainDoll", "DrawMainDollPrefix", prefix: true);
            if (n < 2)
            {
                Log.Message(Bootstrap.Prefix + "RJW panel disabled - only " + n + "/2 hooks installed.");
                return false;
            }

            ready = true;
            return true;
        }

        /// <summary>The fields used to stop overlays leaking outside the button. If one cannot be
        /// found, only that guard is missing.</summary>
        private static void InstallOverlayGuards()
        {
            hediffsField = AccessTools.Field(contextType, "Hediffs");
            scarsField = (hediffsField == null) ? null
                : AccessTools.Field(hediffsField.FieldType, "HediffDictPermanent");
            if (scarsField != null && !typeof(IDictionary).IsAssignableFrom(scarsField.FieldType))
            {
                scarsField = null;
            }

            partFrameField = AccessTools.Field(contextType, "PartFrame");
            Type frameType = partFrameField == null ? null : partFrameField.FieldType;
            if (frameType != null && frameType.IsGenericType
                && typeof(IDictionary).IsAssignableFrom(frameType)
                && frameType.GetGenericArguments().Length == 2)
            {
                tendField = AccessTools.Field(frameType.GetGenericArguments()[1], "Tend");
            }
            if (tendField == null || tendField.FieldType != typeof(int))
            {
                partFrameField = null;
                tendField = null;
            }

            Type humanlike = GenTypes.GetTypeInAnyAssembly("NiceHealthTab.HumanlikeDoll");
            headPartField = (humanlike == null) ? null : AccessTools.Field(humanlike, "headPart");

            if (scarsField == null || partFrameField == null || headPartField == null)
            {
                Log.Message(Bootstrap.Prefix + "RJW panel button - some Nice Health Tab overlays may "
                            + "show outside the button (scars " + (scarsField != null)
                            + ", bandages " + (partFrameField != null)
                            + ", head " + (headPartField != null) + ").");
            }
        }

        /// <summary>While drawing the button: is this part the doll's head? (Head overlays leak
        /// outside the button.)</summary>
        internal static bool IsHeadPart(object doll, Def part)
        {
            return headPartField != null && doll != null && part != null
                && headPartField.DeclaringType.IsInstanceOfType(doll)
                && ReferenceEquals(headPartField.GetValue(doll), part);
        }

        /// <summary>Swaps the scar dictionary and the part info for the button. Restored by
        /// <see cref="RestoreOverlays"/>.</summary>
        private static void SuppressOverlays(object ctx, ref object hediffs, ref object scars, ref object frame)
        {
            if (overlayGuardsBroken)
            {
                return;
            }
            try
            {
                SwapOverlays(ctx, ref hediffs, ref scars, ref frame);
            }
            catch (Exception ex)
            {
                // This runs every frame. After one failure we switch only the guard off and keep
                // drawing the button. Whatever was swapped is put back by RestoreOverlays in the
                // finally block (the fields themselves are left alone).
                overlayGuardsBroken = true;
                Log.Warning(Bootstrap.Prefix + "RJW panel button overlay guard disabled after an error: " + ex);
            }
        }

        private static void SwapOverlays(object ctx, ref object hediffs, ref object scars, ref object frame)
        {
            if (scarsField != null)
            {
                object cache = hediffsField.GetValue(ctx);
                if (cache != null)
                {
                    if (noScars == null)
                    {
                        noScars = Activator.CreateInstance(scarsField.FieldType, true);
                    }
                    ((IDictionary)noScars).Clear();
                    scars = scarsField.GetValue(cache);
                    hediffs = cache;
                    scarsField.SetValue(cache, noScars);
                }
            }

            if (partFrameField != null)
            {
                IDictionary src = partFrameField.GetValue(ctx) as IDictionary;
                if (src != null)
                {
                    if (noTend == null)
                    {
                        noTend = (IDictionary)Activator.CreateInstance(partFrameField.FieldType);
                    }
                    noTend.Clear();
                    // We are in the same frame that just drew the main doll, so every part of the
                    // doll is already in there.
                    foreach (DictionaryEntry e in src)
                    {
                        object v = e.Value;             // A boxed copy of the struct; the original
                                                        // is untouched
                        tendField.SetValue(v, Zero);
                        noTend[e.Key] = v;
                    }
                    frame = src;
                    partFrameField.SetValue(ctx, noTend);
                }
            }
        }

        private static void RestoreOverlays(object ctx, object hediffs, object scars, object frame)
        {
            if (hediffs != null)
            {
                scarsField.SetValue(hediffs, scars);
            }
            if (frame != null)
            {
                partFrameField.SetValue(ctx, frame);
            }
        }

        private static int Patch(Harmony harmony, string target, string ours, bool prefix)
        {
            try
            {
                MethodInfo m = AccessTools.Method(widgetType, target);
                if (m == null)
                {
                    return 0;
                }
                HarmonyMethod hm = new HarmonyMethod(typeof(PanelRenderer).GetMethod(
                    ours, BindingFlags.Static | BindingFlags.Public));
                harmony.Patch(m, prefix ? hm : null, prefix ? null : hm);
                return 1;
            }
            catch (Exception ex)
            {
                Log.Message(Bootstrap.Prefix + "could not hook DollWidget." + target
                            + " (" + ex.GetType().Name + ").");
                return 0;
            }
        }

        // ------------------------------------------------------------------ Helpers
        private static bool Enabled(object widget, out object ctx)
        {
            ctx = null;
            if (!ready || !Bootstrap.Ready || !NHTRJWSettings.Current.showRjwPanel)
            {
                return false;
            }
            ctx = ctxField.GetValue(widget);
            if (ctx == null)
            {
                return false;
            }
            // A doll none of our parts belongs to (another mod built it for this race) would give
            // an empty panel, so we show neither it nor its button.
            Def doll = (mainDollField == null) ? null : mainDollField.GetValue(ctx) as Def;
            return doll != null && Bootstrap.HasPartsFor(doll.defName);     // lent parts count
        }

        /// <summary>Works the widget's inner rect back out of the doll rect; dollRect is 90% of
        /// its height.</summary>
        private static Rect InnerOf(Rect dollRect)
        {
            return new Rect(dollRect.x, dollRect.y,
                            dollRect.width, dollRect.height / DollFraction);
        }

        /// <summary>Template pixels (from the doll column's top left) -> a screen rect.</summary>
        private static Rect FromTemplate(Rect dollRect, Rect px)
        {
            Rect inner = InnerOf(dollRect);
            return new Rect(inner.x + px.x / TemplateScale, inner.y + px.y / TemplateScale,
                            px.width / TemplateScale, px.height / TemplateScale);
        }

        private static Rect PanelOf(Rect dollRect)
        {
            return FromTemplate(dollRect, PanelPx);
        }

        private static Texture2D StaticTex(string typeName, string field)
        {
            Type t = GenTypes.GetTypeInAnyAssembly(typeName);
            FieldInfo f = (t == null) ? null : t.GetField(field, BindingFlags.Static | BindingFlags.Public);
            return (f == null) ? null : f.GetValue(null) as Texture2D;
        }

        private static Texture2D BackgroundFor(string dollName)
        {
            Texture2D tex;
            if (!backgrounds.TryGetValue(dollName, out tex))
            {
                tex = ContentFinder<Texture2D>.Get(PanelTexRoot + "Background/" + dollName, false);
                backgrounds[dollName] = tex;
            }
            return tex;
        }

        /// <summary>Panel coordinates -> screen. rect is the canvas rect Fit returned.</summary>
        private static Rect ToScreen(Rect rect, Rect box)
        {
            float kx = rect.width / PanelBox.width;
            float ky = rect.height / PanelBox.height;
            return new Rect(rect.x + (box.x - PanelBox.x) * kx,
                            rect.y + (box.y - PanelBox.y) * ky,
                            box.width * kx, box.height * ky);
        }

        private static Rect ButtonOf(Rect dollRect)
        {
            return FromTemplate(dollRect, ButtonPx);
        }

        /// <summary>Where the main doll's anus window goes this frame. False when the main doll has
        /// not been drawn yet.</summary>
        /// <summary>
        /// Where the anus sits **in the coordinates of a doll with this bounding box**, so that it
        /// lands inside the anus window.
        ///
        /// The window is at a fixed place in the doll column, while a part is placed in doll
        /// coordinates, and how those two meet depends on the doll's bounding box (NHT's Doll.Fit
        /// scales the box into the column). gen_defs.py bakes this for the dolls we ship
        /// (panel_to_doll), but a doll another mod builds has a bounding box of its own, so the
        /// baked value would put the anus somewhere else - it has to be worked out again here.
        /// </summary>
        internal static bool AnusPlacement(Rect bb, Vector2 panelPos, float panelScale,
                                           out Vector2 position, out float scale)
        {
            position = Vector2.zero;
            scale = 0f;
            if (bb.width <= 0f || bb.height <= 0f || panelScale <= 0f)
            {
                return false;
            }
            float s = Mathf.Min(DollRectUnits.x / bb.width, DollRectUnits.y / bb.height);
            if (s <= 0f)
            {
                return false;
            }
            float fx = (DollRectUnits.x - bb.width * s) * 0.5f;
            float fy = (DollRectUnits.y - bb.height * s) * 0.5f;
            // Panel coordinates -> doll column pixels -> UI units -> doll coordinates.
            float ux = (PanelPx.x + PanelPx.width * 0.5f + panelPos.x) / TemplateScale;
            float uy = (PanelPx.y + PanelPx.height * 0.5f + panelPos.y) / TemplateScale;
            position = new Vector2(bb.x + (ux - fx) / s, bb.y + (uy - fy) / s);
            scale = panelScale / (TemplateScale * s);
            return true;
        }

        internal static bool TryAnusWindowRect(out Rect rect)
        {
            if (lastDollFrame != Time.frameCount)
            {
                rect = default(Rect);
                return false;
            }
            rect = FromTemplate(lastDollRect, AnusWindowPx);
            return true;
        }

        /// <summary>The anus window: buttocks background plus outline, shared in the same shape by
        /// the main doll and the panel.</summary>
        internal static void DrawAnusWindow(Rect rect)
        {
            // The window and the background have different aspects (56x71 / 128x128), so the
            // overflowing side is cropped to fill.
            // The colour is set here rather than inherited: we draw between other parts, and
            // whatever tint was left behind would be multiplied into the background.
            Color before = GUI.color;
            GUI.color = Color.white;
            if (anusBgTex != null)
            {
                GUI.DrawTexture(rect, anusBgTex, ScaleMode.ScaleAndCrop);
            }
            DrawFrame(rect);
            GUI.color = before;
        }

        // ------------------------------------------------------------------ Hooks
        /// <summary>Before the main doll is drawn, tells it to block clicks under the panel.</summary>
        public static void DrawMainDollPrefix(object __instance, Rect dollRect)
        {
            try
            {
                MainDollPrefix(__instance, dollRect);
            }
            catch (Exception ex)
            {
                // Never let anything of ours escape into NHT's drawing: an exception between its
                // GUI groups leaves the whole tab broken until the window is reopened.
                ready = false;
                GUI.color = Color.white;
                Log.Warning(Bootstrap.Prefix + "RJW panel hook disabled after an error: " + ex);
            }
        }

        private static void MainDollPrefix(object __instance, Rect dollRect)
        {
            // Recorded even with the panel closed - the main doll's anus window is placed from
            // this rect.
            lastDollRect = dollRect;
            lastDollFrame = Time.frameCount;

            // The anus window (buttocks background plus outline) is laid **here**, right before
            // the doll is drawn, so it sits behind every part and the anus glyph lands on top of
            // it. It used to be laid during the anus part's own turn, but a part only gets a turn
            // when its remapped index resolves and the view shows it - a healthy organ in the
            // normal view gets none, and then the window was missing (user report).
            LayAnusWindowFor(__instance, dollRect);

            object ctx;
            if (!ReferenceEquals(openFor, __instance) || !Enabled(__instance, out ctx))
            {
                return;
            }
            FieldInfo f = AccessTools.Field(contextType, "OverlayBlockRect");
            if (f != null)
            {
                f.SetValue(ctx, PanelOf(dollRect));
            }
        }

        /// <summary>Draws the button and the panel. Drawn after the hand and foot strip, so it
        /// sits on top.</summary>
        public static void DrawOverlayPostfix(object __instance, Rect dollRect, Rect buttonRect)
        {
            try
            {
                OverlayPostfix(__instance, dollRect, buttonRect);
            }
            catch (Exception ex)
            {
                ready = false;
                GUI.color = Color.white;
                Log.Warning(Bootstrap.Prefix + "RJW panel disabled after an error: " + ex);
            }
        }

        private static void OverlayPostfix(object __instance, Rect dollRect, Rect buttonRect)
        {
            object ctx;
            if (!Enabled(__instance, out ctx))
            {
                return;
            }

            // If the hand and foot panel is open we stand back; both use the same doll area.
            // overlayMode is the value left after the original DrawOverlay decided whether to draw
            // its panel this frame.
            bool handsOpen = Convert.ToInt32(overlayModeField.GetValue(__instance)) != 0;
            if (handsOpen)
            {
                openFor = null;

                // NHT's hand and foot panel (DollWidget.DrawOverlay) is an opaque band covering
                // the bottom 25% of the doll column. We draw **after** it (a postfix), so left
                // alone our button would sit on top of that panel. When the panel covers the
                // button's place we treat the button as being underneath: it is neither drawn nor
                // clickable - drawing under an opaque panel shows nothing, and a click there would
                // steal a click meant for the panel.
                float bandH = dollRect.height * 0.25f;
                Rect band = new Rect(dollRect.x, dollRect.yMax - bandH, dollRect.width, bandH);
                if (band.Overlaps(ButtonOf(dollRect)))
                {
                    return;
                }
            }

            if (ReferenceEquals(openFor, __instance))
            {
                DrawPanel(ctx, PanelOf(dollRect), ButtonOf(dollRect));
            }
            DrawButton(__instance, ctx, ButtonOf(dollRect));
        }

        /// <summary>
        /// Lays the anus window for the doll about to be drawn, unless the view has no window
        /// (bones, armour) or we have nothing to put in it.
        /// </summary>
        private static void LayAnusWindowFor(object widget, Rect dollRect)
        {
            if (!ready || !Bootstrap.Ready)
            {
                return;
            }
            object ctx = (ctxField == null) ? null : ctxField.GetValue(widget);
            if (ctx == null)
            {
                return;
            }
            Def doll = (mainDollField == null) ? null : mainDollField.GetValue(ctx) as Def;
            if (doll == null || !Bootstrap.HasPartsFor(doll.defName))
            {
                return;         // No anus of ours on this doll
            }
            if (!AnusPartLive(doll.defName))
            {
                if (!windowNoticeLogged)
                {
                    windowNoticeLogged = true;
                    Log.Message(Bootstrap.Prefix + "the anus part of doll '" + doll.defName
                                + "' is switched off this frame; its window is left out.");
                }
                return;
            }
            bool armor = ctxArmorField != null && (bool)ctxArmorField.GetValue(ctx);
            int filterMode = (filterModeField == null)
                ? 0
                : Convert.ToInt32(filterModeField.GetValue(ctx));
            if (armor || filterMode == 1 || !AnusWindow.ClaimDraw())
            {
                return;
            }
            try
            {
                DrawAnusWindow(FromTemplate(dollRect, AnusWindowPx));
            }
            catch (Exception ex)
            {
                Log.Warning(Bootstrap.Prefix + "anus window failed: " + ex);
            }
        }

        /// <summary>
        /// Whether our anus part is switched on for this doll right now. DollStateApplier keeps the
        /// part's index alive whenever the window belongs on the doll - even with no anus to draw
        /// inside it - so this is the honest answer to "does this doll have our window".
        /// </summary>
        private static bool AnusPartLive(string dollName)
        {
            if (Bootstrap.BodyPartIdField == null)
            {
                return false;
            }
            string source = ForeignDolls.SourceName(dollName);
            List<BoundPart> parts = Bootstrap.BoundParts;
            for (int i = 0; i < parts.Count; i++)
            {
                BoundPart p = parts[i];
                if (p.slot == Bootstrap.AnusSlot && p.dollName == source)
                {
                    return (int)Bootstrap.BodyPartIdField.GetValue(p.def) >= 0;
                }
            }
            return false;
        }

        private static void DrawButton(object widget, object ctx, Rect rect)
        {
            bool open = ReferenceEquals(openFor, widget);
            bool hover = Mouse.IsOver(rect);

            // No outline and no ground (as specified). On hover, or while the panel is open, only
            // the same soft glow as NHT's hand and foot buttons (DollWidget.DrawMiniatureGlow).
            if ((hover || open) && vGradient != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTextureWithTexCoords(new Rect(rect.x - 4f, rect.y + 3f, rect.width + 8f, rect.height),
                                             vGradient, new Rect(0f, 0.5f, 1f, -0.5f));
                GUI.color = Color.white;
            }

            // Instead of a button picture we show **this pawn's crotch**, cropped: outer genitals
            // only.
            DrawCrotch(ctx, rect);

            TooltipHandler.TipRegion(rect, "NHTRJW_PanelTip".Translate());
            if (Widgets.ButtonInvisible(rect, true))
            {
                // When the hand and foot strip is open, NHT closes it itself once the mouse
                // leaves. All we have to do is stand back while theirs is open.
                openFor = open ? null : widget;
            }
        }

        /// <summary>
        /// Draws the current pawn's crotch, cropped, in the button's place: the whole doll is drawn
        /// large and everything outside the button is clipped away (GUI.BeginClip). NHT draws the
        /// doll in its normal view while we drop organs and the anus for the duration of
        /// <see cref="Cropping"/>, leaving the body and the outer genitals.
        /// </summary>
        private static void DrawCrotch(object ctx, Rect rect)
        {
            if (drawDollMethod == null || dollBoundsField == null)
            {
                return;
            }
            object mainDoll = mainDollField.GetValue(ctx);
            string dollName = ForeignDolls.SourceName(
                ((mainDoll as Def) == null) ? null : ((Def)mainDoll).defName);
            if (dollName.NullOrEmpty())
            {
                return;
            }

            // The frame: the glyph area of the outer genitals visible on this pawn, or every form
            // of that body type when there is none.
            float x0, y0, x1, y1;
            Pawn pawn = (pawnField == null) ? null : pawnField.GetValue(ctx) as Pawn;
            Hediff penis = DollPartFormDef.PenisOf(pawn);
            string kind = (penis == null || penis.def == null) ? null : penis.def.defName;
            // The frame is measured in doll coordinates, so it moves with the body on a doll that
            // lays it elsewhere (ForeignDolls).
            Vector2 shift = ForeignDolls.OffsetFor(pawn);
            if (!ExternalBounds(dollName, true, kind, out x0, out y0, out x1, out y1)
                && !ExternalBounds(dollName, false, null, out x0, out y0, out x1, out y1))
            {
                return;
            }
            Rect bb = (Rect)dollBoundsField.GetValue(mainDoll);
            float aspect = rect.width / rect.height;
            float h = Mathf.Max((y1 - y0) * CropPad, (x1 - x0) * CropPad / aspect,
                                bb.height * CropMinFraction);
            float z = rect.height / h;                  // screen UI units per doll unit
            float cx = (x0 + x1) * 0.5f + shift.x;
            float cy = (y0 + y1) * 0.5f + shift.y;
            Rect whole = new Rect(rect.width * 0.5f - (cx - bb.x) * z,
                                  rect.height * 0.5f - (cy - bb.y) * z,
                                  bb.width * z, bb.height * z);

            object savedFilter = filterModeField.GetValue(ctx);
            object savedArmor = (armorField == null) ? null : armorField.GetValue(ctx);
            bool savedCropping = Cropping;
            object hediffs = null, scars = null, frame = null;
            GUI.BeginClip(rect);
            try
            {
                // Even in the organ or armour view, the button shows the normal view.
                filterModeField.SetValue(ctx, Enum.ToObject(filterModeField.FieldType, 0));
                if (armorField != null)
                {
                    armorField.SetValue(ctx, false);
                }
                Cropping = true;
                // Scars and bandages are not clipped to the button frame, so for the duration of
                // the draw we pretend there are none.
                SuppressOverlays(ctx, ref hediffs, ref scars, ref frame);
                drawDollMethod.Invoke(null, new object[] { ctx, mainDoll, whole, false, true });
            }
            finally
            {
                RestoreOverlays(ctx, hediffs, scars, frame);
                Cropping = savedCropping;
                filterModeField.SetValue(ctx, savedFilter);
                if (armorField != null)
                {
                    armorField.SetValue(ctx, savedArmor);
                }
                GUI.EndClip();
                GUI.color = Color.white;
            }
        }

        /// <summary>
        /// The union of the outer genital glyph areas (penis / vulva and surface testicles).
        /// With current true, only the forms visible on this pawn right now; with false, every form
        /// of that body type.
        /// </summary>
        private static bool ExternalBounds(string dollName, bool current, string kind,
                                           out float x0, out float y0, out float x1, out float y1)
        {
            x0 = y0 = float.MaxValue;
            x1 = y1 = float.MinValue;
            bool any = false;
            List<BoundPart> parts = Bootstrap.BoundParts;
            for (int i = 0; i < parts.Count; i++)
            {
                BoundPart p = parts[i];
                if (p.dollName != dollName
                    || (p.slot != Bootstrap.OuterGenitalsSlot && p.slot != Bootstrap.OuterGonadsSlot))
                {
                    continue;
                }
                if (current)
                {
                    any |= Grow(p.currentForm, kind, ref x0, ref y0, ref x1, ref y1);
                }
                else if (p.forms != null)
                {
                    for (int k = 0; k < p.forms.Count; k++)
                    {
                        any |= Grow(p.forms[k], null, ref x0, ref y0, ref x1, ref y1);
                    }
                }
            }
            return any;
        }

        private static bool Grow(DollPartFormDef f, string kind,
                                 ref float x0, ref float y0, ref float x1, ref float y1)
        {
            Vector2 min, max;
            if (f == null || !f.GlyphBoundsFor(kind, out min, out max))
            {
                return false;
            }
            x0 = Mathf.Min(x0, min.x);
            y0 = Mathf.Min(y0, min.y);
            x1 = Mathf.Max(x1, max.x);
            y1 = Mathf.Max(y1, max.y);
            return true;
        }

        /// <summary>
        /// Draws it in the same shape as NHT's hand and foot panel (DollOverlay.Draw): no outline,
        /// a ground, shadows above and below, shadows at the sides, and a half-rhombus pointing at
        /// the button.
        /// </summary>
        private static void DrawPanel(object ctx, Rect panel, Rect button)
        {
            Widgets.DrawBoxSolid(panel, bgColor);
            if (vGradient != null)
            {
                GUI.color = ShadowDeep;
                GUI.DrawTextureWithTexCoords(new Rect(panel.xMin, panel.yMin - BandShadow, panel.width, BandShadow),
                                             vGradient, new Rect(0f, 0.5f, 1f, -0.5f));
                GUI.DrawTextureWithTexCoords(new Rect(panel.xMin, panel.yMax, panel.width, BandShadow),
                                             vGradient, new Rect(0f, 0f, 1f, 0.5f));
            }
            if (hGradient != null)
            {
                GUI.color = ShadowHalfDeep;
                GUI.DrawTextureWithTexCoords(new Rect(panel.xMin, panel.yMin, SideShadow, panel.height),
                                             hGradient, new Rect(0.5f, 0f, 0.5f, 1f));
                GUI.DrawTextureWithTexCoords(new Rect(panel.xMax - SideShadow, panel.yMin, SideShadow, panel.height),
                                             hGradient, new Rect(1f, 0f, -0.5f, 1f));
            }
            if (halfRomb != null)
            {
                // The half-rhombus that juts from the panel's lower edge towards the button
                // (DollWidget.DrawIndicator)
                float size = Mathf.Min(panel.height, 24f);
                GUI.color = bgColor;
                GUI.DrawTexture(new Rect(button.center.x - size * 0.5f, panel.yMax - 1f, size, size),
                                halfRomb);
            }
            GUI.color = Color.white;

            // The click blocking we turned on to cover the main doll would also apply inside this
            // panel. NHT's DollOverlay.Draw does the same thing: switch it off while drawing.
            object savedBlock = (blockField == null) ? null : blockField.GetValue(ctx);
            if (blockField != null)
            {
                blockField.SetValue(ctx, false);
            }
            try
            {
                DrawPanelInto(ctx, panel, selectable: true);
            }
            finally
            {
                if (blockField != null)
                {
                    blockField.SetValue(ctx, savedBlock);
                }
            }
        }

        // ------------------------------------------------------------------ Drawing
        private static void DrawPanelInto(object ctx, Rect area, bool selectable)
        {
            if (area.width <= 1f || area.height <= 1f)
            {
                return;
            }
            object mainDoll = mainDollField.GetValue(ctx);
            // A doll another mod built at runtime borrows our placements from the doll it was
            // built on (ForeignDolls).
            string dollName = ForeignDolls.SourceName(
                ((mainDoll as Def) == null) ? null : ((Def)mainDoll).defName);
            if (dollName.NullOrEmpty())
            {
                return;
            }

            object[] fitArgs = { area, 0f };
            Rect rect = (Rect)fitMethod.Invoke(panelDoll, fitArgs);
            float scale = (float)fitArgs[1];

            // The genitals box: an outline over the per-body-type background.
            Rect repro = ToScreen(rect, ReproBox);
            Texture2D bg = BackgroundFor(dollName);
            if (bg != null)
            {
                GUI.DrawTexture(repro, bg, ScaleMode.StretchToFill);
            }
            DrawFrame(repro);

            // The anus window, in the same shape as the main doll's.
            DrawAnusWindow(ToScreen(rect, AnusBox));

            // Ovulation / fertilization / implantation: the picture menstruation picked, as it is.
            // No outline.
            Pawn pawn = (pawnField == null) ? null : pawnField.GetValue(ctx) as Pawn;
            Texture2D egg;
            if (pawn != null && MenstruationReader.TryEggIcon(pawn, out egg))
            {
                GUI.DrawTexture(ToScreen(rect, EggBox), egg, ScaleMode.ScaleToFit);
            }

            object savedFilter = filterModeField.GetValue(ctx);
            bool savedDrawing = Drawing;
            Drawing = true;     // The panel already drew the window for its anus, so the main
                                // doll's window is not drawn again
            // The pawn's penis kind. A long kind (a horse penis, say) keeps the default placement
            // of the genitals group and is cut off at the genitals box instead of shrinking and
            // shifting the whole group.
            Hediff penis = DollPartFormDef.PenisOf(pawn);
            string kind = (penis == null || penis.def == null) ? null : penis.def.defName;
            bool overRepro = Mouse.IsOver(repro);
            try
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    // Every slot but the anus window is clipped to the genitals box. The clip
                    // keeps the parent's coordinates (scroll offset = -box position), so the rects
                    // NHT stores for later (hover tooltip, armour cover) stay valid outside it.
                    // A part cut off at the box is not clickable outside the box either.
                    bool genitals = Slots[i] != Bootstrap.AnusSlot;
                    if (genitals)
                    {
                        GUI.BeginClip(repro, -repro.position, Vector2.zero, false);
                    }
                    try
                    {
                        DrawSlot(ctx, Slots[i], dollName, rect, scale,
                                 selectable && (!genitals || overRepro), pawn, kind, genitals);
                    }
                    finally
                    {
                        if (genitals)
                        {
                            GUI.EndClip();
                        }
                    }
                }
            }
            finally
            {
                Drawing = savedDrawing;
                filterModeField.SetValue(ctx, savedFilter);
                GUI.color = Color.white;
            }
        }

        private static void DrawFrame(Rect r)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            Widgets.DrawBox(r);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Whether a part's glyph, drawn at this panel placement, reaches outside the genitals box.
        /// Its art is cut off there, but NHT draws scars and bandages with Graphics.DrawTexture,
        /// which ignores the clip - so such a part is drawn without them.
        /// </summary>
        private static bool GlyphLeavesReproBox(DollPartFormDef form, DollPartFormDef place, string kind,
                                                Vector2 panelPos, float panelScale)
        {
            Vector2 min, max;
            if (place.scale == 0f || !form.GlyphBoundsFor(kind, out min, out max))
            {
                return false;
            }
            // Panel = origin + doll x k: the same linear map gen_defs.py uses for the group.
            float k = panelScale / place.scale;
            Vector2 origin = panelPos - place.position * k;
            float x0 = origin.x + Math.Min(min.x * k, max.x * k);
            float x1 = origin.x + Math.Max(min.x * k, max.x * k);
            float y0 = origin.y + Math.Min(min.y * k, max.y * k);
            float y1 = origin.y + Math.Max(min.y * k, max.y * k);
            const float Slack = 1f;
            return x0 < ReproBox.xMin - Slack || x1 > ReproBox.xMax + Slack
                || y0 < ReproBox.yMin - Slack || y1 > ReproBox.yMax + Slack;
        }

        private static void DrawSlot(object ctx, string slot, string dollName,
                                     Rect rect, float scale, bool selectable,
                                     Pawn pawn, string kind, bool clipped)
        {
            List<BoundPart> parts = Bootstrap.BoundParts;
            BoundPart p = null;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].slot == slot && parts[i].dollName == dollName)
                {
                    p = parts[i];
                    break;
                }
            }
            if (p == null || p.currentForm == null || p.currentForm.panelScale <= 0f)
            {
                return;
            }
            // Parts DollStateApplier hid do not show in the panel either.
            if ((int)Bootstrap.BodyPartIdField.GetValue(p.def) < 0)
            {
                return;
            }
            // A penis kind without testicles: the organ view keeps its marker, the panel does not.
            if (p.currentForm.hiddenKindsKeepOrgan && p.currentForm.HidesKindOf(pawn))
            {
                return;
            }

            // Where it goes: the form's own panel placement, the same for every kind.
            // Kind testicle art is placed like the penis it was drawn with (placementForm).
            DollPartFormDef place = p.placementForm ?? p.currentForm;
            Vector2 panelPos = place.panelPosition;
            float panelScale = place.panelScale;
            // Kind testicle glyphs are measured on the penis canvas, so they only apply while the
            // kind art is what is drawn.
            string glyphKind = (p.currentForm.variantOnPenisCanvas && p.placementForm == null) ? null : kind;
            bool leaks = clipped && GlyphLeavesReproBox(p.currentForm, place, glyphKind, panelPos, panelScale);

            // Organ-layer parts are drawn regardless of status only in the organ filter, and
            // surface-layer parts the other way round. Both must always show in the panel, so we
            // switch the filter mode per part.
            //   DollFilterMode: None=0 / Bones=1 / Organs=2
            bool isOrgan = organType != null && organType.IsInstanceOfType(p.def);
            filterModeField.SetValue(ctx, Enum.ToObject(filterModeField.FieldType, isOrgan ? 2 : 0));

            Vector2 savedPos = (Vector2)Bootstrap.PositionField.GetValue(p.def);
            float savedW = (float)Bootstrap.WidthField.GetValue(p.def);
            float savedH = (float)Bootstrap.HeightField.GetValue(p.def);
            object hediffs = null, scars = null, frame = null;
            try
            {
                if (leaks)
                {
                    SuppressOverlays(ctx, ref hediffs, ref scars, ref frame);
                }
                Bootstrap.PositionField.SetValue(p.def, panelPos);
                Bootstrap.WidthField.SetValue(p.def, panelScale);
                Bootstrap.HeightField.SetValue(p.def, panelScale);
                drawPartMethod.Invoke(null, new object[]
                {
                    ctx, p.def, panelDoll, rect, scale, selectable, Vector2.zero,
                });
            }
            finally
            {
                RestoreOverlays(ctx, hediffs, scars, frame);
                Bootstrap.PositionField.SetValue(p.def, savedPos);
                Bootstrap.WidthField.SetValue(p.def, savedW);
                Bootstrap.HeightField.SetValue(p.def, savedH);
            }
        }
    }
}
