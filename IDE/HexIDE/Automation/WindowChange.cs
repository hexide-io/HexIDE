using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Layout;

namespace HexIDE.Automation;

public static class WindowChange
{
    internal static async Task<bool> WaitAsync(Func<bool> confirmed, CancellationToken ct, int milliseconds = 1000)
    {
        var elapsed = Stopwatch.StartNew();
        do
        {
            ct.ThrowIfCancellationRequested();
            if (confirmed()) return true;
            await Task.Delay(10, ct);
        } while (elapsed.ElapsedMilliseconds < milliseconds);
        return confirmed();
    }

    public static async Task<bool> ApplyAsync(Window window, WindowState target,
        int? x, int? y, int? width, int? height, CancellationToken ct)
    {
        window.WindowState = target;
        // X11 can raise a synthetic property change carrying the OLD state. Read the getter instead.
        if (!await WaitAsync(() => window.WindowState == target, ct)) return false;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        if (x is null && y is null && width is null && height is null) return true;

        var position = new PixelPoint(x ?? window.Position.X, y ?? window.Position.Y);
        double? allowedWidth = width;
        double? allowedHeight = height;
        if ((width is not null || height is not null)
            && (window.Screens.ScreenFromPoint(position) ?? window.Screens.ScreenFromWindow(window)) is { } screen)
        {
            // Clamp before layout sees the request: Dock can retain proportions from even one oversized pass (#4000).
            var area = screen.WorkingArea;
            var frame = window.FrameSize ?? window.ClientSize;
            var borderWidth = Math.Max(0, frame.Width - window.ClientSize.Width);
            var borderHeight = Math.Max(0, frame.Height - window.ClientSize.Height);
            if (width is not null)
                allowedWidth = Math.Min(width.Value, Math.Max(1, (area.Right - Math.Max(area.X, position.X)) / screen.Scaling - borderWidth));
            if (height is not null)
                allowedHeight = Math.Min(height.Value, Math.Max(1, (area.Bottom - Math.Max(area.Y, position.Y)) / screen.Scaling - borderHeight));
        }

        var moved = x is null && y is null || (x ?? window.Position.X) == window.Position.X && (y ?? window.Position.Y) == window.Position.Y;
        var resized = width is null && height is null || (allowedWidth ?? window.ClientSize.Width) == window.ClientSize.Width && (allowedHeight ?? window.ClientSize.Height) == window.ClientSize.Height;
        void OnMoved(object? sender, PixelPointEventArgs e) => moved = true;
        void OnResized(object? sender, WindowResizedEventArgs e) => resized = true;
        window.PositionChanged += OnMoved;
        window.Resized += OnResized;
        try
        {
            if (x is not null || y is not null)
                window.Position = position;
            if (allowedWidth is not null) window.Width = allowedWidth.Value;
            if (allowedHeight is not null) window.Height = allowedHeight.Value;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            var applied = await WaitAsync(() => window.WindowState == target && moved && resized, ct);
            if (applied && (width is not null || height is not null))
            {
                // A repeated native size can leave explicit layout dimensions at an oversized request (#4000).
                var actual = window.ClientSize;
                if (window.Width != actual.Width || window.Height != actual.Height)
                {
                    window.Width = actual.Width;
                    window.Height = actual.Height;
                    LayoutHelper.InvalidateSelfAndChildrenMeasure(window);
                    window.UpdateLayout();
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                }
            }
            return applied;
        }
        finally
        {
            window.PositionChanged -= OnMoved;
            window.Resized -= OnResized;
        }
    }
}
