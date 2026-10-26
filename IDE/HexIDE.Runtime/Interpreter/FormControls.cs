using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
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
            if (!controls.Contains(control))
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
            "add" or "remove" => throw new NotImplementedException(
                $"Controls.{method} is not implemented: adding a control by its class name at run time is #1556"),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        });
}
