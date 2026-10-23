using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// The frame VB6 draws round a form's client area, which the form's Width and Height include at run time (#1072), and
/// the size a program last assigned them.
/// </summary>
/// <remarks>
/// <para>
/// Measured on vb6.exe, built EXEs on the themed Windows 10 desktop at 96 dpi, native and p-code agreeing (the oracle's "A
/// form's Width and Height: the frame round its client area"). In twips:
/// <list type="bullet">
/// <item>the border, across and down: 240 for Sizable and Sizable ToolWindow, 90 for the fixed styles, and none for None;</item>
/// <item>a caption bar, 345 more down, unless ControlBox is False and the Caption is empty. Then Fixed Single and Fixed
/// ToolWindow have a border of 30;</item>
/// <item>a menu bar, 300 more down. A borderless form with a menu has Fixed Single's border and caption bar.</item>
/// </list>
/// </para>
/// <para>
/// These are the themed desktop's values, which is what a user's desktop draws. Session 0's unthemed frame is smaller: 120
/// across a Sizable form, 375 down a Fixed Single one, and a menu bar of 285. A classic-theme profile would supply those
/// here. The frame is VB6's, not the window's: HexIDE draws every form as a sizable window (#1575), and a frame that
/// changed across Show, or between hosts, would give a program different numbers from one run to the next.
/// </para>
/// </remarks>
internal static class FormFrame
{
    private sealed class State
    {
        public (double Across, double Down) Frame = Of(borderStyle: 2, captionBar: true, menu: false);

        // The value a program last assigned Width and Height, and the client size in pixels it gave the window: VB6 reads
        // back what was assigned while the window keeps that size (measured: 4000 reads 4000 on a window of 267 pixels).
        public (double Twips, double Client)? Width;
        public (double Twips, double Client)? Height;
    }

    private static readonly ConditionalWeakTable<Window, State> states = new();

    private const double CaptionBar = 345;
    private const double MenuBar = 300;

    /// <summary>The frame for these properties, across and down, in twips.</summary>
    public static (double Across, double Down) Of(int borderStyle, bool captionBar, bool menu)
    {
        if (borderStyle == 0)
        {
            if (!menu)
                return (0, 0);
            (borderStyle, captionBar) = (1, true);
        }
        var border = borderStyle switch
        {
            2 or 5 => 240,
            1 or 4 when !captionBar => 30,
            _ => 90,
        };
        return (border, border + (captionBar ? CaptionBar : 0) + (menu ? MenuBar : 0));
    }

    /// <summary>Give <paramref name="window"/> the frame its form's file describes.</summary>
    public static void Set(Window window, ComponentInstance form, bool menu)
    {
        var borderStyle = VBLoader.RawNumber(form, "BorderStyle") ?? 2;
        var captionBar = VBLoader.RawNumber(form, "ControlBox") != 0
            || !string.IsNullOrEmpty(form.GetPropertyOrDefault(VBProperties.CaptionProperty));
        states.GetOrCreateValue(window).Frame = Of(borderStyle, captionBar, menu);
    }

    /// <summary>The form's Width or Height, in twips: what was assigned, or the client area and the frame.</summary>
    public static double Read(Window window, bool vertical)
    {
        var state = states.GetOrCreateValue(window);
        var client = ControlPositions.ClientPixels(window, vertical);
        if ((vertical ? state.Height : state.Width) is { } assigned && assigned.Client == client)
            return assigned.Twips;
        return client * VBScaleModeExtensions.PixelToTwips + (vertical ? state.Frame.Down : state.Frame.Across);
    }

    /// <summary>
    /// The client size, in pixels, that an assigned Width or Height of <paramref name="twips"/> leaves: the window is a whole
    /// number of pixels, and the frame comes off it. Remembered, so the value reads back as assigned.
    /// </summary>
    public static double ClientFor(Window window, bool vertical, double twips)
    {
        var state = states.GetOrCreateValue(window);
        var frame = (vertical ? state.Frame.Down : state.Frame.Across) / VBScaleModeExtensions.PixelToTwips;
        var client = Math.Max(0, Math.Round(twips / VBScaleModeExtensions.PixelToTwips) - frame);
        if (vertical)
            state.Height = (twips, client);
        else
            state.Width = (twips, client);
        return client;
    }
}
