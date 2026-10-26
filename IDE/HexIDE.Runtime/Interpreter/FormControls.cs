using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// A form's <c>Controls</c> (#1060): <c>Count</c>, <c>Item</c> (its default member) and For Each. Measured with vb6.exe,
/// native and p-code alike (the oracle's "A form's Controls collection"):
/// <list type="bullet">
/// <item>It holds every control on the form, in the order the file holds their blocks: a container before what it holds,
/// each element of a control array where its own block is, a control HexIDE keeps as text where it was read, and the menus
/// and their submenus. <c>ZOrder</c> doesn't change it. An element loaded at run time goes at the end, and one unloaded
/// goes out.</item>
/// <item>An index is a position from 0, converted as CInt converts: 2.5 is 2 and 100000 is 6. A String is a position too
/// when it starts with a digit (<c>"1e0"</c> is 1), and otherwise a name, found in any case: a control array's name gives
/// the array. A position out of range is 9, a name not there 730, <c>""</c> 728 and Null 13.</item>
/// <item>A For Each goes on from the control it is on, whatever is unloaded: it doesn't reach one unloaded ahead of it, nor
/// one loaded while it runs.</item>
/// </list>
/// </summary>
public sealed class FormControls : ICSharpAsyncProxy
{
    private readonly List<Control> controls = [];
    private readonly IReadOnlyDictionary<string, ControlArrayGroup> arrays;

    /// <param name="inFileOrder">The form's controls, in the order its file holds their blocks.</param>
    /// <param name="arrays">The form's control arrays, by name.</param>
    public FormControls(IEnumerable<Control> inFileOrder, IReadOnlyDictionary<string, ControlArrayGroup> arrays)
    {
        controls.AddRange(inFileOrder);
        this.arrays = arrays;
        foreach (var control in controls)
            Owners.AddOrUpdate(control, this);
    }

    // The form each control is on, which a TabIndex set renumbers (#1974).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, FormControls> Owners = new();

    /// <summary>The Controls of the form <paramref name="control"/> is on.</summary>
    internal static bool TryOf(Control control, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out FormControls? form) =>
        Owners.TryGetValue(control, out form);

    public int Count => controls.Count;

    /// <summary>The form's controls, in the order <c>Controls</c> holds them.</summary>
    internal IReadOnlyList<Control> All => controls;

    /// <summary>An element loaded at run time, which goes at the end (measured, menus' and controls' alike).</summary>
    internal void Loaded(Control control)
    {
        controls.Add(control);
        Owners.AddOrUpdate(control, this);
    }

    /// <summary>An element unloaded at run time, which goes out.</summary>
    internal void Unloaded(Control control)
    {
        controls.Remove(control);
        Owners.Remove(control);
    }

