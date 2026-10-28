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

    /// <summary>
    /// Taken when a message box opens: answers, when it closes, whether the run that opened it has ended since. End was
    /// reached, the debugger is tearing the run down, or a new run has begun. The last is why this is taken at the
    /// start rather than asked at the end: Restart ends one run and begins the next in a single turn, and the box's
    /// answer arrives after both, when nothing about the new run says the old one ended.
    /// </summary>
    public Func<Func<bool>>? WatchRunEnd { get; set; }

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

    /// <summary>What <c>Command</c> answers: the project's command line arguments, empty where it has none.</summary>
    internal string CommandLine { get; set; } = "";

    /// <summary>
    /// What <c>Erl</c> answers, a Long (#2528): the line number at or above where the latest trapped error was raised, in the
    /// procedure that trapped it and in source order, set as the error is trapped, and 0 again wherever VB6 clears Err itself.
    /// The program's own <c>Err.Clear</c> leaves it.
    /// </summary>
    internal long Erl { get; set; }

    /// <summary>The program's open files (#1145), which FreeFile and EOF read and the file statements use.</summary>
    internal VbFiles Files { get; } = new();

    public async Task<Vb6Value?> EvaluateBuiltInFunction(string name, List<Vb6Value> args, bool compareText = false)
    {
        CheckIntrinsicArgumentCount(name, args);

        // An unknown name is not a built-in, and its callers say so loudly ("Sub or Function not defined", or not
        // implemented). Found first, so that an object argument cannot turn that into a trappable 438.
        BuiltinFn? fn = null;
        var isAsync = string.Equals(name, "msgbox", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "inputbox", StringComparison.OrdinalIgnoreCase);
        if (!isAsync && !Builtins.TryGetValue(name, out fn))
            return null;

        CheckValueBeforeObject(name, args);
        for (var i = 0; i < args.Count; i++)
            args[i] = ArgumentAsUsed(name, i, args[i]);

        // The two async builtins await the standard library; every other builtin is in the sync registry.
        if (string.Equals(name, "msgbox", StringComparison.OrdinalIgnoreCase))
            return await MsgBox(args);
        if (string.Equals(name, "inputbox", StringComparison.OrdinalIgnoreCase))
            return await InputBox(args);
        return fn!(this, args, compareText);
    }

    // Measured (#959, p41): these typed value arguments convert before a class in the text slot is read.
    // Keep the existing order for object numeric arguments and other combinations, which were not measured.
    private static void CheckValueBeforeObject(string name, IReadOnlyList<Vb6Value> args)
    {
        var (text, number) = name.ToUpperInvariant() switch
        {
            "LEFT" or "MID" => (0, 1),
            "REPLACE" => (0, 3),
            "SPLIT" => (0, 2),
            "INSTR" => (1, 3),
            _ => (-1, -1)
        };
        if (number >= 0 && Supplied(args, number) && args[text].Value is VbObject
            && args[number].Type == Vb6Value.ValueType.String)
            _ = AsInt(args[number]);
        if (string.Equals(name, "Left", StringComparison.OrdinalIgnoreCase)
            && args[0].Value is VbObject && args[1].IsNull)
            throw InvalidUseOfNull();
    }

    /// <summary>
    /// A built-in's argument as that function uses it. An object stands for its default member wherever a value is
    /// wanted (ObjectAsValue), which is almost everywhere: measured for the conversions, the string functions, Split,
    /// a UBound dimension, a Join delimiter and a Filter switch. The exceptions, all measured:
    /// <list type="bullet">
    /// <item><c>TypeName</c>, <c>IsObject</c> and <c>IsMissing</c> ask about the argument itself, <c>Array</c>
    /// holds its arguments as elements, and <c>IIf</c> and <c>Choose</c> return one of theirs (#1461, #1464);</item>
    /// <item><c>VarType</c> and the Is functions read the default member when there is one, and otherwise answer about
    /// the object, with no 438;</item>
    /// <item>the array that <c>LBound</c>, <c>UBound</c>, <c>Join</c> and <c>Filter</c> take first. An object there
    /// is not read at all: each raises 13 for any object, a class whose default member returns an array included
    /// (measured, native and in the IDE), and the function's own array check raises that 13;</item>
    /// <item><c>Join</c>'s delimiter, which VB6 reads only when the array has an element (measured: <c>Join(Array(), k)</c>
    /// is "" with no 438). <c>Join</c> reads it itself, after that check.</item>
    /// <item><c>Filter</c>'s match, switch and compare, which VB6 reads one at a time, right to left, each as it is
    /// converted (measured: <c>Filter(d, k, "x")</c> is the switch's 13, not k's 438). Read here, every object would come
    /// before every value. <c>Filter</c> reads them itself.</item>
    /// </list>
    /// </summary>
    private static Vb6Value ArgumentAsUsed(string name, int index, Vb6Value arg)
    {
        // LoadPicture and the financials accept only measured argument shapes; other conversions refuse there.
        if (string.Equals(name, "LoadPicture", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Pmt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "FV", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "PV", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "NPer", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "IPmt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "PPmt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Rate", StringComparison.OrdinalIgnoreCase)) return arg;
        if (TakesTheArgumentItself.Contains(name) || (index == 0 && TakesAnArrayFirst.Contains(name))
            || (index >= 1 && TakesItsChoicesThemselves.Contains(name))
            || (index == 1 && string.Equals(name, "Join", StringComparison.OrdinalIgnoreCase))
            || string.Equals(name, "Filter", StringComparison.OrdinalIgnoreCase)
            // GetSetting reads its default only when it is the answer (measured, #1562).
            || (index == 3 && string.Equals(name, "GetSetting", StringComparison.OrdinalIgnoreCase)))
            return arg;
        if (ReadsADefaultMemberIfAny.Contains(name))
            return ObjectAsValue.DefaultMemberIfAny(arg);
        return ObjectAsValue.Scalar(arg, null);
    }

    // CVar among them: CVar of an object is the object, whether its default member wants an argument, as a Collection's
    // does, or not (measured, #1466: TypeName(CVar(k)) is "K1" and IsObject(CVar(k)) True for a class with a default Value).
    private static readonly HashSet<string> TakesTheArgumentItself = new(StringComparer.OrdinalIgnoreCase)
        { "TypeName", "IsObject", "IsMissing", "Array", "CVar", "ObjPtr" };

    private static readonly HashSet<string> ReadsADefaultMemberIfAny = new(StringComparer.OrdinalIgnoreCase)
        { "VarType", "IsNumeric", "IsEmpty", "IsNull", "IsDate", "IsError", "IsArray" };

    private static readonly HashSet<string> TakesAnArrayFirst = new(StringComparer.OrdinalIgnoreCase)
        { "LBound", "UBound", "Join", "Filter" };

    // IIf's parts and Choose's choices, which each returns as it was given: an object comes back as the object (measured).
    private static readonly HashSet<string> TakesItsChoicesThemselves = new(StringComparer.OrdinalIgnoreCase)
        { "IIf", "Choose" };

    // ---- Built-in function registry ----
    // Per-group partial files (VB6BuiltIns.Strings.cs, .Math.cs, …) register into this table. It is consulted
    // strictly LAST in name resolution (after local vars/arrays and user procedures), so a user `Function Left()`
    // shadows the intrinsic. The delegate carries `self` (for stateful builtins like Rnd) and the calling
    // module's Option Compare; either may be unused.
    internal delegate Vb6Value BuiltinFn(VB6BuiltIns self, IReadOnlyList<Vb6Value> args, bool compareText);

    private static readonly Dictionary<string, BuiltinFn> Builtins = BuildRegistry();

    /// <summary>Whether <paramref name="name"/> is a built-in function this interpreter implements.</summary>
    public static bool IsBuiltInFunction(string name) => Builtins.ContainsKey(name);

    /// <summary>A built-in function by name, for a caller with no interpreter: an <c>#If</c>'s conversions (#1112), which
    /// take no state, so it passes no <c>self</c>.</summary>
    internal static bool TryGetBuiltIn(string name, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out BuiltinFn? function) =>
        Builtins.TryGetValue(name, out function);

    private static Dictionary<string, BuiltinFn> BuildRegistry()
    {
        var d = new Dictionary<string, BuiltinFn>(StringComparer.OrdinalIgnoreCase);
        RegisterStrings(d);
        RegisterConversion(d);
        RegisterMath(d);
        RegisterFinancial(d);
        RegisterArray(d);
        RegisterInspection(d);
        d["LoadPicture"] = (self, args, _) => PanelPicture.Load(args, self.Files);
        RegisterDateTime(d);
        RegisterFormat(d);
        RegisterColor(d);
        RegisterChoice(d);
        RegisterSettings(d);
        RegisterInteraction(d);
        // The command line, a String (measured): empty in a made program run with none.
        d["Command"] = (self, a, _) => a.Count == 0
            ? new Vb6Value(self.CommandLine)
            : throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        // No IME: Long zero, measured on en-GB in #2551.
        d["IMEStatus"] = (_, a, _) => a.Count == 0
            ? new Vb6Value(0L)
            : throw new NotImplementedException("IMEStatus arguments are not implemented (#2551)");
        d["Erl"] = (self, a, _) => a.Count == 0
            ? new Vb6Value(self.Erl)
            : throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        // FreeFile is an Integer, 1 with nothing open, and FreeFile(1) the first from 256 (measured, #1145).
        d["FreeFile"] = (self, a, _) => new Vb6Value(self.Files.FreeFile(Supplied(a, 0) ? AsInt(a[0]) : 0));
        d["CurDir"] = (self, a, _) => new Vb6Value(self.Files.CurDir(Supplied(a, 0) ? (a[0].IsNull ? throw new VBRunTimeException(VBStandardError.TypeMismatch) : AsStr(a[0])) : ""));
        d["FileLen"] = (self, a, _) => new Vb6Value((long)self.Files.FileLength(AsStr(a[0])));
        // Whole seconds, as Now is (measured, #783), and as the file time showed when a fraction was still cut off.
        d["FileDateTime"] = (self, a, _) => new Vb6Value(VbDate.FromDateTime(WholeSeconds(self.Files.FileDateTime(AsStr(a[0])))));
        d["GetAttr"] = (self, a, _) => new Vb6Value((long)self.Files.GetAttributes(AsStr(a[0])));
        d["Dir"] = (self, a, _) => new Vb6Value(self.Files.Dir(Supplied(a, 0) ? (a[0].IsNull ? throw new VBRunTimeException(VBStandardError.TypeMismatch) : AsStr(a[0])) : null, Supplied(a, 1) ? AsInt(a[1]) : 0));
        d["FileAttr"] = (self, a, _) =>
        {
            var number = Convert.ToInt32(NarrowNum(a[0], Vb6Value.ValueType.Integer).Value);
            if (Supplied(a, 1) && a[1].Type != Vb6Value.ValueType.Integer
                && a[1].Type != Vb6Value.ValueType.Long && a[1].Type != Vb6Value.ValueType.Byte)
                throw new NotImplementedException("FileAttr attribute conversion is not implemented: its VB6 behaviour is unmeasured (#2521)");
            return new Vb6Value(self.Files.FileAttr(number, Supplied(a, 1) ? Convert.ToInt64(a[1].Value) : 1));
        };
        d["EOF"] = (self, a, _) => new Vb6Value(self.Files.Eof(AsInt(a[0])));
        // LOF, Loc and Seek are Longs, and Input a String (measured, #1645). Input's file number may be written #n: the
        // expression executor reads it as a file number. InputB returns a byte string, which HexIDE has not got (#1510).
        d["LOF"] = (self, a, _) => new Vb6Value(self.Files.Length(AsInt(a[0])));
        d["Loc"] = (self, a, _) => new Vb6Value(self.Files.Loc(AsInt(a[0])));
        d["Seek"] = (self, a, _) => new Vb6Value(self.Files.SeekPosition(AsInt(a[0])));
        d["Input"] = (self, a, _) => a.Count == 2
            ? new Vb6Value(self.Files.Read(AsInt(a[1]), AsInt(a[0])))
            : throw new VBCompileErrorException("Syntax error");
        d["InputB"] = (_, _, _) => throw new NotImplementedException("InputB is not implemented: it returns a byte string, which HexIDE has not got (#1510)");
        // DoEvents, for code run with no window, as a module test's is: nothing can be waiting, so it does nothing. A program
        // running in a window reaches BasicInterpreter.CallBuiltIn first, which runs what is waiting, as VB6 does (#766).
        // VB6 returns the open-form count; Integer 0 lets both `DoEvents` and `x = DoEvents` run (documented approximation).
        d["DoEvents"] = (_, _, _) => new Vb6Value(0);
        return d;
    }

    // ---- shared coercion helpers (the VB6Visitor.TryUnpack ones aren't reachable here) ----
    private static string AsStr(Vb6Value v) => VbDisplay.ToText(v);

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
        var prompt = args.Count >= 1 ? VbDisplay.ToTextOrNull(args[0]) : "";
        // InputBox(Prompt, [Title], [Default], ...) — the Title was read, but an omitted one arrived as
        // "" and so could never take the application-name default. Same null-vs-empty rule as MsgBox.
        var caption = args.Count >= 2 ? VbDisplay.ToTextOrNull(args[1]) : null;
        caption ??= AppTitle?.Invoke();
        var defaultText = args.Count >= 3 ? VbDisplay.ToTextOrNull(args[2]) : "";
        var runEnded = WatchRunEnd?.Invoke();
        var result = await stdLib.InputBox(prompt ?? "", caption, defaultText ?? "");
        // A box closed because its run ended answers nothing, as a MsgBox does, and the statement is abandoned with the
        // run. Cancel, which a person can press, is also null, and is "" (#1010 review: after Restart, the old run's
        // InputBox in Form_Load would otherwise hand it "" and its Load carry on beside the new run).
        if (result is null && runEnded?.Invoke() == true)
            throw new OperationCanceledException("The run ended while an input box was waiting for an answer");
        return (result ?? "");
    }

    private async Task<Vb6Value> MsgBox(List<Vb6Value> args)
    {
        var text = args.Count >= 1
            ? args[0].Type == Vb6Value.ValueType.Error ? VbPrint.ErrorText(args[0]) : VbDisplay.ToTextOrNull(args[0])
            : "";
        // The style is a Long, coerced as CLng does. It was read only when it arrived as an Integer, so every Long
        // was style 0, an OK button and no icon: every in-box constant is a Long, so `vbYesNo + vbQuestion` showed
        // OK alone and its Yes branch could never run. Measured: a Long, "4" and 4.5 (banker's: 4) are all Yes/No;
        // Empty is OK; Null is 94, "abc" 13, Nothing 91 and an array 13, all before any dialog.
        // An omitted style (`MsgBox "x", , "t"`) is Missing, not supplied, and stays 0.
        var style = (VBMsgBoxStyle)(Supplied(args, 1)
            ? (int)Convert.ToInt64(NarrowNum(args[1], Vb6Value.ValueType.Long).Value)
            : 0);
        // A style with any field out of range is a plain OK box with no icon, whatever its other fields say. Measured:
        // buttons 6 to 15 (6 + vbCritical shows no icon), an icon of 80 or 112 beside vbYesNo, and a default button of
        // 1024 beside vbYesNo all give OK alone. So do 70000 and -1, whose icon field is 112 and 240. The flags above
        // the default button (4096, 65536, 524288) leave vbYesNo as it is.
        if ((int)(style & VBMsgBoxStyle.ButtonsBits) > (int)VBMsgBoxStyle.vbRetryCancel
            || (int)(style & VBMsgBoxStyle.IconBits) > (int)VBMsgBoxStyle.vbInformation
            || (int)(style & VBMsgBoxStyle.DefaultButtonBits) > (int)VBMsgBoxStyle.vbDefaultButton4)
            style = VBMsgBoxStyle.vbOKOnly;
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
        var title = args.Count >= 3 ? VbDisplay.ToTextOrNull(args[2]) : null;
        // Omitted (null) takes App.Title, as in VB6. An explicitly empty title is NOT omitted and stays
        // empty — see #131 — so this substitutes only for null.
        title ??= AppTitle?.Invoke();

        var runEnded = WatchRunEnd?.Invoke();
        var result = await stdLib.MsgBox(text ?? "", title, buttons, icon);
        // None is a box that went away without an answer. The hosts answer a person's close themselves, as Windows does
        // (measured: OK alone answers vbOK, a box with Cancel answers vbCancel, and Yes/No and Abort/Retry/Ignore
        // have no close), and keep a box waiting when its owner hides. So None means HexIDE closed the box:
        //  - The run that opened it has ended, or is ending. No answer is right, and the statement is abandoned, as the
        //    run is. Asked of the run that opened the box, not the one going now: after Restart, the old run's
        //    statement carried on beside the new run with a Cancel nobody pressed.
        //  - Otherwise, a host or a test that reports a close as None, and the close's own answer stands. It used to be
        //    vbOK for every kind, so closing `If MsgBox("Delete all?", vbOKCancel) = vbOK` deleted all.
        //  - A box with no close has no such answer, and a statement that carried on, or stopped in silence, would be
        //    a wrong result. That is raised as a fault, so the run ends and says why.
        if (result == MessageBoxResult.None)
        {
            if (runEnded?.Invoke() == true)
                throw new OperationCanceledException("The run ended while a message box was waiting for an answer");
            result = Dialogs.MessageBox.AnswerOnClose(buttons) ?? throw new InvalidOperationException(
                "A message box with no close went away without an answer, while the run went on");
        }
        var vbResult = result switch
        {
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
        // A Long: TypeName(MsgBox(...)) is "Long", measured.
        return new Vb6Value((long)vbResult);
    }

    // Mid / UCase / LCase moved to VB6BuiltIns.Strings.cs; LBound / UBound to VB6BuiltIns.Array.cs.

    /// <summary>An unqualified in-box constant. Resolved in VB6's reference order — VBA, VBRUN, VB,
    /// stdole — first match wins, which is what makes a bare <c>vbCancel</c> answer VBA's 2 rather than
    /// VBRUN's 0 (measured). The caller must check the project's own declarations first: a user
    /// <c>Enum</c> or <c>Const</c> of the same name wins over any library.</summary>
    public bool TryGetBuiltInConstant(string name, out Vb6Value constant)
        => VB6InBoxLibraries.TryBare(name, out constant)
           || (CommonDialogReferenced && CommonDialogConstants.TryBare(name, out constant))
           || TryControlLibraryConstant(name, out constant);

    /// <summary>Whether the project references the CommonDialog control, whose constants then resolve (#1084).</summary>
    internal bool CommonDialogReferenced { get; set; }

    /// <summary>
    /// The in-box control libraries the project references, in its order (#1082). Their constants resolve as the
    /// CommonDialog's do, after the four in-box libraries, and only while the project references the library: a bare
    /// <c>ccScrollingSmooth</c> without MSCOMCTL.OCX is an undeclared variable, as in VB6. The 5.0 and 6.0 libraries
    /// declare their 152 shared names with the same values in the same enums (measured), so their order never changes a value.
    /// </summary>
    internal IReadOnlyList<Components.InBox.RuntimeLibrary> ControlLibraries { get; set; } = [];

    private bool TryControlLibraryConstant(string name, out Vb6Value constant)
    {
        foreach (var library in ControlLibraries)
            if (library.TryBare(name, out constant))
                return true;
        constant = default;
        return false;
    }
}