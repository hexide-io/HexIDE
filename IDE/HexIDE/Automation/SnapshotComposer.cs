using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;

namespace HexIDE.Automation;

/// <summary>
/// Captures a window <i>and</i> whatever is floating above it.
///
/// <para>A popup — a dropped-down menu, a combo's list, a flyout — is not drawn into its parent window.
/// It is realised in its own top-level root, so rendering the window alone produces a picture with a menu
/// bar and no menu, which is what gap 12 in <c>docs/mcp-server-gaps.md</c> reported: the tools could see a
/// menu's <i>structure</i> but never show it.</para>
///
/// <para>So render each piece and compose them. The canvas is the union of the window and every open
/// popup, in screen pixels, because a menu routinely hangs past the window's own edge and clipping it back
/// would lose exactly the part worth looking at.</para>
/// </summary>
public static class SnapshotComposer
{
    /// <summary>
    /// Renders <paramref name="window"/> with every open popup drawn in its real position.
    /// Returns null when the window has no area to capture.
    /// </summary>
    public static RenderTargetBitmap? Capture(Window window)
    {
        var scaling = window.DesktopScaling;
        var windowRect = ScreenRect(window, window.ClientSize, scaling);
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            return null;

        var popups = new List<TopLevel>();
        CollectOpenPopupRoots(window, window, popups);

        // Union first, so a menu hanging off the window's edge widens the canvas rather than being cut.
        var canvas = windowRect;
        var placements = new List<(Visual Visual, PixelRect Rect)>();
        foreach (var popup in popups)
        {
            var rect = ScreenRect(popup, popup.ClientSize, scaling);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            placements.Add((popup, rect));
            canvas = canvas.Union(rect);
        }

        var bitmap = new RenderTargetBitmap(canvas.Size, new Vector(96 * scaling, 96 * scaling));

        // The window goes down through Render, not through the drawing context. That path was already
        // correct and stays pixel-for-pixel what a snapshot used to be; routing it through DrawImage
        // instead came out `scaling` times too large on a 150% display, which reads as a rendering fault
        // and is really a units one — the context works in device-independent pixels while every rect here
        // is a device pixel.
        bitmap.Render(window);
        if (placements.Count == 0)
            return bitmap;

        using (var ctx = bitmap.CreateDrawingContext(false))
        {
            // In list order, which is visual-tree order: a submenu's popup is realised inside its parent
            // menu's popup, so children arrive after their parents and land on top, as on screen.
            foreach (var (visual, rect) in placements)
            {
                using var piece = new RenderTargetBitmap(rect.Size, new Vector(96 * scaling, 96 * scaling));
                piece.Render(visual);

                // Source rect in the piece's PIXELS, destination in the context's device-independent
                // pixels. Both are needed explicitly: the one-rect overload takes the source's DIP extent
                // as the region to sample, so on a 150% display it read the top-left two-thirds of the
                // piece and stretched that to fill — a correctly placed, correctly sized menu box with
                // magnified, clipped contents inside it.
                ctx.DrawImage(piece,
                    new Rect(0, 0, rect.Width, rect.Height),
                    new Rect(
                        (rect.X - canvas.X) / scaling, (rect.Y - canvas.Y) / scaling,
                        rect.Width / scaling, rect.Height / scaling));
            }
        }

        return bitmap;
    }

    private static PixelRect ScreenRect(Visual visual, Size size, double scaling)
    {
        var origin = visual.PointToScreen(new Point(0, 0));
        return new PixelRect(origin.X, origin.Y, (int)(size.Width * scaling), (int)(size.Height * scaling));
    }

    /// <summary>
    /// The realised content of every open popup beneath <paramref name="root"/>, parents before children.
    /// </summary>
    /// <remarks>
    /// <para>A closed popup has nothing realised, and an open one's content usually lives in its own root —
    /// so this walks out of the window's tree and into each popup's, the same crossing
    /// <c>UiAutomationDriver</c> makes to report a menu's items.</para>
    ///
    /// <para><b>Usually, not always.</b> Avalonia hosts a popup either as a real top-level window or as an
    /// overlay inside its parent — desktop does the former, headless and single-window platforms the
    /// latter. An overlay popup is already part of the window's own render, so composing it again would
    /// draw it twice and widen the canvas for content that was never outside the window. Anything whose
    /// top-level IS this window is therefore skipped.</para>
    /// </remarks>
    private static void CollectOpenPopupRoots(Visual root, Window window, List<TopLevel> acc)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is Popup { IsOpen: true } popup)
            {
                // The popup's ROOT, not its Child. The root is the popup's analogue of a window: it owns
                // the border, padding and shadow the menu is actually drawn with, and its ClientSize is the
                // whole thing. Rendering Child alone gave a box clipped to the content and placed by the
                // content's origin, so a menu came out short and shifted.
                if (popup.Child is { } content
                    && TopLevel.GetTopLevel(content) is { } popupRoot
                    && !ReferenceEquals(popupRoot, window))
                {
                    acc.Add(popupRoot);
                    CollectOpenPopupRoots(content, window, acc);
                }
                continue;
            }
            CollectOpenPopupRoots(child, window, acc);
        }
    }
}
