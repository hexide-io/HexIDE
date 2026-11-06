using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace HexIDE.Automation;

/// <summary>
/// Captures a window <i>and</i> whatever is floating above it.
///
/// <para>A popup — a dropped-down menu, a combo's list, a flyout — is not drawn into its parent window.
/// It is realised in its own top-level root, so rendering the window alone produces a picture with a menu
/// bar and no menu, which is what gap 12 in <c>docs/mcp-server-gaps.md</c> reported: the
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

    /// <summary>
    /// A capture: the image, where the window's origin lies in it, the popups it left out, each with why, when the
    /// compositor drew nothing in time, a note on the fallback that drew it instead, and, for a frame capture copied from
    /// the screen, a note on what covers it.
    /// </summary>
    public sealed record Snapshot(Bitmap Image, PixelPoint WindowOffset, IReadOnlyList<LeftOutPopup> LeftOut, string? Fallback,
        string? Frame = null) : IDisposable
    {
        public void Dispose() => Image.Dispose();
    }

    /// <summary>
    /// Draws <paramref name="window"/> with every open popup drawn in its real position, as the compositor draws them on
    /// screen. Null when the window has no area to capture.
    /// </summary>
    /// <summary>
    /// Why a minimized window is not captured: nothing of it is on the screen, and the compositor still holds the frame it
    /// drew before it was minimized, which came back as if current (#2416). <paramref name="name"/> is what reaches it.
    /// </summary>
    internal static string MinimizedRefusal(string name) =>
        $"'{name}' is minimized, so nothing of it is on the screen, and the last frame it drew before it was minimized "
        + "would read as current; nothing was captured. Restore it with set_window_state {\"state\":\"Normal\",\"window\":"
        + $"\"{name}\"}}, then capture it.";

    public static async Task<Snapshot?> CaptureAsync(Window window)
    {
        var scaling = window.DesktopScaling;
        var windowRect = ScreenRect(window, window.ClientSize, scaling);
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            return null;

        var (placements, noArea, screens) = PopupPlacements(window, scaling);
        var snapshot = await ComposeAsync(window, windowRect, placements, scaling, screens);
        return noArea.Count > 0 ? snapshot with { LeftOut = [.. noArea, .. snapshot.LeftOut] } : snapshot;
    }

    /// <summary>
    /// <paramref name="window"/> with its title bar and frame, as Windows renders it even where another window covers it
    /// (#2182), and every open popup drawn over it as <see cref="CaptureAsync"/> draws them; or why it can't be captured.
    /// </summary>
    public static async Task<(Snapshot? Snapshot, string? Error)> CaptureFrameAsync(Window window)
    {
        if (NativeWindowFrame.Capture(window, out var error) is not { } frame)
            return (null, error);
        var scaling = window.DesktopScaling;
        var client = new PixelRect(frame.ClientOrigin, PixelSize.FromSize(window.ClientSize, scaling));
        var (placements, noArea, screens) = PopupPlacements(window, scaling);
        var snapshot = await ComposeAsync(window, client, placements, scaling, screens, (frame.Image, frame.Frame));
        return (snapshot with { LeftOut = [.. noArea, .. snapshot.LeftOut], Frame = frame.Note }, null);
    }

    /// <summary>
    /// Where each open popup of <paramref name="window"/> is on screen, those with no area to draw, and the screens' bounds,
    /// null where the platform reports none with an area.
    /// </summary>
    private static (List<(Visual Visual, PixelRect Rect)> Placements, List<LeftOutPopup> NoArea, IReadOnlyList<PixelRect>? Screens)
        PopupPlacements(Window window, double scaling)
    {
        var popups = new List<TopLevel>();
        CollectOpenPopupRoots(window, window, popups);

        var placements = new List<(Visual Visual, PixelRect Rect)>();
        var noArea = new List<LeftOutPopup>();
        foreach (var popup in popups)
        {
            var rect = ScreenRect(popup, popup.ClientSize, scaling);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                noArea.Add(new LeftOutPopup(KindOf(popup), rect, "which has no area to draw"));
                continue;
            }
            placements.Add((popup, rect));
        }

        // Where the platform reports no screens, or only ones with no area, none rules a popup out.
        var screens = window.Screens?.All.Select(s => s.Bounds).Where(b => b.Width > 0 && b.Height > 0).ToList();
        return (placements, noArea, screens is { Count: > 0 } ? screens : null);
    }

    /// <summary>
    /// A popup the snapshot does not show: what it holds, where it was reported in screen pixels, and why it was left out.
    /// </summary>
    public sealed record LeftOutPopup(string Kind, PixelRect Rect, string Reason);

    /// <summary>
    /// The most bytes one render target is given: 16,384 × 16,384 pixels' worth, 1 GiB. A render target's size in bytes is
    /// an Int32, so a canvas past 2 GiB overflowed it, and a negative size threw "Positive number required" (#1960). A
    /// popup reported far from the window would do it, for example at (-32000, -32000), since the canvas is the union of
    /// the window and its popups. Where the popup in #1960 was really reported is not established.
    /// </summary>
    private const long MaxCanvasBytes = 16384L * 16384 * 4;

    // What a popup holds, a MenuFlyoutPresenter or a ComboBox's list, which is how a reply can tell popups apart.
    private static string KindOf(Visual popup) =>
        popup is ContentControl { Content: Control content } ? content.GetType().Name : popup.GetType().Name;

    // Explicit screen rectangles let headless tests exercise desktop composition without overlay popups. A window already
    // drawn, as a frame capture is, comes as 'drawn', the image and the screen rectangle it shows, and is not drawn again.
    internal static async Task<Snapshot> ComposeAsync(Visual window, PixelRect windowRect,
        List<(Visual Visual, PixelRect Rect)> placements, double scaling, IReadOnlyList<PixelRect>? screens = null,
        (Bitmap Image, PixelRect Rect)? drawn = null)
    {
        // A popup on no screen is not on screen, so a picture of what is on screen leaves it out. One that would make
        // the image too big to allocate is left out too, rather than costing the whole snapshot. Either is named. The
        // extent is reckoned in longs: a rectangle's right edge is an Int32, and a popup's own size can be the largest
        // one, which is what a cast from an infinite size gives.
        var canvas = drawn?.Rect ?? windowRect;
        var dropped = new List<LeftOutPopup>();
        placements = placements.Where(p =>
        {
            if (screens is not null && !screens.Any(screen => screen.Intersects(p.Rect)))
            {
                dropped.Add(new LeftOutPopup(KindOf(p.Visual), p.Rect, "which is on none of the screens"));
                return false;
            }
            var width = Math.Max((long)canvas.X + canvas.Width, (long)p.Rect.X + p.Rect.Width) - Math.Min(canvas.X, p.Rect.X);
            var height = Math.Max((long)canvas.Y + canvas.Height, (long)p.Rect.Y + p.Rect.Height) - Math.Min(canvas.Y, p.Rect.Y);
            if (width * height * 4 > MaxCanvasBytes)
            {
                dropped.Add(new LeftOutPopup(KindOf(p.Visual), p.Rect,
                    $"which would make the image {width} × {height} pixels with what is drawn before it, more than "
                    + "16,384 × 16,384 pixels' worth"));
                return false;
            }
            canvas = canvas.Union(p.Rect);
            return true;
        }).ToList();
        var windowOffset = new PixelPoint(windowRect.X - canvas.X, windowRect.Y - canvas.Y);
        var fellBack = new List<string>();

        // The window as it is drawn, when nothing is composed around it.
        if (drawn is { } alone && placements.Count == 0)
            return new Snapshot(alone.Image, windowOffset, dropped, null);
        if (drawn is null && windowOffset == default && placements.Count == 0)
            return new Snapshot(await DrawnAsync(window, windowRect.Size, scaling, fellBack), windowOffset, dropped,
                FallbackNote(fellBack));

        var pieces = new List<(Bitmap Piece, PixelRect Rect)>();
        if (drawn is { } first)
            pieces.Add(first);
        else
            placements.Insert(0, (window, windowRect));
        try
        {
            foreach (var (visual, rect) in placements)
                pieces.Add((await DrawnAsync(visual, rect.Size, scaling, fellBack), rect));

            // Only images are drawn into the canvas, so its own scale meets none of the trouble DrawnAsync avoids.
            var bitmap = new RenderTargetBitmap(canvas.Size, new Vector(96 * scaling, 96 * scaling));
            using (var ctx = bitmap.CreateDrawingContext(false))
            {
                // In list order, which is visual-tree order: a submenu's popup is realised inside its parent
                // menu's popup, so children arrive after their parents and land on top, as on screen.
                foreach (var (piece, rect) in pieces)
                {
                    // Source rect in the piece's PIXELS, destination in the context's device-independent
                    // pixels. Both are needed explicitly: the one-rect overload takes the source's DIP extent
                    // as the region to sample, so on a 150% display it read the top-left two-thirds of the
                    // piece and stretched that to fill — a correctly placed, correctly sized menu box with
                    // magnified, clipped contents inside it. The piece's own size, which the compositor may
                    // round differently from the rect, is what it holds.
                    var size = piece.PixelSize;
                    ctx.DrawImage(piece,
                        new Rect(0, 0, size.Width, size.Height),
                        new Rect(
                            (rect.X - canvas.X) / scaling, (rect.Y - canvas.Y) / scaling,
                            size.Width / scaling, size.Height / scaling));
                }
            }
            return new Snapshot(bitmap, windowOffset, dropped, FallbackNote(fellBack));
        }
        finally
        {
            foreach (var (piece, _) in pieces)
                piece.Dispose();
        }
    }

    /// <summary>How long a piece waits for the compositor to draw it before it is drawn another way.</summary>
    internal static TimeSpan CompositorWait = TimeSpan.FromSeconds(5);

    /// <summary>
    /// <paramref name="visual"/>, a window or a popup's root, as the compositor draws it on screen, at
    /// <paramref name="scaling"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a <see cref="RenderTargetBitmap"/>, which this used to be. One at a DPI other than 96 keeps its scale apart
    /// and multiplies it into each transform set. Avalonia.Skia's <c>PopClip</c> then rebuilds the current transform from
    /// the canvas matrix, which already holds that scale, so the first transform pushed after a clip is popped gets it
    /// twice. Drawing text pushes one for the text's origin, so above 100% text a control draws after popping a clip
    /// landed outside the bitmap. A running StatusBar's later panels came out blank while the screen showed them, and a
    /// live check reported a fault that was not there (#2293). The compositor applies scaling another way, and what it
    /// draws is what the screen shows.
    /// </para>
    /// <para>
    /// Its snapshot is a job on the next commit, after what the UI thread has changed so far, so it shows the state the
    /// caller just left. Should the compositor draw nothing within <see cref="CompositorWait"/>, the piece is drawn by a
    /// render target after all, and <paramref name="fellBack"/> says so, since that picture may lack such text.
    /// </para>
    /// </remarks>
    private static async Task<Bitmap> DrawnAsync(Visual visual, PixelSize size, double scaling, List<string> fellBack)
    {
        var waited = Task.Delay(CompositorWait);
        while (!waited.IsCompleted)
        {
            var drawing = ElementComposition.GetElementVisual(visual) is { } composition
                ? composition.Compositor.CreateCompositionVisualSnapshot(composition, scaling)
                : null;
            if (drawing is not null)
                await Task.WhenAny(drawing, waited);
            if (drawing is { IsCompletedSuccessfully: true })
                return drawing.Result;
            // In no frame the compositor has drawn yet, as a window shown a moment ago is not, so it has no visual or one
            // with no root, which the snapshot refuses: try again after the next frame.
            if (drawing is null || drawing.Exception?.GetBaseException() is InvalidOperationException)
            {
                if (TopLevel.GetTopLevel(visual) is not { } top)
                    break;
                var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                top.RequestAnimationFrame(_ => frame.TrySetResult());
                await Task.WhenAny(frame.Task, waited);
                continue;
            }
            if (drawing.Exception?.GetBaseException() is { } failed)
            {
                fellBack.Add($"{KindOf(visual)}, whose snapshot failed: {failed.Message}");
                return Rendered(visual, size, scaling);
            }
            // A late image is let go when it comes, rather than kept by nobody.
            _ = drawing.ContinueWith(late =>
            {
                if (late.IsCompletedSuccessfully)
                    late.Result.Dispose();
            }, TaskScheduler.Default);
            break;
        }
        fellBack.Add(KindOf(visual));
        return Rendered(visual, size, scaling);
    }

    private static RenderTargetBitmap Rendered(Visual visual, PixelSize size, double scaling)
    {
        var bitmap = new RenderTargetBitmap(size, new Vector(96 * scaling, 96 * scaling));
        bitmap.Render(visual);
        return bitmap;
    }

    private static string? FallbackNote(List<string> fellBack) => fellBack.Count == 0 ? null
        : $"The compositor gave no picture of {string.Join(" and ", fellBack.Distinct().Select(k => $"'{k}'"))} within "
          + $"{CompositorWait.TotalSeconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} s, so a render target drew it instead. "
          + "Above 100% scaling that can leave out text a control draws after popping a clip, so check missing text against "
          + "the screen.";

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
