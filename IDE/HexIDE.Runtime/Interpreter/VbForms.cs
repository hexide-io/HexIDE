using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The global <c>Forms</c> (#2496): <c>Count</c>, <c>Item</c> (its default member) and For Each. Measured with vb6.exe, native
/// and p-code alike (the oracle's "The Forms collection"):
/// <list type="bullet">
/// <item>It holds the program's loaded forms in the order they loaded. Show, Hide and ZOrder don't move one; an unload takes
/// it out and a later load puts it at the end. An instance made and not loaded isn't held. A form is held from its own Load
/// to the end of its own Unload, as <see cref="VBProgram"/>'s loaded forms are.</item>
/// <item>An index is a position from 0, converted as CInt converts: 1.5 and 2.5 are both 2, <c>"1"</c> is 1 and 70000 is 6.
/// There are no keys, so any other String is 13, and so is Null. A position out of range is 9.</item>
/// <item>A For Each walks the forms held when it began: it hands out one unloaded ahead of it, which a form's Controls does
/// not, and doesn't reach one loaded while it runs.</item>
/// <item>Late-bound, <c>Add</c> is 424 and any other member 438.</item>
/// </list>
/// </summary>
public sealed class VbForms(BasicInterpreter interpreter) : ICSharpAsyncProxy
{
    /// <summary>
    /// The loaded forms, in the order they loaded. A run with no program of forms, as a test's bare interpreter is, has none
    /// when it has no form module either; one that has a form module and no program to load it in is refused, rather than
    /// answering a count that may be wrong.
    /// </summary>
    private IReadOnlyList<VbObject> Loaded =>
        interpreter.Forms?.LoadedForms
        ?? (interpreter.Modules.All.Any(m => m.Kind == InterpreterModuleKind.Form)
            ? throw new NotImplementedException("Forms is not available in this host, which runs no program of forms (#2496)")
            : []);

    public int Count => Loaded.Count;

    /// <summary>The default member, <c>Forms(i)</c>: Item.</summary>
    internal Vb6Value Item(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        if (args[0].IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var position = Convert.ToInt32(VB6BuiltIns.NarrowNum(args[0], Vb6Value.ValueType.Integer).Value);
        var loaded = Loaded;
        return position >= 0 && position < loaded.Count
            ? Vb6Value.NewObject(loaded[position])
            : throw new VBRunTimeException(VBStandardError.SubscriptOutOfRange);
    }

    /// <summary>Where a For Each over it has got to: the forms held when the loop began, every one of which it hands out.</summary>
    internal sealed class Cursor(VbObject[] forms)
    {
        public readonly VbObject[] Forms = forms;
        public int Position;
    }

    internal Cursor StartEnumeration() => new([.. Loaded]);

    internal static bool TryNext(Cursor cursor, out Vb6Value item)
    {
        if (cursor.Position < cursor.Forms.Length)
        {
            item = Vb6Value.NewObject(cursor.Forms[cursor.Position++]);
            return true;
        }
        item = default;
        return false;
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException("Forms calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) =>
        Task.FromResult<Vb6Value?>(method.ToLowerInvariant() switch
        {
            "count" when args.Count == 0 => new Vb6Value(Count),
            "count" => throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment),
            "item" => Item(args),
            // Late-bound, Add raised 424 where Remove raised 438 (measured; recorded, not explained).
            "add" => throw new VBRunTimeException(VBStandardError.ObjectRequired),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        });
}
