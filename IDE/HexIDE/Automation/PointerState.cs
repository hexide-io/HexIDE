using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Diagnostics;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Automation;

/// <summary>Where the pointer a control shows is set (#2453).</summary>
public enum PointerSource
{
    /// <summary>A value of its own, as a control's MousePointer sets it.</summary>
    Own,

    /// <summary>A value set above its own, as a running program's Screen.MousePointer sets it over every control.</summary>
    Override,

    /// <summary>A style or template, as a TextBox's theme gives it the I-beam.</summary>
    Style,
}

/// <summary>
/// The mouse pointer a control shows, as <c>inspect_element</c> reports it (#2453): a running program's MousePointer by
/// number and VB6 name, what Avalonia draws for it, and where it is set.
/// </summary>
/// <param name="MousePointer">The MousePointer it was made from, or null for a pointer the runtime did not make.</param>
/// <param name="Name">That MousePointer's VB6 name, such as "Hourglass".</param>
/// <param name="Shows">What is drawn, as Avalonia's standard pointer, such as "Wait"; null when that is not recorded.</param>
/// <param name="SetOn">"self", or the path of the control it is set on, which it inherits from.</param>
/// <param name="Note">Why the kind is not given, for a pointer the runtime did not make.</param>
public sealed record PointerState(int? MousePointer, string? Name, string? Shows, PointerSource? SetBy, string? SetOn, string? Note);

internal static class PointerReader
{
    /// <summary>
    /// The pointer <paramref name="control"/> shows, or null when none is set on it or anything it is inside, so the
    /// platform's arrow shows. Avalonia's pointer is inherited, so the one drawn may be set further up; it is named by
    /// <paramref name="pathOf"/>.
    /// </summary>
    internal static PointerState? Read(Control control, Func<Control, string?> pathOf)
    {
        if (control.Cursor is not { } cursor)
            return null;
        Control? at = null;
        var priority = BindingPriority.Unset;
        foreach (var candidate in control.GetSelfAndVisualAncestors().OfType<Control>())
        {
            var set = candidate.GetDiagnostic(InputElement.CursorProperty).Priority;
            if (set is BindingPriority.Inherited or BindingPriority.Unset)
                continue;
            (at, priority) = (candidate, set);
            break;
        }
        var setBy = priority switch
        {
            BindingPriority.LocalValue => PointerSource.Own,
            BindingPriority.Animation => PointerSource.Override,
            BindingPriority.Style or BindingPriority.StyleTrigger or BindingPriority.Template => PointerSource.Style,
            _ => (PointerSource?)null,
        };
        var setOn = at is null ? null : ReferenceEquals(at, control) ? "self" : pathOf(at);
        return VBCursorTypeExtensions.PointerOf(cursor) is { } pointer
            ? new PointerState((int)pointer, Vb6EnumNames.For(pointer), pointer.ToStandardCursorType().ToString(), setBy, setOn, null)
            : new PointerState(null, null, null, setBy, setOn,
                "A pointer HexIDE's own UI or a control's theme sets, whose kind is not recorded; only a running "
                + "program's MousePointer is.");
    }
}
