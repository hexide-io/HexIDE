using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// <c>Screen.MousePointer</c> over a form's window (#2431): while it is not 0, the window and every control in it show that
/// pointer, over the pointer a control's theme gives it and over the one its own MousePointer sets, as VB6's does. It is
/// given above a control's own value, so taking it away leaves each control's as it was. A control made after it was set,
/// by <c>Load</c> or <c>Controls.Add</c>, shows its own until the pointer is next set.
/// </summary>
internal static class ScreenPointer
{
    private static readonly ConditionalWeakTable<Control, List<IDisposable>> Applied = new();

    internal static void Apply(Control window, VBCursorType pointer)
    {
        if (Applied.TryGetValue(window, out var previous))
        {
            foreach (var undo in previous)
                undo.Dispose();
            Applied.Remove(window);
        }
        if (pointer == VBCursorType.Default)
            return;
        var cursor = pointer.ToCursor();
        var given = new List<IDisposable>();
        foreach (var control in window.GetSelfAndLogicalDescendants().OfType<Control>())
            if (control.SetValue(InputElement.CursorProperty, cursor, BindingPriority.Animation) is { } disposable)
                given.Add(disposable);
        Applied.Add(window, given);
    }
}
