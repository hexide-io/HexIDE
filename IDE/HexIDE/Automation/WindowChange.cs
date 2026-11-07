using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

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

        var moved = x is null && y is null || (x ?? window.Position.X) == window.Position.X && (y ?? window.Position.Y) == window.Position.Y;
        var resized = width is null && height is null || (width ?? window.ClientSize.Width) == window.ClientSize.Width && (height ?? window.ClientSize.Height) == window.ClientSize.Height;
        void OnMoved(object? sender, PixelPointEventArgs e) => moved = true;
        void OnResized(object? sender, WindowResizedEventArgs e) => resized = true;
        window.PositionChanged += OnMoved;
        window.Resized += OnResized;
        try
        {
            if (x is not null || y is not null)
                window.Position = new PixelPoint(x ?? window.Position.X, y ?? window.Position.Y);
            if (width is not null) window.Width = width.Value;
            if (height is not null) window.Height = height.Value;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            return await WaitAsync(() => window.WindowState == target && moved && resized, ct);
        }
        finally
        {
            window.PositionChanged -= OnMoved;
            window.Resized -= OnResized;
        }
    }
}
