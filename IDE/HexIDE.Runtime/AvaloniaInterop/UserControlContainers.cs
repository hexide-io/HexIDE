using System;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// #1631 part C, issuecomment-6425849231: Container keeps coordinates, and an ineligible UC is 425.
internal static class UserControlContainers
{
    private sealed record Holder(Control? Owner);
    private static readonly ConditionalWeakTable<Control, Holder> Owners = new();

    internal static void Register(Control control, Control? owner) => Owners.AddOrUpdate(control, new(owner));

    internal static bool TryGet(Control control, string member, out Vb6Value value)
    {
        value = default;
        if (!member.Equals("Container", StringComparison.OrdinalIgnoreCase) || !Owners.TryGetValue(control, out var holder)
            || holder.Owner is not VBUserControl)
            return false;
        value = new Vb6Value(holder.Owner);
        return true;
    }

    internal static bool TrySet(Control control, string member, Vb6Value value)
    {
        if (!member.Equals("Container", StringComparison.OrdinalIgnoreCase) || value.Value is not VBUserControl uc)
            return false;
        if (!UserControlMembers.IsContainer(uc))
            throw new VBRunTimeException(VBStandardError.InvalidObjectUse);
        if (control.Parent is not Panel old)
            throw new NotImplementedException("Container assignment from this host is not implemented (#1631)");
        old.Children.Remove(control);
        uc.ContainerHost.Children.Add(control);
        Register(control, uc);
        return true;
    }
}
