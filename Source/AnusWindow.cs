using UnityEngine;

namespace NHTRJWGenitalia
{
    /// <summary>
    /// The anus window on the main doll: whether to draw the window this frame, and
    /// whether to draw the anus inside it.
    ///
    /// The anus is not drawn at its anatomical spot (the crotch) but inside a **window**
    /// at the lower right of the doll column, pixel-identical to the panel's anus window.
    /// The window (background + outline) shows even when the pawn has no anus; the anus
    /// glyph is laid on top of it only when there is one.
    ///
    /// <see cref="DollStateApplier"/> decides this right before drawing and
    /// <see cref="TintedPartRenderer"/> reads it when the anus part's turn comes. Like the
    /// other layers it is **frame-stamped**, so the window never lingers into a frame that
    /// was not prepared.
    /// </summary>
    internal static class AnusWindow
    {
        private static bool visible;
        private static bool glyph;
        private static int preparedFrame = -1;

        /// <summary>Whether to draw the window this frame.</summary>
        internal static bool Visible
        {
            get { return visible && preparedFrame == Time.frameCount; }
        }

        /// <summary>Whether to draw the anus glyph inside the window. False when the pawn
        /// has no anus - then only the window remains.</summary>
        internal static bool ShowGlyph
        {
            get { return glyph; }
        }

        internal static void Prepare(bool showGlyph)
        {
            visible = true;
            glyph = showGlyph;
            preparedFrame = Time.frameCount;
        }

        internal static void Clear()
        {
            visible = false;
            glyph = false;
            preparedFrame = -1;
        }
    }
}
