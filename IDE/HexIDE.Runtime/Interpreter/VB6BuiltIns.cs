using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Antlr4.Runtime;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

public partial class VB6BuiltIns
{
    private readonly IBasicStandardLibrary stdLib;

    /// <summary>
    /// The application name <c>MsgBox</c> / <c>InputBox</c> put in the caption when the caller omits the
    /// Title argument — <c>App.Title</c>. Set by the interpreter that owns these builtins.
    /// </summary>
    /// <remarks>
    /// Returns null when there is no project behind the program (the test harness, the bare interpreter):
    /// an omitted title then stays omitted all the way to the host, which supplies its own last-resort
    /// caption. Substituting an empty string here instead would look like a deliberately blank title and
    /// lose the distinction #131 exists to keep.
    /// </remarks>
    public Func<string?>? AppTitle { get; set; }

    // The 713-entry flat name-to-value table that used to live here is gone. It could not answer VB6
    // correctly: the qualifier SELECTS, so vbCancel is 2 through VBA and 0 through VBRUN, and a
    // library-blind map has to be wrong about one of them. It also built every value through
    // Vb6Value(int) and its magnitude rule, so small constants came back Integer where VB6 reports
    // Long - a wrong type on ~700 values.
    //
    // VB6InBoxLibraries holds the structured replacement, measured from the real type libraries:
    // 728 constants in 77 enums and 2 constant modules across four libraries. See
    // docs/vb6-inbox-constants.md.

    public VB6BuiltIns(IBasicStandardLibrary stdLib)
    {
        this.stdLib = stdLib;
    }

    public async Task<Vb6Value?> EvaluateBuiltInFunction(string name, List<Vb6Value> args)
    {
        // The two async builtins await the standard library; every other builtin is in the sync registry.
        if (string.Equals(name, "msgbox", StringComparison.OrdinalIgnoreCase))
            return await MsgBox(args);
        if (string.Equals(name, "inputbox", StringComparison.OrdinalIgnoreCase))
            return await InputBox(args);
        return Builtins.TryGetValue(name, out var fn) ? fn(this, args, null) : (Vb6Value?)null;
    }

    // ---- Built-in function registry ----
    // Per-group partial files (VB6BuiltIns.Strings.cs, .Math.cs, …) register into this table. It is consulted
    // strictly LAST in name resolution (after local vars/arrays and user procedures), so a user `Function Left()`
    // shadows the intrinsic. The delegate carries `self` (for stateful builtins like Rnd) and the call-site parse
    // context (for error locations); either may be unused.
    internal delegate Vb6Value BuiltinFn(VB6BuiltIns self, IReadOnlyList<Vb6Value> args, ParserRuleContext? ctx);

    private static readonly Dictionary<string, BuiltinFn> Builtins = BuildRegistry();

    private static Dictionary<string, BuiltinFn> BuildRegistry()
    {
        var d = new Dictionary<string, BuiltinFn>(StringComparer.OrdinalIgnoreCase);
        RegisterStrings(d);
        RegisterConversion(d);
        RegisterMath(d);
        RegisterArray(d);
        RegisterInspection(d);
        RegisterDateTime(d);
        RegisterFormat(d);
        // DoEvents — a no-op here (the tree-walking interpreter has no message pump). VB6 yields to the message queue
        // and returns the open-form count; returning Integer 0 lets both `DoEvents` and `x = DoEvents` run without
        // crashing (documented approximation).
        d["DoEvents"] = (_, _, _) => new Vb6Value(0);
        return d;
    }

    // ---- shared coercion helpers (the VB6Visitor.TryUnpack ones aren't reachable here) ----
    private static string AsStr(Vb6Value v) => v.Value?.ToString() ?? "";

    /// <summary>
    /// Was argument <paramref name="i"/> actually supplied at the call site?
    ///
    /// A SKIPPED argument (`Split(s, , 2)`) arrives as <see cref="Vb6Value.Missing"/>, not as Empty — that
    /// is what <c>ExpressionExecutor</c> puts in a blank slot. Testing for Empty instead, as this used to,
    /// meant the default was never selected and `AsInt(Missing)` then threw Err 13 on a perfectly ordinary
    /// call. (#190)
    ///
    /// An EXPLICITLY passed Empty is supplied, and the two are genuinely different in VB6 — measured:
    /// `Split("a b c", , 2)` splits on the default space and gives two elements, while `Dim e : Split("a b
    /// c", e, 2)` uses "" as the delimiter and gives the whole string back. So this tests Missing only.
    ///
    /// Only a MIDDLE argument can be skipped: VB6 rejects a trailing `f(x, )` as a syntax error (measured),
    /// so a short list really does mean "the rest were omitted".
    /// </summary>
    private static bool Supplied(IReadOnlyList<Vb6Value> a, int i) =>
        a.Count > i && a[i].Type != Vb6Value.ValueType.Missing;

