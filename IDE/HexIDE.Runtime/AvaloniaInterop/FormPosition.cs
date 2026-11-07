using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>A form's outer position, always in twips, retaining a written twip between whole pixels (#852).</summary>
internal static class FormPosition
{
    private sealed class State
    {
        public (double Twips, int Pixel)? Left;
        public (double Twips, int Pixel)? Top;
    }

    private static readonly ConditionalWeakTable<Window, State> states = new();

    public static double Read(Window window, bool vertical)
    {
        var state = states.GetOrCreateValue(window);
        var pixel = vertical ? window.Position.Y : window.Position.X;
        var assigned = vertical ? state.Top : state.Left;
        return assigned is { } value && value.Pixel == pixel ? value.Twips : Twips(pixel, window.DesktopScaling);
    }

    public static void Write(Window window, bool vertical, double pixels)
    {
        var pixel = ScreenPixel(pixels, window.DesktopScaling);
        window.Position = vertical ? new PixelPoint(window.Position.X, pixel) : new PixelPoint(pixel, window.Position.Y);
        var state = states.GetOrCreateValue(window);
        var value = (pixels * VBScaleModeExtensions.PixelToTwips, pixel);
        if (vertical) state.Top = value;
        else state.Left = value;
    }

    public static void FromClient(Window window, double left, double top, FormChrome chrome, bool menu)
    {
        var frame = FormFrame.Of(chrome.BorderStyle, chrome.CaptionBar, menu);
        var border = frame.Across / 2;
        Write(window, false, (int)left - border / VBScaleModeExtensions.PixelToTwips);
        Write(window, true, (int)top - (frame.Down - border) / VBScaleModeExtensions.PixelToTwips);
    }

    public static void Center(Window window)
    {
        if (window.Screens.Primary is not { } screen) return;
        Write(window, false, Centred(screen.Bounds.X, screen.Bounds.Width, FormFrame.Read(window, false), screen.Scaling));
        Write(window, true, Centred(screen.Bounds.Y, screen.Bounds.Height, FormFrame.Read(window, true), screen.Scaling));
    }

    // A twip is a fifteenth of a device-independent pixel, as Screen's size is, while a window's Position is in the
    // screen's own pixels. The two agree only at 100% scaling, so every crossing between them goes through these.

    /// <summary>A position in device-independent pixels, as the screen pixel the window goes to.</summary>
    internal static int ScreenPixel(double pixels, double scaling) => (int)Math.Round(pixels * scaling);

    /// <summary>A screen pixel, as twips.</summary>
    internal static double Twips(int screenPixel, double scaling) => screenPixel / scaling * VBScaleModeExtensions.PixelToTwips;

    /// <summary>
    /// Where a window whose outer extent is <paramref name="outerTwips"/> starts when centred on a screen, in whole
    /// device-independent pixels: CenterScreen centres in whole pixels (measured, #852).
    /// </summary>
    internal static double Centred(int screenOrigin, int screenExtent, double outerTwips, double scaling) =>
        screenOrigin / scaling + Math.Floor((screenExtent / scaling - outerTwips / VBScaleModeExtensions.PixelToTwips) / 2);
}
