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
/// bar and no menu, which is what gap 12 in <c>docs/archive/mcp-server-gaps-closed.md</c> reported: the
/// tools could see a menu's <i>structure</i> but never show it.</para>
///
/// <para>So render each piece and compose them. The canvas is the union of the window and every open
/// popup, in screen pixels, because a menu routinely hangs past the window's own edge and clipping it back
/// would lose exactly the part worth looking at.</para>
/// </summary>
public static class SnapshotComposer
{
    private static int _captures;

    /// <summary>
    /// Where the next capture is written: a file of its own in <paramref name="folder"/>, named for
    /// <paramref name="now"/> and a per-process count, so a path a caller was given keeps meaning that capture.
    /// </summary>
    /// <remarks>
    /// Every capture used to go to one <c>hexide_snapshot.png</c>, so the next call overwrote the image the last
    /// one had returned, and a before/after pair read the "after" twice (#756). The count separates two captures
    /// inside one millisecond. Nothing deletes the files: they are in the temp folder, where the OS expects them.
    /// The time is UTC and says so with a Z, so a name lines up with every stamp the tools return (#956 review).
    /// </remarks>
    public static string NewCapturePath(string folder, DateTime utcNow)
    {
        var count = Interlocked.Increment(ref _captures);
        return System.IO.Path.Combine(folder,
            $"hexide_snapshot_{utcNow.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture)}Z_{count}.png");
    }

    /// <summary>Captures the visible Windows frame rectangle, or explains why the host cannot.</summary>
    public static Bitmap? CaptureFrame(Window window, out PixelPoint windowOffset, out string? error) =>
        NativeWindowFrame.Capture(window, out windowOffset, out error);

    /// <summary>Renders the client and open popups; returns null when the window has no area.</summary>
    public static RenderTargetBitmap? Capture(Window window) => Capture(window, out _);

    public static RenderTargetBitmap? Capture(Window window, out PixelPoint windowOffset)
    {
        windowOffset = default;
        var scaling = window.DesktopScaling;
        var windowRect = ScreenRect(window, window.ClientSize, scaling);
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            return null;

        var popups = new List<TopLevel>();
        CollectOpenPopupRoots(window, window, popups);

        var placements = new List<(Visual Visual, PixelRect Rect)>();
        foreach (var popup in popups)
        {
            var rect = ScreenRect(popup, popup.ClientSize, scaling);
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            placements.Add((popup, rect));
        }

        return Compose(window, windowRect, placements, scaling, out windowOffset);
    }

    // Explicit screen rectangles let headless tests exercise desktop composition without overlay popups.
    internal static RenderTargetBitmap Compose(Visual window, PixelRect windowRect,
        List<(Visual Visual, PixelRect Rect)> placements, double scaling, out PixelPoint windowOffset)
    {
        var canvas = windowRect;
        foreach (var (_, rect) in placements)
            canvas = canvas.Union(rect);
        windowOffset = new PixelPoint(windowRect.X - canvas.X, windowRect.Y - canvas.Y);

        var bitmap = new RenderTargetBitmap(canvas.Size, new Vector(96 * scaling, 96 * scaling));

        // Keep the direct render when the window is at the canvas origin. Otherwise it needs the
        // same pixel-source / DIP-destination composition as the popups, leaving room for the overhang.
        if (windowOffset == default)
        {
            bitmap.Render(window);
            if (placements.Count == 0)
                return bitmap;
        }
        else
            placements.Insert(0, (window, windowRect));

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
            // Attached first, because the owner is an ordinary control the visual walk lands on and its
            // popup is not a visual child of anything: a context menu and a toolbar flyout are parented
            // LOGICALLY, so composing only visual-child popups drew a window with the menu missing.
            if (child is Control owner)
                foreach (var attached in UiAutomationDriver.AttachedPopupsOfPublic(owner).Where(p => p.IsOpen))
                    AddPopupRoot(attached, window, acc);

            if (child is Popup { IsOpen: true } popup)
            {
                // The popup's ROOT, not its Child. The root is the popup's analogue of a window: it owns
                // the border, padding and shadow the menu is actually drawn with, and its ClientSize is the
                // whole thing. Rendering Child alone gave a box clipped to the content and placed by the
                // content's origin, so a menu came out short and shifted.
                AddPopupRoot(popup, window, acc);
                continue;
            }
            CollectOpenPopupRoots(child, window, acc);
        }
    }

    /// <summary>Adds an open popup's realised root, then recurses into it for nested popups.</summary>
    private static void AddPopupRoot(Popup popup, Window window, List<TopLevel> acc)
    {
        if (popup.Child is { } content
            && TopLevel.GetTopLevel(content) is { } popupRoot
            && !ReferenceEquals(popupRoot, window)
            && !acc.Contains(popupRoot))
        {
            acc.Add(popupRoot);
            CollectOpenPopupRoots(content, window, acc);
        }
    }
}
