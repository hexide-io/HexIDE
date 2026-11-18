using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;

namespace HexIDE.Automation;

internal static class WaylandToolTips
{
    private static bool initialized;
    private static readonly ConditionalWeakTable<Control, IDisposable> placements = new();

    internal static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        ToolTip.ToolTipOpeningEvent.AddClassHandler<Control>((control, _) =>
        {
            // Work around AvaloniaUI/Avalonia#22780: Pointer omits Wayland's client-side decoration inset.
            // Use a style-priority value only while open, so a control's own placement still wins.
            if (!control.IsSet(ToolTip.PlacementProperty) && TopLevel.GetTopLevel(control) is { } top && NativeWayland.Hosts(top))
                placements.Add(control, control.SetValue(ToolTip.PlacementProperty, PlacementMode.Bottom, BindingPriority.Style)!);
        });
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (!ToolTip.GetIsOpen(control) && placements.TryGetValue(control, out var placement))
            {
                placement.Dispose();
                placements.Remove(control);
            }
        });
    }
}