    private static int AsInt(Vb6Value v)
    {
        if (v.Value is int i) return i;
        if (v.Value is long l) return (int)l;
        if (v.Value is byte b) return b;
        if (Vb6Value.TryNumericToDouble(v, out var d)) return (int)Math.Round(d, MidpointRounding.ToEven);
        if (v.Type == Vb6Value.ValueType.String) return (int)Math.Round(ToNum(v), MidpointRounding.ToEven);
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    private static double AsDouble(Vb6Value v)
    {
        if (v.Value is bool bo) return bo ? -1 : 0;
        if (Vb6Value.TryNumericToDouble(v, out var d)) return d;
        // A NUMERIC string is a valid operand in VB6 and a non-numeric one is Err 13 — measured: `Abs("5")`
        // is 5 (as a Double), `Abs(" 5 ")` is 5, `Abs("&H10")` is 16, `Abs("5abc")` and `Abs("")` are both
        // Err 13. Rejecting every string, as this used to, was wrong in one direction only, and the defect
        // report that found it described it as the opposite. ToNum is the interpreter's already-pinned
        // string→number rule, so this shares one parser with CDbl and coercion-on-store rather than
        // growing a second one that can drift. (#190)
        if (v.Type == Vb6Value.ValueType.String) return ToNum(v);
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    private async Task<Vb6Value> InputBox(List<Vb6Value> args)
    {
        var prompt = args.Count >= 1 ? args[0].Value?.ToString() : "";
        // InputBox(Prompt, [Title], [Default], ...) — the Title was read, but an omitted one arrived as
        // "" and so could never take the application-name default. Same null-vs-empty rule as MsgBox.
        var caption = args.Count >= 2 ? args[1].Value?.ToString() : null;
        caption ??= AppTitle?.Invoke();
        var defaultText = args.Count >= 3 ? args[2].Value?.ToString() : "";
        var result = await stdLib.InputBox(prompt ?? "", caption, defaultText ?? "");
        return (result ?? "");
    }

    private async Task<Vb6Value> MsgBox(List<Vb6Value> args)
    {
        var text = args.Count >= 1 ? args[0].Value?.ToString() : "";
        var style = (VBMsgBoxStyle)(args.Count >= 2 ? args[1].Value as int? ?? 0 : 0);
        var styleIcon = style & VBMsgBoxStyle.IconBits;
        var styleButtons = style & VBMsgBoxStyle.ButtonsBits;
        var icon = default(MessageBoxIcon);
        var buttons = MessageBoxButtons.Ok;
        if (styleIcon == VBMsgBoxStyle.vbCritical)
            icon = MessageBoxIcon.Error;
        else if (styleIcon == VBMsgBoxStyle.vbExclamation)
            icon = MessageBoxIcon.Warning;
        else if (styleIcon == VBMsgBoxStyle.vbQuestion)
            icon = MessageBoxIcon.Question;
        else if (styleIcon == VBMsgBoxStyle.vbInformation)
            icon = MessageBoxIcon.Information;

        if (styleButtons == VBMsgBoxStyle.vbOKOnly)
            buttons = MessageBoxButtons.Ok;
        else if (styleButtons == VBMsgBoxStyle.vbOKCancel)
            buttons = MessageBoxButtons.OkCancel;
        else if (styleButtons == VBMsgBoxStyle.vbAbortRetryIgnore)
            buttons = MessageBoxButtons.AbortRetryIgnore;
        else if (styleButtons == VBMsgBoxStyle.vbYesNoCancel)
            buttons = MessageBoxButtons.YesNoCancel;
        else if (styleButtons == VBMsgBoxStyle.vbYesNo)
            buttons = MessageBoxButtons.YesNo;
        else if (styleButtons == VBMsgBoxStyle.vbRetryCancel)
            buttons = MessageBoxButtons.RetryCancel;

        // MsgBox(Prompt, [Buttons], [Title], ...). The Title argument used to be dropped on the floor,
        // so every message box came out captionless however it was called.
        //
        // null and "" are NOT the same thing here, which is why this is not `?? ""`: VB6 shows an
        // explicitly empty title as empty, and substitutes the application name only when the argument
        // was OMITTED. Collapsing the two would make `MsgBox "x", 0, ""` sprout a caption the author
        // deliberately suppressed. Supplying the omitted-case default is the caller's job — and properly
        // it is App.Title, which does not exist yet (#136).
        var title = args.Count >= 3 ? args[2].Value?.ToString() : null;
        // Omitted (null) takes App.Title, as in VB6. An explicitly empty title is NOT omitted and stays
        // empty — see #131 — so this substitutes only for null.
        title ??= AppTitle?.Invoke();

        var result = await stdLib.MsgBox(text ?? "", title, buttons, icon);
        var vbResult = result switch
        {
            MessageBoxResult.None => VBMsgBoxResult.vbOK,
            MessageBoxResult.Ok => VBMsgBoxResult.vbOK,
            MessageBoxResult.Cancel => VBMsgBoxResult.vbCancel,
            MessageBoxResult.Abort => VBMsgBoxResult.vbAbort,
            MessageBoxResult.Retry => VBMsgBoxResult.vbRetry,
            MessageBoxResult.Ignore => VBMsgBoxResult.vbIgnore,
            MessageBoxResult.Yes => VBMsgBoxResult.vbYes,
            MessageBoxResult.No => VBMsgBoxResult.vbNo,
            MessageBoxResult.TryAgain => VBMsgBoxResult.vbOK,
            MessageBoxResult.Continue => VBMsgBoxResult.vbOK,
            _ => throw new ArgumentOutOfRangeException()
        };
        return (int)vbResult;
    }

    // Mid / UCase / LCase moved to VB6BuiltIns.Strings.cs; LBound / UBound to VB6BuiltIns.Array.cs.

    /// <summary>An unqualified in-box constant. Resolved in library precedence order — VBA, then VBRUN,
    /// then stdole, then VB — first match wins, which is what makes a bare <c>vbCancel</c> answer VBA's 2
    /// rather than VBRUN's 0 (measured). The caller must check the project's own declarations first: a
    /// user <c>Enum</c> or <c>Const</c> of the same name wins over any library.</summary>
    public bool TryGetBuiltInConstant(string name, out Vb6Value constant)
        => VB6InBoxLibraries.TryBare(name, out constant);
}