    /// <summary>The default member, <c>Controls(i)</c>, <c>Controls("Name")</c> and <c>Controls!Name</c>: Item.</summary>
    internal Vb6Value Item(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        var index = args[0];
        if (index.IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (index.Type == Vb6Value.ValueType.String && index.Value is string name && !(name.Length > 0 && char.IsAsciiDigit(name[0])))
            return Named(name);
        var position = Convert.ToInt32(VB6BuiltIns.NarrowNum(index, Vb6Value.ValueType.Integer).Value);
        return position >= 0 && position < controls.Count
            ? new Vb6Value(controls[position])
            : throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
    }

    private Vb6Value Named(string name)
    {
        if (name.Length == 0)
            throw new VBRunTimeException(VBStandardError.NotALegalControlName(name));
        if (arrays.TryGetValue(name, out var array))
            return new Vb6Value(array);
        foreach (var control in controls)
            if (string.Equals(VBProps.GetName(control), name, StringComparison.OrdinalIgnoreCase))
                return new Vb6Value(control);
        throw new VBRunTimeException(VBStandardError.ControlNotFound(name));
    }

    /// <summary>
    /// Where a For Each over it has got to: the controls it held when the loop began, and how far through them it is. A
    /// control no longer on the form when the loop reaches it is passed over, and one loaded since is not among them.
    /// </summary>
    internal sealed class Cursor(List<Control> remaining)
    {
        public readonly List<Control> Remaining = remaining;
        public int Position;
    }

    internal Cursor StartEnumeration() => new([.. controls]);

    internal bool TryNext(Cursor cursor, out Vb6Value item)
    {
        while (cursor.Position < cursor.Remaining.Count)
        {
            var control = cursor.Remaining[cursor.Position++];
            // An element unloaded ahead of the loop is passed over, but a control Controls.Remove took out is still handed
            // out, and using it is 735 (measured, #1556).
            if (!controls.Contains(control) && !VBProps.GetRemoved(control))
                continue;
            item = new Vb6Value(control);
            return true;
        }
        item = default;
        return false;
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException("Controls calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) =>
        Task.FromResult<Vb6Value?>(method.ToLowerInvariant() switch
        {
            "count" when args.Count == 0 => new Vb6Value((int)controls.Count),
            "count" => throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment),
            "item" => Item(args),
            "add" => Add(args),
            "remove" => Remove(args),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        });

    /// <summary>
    /// Makes a control for <c>Controls.Add</c>: of its class, by its name, in the container's own host, or on the form where
    /// that is null. The loader sets it, since it knows the form.
    /// </summary>
    internal Func<ComponentBaseClass, string, Control?, Control>? Maker { get; set; }

    // The Frame or PictureBox each control added into one was added into. A container's children sit on its child host,
    // which is a part of its template, so the tree does not lead from one to the other.
    private readonly Dictionary<Control, Control> containers = [];

    // The VB library's classes that are no controls, which Add refuses by their own name (measured).
    private static readonly HashSet<string> NotControls = new(StringComparer.OrdinalIgnoreCase)
        { "VB.Form", "VB.MDIForm", "VB.Menu", "VB.UserControl" };

    // The VB library's other classes, whose answer to Add was not measured, and its controls HexIDE doesn't implement.
    private static readonly HashSet<string> NotMeasuredOrNotImplemented = new(StringComparer.OrdinalIgnoreCase)
    {
        "VB.PropertyPage", "VB.UserDocument", "VB.App", "VB.Clipboard", "VB.Forms", "VB.Global", "VB.LicenseInfo", "VB.Licenses",
        "VB.Printer", "VB.Printers", "VB.Screen", "VB.Controls", "VB.VBControlExtender",
        "VB.DriveListBox", "VB.DirListBox", "VB.FileListBox", "VB.Data", "VB.OLE",
    };

    /// <summary>
    /// <c>Controls.Add(ProgID, Name[, Container])</c> (#1556), measured with vb6.exe, native and p-code alike (the oracle's
    /// "Controls.Add and Controls.Remove"). The name is checked first, then the class, then the container: 727 for a name
    /// a control on the form has in any case, 728 for <c>""</c>, and no other check on it; 711 for a class nothing is
    /// registered for, 720 for one that is no control; 721 for a container that holds no controls. Too few or too many
    /// arguments, and Null or a String where an object is wanted, are the late-bound call's raw HRESULTs.
    /// </summary>
    private Vb6Value Add(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count is < 2 or > 3)
            throw new VBRunTimeException(VBStandardError.DispatchWrongNumberOfArguments);
        var progId = TextArgument(args[0], "class");
        var name = TextArgument(args[1], "name");

        if (name.Length == 0)
            throw new VBRunTimeException(VBStandardError.NotALegalControlName(name));
        if (arrays.ContainsKey(name) || controls.Exists(c => string.Equals(VBProps.GetName(c), name, StringComparison.OrdinalIgnoreCase)))
            throw new VBRunTimeException(VBStandardError.ControlNameTaken(name));

        if (NotControls.Contains(progId))
            throw new VBRunTimeException(VBStandardError.NotAValidControlType(progId[(progId.IndexOf('.') + 1)..]));
        if (NotMeasuredOrNotImplemented.Contains(progId))
            throw new NotImplementedException($"Controls.Add of {progId} is not implemented (#1556)");
        if (progId.Length == 0
            || progId.StartsWith("VB.", StringComparison.OrdinalIgnoreCase) && !Serialization.FormDeserializer.TryGetComponentClass(progId, out _))
            throw new VBRunTimeException(VBStandardError.InvalidClassString(progId));
        // Anything else is an ActiveX ProgID, which vb6.exe answers from the registry: 711 when nothing is registered for it,
        // 720 for Scripting.Dictionary, and an unmeasured answer for a control the project references.
        if (!progId.StartsWith("VB.", StringComparison.OrdinalIgnoreCase)
            || !Serialization.FormDeserializer.TryGetComponentClass(progId, out var componentClass))
            throw new NotImplementedException($"Controls.Add of an ActiveX control, {progId}, is not implemented (#1556)");

        var container = args.Count == 3 ? ContainerArgument(args[2]) : null;
        var control = (Maker ?? throw new NotImplementedException("Controls.Add on this form is not implemented (#1556)"))
            (componentClass, name, container);
        VBProps.SetAddedAtRunTime(control, true);
        if (container is not null)
            containers[control] = container;
        // Last in the tab order, one past the highest on the form (measured), where its class has a TabIndex.
        if (componentClass.Properties.Contains(VBProperties.TabIndexProperty))
            KeyboardNavigation.SetTabIndex(control,
                controls.Where(HasTabIndex).Select(KeyboardNavigation.GetTabIndex).DefaultIfEmpty(-1).Max() + 1);
        controls.Add(control);
        return new Vb6Value(control);
    }

    // A class name or a control name, as Add reads it: a String as it is, a number as its text, Empty as "".
    private static string TextArgument(Vb6Value value, string which)
    {
        if (value.IsNull)
            throw new VBRunTimeException(VBStandardError.DispatchTypeMismatch);
        if (value.Type == Vb6Value.ValueType.String)
            return (string)value.Value!;
        if (value.Type == Vb6Value.ValueType.EmptyVariant)
            return "";
        if (Vb6Value.TryNumericToDouble(value, out _) && value.Type != Vb6Value.ValueType.Date)
            return VbDisplay.ToText(value);
        throw new NotImplementedException($"Controls.Add with a {value.Type} for its {which} is not implemented (#1556)");
    }

    // Add's third argument: a Frame or a PictureBox, or the form itself, which is where the control goes without one.
    private Control? ContainerArgument(Vb6Value value)
    {
        if (value.Value is VbObject form)
            return ReferenceEquals(form.Controls, this)
                ? null
                : throw new NotImplementedException("Controls.Add into another form is not implemented (#1556)");
        if (value.Value is Control control)
        {
            if (!controls.Contains(control))
                throw new NotImplementedException("Controls.Add into another form's control is not implemented (#1556)");
            return control is VBFrame or VBPictureBox
                ? control
                : throw new VBRunTimeException(VBStandardError.NotAValidControlContainer(VB6BuiltIns.DebugTypeName(value)));
        }
        if (value.Type == Vb6Value.ValueType.Object && value.Value is null)
            throw new VBRunTimeException(VBStandardError.NotAValidControlContainer("<Nothing>"));
        if (value.Type == Vb6Value.ValueType.String)
            throw new VBRunTimeException(VBStandardError.DispatchTypeMismatch);
        throw new NotImplementedException($"Controls.Add into a {value.Type} is not implemented (#1556)");
    }

    // Whether a control has a place in the tab order: an Image, Shape, Line, Timer, menu or CommonDialog has none.
    private static bool HasTabIndex(Control control) => control is not (VBImage or VBShape or VBLine or VBTimer or VBMenu or VBCommonDialog);

    /// <summary>
    /// <c>Controls.Remove(Index)</c> (#1556): a control added with Add, by its name in any case, the control itself, or its
    /// position, as <c>Controls(i)</c> reads one. A design-time control is 729, a name not there 730, <c>""</c> 728, and a
    /// position out of range 9. A container goes with everything in it, every control taken out closes its gap in the tab
    /// order, and a variable still holding one can no longer use it (735).
    /// </summary>
    private Vb6Value? Remove(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new VBRunTimeException(VBStandardError.DispatchWrongNumberOfArguments);
        var target = RemoveTarget(args[0]);
        if (!VBProps.GetAddedAtRunTime(target))
            throw new VBRunTimeException(VBStandardError.OnlyAddedControlsCanBeRemoved);
        (target.Parent as Panel)?.Children.Remove(target);
        // What it holds first, then itself.
        foreach (var gone in controls.Where(c => !ReferenceEquals(c, target) && IsInside(c, target)).Append(target).ToList())
        {
            controls.Remove(gone);
            containers.Remove(gone);
            VBProps.SetRemoved(gone, true);
            if (!HasTabIndex(gone))
                continue;
            var tab = KeyboardNavigation.GetTabIndex(gone);
            foreach (var other in controls.Where(c => HasTabIndex(c) && KeyboardNavigation.GetTabIndex(c) > tab))
                KeyboardNavigation.SetTabIndex(other, KeyboardNavigation.GetTabIndex(other) - 1);
        }
        return null;
    }

    private Control RemoveTarget(Vb6Value what)
    {
        if (what.IsNull)
            throw new VBRunTimeException(VBStandardError.DispatchTypeMismatch);
        if (what.Value is Control control)
            return controls.Contains(control)
                ? control
                : throw new NotImplementedException("Controls.Remove of a control not on this form is not implemented (#1556)");
        if (what.Type == Vb6Value.ValueType.String && what.Value is string name && !(name.Length > 0 && char.IsAsciiDigit(name[0])))
        {
            // A control array's name is the late-bound E_NOINTERFACE (measured).
            if (name.Length > 0 && arrays.ContainsKey(name))
                throw new VBRunTimeException(VBStandardError.NoSuchInterfaceSupported);
            return (Control)Named(name).Value!;
        }
        if (what.Type != Vb6Value.ValueType.String && !Vb6Value.TryNumericToDouble(what, out _))
            throw new NotImplementedException($"Controls.Remove of a {what.Type} is not implemented (#1556)");
        int position;
        try
        {
            position = Convert.ToInt32(VB6BuiltIns.NarrowNum(what, Vb6Value.ValueType.Integer).Value);
        }
        // "1abc" is a position that doesn't convert, which the late-bound call answers as its raw HRESULT (measured).
        catch (VBRunTimeException e) when (e.Error.ErrNo == VBStandardError.TypeMismatch.ErrNo)
        {
            throw new VBRunTimeException(VBStandardError.DispatchTypeMismatch);
        }
        return position >= 0 && position < controls.Count
            ? controls[position]
            : throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
    }

    private bool IsInside(Control control, Control container)
    {
        for (var holder = control; containers.TryGetValue(holder, out var next); holder = next)
            if (ReferenceEquals(next, container))
                return true;
        return false;
    }
}
