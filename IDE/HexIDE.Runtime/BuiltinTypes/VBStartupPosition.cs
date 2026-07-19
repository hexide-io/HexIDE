using System;
using Avalonia.Controls;

namespace HexIDE.Runtime.BuiltinTypes;

public static class VBStartupPositionExtensions
{
    public static WindowStartupLocation ToAvalonia(this VBStartupPosition position)
    {
        return position switch
        {
            VBStartupPosition.StartUpManual => WindowStartupLocation.Manual,
            VBStartupPosition.StartUpOwner => WindowStartupLocation.CenterOwner,
            VBStartupPosition.StartUpScreen => WindowStartupLocation.CenterScreen,
            VBStartupPosition.StartUpWindowsDefault => WindowStartupLocation.CenterScreen,
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
        };
    }
}