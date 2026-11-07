using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// Container is the object owning the placement canvas; Parent is always the form (#2064, native and p-code).
internal static class ContainerMembers
{
    private sealed record Owner(Vb6Value Value);
    private static readonly ConditionalWeakTable<Canvas, Owner> Containers = new();

    public static void Register(Canvas host, Vb6Value owner) => Containers.AddOrUpdate(host, new Owner(owner));

    public static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (member.Equals("Parent", StringComparison.OrdinalIgnoreCase)
            && FormControls.TryOf(control, out var controls) && controls.Form is { } form)
        {
            value = Vb6Value.NewObject(form);
            return true;
        }
        // The Timer/Data errors are unexplained; menu containment was not measured for #2064.
        if (member.Equals("Container", StringComparison.OrdinalIgnoreCase)
            && control is not VBTimer && !MenuMembers.IsMenu(control)
            && control.Parent is Canvas host && Containers.TryGetValue(host, out var owner))
        {
            value = owner.Value;
            return true;
        }
        return false;
    }
}
