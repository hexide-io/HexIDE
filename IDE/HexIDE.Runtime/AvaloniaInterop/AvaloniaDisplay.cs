using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// The <c>Screen</c> object's host on the desktop (#2431): the primary screen of the top level the program's windows belong to,
/// in device-independent pixels, and the font families the platform's font manager lists.
/// </summary>
public sealed class AvaloniaDisplay(Func<TopLevel?> topLevel) : IVbDisplay
{
    private IReadOnlyList<string>? fonts;

    public Avalonia.Size Size => topLevel()?.Screens?.Primary is { } primary
        ? primary.Bounds.Size.ToSize(primary.Scaling)
        : throw new NotImplementedException("There is no screen for the Screen object to describe (#2431)");

    public IReadOnlyList<string> Fonts => fonts ??= [.. FontManager.Current.SystemFonts.Select(family => family.Name)];
}
