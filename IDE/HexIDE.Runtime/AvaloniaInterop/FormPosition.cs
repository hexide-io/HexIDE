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
        return assigned is { } value && value.Pixel == pixel ? value.Twips : pixel * VBScaleModeExtensions.PixelToTwips;
    }

    public static void Write(Window window, bool vertical, double pixels)
    {
        var pixel = (int)Math.Round(pixels);
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
        states.Remove(window);
        window.Position = new PixelPoint(
            screen.Bounds.X + (int)Math.Floor((screen.Bounds.Width - FormFrame.Read(window, false) / VBScaleModeExtensions.PixelToTwips) / 2),
            screen.Bounds.Y + (int)Math.Floor((screen.Bounds.Height - FormFrame.Read(window, true) / VBScaleModeExtensions.PixelToTwips) / 2));
    }
}
