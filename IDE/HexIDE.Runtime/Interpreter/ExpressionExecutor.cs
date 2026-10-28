using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Antlr4.Runtime.Tree;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Interpreter;

public partial class ExpressionExecutor : VB6Visitor<Task<object?>>
{
    private readonly BasicInterpreter interpreter;
    private readonly ExecutionEnvironment env;

    // Threaded per-activation alongside env (see StatementExecutor for why these are not ambient interpreter
    // fields): the module this expression is being evaluated in, and the enclosing activation's `With` targets.
    private readonly ModuleInfo currentModule;

    // The bare name a read last found unclaimed, which VB6 would declare there. A read does not declare it (see the read), but
    // ResolveArg does, when that name is the argument it is resolving (#1505).
    private VB6Parser.ICS_S_VariableOrProcedureCallContext? unclaimedRead;
    private readonly Stack<Vb6Value> withTargets;

    // The enclosing activation's per-statement temporary-frame stack (Phase 4.2b) — shared by reference with the
    // owning StatementExecutor, so a `New` object or a call result created while evaluating this expression is
    // adopted into (and terminated by) the current statement's frame.
    private readonly Stack<List<Vb6Value>> stmtFrames;

    protected override Task<object?> DefaultResult { get; } = Task.FromResult<object?>(null);

    public ExpressionExecutor(BasicInterpreter interpreter,
        ExecutionEnvironment env,
        ModuleInfo currentModule,
        Stack<Vb6Value> withTargets,
        Stack<List<Vb6Value>>? stmtFrames = null)
    {
        this.interpreter = interpreter;
        this.env = env;
        this.currentModule = currentModule;
        this.withTargets = withTargets;
        this.stmtFrames = stmtFrames ?? new Stack<List<Vb6Value>>();
    }

    public async Task<Vb6Value?> EvaluateFunction(string name, List<Vb6Value> args)
    {
        if (await interpreter.CallBuiltIn(name, args, currentModule.PrePass.OptionCompareText) is not { } result)
            return null;
        if (result.Value is BasicInterpreter.LibraryObject)
            return interpreter.AdoptNewTemp(result, stmtFrames);
        return IntrinsicAsRead(name, args, result);
    }

    // Which intrinsics VB6 types and which return a Variant, measured one by one (#987, native and p-code: a Select Case
    // on each result against `Case Is < N.4`, or `Case Is > 9` for text, and its product past an Integer's or a Long's
    // ceiling). A name not listed was not run and reads as it always has. CSng was measured with #234: two CSng results
    // whose sum is past a Single's ceiling raise Err 6, where two Variant Singles widen to Double.
    // AscW and ChrW, and Str (" 19" is read as 19 against `Case Is > 10`), were measured with #1134, and CVar with #1466.
    // Val, CDate and Timer, and Space, DateSerial, Now, Date and Time, were measured with #1646 by what Put writes, which is
    // the static type itself: a Variant's goes with its type first, a typed value bare (Val("2") is 8 bytes of Double, and
    // Space(1) is 08 00 01 00 20).
    private static readonly HashSet<string> TypedIntrinsics = new(StringComparer.OrdinalIgnoreCase)
        { "Sgn", "Asc", "AscW", "CInt", "CLng", "CByte", "CSng", "CStr", "UBound", "LBound", "Replace", "StrReverse", "Val", "CDate", "Timer" };

    private static readonly HashSet<string> VariantIntrinsics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hour", "Minute", "Second", "Day", "Month", "Year", "Weekday", "IIf", "Choose", "Switch",
        "Mid", "Left", "Right", "Trim", "LTrim", "LCase", "UCase", "Hex", "Oct", "String", "Format", "Chr", "ChrW",
        "Str", "CVar", "Space", "DateSerial", "Now", "Date", "Time",
    };

    // Typed when given typed arguments, and a Variant's when given a Variant (measured: `Len(v)`, `InStr(v, "5")` and
    // `Abs(v)` widen, `Len("abcde")`, `Abs(n)` and `InStr("abcd5", "5")` overflow). `Int` and `Fix` of a typed Integer
    // are typed too; of a Variant they were not run.
    private static readonly HashSet<string> ArgumentTypedIntrinsics = new(StringComparer.OrdinalIgnoreCase) { "Len", "InStr", "Abs" };
    private static readonly HashSet<string> TypedArgumentIntrinsics = new(StringComparer.OrdinalIgnoreCase) { "Int", "Fix" };

    private static Vb6Value IntrinsicAsRead(string name, List<Vb6Value> args, Vb6Value result)
    {
        if (TypedIntrinsics.Contains(name))
            return result.AsFixedType();
        if (VariantIntrinsics.Contains(name))
            return result.AsVariant();
        var argumentTyped = ArgumentTypedIntrinsics.Contains(name);
        if (!argumentTyped && !TypedArgumentIntrinsics.Contains(name) || args.Count == 0)
            return result;
        if (args.Exists(a => a.HeldAsVariant))
            return argumentTyped ? result.AsVariant() : result;
        return args.TrueForAll(a => a.HasFixedType) ? result.AsFixedType() : result;
    }

    public async Task<(Vb6Value, Vb6Value)> GetTwoValues(VB6Parser.ValueStmtContext[] context)
    {
        if (context.Length != 2)
            throw new Exception("This should only be called for two operands instructions");
        // Every binary operator's operands come through here: an object stands for its default member (#174).
        var left = await EvaluateScalar(context[0]);
        var right = await EvaluateScalar(context[1]);
        return (left, right);
    }

    public async Task<(Vb6Value, Vb6Value)> GetTwoValuesSameTypesOrNull(VB6Parser.ValueStmtContext[] context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context);
        if ((leftValue.Type == Vb6Value.ValueType.Error || rightValue.Type == Vb6Value.ValueType.Error)
            && leftValue.Type != rightValue.Type)
            throw new VBRunTimeException(context[0], VBStandardError.TypeMismatch);
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return (leftValue, rightValue);

        // Empty coerces to its partner's ZERO — 0 for a numeric, "" for a String, False for a Boolean. That
        // is the whole character of the value: it has not decided what it is yet, so `Empty = 0`,
        // `Empty = ""` and `Empty = False` are ALL True (measured). Only the numeric half was handled, which
        // nothing noticed while the `Empty` literal was unwritable — an un-assigned Variant is the only other
        // way to reach this, and comparing one against "" is rarer than writing the keyword.
        //
        // Date is deliberately not in the set: Empty against a Date is unmeasured, and guessing its zero
        // (serial 0 = 1899-12-30) would be inventing a rule rather than reproducing one.
        static bool ComparesAgainstEmpty(Vb6Value.ValueType t) =>
            NumericRank(t) >= 0 || t == Vb6Value.ValueType.String || t == Vb6Value.ValueType.Boolean;

        if (leftValue.Type == Vb6Value.ValueType.EmptyVariant && ComparesAgainstEmpty(rightValue.Type))
            leftValue = new Vb6Value(rightValue.Type);
        if (rightValue.Type == Vb6Value.ValueType.EmptyVariant && ComparesAgainstEmpty(leftValue.Type))
            rightValue = new Vb6Value(leftValue.Type);

        if (leftValue.Type == rightValue.Type)
            return (leftValue, rightValue);

        // A Boolean beside a number takes the number's type, as that type's conversion takes it: True is -1, and for a Byte
        // 255, its bits. Measured: `True = -1`, `True < 0` and `CDbl(-1) >= True` are True, and `CByte(1) > True` is False.
        // A chained comparison reaches this, since `3 = 3 < 4` is (3 = 3) < 4 (#1074).
        if (leftValue.Type == Vb6Value.ValueType.Boolean && NumericRank(rightValue.Type) >= 0)
            leftValue = BooleanAs(leftValue, rightValue.Type);
        if (rightValue.Type == Vb6Value.ValueType.Boolean && NumericRank(leftValue.Type) >= 0)
            rightValue = BooleanAs(rightValue, leftValue.Type);
        if (leftValue.Type == rightValue.Type)
            return (leftValue, rightValue);

        // Both numeric but different subtypes: promote both to the wider one on the VB6 widening ladder
        // (Byte < Integer < Long < Single < Currency < Decimal < Double). The operator/comparison cascades
        // then read the common type. NOTE: this is a stopgap that unblocks the new types — the VB6-correct
        // per-operator RESULT type (e.g. Long+Long -> Long, not Double) is 2.3's VbNumeric job.
        var lr = NumericRank(leftValue.Type);
        var rr = NumericRank(rightValue.Type);
        if (lr >= 0 && rr >= 0)
        {
            var target = lr >= rr ? leftValue.Type : rightValue.Type;
            return (PromoteNumeric(leftValue, target), PromoteNumeric(rightValue, target));
        }

        throw new VBRunTimeException(context[0], VBStandardError.TypeMismatch);
    }

    private static Vb6Value BooleanAs(Vb6Value boolean, Vb6Value.ValueType target)
    {
        var isTrue = boolean.Value is true;
        return target == Vb6Value.ValueType.Byte
            ? new Vb6Value((byte)(isTrue ? 255 : 0))
            : PromoteNumeric(new Vb6Value(isTrue ? -1 : 0), target);
    }

    // --- 2.2 numeric widening helpers (superseded by VbNumeric in 2.3) ---

    private static int NumericRank(Vb6Value.ValueType t)
    {
        if (t == Vb6Value.ValueType.Byte) return 0;
        if (t == Vb6Value.ValueType.Integer) return 1;
        if (t == Vb6Value.ValueType.Long) return 2;
        if (t == Vb6Value.ValueType.Single) return 3;
        if (t == Vb6Value.ValueType.Currency) return 4;
        if (t == Vb6Value.ValueType.Decimal) return 5;
        if (t == Vb6Value.ValueType.Double) return 6;
        return -1;   // not a numeric subtype (String, Date, Boolean, ...)
    }

    private static Vb6Value PromoteNumeric(Vb6Value v, Vb6Value.ValueType target)
    {
        if (v.Type == target)
            return v;
        if (target == Vb6Value.ValueType.Integer)
            return new Vb6Value((int)AsLong(v));                     // reached only from Byte
        if (target == Vb6Value.ValueType.Long)
            return new Vb6Value(AsLong(v));
        if (target == Vb6Value.ValueType.Single)
            return new Vb6Value((float)AsDouble(v));
        if (target == Vb6Value.ValueType.Currency)
            return VbNumeric.Narrow(v, Vb6Value.ValueType.Currency, null);
        if (target == Vb6Value.ValueType.Decimal)
            return Vb6Value.NewDecimal(AsDecimal(v));
        if (target == Vb6Value.ValueType.Double)
            return new Vb6Value(AsDouble(v));
        return v;
    }

    private static long AsLong(Vb6Value v) => v.Value switch
    {
        byte b => b,
        int i => i,
        long l => l,
        _ => (long)AsDouble(v)
    };

    private static decimal AsDecimal(Vb6Value v) => v.Value switch
    {
        byte b => b,
        int i => i,
        long l => l,
        float f => (decimal)f,
        double d => (decimal)d,
        decimal m => m,
        _ => 0m
    };

    private static double AsDouble(Vb6Value v) => Vb6Value.TryNumericToDouble(v, out var d) ? d : 0.0;

    public async Task<(Vb6Value, Vb6Value)> GetTwoValuesSameTypes(VB6Parser.ValueStmtContext[] context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypesOrNull(context);
        if (leftValue.Type != rightValue.Type)
            throw new VBRunTimeException(context[0], VBStandardError.TypeMismatch);
        return (leftValue, rightValue);
    }


    public override Task<object?> Visit(IParseTree tree)
    {
        // Flat operator chains pass the parser's nesting guard but still recurse through the evaluator.
        if (!System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new VBRunTimeException(tree as Antlr4.Runtime.ParserRuleContext, VBStandardError.OutOfStackSpace);
        return base.Visit(tree);
    }

    public override async Task<object?> VisitChildren(IRuleNode node)
    {
      object? result = null;
      int childCount = node.ChildCount;
      for (int i = 0; i < childCount; ++i) // && this.ShouldVisitNextChild(node, result)
      {
          object? nextResult = await node.GetChild(i).Accept<Task<object?>>((IParseTreeVisitor<Task<object?>>) this);
          result = nextResult; //this.AggregateResult(result, Task.FromResult(nextResult));
      }
      return result;
    }

    public async Task<Vb6Value> EvaluateValue(IParseTree arg)
    {
        if (await Visit(arg) is not Vb6Value vb6Value)
            throw new NotImplementedException($"{arg.GetType()} expression is not supported");
        return vb6Value;
    }

    /// <summary>
    /// <see cref="EvaluateValue"/> where a value is wanted, not an object: an operand, a condition, a bound, a Select
    /// Case. A class instance there stands for its default member (<see cref="ObjectAsValue"/>).
    /// </summary>
    public async Task<Vb6Value> EvaluateScalar(IParseTree arg)
    {
        return ObjectAsValue.Scalar(await EvaluateValue(arg), arg as Antlr4.Runtime.ParserRuleContext);
    }

    /// <summary>
    /// The argument slots of a call, blanks included.
    ///
    /// <para>The grammar's <c>argsCall</c> makes each <c>argCall</c> optional, so a blank argument —
    /// <c>Foo 1, , 3</c>, which VB6 allows freely — produces no node at all. Reading
    /// <c>context.argCall()</c> therefore yields only the arguments that were written, and everything
    /// after a blank silently shifts one position left: in <c>MsgBox "x", , vbCritical</c> the icon
    /// constant, written in the Title position, arrived as Buttons and set an icon nobody asked for.</para>
    ///
    /// <para>Positions come from the separators instead: slots = commas + 1, each holding the argument
    /// written between them or <c>null</c> for a blank.</para>
    /// </summary>
    private static List<VB6Parser.ArgCallContext?> ArgSlots(VB6Parser.ArgsCallContext? context)
    {
        var slots = new List<VB6Parser.ArgCallContext?>();
        if (context?.children is null) return slots;

        VB6Parser.ArgCallContext? pending = null;
        var sawAnything = false;
        foreach (var child in context.children)
        {
            switch (child)
            {
                case VB6Parser.ArgCallContext arg:
                    pending = arg;
                    sawAnything = true;
                    break;
                case ITerminalNode t when t.Symbol.Type is VB6Parser.COMMA or VB6Parser.SEMICOLON:
                    slots.Add(pending);
                    pending = null;
                    sawAnything = true;
                    break;
            }
        }
        // The final slot closes at the end of the list rather than at a separator.
        if (sawAnything) slots.Add(pending);
        return slots;
    }

    public async Task<List<Vb6Value>> EvaluateCallArgs(VB6Parser.ArgsCallContext? context, Func<Task>? beforeLast = null)
    {
        List<Vb6Value> callArgs = new();
        var slots = ArgSlots(context).ToList();
        foreach (var arg in slots)
        {
            if (beforeLast is not null && callArgs.Count == slots.Count - 1)
                await beforeLast();
            if (arg is null)
            {
                callArgs.Add(Vb6Value.Missing);
                continue;
            }
            if (arg.BYREF() != null)
                throw new NotImplementedException("ByReference arguments are not supported");
            if (arg.PARAMARRAY() != null)
                throw new NotImplementedException("PARAMARRAY arguments are not supported");

            callArgs.Add(await EvaluateValue(arg.valueStmt()));
        }
        if (beforeLast is not null && slots.Count == 0)
            await beforeLast();

        return callArgs;
    }

    // Like EvaluateCallArgs, but also captures each argument's caller slot when it is a bare lvalue, so a
    // ByRef parameter can alias it. Used for user-procedure calls (builtins take values only).
    public async Task<List<CallArg>> ResolveCallArgs(VB6Parser.ArgsCallContext? context, Func<Task>? beforeLast = null)
    {
        var result = new List<CallArg>();
        var named = false;
        var slots = ArgSlots(context).ToList();
        foreach (var arg in slots)
        {
            if (beforeLast is not null && result.Count == slots.Count - 1)
                await beforeLast();
            // A blank slot has no lvalue to alias, so it can never be ByRef.
            if (arg is null)
            {
                if (named)
                    throw new VBCompileErrorException("Syntax error");
                result.Add(new CallArg(Vb6Value.Missing, null, Omitted: true));
                continue;
            }
            if (arg.PARAMARRAY() != null)
                throw new NotImplementedException("ParamArray arguments are not yet supported");
            // A named argument, `c:=5` (#1465), is placed and evaluated by the call's binding (NamedArguments), in parameter
            // order, which is the order VB6 evaluates them in. A positional one after it is VB6's "Syntax error" (measured).
            if (arg.valueStmt() is VB6Parser.VsAssignContext assign)
            {
                named = true;
                var value = assign.valueStmt();
                var byVal = arg.BYVAL() != null;
                result.Add(new CallArg(Vb6Value.Missing, null, Name: assign.implicitCallStmt_InStmt().GetText(),
                    Deferred: () => ResolveArg(value, byVal)));
                continue;
            }
            if (named)
                throw new VBCompileErrorException("Syntax error");
            result.Add(await ResolveArg(arg.valueStmt(), arg.BYVAL() != null));
        }
        if (beforeLast is not null && slots.Count == 0)
            await beforeLast();
        return result;
    }

    // One argument, with what a ByRef parameter needs to alias it. A call-site ByVal keyword (or a non-lvalue like a literal
    // or `(x)`) forces a copy.
    /// <summary>An operand as a ByRef argument resolves it: its slot, the field or element it names, or just its value (#1646).</summary>
    internal Task<CallArg> ResolveByRef(VB6Parser.ValueStmtContext valueStmt) => ResolveArg(valueStmt, byVal: false);

    private async Task<CallArg> ResolveArg(VB6Parser.ValueStmtContext valueStmt, bool byVal)
    {
        if (!byVal && await TryGetArgElement(valueStmt) is var (elementValue, element))
            return new CallArg(elementValue, null, element);
        unclaimedRead = null;
        var value = await EvaluateValue(valueStmt);
        int? location = byVal ? null : TryGetArgLocation(valueStmt) ?? DeclareUnclaimed(valueStmt);
        var field = byVal || location is not null ? null : TryGetArgField(valueStmt);
        return new CallArg(value, location, field);
    }

    /// <summary>
    /// An intrinsic's arguments. With any named (`Mid(String:=s, Start:=2)`, #1465), each is placed by the intrinsic's
    /// parameter names and evaluated in parameter order (VB6BuiltIns.ArrangeIntrinsicArguments); otherwise as
    /// <see cref="EvaluateCallArgs"/> evaluates them.
    /// </summary>
    public async Task<List<Vb6Value>> EvaluateIntrinsicArgs(string name, VB6Parser.ArgsCallContext? context)
    {
        if (await NamedCallArgs(context) is { } args)
            return await VB6BuiltIns.ArrangeIntrinsicArguments(name, args);
        var slots = ArgSlots(context);
        // Input(n, #f): the second argument may be a file number written with its #, `#f` being f's value (#1645).
        if (name.Equals("Input", StringComparison.OrdinalIgnoreCase) && slots.Count == 2 && slots[0] is { } count
            && slots[1]?.valueStmt() is { } file && file.GetText().StartsWith('#'))
        {
            var input = new VB6BuiltIns.IntrinsicArgumentValues(2);
            input.AddArgument(await EvaluateValue(count.valueStmt()), false);
            input.AddArgument(new Vb6Value(await FileNumber(file)), false);
            return input;
        }
        var values = await EvaluateCallArgs(context);
        var result = new VB6BuiltIns.IntrinsicArgumentValues(values.Count);
        for (var i = 0; i < values.Count; i++)
            result.AddArgument(values[i], slots[i] is null);
        if ((name.Equals("Len", StringComparison.OrdinalIgnoreCase) || name.Equals("LenB", StringComparison.OrdinalIgnoreCase))
            && values.Count == 1
            && slots[0]?.valueStmt() is { } operand)
        {
            // Len sizes a declared scalar, but a Variant still sizes its text (#780, measured). So does LenB, and a number that is
            // not a variable is the same compile error for it (#1510, measured).
            if (TryGetArgLocation(operand) is { } location)
            {
                var type = interpreter.ExecutionContext.State.DeclaredTypeOf(location);
                result.LenVariableSize = type == Vb6Value.ValueType.Byte ? 1
                    : type == Vb6Value.ValueType.Integer || type == Vb6Value.ValueType.Boolean ? 2
                    : type == Vb6Value.ValueType.Long || type == Vb6Value.ValueType.Single ? 4
                    : type == Vb6Value.ValueType.Double || type == Vb6Value.ValueType.Currency || type == Vb6Value.ValueType.Date ? 8
                    : null;
            }
            else if (operand is not VB6Parser.VsStructContext
                && (Vb6Value.TryNumericToDouble(values[0], out _)
                    || values[0].Value is bool && name.Equals("LenB", StringComparison.OrdinalIgnoreCase))   // LenB(True), measured
                && (operand is not VB6Parser.VsICSContext ics
                    || ics.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall() is { } bare
                        && bare.typeHint() is null))
                throw new VBCompileErrorException("Variable required - can't assign to this expression");
        }
        return result;
    }

    /// <summary>
    /// A file number. <c>#f</c> is lexed as one token: the number, or the variable, after the <c>#</c>. Without the
    /// <c>#</c> it is an ordinary expression, as in <c>Close 3</c>, which VB6 takes too (measured).
    /// </summary>
    internal async Task<int> FileNumber(VB6Parser.ValueStmtContext valueStmt)
    {
        var text = valueStmt.GetText();
        Vb6Value value;
        if (text.StartsWith('#'))
        {
            var name = text[1..];
            if (int.TryParse(name, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var literal))
                return literal;
            if (!interpreter.TryReadNow(env, name, out var found, out value, out var need))
                (found, value) = await interpreter.ReadVariableAfter(env, name, need!);
            if (!found)
            {
                // An undeclared name is an Empty Variant, so number 0, where Option Explicit doesn't refuse it.
                if (currentModule.PrePass.RequireVariableDefinitions)
                    throw new VBVariableNotDefinedException(name);
                value = new Vb6Value(0);
            }
        }
        else
            value = await EvaluateValue(valueStmt);
        return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, valueStmt).Value,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The arguments to a member of an in-box object, such as a Collection's <c>Add</c>. With any named (`Key:=`), each is
    /// placed by the member's parameter names and evaluated in parameter order, as an intrinsic's are (#1471); an object
    /// that names no parameters for the member refuses them, as before (#1474).
    /// </summary>
    public async Task<List<Vb6Value>> EvaluateMemberArgs(object? target, string member, VB6Parser.ArgsCallContext? context)
    {
        if (target is not INamedParameterSource source || await NamedCallArgs(context) is not { } args)
            return await EvaluateCallArgs(context);
        if (!source.TryGetParameters(member, out var names, out var required))
            throw new NotImplementedException($"Named arguments to {member} are not implemented yet (#1474)");
        var arranged = await NamedArguments.Arrange(args, names, i => i < required);
        var values = new List<Vb6Value>(arranged.Count);
        foreach (var arg in arranged)
            values.Add(arg.Value);
        return values;
    }

    /// <summary>
    /// A call to a Collection's member, its arguments refused as VB6 refuses them (#1471): before the program runs through a
    /// variable declared As Collection, which HexIDE raises as the statement runs, and at run time through an Object or a
    /// Variant, as 448, 450 or 451 (VbCollection.LateBound). Anything else is called as it is.
    /// </summary>
    internal static async Task<T> AsBound<T>(Vb6Value reference, string member, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (VBCompileErrorException refused) when (reference.Value is VbCollection && !VbCollection.IsEarlyBound(reference)
            && VbCollection.LateBound(refused, member) is { } lateBound)
        {
            throw lateBound;
        }
    }

    // A call's arguments with a named one among them, each named argument left for the call to place and evaluate in
    // parameter order (#1465); null when none is named.
    private async Task<List<CallArg>?> NamedCallArgs(VB6Parser.ArgsCallContext? context)
    {
        var slots = ArgSlots(context);
        var anyNamed = false;
        foreach (var arg in slots)
            if (arg?.valueStmt() is VB6Parser.VsAssignContext)
                anyNamed = true;
        if (!anyNamed)
            return null;
        var args = new List<CallArg>();
        var named = false;
        foreach (var arg in slots)
        {
            if (arg is null)
            {
                if (named)
                    throw new VBCompileErrorException("Syntax error");
                args.Add(new CallArg(Vb6Value.Missing, null, Omitted: true));
                continue;
            }
            if (arg.BYREF() != null)
                throw new NotImplementedException("ByReference arguments are not supported");
            if (arg.PARAMARRAY() != null)
                throw new NotImplementedException("PARAMARRAY arguments are not supported");
            if (arg.valueStmt() is VB6Parser.VsAssignContext assign)
            {
                named = true;
                var value = assign.valueStmt();
                args.Add(new CallArg(Vb6Value.Missing, null, Name: assign.implicitCallStmt_InStmt().GetText(),
                    Deferred: async () => new CallArg(await EvaluateValue(value), null)));
                continue;
            }
            if (named)
                throw new VBCompileErrorException("Syntax error");
            args.Add(new CallArg(await EvaluateValue(arg.valueStmt()), null));
        }
        return args;
    }

    // A constant, a user Const or an Enum member, has no location to pass: VB6 passes it through a temporary, so a ByRef
    // parameter's write leaves it as it was, raising nothing (measured, #1001: a module Const, a local one, a typed one
    // and an Enum member, each through `v As Variant`, an untyped `v` and an `x As Integer`). It was aliased, so the
    // write overwrote a Const, and was refused for an Enum member.
    // A name nothing claims, passed as an argument, is declared there, as VB6 declares it, so a ByRef parameter writes into it
    // (measured, #1505: `Bump a12` twice, with `Sub Bump(x)`, leaves an Integer 2). It was passed as a copy, and the write was
    // lost. Only the bare name the read has just found unclaimed: a name that is a Function is called, and `(a12)` is a copy,
    // as in VB6.
    private int? DeclareUnclaimed(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is not VB6Parser.VsICSContext ics
            || ics.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall() is not { } name
            || !ReferenceEquals(name, unclaimedRead))
            return null;
        StatementExecutor.AllocImplicitVariable(interpreter, env, currentModule, name.ambiguousIdentifier().GetText());
        return TryGetArgLocation(valueStmt);
    }

    // A name written with its type character is the variable too, `Bump i%` and `App t$` writing into the caller's (measured,
    // #742). The read that evaluated the argument has already checked the character, or declared the name with it.
    private int? TryGetArgLocation(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is VB6Parser.VsICSContext ics
            && ics.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall() is { } vp
            && vp.dictionaryCallStmt() == null
            && env.TryGetVariableLocation(vp.ambiguousIdentifier().GetText(), out var loc)
            && !interpreter.ExecutionContext.State.IsConstant(loc)
            && !interpreter.ExecutionContext.State.IsReadOnly(loc))
            return loc;
        if (valueStmt is VB6Parser.VsICSContext arrayIcs
            && arrayIcs.implicitCallStmt_InStmt()?.iCS_S_ProcedureOrArrayCall() is { } call
            && ParenGroups(call) is [null]
            && call.ambiguousIdentifier() is { } name
            && env.TryGetVariableLocation(name.GetText(), out var arrayLocation)
            && interpreter.ExecutionContext.State.IsDeclaredArray(arrayLocation)
            && !env.VariantParameters.Contains(name.GetText()))
            return arrayLocation;
        return null;
    }

    // A UDT field written as an argument (`u.l`, `u.inner.y`): the Type instance holding it and its name, so a ByRef
    // parameter can alias it (#1451). Not a field reached through an index, a With or anything but a variable.
    private UdtFieldRef? TryGetArgField(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is not VB6Parser.VsICSContext ics
            || ics.implicitCallStmt_InStmt()?.iCS_S_MembersCall() is not { } membersCall
            || membersCall.iCS_S_VariableOrProcedureCall() is not { } lead
            || lead.typeHint() != null || lead.dictionaryCallStmt() != null
            || membersCall.dictionaryCallStmt() != null
            || !env.TryGetVariableLocation(lead.ambiguousIdentifier().GetText(), out var loc))
            return null;
        var current = interpreter.ExecutionContext.State[loc];
        var chain = membersCall.iCS_S_MemberCall();
        for (int i = 0; i < chain.Length; i++)
        {
            if (current.Value is not VbUdt bag
                || chain[i].iCS_S_VariableOrProcedureCall() is not { } part
                || part.typeHint() != null || part.dictionaryCallStmt() != null)
                return null;
            var name = part.ambiguousIdentifier().GetText();
            if (i == chain.Length - 1)
                return bag.Has(name) ? new UdtFieldRef(bag, name) : null;
            if (!bag.TryGet(name, out current))
                return null;
        }
        return null;
    }

    // An array element written as an argument (`a(1)`, `m(i, j)`, `al(0 + 0)`), with its value, so a ByRef parameter can alias
    // it (#1455). The subscripts are evaluated here, once, which is when VB6 evaluates them (measured). Not an element reached
    // through a member or a With, nor a control array's.
    private async Task<(Vb6Value Value, ArrayElementRef Ref)?> TryGetArgElement(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is not VB6Parser.VsICSContext ics
            || ics.implicitCallStmt_InStmt()?.iCS_S_ProcedureOrArrayCall() is not { } call
            || call.ambiguousIdentifier() is not { } id
            || call.baseType() != null || call.iCS_S_NestedProcedureCall() != null
            || call.dictionaryCallStmt() != null
            || call.argsCall().Length != 1
            || await interpreter.ReadVariable(env, id.GetText()) is not (true, var holder)
            || !holder.Type.IsArray || holder.Value is not VBArray array)
            return null;
        // `Bump a%(1)` passes the element, its character checked against the array's element type (measured, #742).
        if (call.typeHint() is { } hint)
        {
            if (!env.TryGetVariableLocation(id.GetText(), out var arrayLocation))
                return null;
            RefuseMismatchedVariableCharacter(arrayLocation, holder, hint);
        }
        var index = Subscripts(await EvaluateCallArgs(call.argsCall(0)));
        try
        {
            return (ElementAsRead(holder, array, array.GetValue(index)), new ArrayElementRef(array, index, holder.HeldAsVariant));
        }
        catch (IndexOutOfRangeException)
        {
            throw new VBRunTimeException(call, VBStandardError.SubscriptOutOfRange);
        }
    }

    public async Task<string> ExtractIdentifier(VB6Parser.ICS_S_VariableOrProcedureCallContext context)
    {
        if (context.typeHint() != null)
            throw new NotImplementedException("Type hint is not supported");
        if (context.dictionaryCallStmt() != null)
            throw new NotImplementedException("dictionaryCallStmt is not supported");
        var identifier = context.ambiguousIdentifier().GetText();
        return identifier;
    }


    // EXPRESSION
    /// <summary>
    /// A literal carries a FIXED type, not a Variant subtype — measured: `Dim a As Integer` plus the literal
    /// 30000 raises Err 6 rather than widening, and `30000 * 3` written out raises Err 6 too. Treating
    /// literals as Variants would silently promote `i = i + 1` past a declared Integer's ceiling, which is
    /// the common shape rather than a rare one. (#122)
    /// </summary>
    public override Task<object?> VisitVsLiteral(VB6Parser.VsLiteralContext literalContext) =>
        Task.FromResult<object?>(LiteralValue(literalContext.literal()).AsFixedType());

    /// <summary>A literal's value, typed as VB6 types it. An <c>#If</c>'s expression reads its literals here too (#1112).</summary>
    internal static Vb6Value LiteralValue(VB6Parser.LiteralContext literal)
    {
        if (literal.STRINGLITERAL() is { } stringliteral)
        {
            // `""` inside a literal is VB6's escape for ONE quote, and it is the only escape the language
            // has. Stripping the delimiters without unescaping meant `"he said ""hi"""` evaluated to
            // `he said ""hi""` — a wrong value on ordinary code, everywhere a quoted quotation appears,
            // and there is exactly one place in the interpreter that reads a string literal so exactly one
            // place it was wrong.
            //
            // Order matters: strip the delimiters FIRST, then unescape. Unescaping the raw token text
            // would turn the empty literal `""` into a single quote — measured, `Len("")` is 0 while
            // `Len("""")` is 1, and those two are only distinguishable once the outer pair is gone.
            var text = stringliteral.GetText();
            var str = text.Substring(1, text.Length - 2).Replace("\"\"", "\"");
            return new Vb6Value(str);
        }
        if (literal.INTEGERLITERAL() is { } integerliteral)
            return ClassifyIntegerLiteral(integerliteral.GetText());
        if (literal.OCTALLITERAL() is { } octalliteral)
            return ClassifyRadixLiteral(octalliteral.GetText(), 8);
        if (literal.DOUBLELITERAL() is { } doubleliteral)
        {
            // Source numbers always use a point, whatever the host's culture (#781).
            var text = ExponentAsE(doubleliteral.GetText());
            char suffix = text[^1];
            // A literal with a point and an Integer or Long suffix does not compile: 2.5%, 5.& and 2.5E1& are each "Syntax
            // error" (measured, #1615). It was rounded to an Integer or a Long.
            if (suffix is '&' or '%')
                throw new VBCompileErrorException("Syntax error");
            if (suffix is '#' or '!' or '@')
            {
                var body = text[..^1];
                return InTypeRange(suffix switch
                {
                    '!' => new Vb6Value(float.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)),
                    '@' => Vb6Value.NewCurrency(decimal.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)), // @ forces Currency
                    _ => new Vb6Value(double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)), // #
                });
            }
            // VB6: an unsuffixed floating-point literal (with a '.' or exponent) defaults to Double.
            return InTypeRange(new Vb6Value(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)));
        }
        if (literal.TRUE() is { })
        {
            Vb6Value val = new Vb6Value(true);
            return val;
        }
        if (literal.FALSE() is { })
        {
            Vb6Value val = new Vb6Value(false);
            return val;
        }
        if (literal.NULL() is { })
        {
            return Vb6Value.Null;
        }
        if (literal.NOTHING() is { })
        {
            return Vb6Value.Nothing;   // a null object reference
        }
        if (literal.EMPTY_() is { })
        {
            // The fourth of VB6's value keywords, and the one that was missing — `Empty` had no token at
            // all, so it lexed as an identifier and reported as "Variable not defined (Empty)", naming a
            // variable the author never wrote. It is a keyword, not a constant: `Dim Empty As Integer` is a
            // syntax error in vb6.exe (measured), which is why it belongs in the lexer beside NOTHING/NULL
            // rather than in the built-in constants table where a user variable could shadow it.
            //
            // The VALUE was always reachable — an un-assigned Variant is Empty, and every measured
            // behaviour (Empty = 0 and Empty = "" are both True, Empty = Null is Null, Empty + 1 is 1,
            // Empty & "x" is "x", IsEmpty is True, VarType is 0) already matched EmptyVariant. Only the
            // spelling was unavailable.
            return new Vb6Value(Vb6Value.ValueType.EmptyVariant);
        }
        if (literal.DATELITERAL() is { } dateliteral)
        {
            // VB6 date literals are culture-independent, US month/day/year — parse invariantly. A time-only literal is
            // that time on the zero date, not today (#771).
            var inner = dateliteral.GetText().Trim('#');
            return new Vb6Value(VbDate.FromDateTime(VB6BuiltIns.OnZeroDateIfTimeOnly(DateTime.Parse(inner,
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.NoCurrentDateDefault))));
        }
        if (literal.HEXLITERAL() is { } hexliteral)
            return ClassifyRadixLiteral(hexliteral.GetText(), 16);   // VB6 &H is numeric, not a colour
        // A file number written #n is the Input function's second argument (EvaluateIntrinsicArgs) and a statement's; as a
        // value anywhere else it does not compile: LOF(#1), EOF(#1), Loc(#1) and Seek(#1) are "Syntax error" (measured, #1645).
        if (literal.FILENUMBER() is { })
            throw new VBCompileErrorException("Syntax error");
        throw new NotImplementedException($"{literal.GetChild(0)} literal is not supported");
    }

    // VB6 whole-number literal typing: a type-char suffix wins (& Long, @ Currency, ! Single, # Double);
    // an exponent makes it Double; otherwise Integer if it fits Int16, else Long if it fits Int32, else Double.
    internal static Vb6Value ClassifyIntegerLiteral(string text)
    {
        text = ExponentAsE(text);
        char suffix = text[^1];
        if (suffix is '&' or '@' or '!' or '#' or '%')
        {
            var body = text[..^1];
            // An exponent with an Integer or Long suffix does not compile: 1E3% and 1D3& are "Syntax error" (measured, #1611).
            if (suffix is '&' or '%' && body.Contains('E'))
                throw new VBCompileErrorException("Syntax error");
            return InTypeRange(suffix switch
            {
                '&' => new Vb6Value(long.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lp) ? lp : (long)double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)),
                '@' => Vb6Value.NewCurrency(decimal.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)),
                '!' => new Vb6Value(float.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)),
                '%' => new Vb6Value(int.Parse(body, NumberStyles.Integer, CultureInfo.InvariantCulture)),   // Integer (magnitude ctor keeps it Integer in Int16 range)
                _ => new Vb6Value(double.Parse(body, NumberStyles.Float, CultureInfo.InvariantCulture)),   // '#'
            });
        }
        if (text.IndexOf('E') >= 0)
            return InTypeRange(new Vb6Value(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)));   // exponent without a dot -> Double (e.g. 1e5)
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lv))
            return lv >= int.MinValue && lv <= int.MaxValue ? new Vb6Value((int)lv) : new Vb6Value((double)lv);
        return new Vb6Value(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A literal's text with its exponent marker as E. VB6 reads D as E, in either case: 2.5D2 is 250, 1d3 is 1000 and
    /// 1D3@ is a Currency, each as the E form is (measured, #1611). A literal token holds no other letter. The lexer takes
    /// a second exponent too, which does not compile: 1D2D2 is "Syntax error" (measured).
    /// </summary>
    internal static string ExponentAsE(string text)
    {
        var normalised = text.Replace('e', 'E').Replace('d', 'E').Replace('D', 'E');
        return normalised.IndexOf('E') == normalised.LastIndexOf('E') ? normalised : throw new VBCompileErrorException("Syntax error");
    }

    // A literal beyond its type's range does not compile: 1E400 and 1D400 (a Double), 1E39! (a Single) and 1E20@ (a
    // Currency) are each "Syntax error" (measured, #1611). .NET reads them as infinity, or as a Currency VB6 cannot hold.
    // Below the range is not refused: 1E-400 is 0.
    private static Vb6Value InTypeRange(Vb6Value literal) => literal.Value switch
    {
        double d when double.IsInfinity(d) => throw new VBCompileErrorException("Syntax error"),
        float f when float.IsInfinity(f) => throw new VBCompileErrorException("Syntax error"),
        decimal c when literal.Type == Vb6Value.ValueType.Currency && Math.Abs(c) > 922337203685477.5807m
            => throw new VBCompileErrorException("Syntax error"),
        _ => literal,
    };

    // VB6 &H hex / &O octal literals are unsigned bit-patterns (verified against vb6.exe): with no suffix,
    // a value that fits 16 bits is an Integer via 16-bit two's-complement (&HFFFF -> -1, &H8000 -> -32768),
    // else it fits 32 bits as a Long via 32-bit two's-complement (&HFFFFFFFF -> -1). A trailing & forces the
    // 32-bit/Long reading (&HFFFF& -> 65535); a trailing % forces the 16-bit/Integer reading (&HFFFF% -> -1).
    internal static Vb6Value ClassifyRadixLiteral(string text, int radix)
    {
        bool neg = text[0] == '-';
        if (neg || text[0] == '+') text = text[1..];
        char suffix = text[^1];
        bool forceLong = suffix == '&', forceInt = suffix == '%';
        if (forceLong || forceInt) text = text[..^1];
        ulong u = Convert.ToUInt64(text[2..], radix);   // strip the "&H" / "&O" prefix

        if (forceInt || (!forceLong && u <= 0xFFFF))
        {
            int v = (short)(ushort)u;                   // 16-bit two's-complement -> Integer
            return new Vb6Value(neg ? -v : v);
        }
        if (u <= 0xFFFFFFFF)
        {
            long v = (int)(uint)u;                      // 32-bit two's-complement -> Long
            return new Vb6Value(neg ? -v : v);
        }
        return new Vb6Value(neg ? -(double)u : u);      // > 32 bits (rare) -> Double
    }

    /// <summary>
    /// In a form's code, the form's own property of this name, read, or null where the code isn't a form's or its Form has
    /// no such property to read without arguments.
    /// </summary>
    private async Task<Vb6Value?> BareFormMember(string name) =>
        FormMembers.TryBare(interpreter.ExecutionContext, env, name, out var form, out var member) && member.CanGet && member.RequiredArgs == 0
            ? (Vb6Value?)await interpreter.Loading((Form: form, Name: name), static s => FormMembers.Read(s.Form, s.Name))
            : null;

    // Directly evaluating an implicitCallStmt_InStmt (e.g. a `With <target>`) must use the SAME full resolution
    // as an expression-position VsICS — not a bare-variable shortcut (which NRE'd on a member target like
    // `With p.Home`).
    public override async Task<object?> VisitImplicitCallStmt_InStmt(VB6Parser.ImplicitCallStmt_InStmtContext context)
        => await ResolveImplicitCall(context);

    public override async Task<object?> VisitICS_S_VariableOrProcedureCall(VB6Parser.ICS_S_VariableOrProcedureCallContext context)
    {
        // `c!key`, a Collection's item by key (#1471).
        if (context.typeHint() is null && context.dictionaryCallStmt() is { } bang)
        {
            var name = context.ambiguousIdentifier().GetText();
            if (!interpreter.TryReadNow(env, name, out var bangFound, out var bangTarget, out var bangNeed))
                (bangFound, bangTarget) = await interpreter.ReadVariableAfter(env, name, bangNeed!);
            if (!bangFound && await BareFormMember(name) is { } bangMember)
                (bangFound, bangTarget) = (true, bangMember);
            // `Forms!Form1`, where nothing the program declares claims the name (#2496).
            if (!bangFound && interpreter.UnclaimedLibraryObject(name) is { Value: VbForms } forms)
                (bangFound, bangTarget) = (true, forms);
            if (!bangFound)
                throw new NotImplementedException($"`{context.GetText()}` is supported only on a Collection and a form's Controls");
            return await Bang(bangTarget, bang);
        }
        var identifier = await ExtractIdentifier(context);
        RefuseAmbiguousEnumMember(identifier);
        // Through Loading: a form's name or control may need the form made or loaded first (#822).
        if (!interpreter.TryReadNow(env, identifier, out var found, out var var, out var foundNeed))
            (found, var) = await interpreter.ReadVariableAfter(env, identifier, foundNeed!);
        if (found)
            return var;
        // In a form's code, a member of the form heads the chain, `Controls.Count` (#1060), as it is read bare (#995).
        if (await BareFormMember(identifier) is { } formMember)
            return formMember;
        if (identifier.Equals("Clipboard", StringComparison.OrdinalIgnoreCase))
        {
            // A function named Clipboard can supply the member-chain's object too, not just a bare value.
            if (interpreter.TryResolveProcedure(identifier, currentModule, out _, out var proc) && proc.IsFunction
                && await interpreter.CallProcedure(identifier, [], currentModule, callerEnv: env, callerFrames: stmtFrames) is { } result)
                return result;
            return new Vb6Value(interpreter.Clipboard);
        }
        if (interpreter.UnclaimedLibraryObject(identifier) is { } libraryObject)
        {
            if (interpreter.TryResolveProcedure(identifier, currentModule, out _, out var objectProc) && objectProc.IsFunction
                && await interpreter.CallProcedure(identifier, [], currentModule, callerEnv: env, callerFrames: stmtFrames) is { } objectResult)
                return objectResult;
            return libraryObject;
        }
        VbIntrinsicNames.RefuseMissingObject(identifier);
        throw new VBVariableNotDefinedException(identifier);
    }

    /// <summary>
    /// A bare name that two of this module's Enums both declare is AMBIGUOUS, and VB6 says so at the USE:
    /// the declarations are accepted and reading the name is "Ambiguous name detected". Measured.
    ///
    /// <para>
    /// Checked here rather than at the hoist because both members really are declared — refusing the
    /// declaration would reject a legal module. Before this, the second member silently overwrote the
    /// first and a read handed back the wrong enum's value with nothing to show for it. The qualified
    /// forms (<c>EOne.shared_</c>, <c>Module2.shared_</c>) stay usable, which is why this only guards the
    /// bare path.
    /// </para>
    /// </summary>
    private void RefuseAmbiguousEnumMember(string identifier)
    {
        if (currentModule?.PrePass.AmbiguousEnumMembers.Contains(identifier) == true)
            throw new VBCompileErrorException("Ambiguous name detected: " + identifier);
    }

    public override async Task<object?> VisitVsICS(VB6Parser.VsICSContext icsContext)
        => await ResolveImplicitCall(icsContext.implicitCallStmt_InStmt());

    // The shared implicit-call resolver: a bare variable / zero-arg function, a namespace-qualified member
    // (`Module1.Foo`, `VBA.Abs`), a UDT field chain (`e.Address.City`), a `.Member` against a With target, or
    // `arr(i)`. Used by both VsICS (expression position) and a direct implicitCallStmt_InStmt (a With target).
    private async Task<object?> ResolveImplicitCall(VB6Parser.ImplicitCallStmt_InStmtContext ctx)
    {
        if (ctx.iCS_S_VariableOrProcedureCall() is { } varOrProcCall)
        {
            // `Command$` with no parentheses, the library's function with no arguments and its result a String (measured). The
            // program's own Function, and a variable, a constant or an Enum member, are read through their character (#740,
            // #742). Any other name with a type character is still refused.
            if (varOrProcCall.typeHint() is { } hint)
            {
                var hinted = varOrProcCall.ambiguousIdentifier().GetText();
                if (hint.GetText() == "$" && BareDollarForms.Contains(hinted)
                    && !env.TryGetVariableLocation(hinted, out _)
                    && !interpreter.TryResolveProcedure(hinted, currentModule, out _, out _)
                    && await EvaluateFunction(hinted, []) is { } bareResult)
                    return DollarResult(bareResult, varOrProcCall);
                // The program's own Function read bare, `F% + 1` for `Function F%()`: the character must be its return type
                // (measured, #740).
                if (!env.TryGetVariableLocation(hinted, out _)
                    && interpreter.TryResolveProcedure(hinted, currentModule, out _, out var hintedProc) && hintedProc.IsFunction)
                {
                    RefuseMismatchedCharacter(hintedProc, hint);
                    if (await interpreter.CallProcedure(hinted, [], currentModule, callerEnv: env, callerFrames: stmtFrames) is { } hintedResult)
                        return hintedResult;
                }
                if (env.TryGetVariableLocation(hinted, out var hintedLocation))
                {
                    RefuseAmbiguousEnumMember(hinted);
                    if (!interpreter.TryReadNow(env, hinted, out _, out var hintedValue, out var hintedNeed))
                        (_, hintedValue) = await interpreter.ReadVariableAfter(env, hinted, hintedNeed!);
                    RefuseMismatchedVariableCharacter(hintedLocation, hintedValue, hint);
                    return hintedValue;
                }
                // A name nothing else claims is declared by the read, with the character's type ahead of a DefType: `u$` read
                // first is the String "", and a bare `u` after it reads the same variable (measured). The read allocates, as an
                // assignment does, so that the later bare read finds it typed.
                if (NothingClaims(hinted))
                {
                    VbIntrinsicNames.RefuseMissingObject(hinted);
                    if (currentModule.PrePass.RequireVariableDefinitions || interpreter.SuppressImplicitDeclaration)
                        throw new VBVariableNotDefinedException(hinted);
                    var declared = BaseTypeMapper.Map(hint);
                    interpreter.ExecutionContext.AllocVariable(env, hinted, new Vb6Value(declared), declared);
                    return (await interpreter.ReadVariable(env, hinted)).Value;
                }
                throw new NotImplementedException($"`{varOrProcCall.GetText()}` is not implemented: a type character on a name that is not a variable (#742)");
            }
            if (varOrProcCall.dictionaryCallStmt() != null)
                return await VisitICS_S_VariableOrProcedureCall(varOrProcCall);
            var identifier = varOrProcCall.ambiguousIdentifier().GetText();
            RefuseAmbiguousEnumMember(identifier);

            if (!interpreter.TryReadNow(env, identifier, out var found, out var variable, out var foundNeed))
                (found, variable) = await interpreter.ReadVariableAfter(env, identifier, foundNeed!);
            if (interpreter.EnumIsQualifier(identifier, env, currentModule))
                throw new VBCompileErrorException("Expected variable or procedure, not enum type");
            if (!found)
            {
                // In a form's code, a member of the form: `Caption`, `Width`, `Left` (#995). After the procedure's locals and
                // the form's own variables and controls, which the lookup above found, and before the project's globals
                // and VBA, as VB6 resolves it (measured, #822). Its own procedures can't share a name with one.
                if (FormMembers.TryBare(interpreter.ExecutionContext, env, identifier, out var form, out var formMember))
                {
                    if (formMember.CanGet && formMember.RequiredArgs == 0)
                        return await interpreter.Loading((Form: form, Name: identifier), static s => FormMembers.Read(s.Form, s.Name));
                    if (formMember.IsMethod && formMember.RequiredArgs == 0)
                        return await interpreter.Loading((Form: form, Name: identifier), static s => FormMembers.Call(s.Form, s.Name, []));
                }
                if (interpreter.BuiltIns.TryGetBuiltInConstant(identifier, out var builtInConst))
                    return builtInConst;
                // A bare name in an expression may be a zero-argument (or all-optional) Function call — this
                // module's, or another module's Public Function (via the cross-module resolver).
                if (interpreter.TryResolveProcedure(identifier, currentModule, out _, out var proc) && proc.IsFunction
                    && await interpreter.CallProcedure(identifier, [], currentModule, callerEnv: env, callerFrames: stmtFrames) is { } fnResult)
                    return fnResult;
                if (interpreter.GlobalMember(identifier) is { } global)
                    return await interpreter.CallLibraryMember(global, identifier, [])
                        ?? throw new VBSubOrFunctionNotDefinedException(identifier);
                // ...or a zero-argument intrinsic (Now, Date, Time, Timer, Rnd, ...). Consulted last, so a
                // variable/constant/user Function of the same name still wins above.
                if (await EvaluateFunction(identifier, []) is { } builtInFn)
                    return builtInFn;
                if (identifier.Equals("Clipboard", StringComparison.OrdinalIgnoreCase))
                    return new Vb6Value(interpreter.Clipboard);
                if (interpreter.UnclaimedLibraryObject(identifier) is { } libraryObject)
                    return libraryObject;
                // Nothing else claims the name, so VB6 makes it a variable: reading an undeclared name is
                // legal and yields Empty (measured). LAST, after procedures and intrinsics — a mistyped
                // function call must not become a silent Empty. (#171)
                //
                // Returned WITHOUT allocating. Creating here would make evaluating an expression mutate
                // the environment, and the debugger evaluates watch expressions on its own schedule — a
                // watch on an undeclared name would create it and fire a change the program never made.
                // That is not hypothetical: it broke WatchBreakTests, which caught the observer changing
                // what it observed. The write path allocates; a read only reports.
                VbIntrinsicNames.RefuseMissingObject(identifier);
                if (currentModule.PrePass.RequireVariableDefinitions || interpreter.SuppressImplicitDeclaration)
                    throw new VBVariableNotDefinedException(identifier);
                // A name VB6 defines as an intrinsic is NOT an undeclared variable, and must not quietly
                // become one. `s = CurDir` used to reach here and evaluate to Empty, so the program carried
                // on with a wrong value and nothing said why — the worst failure this interpreter can
                // produce. The registry was consulted above, so arriving here means VB6 has it and we do
                // not. (#191)
                if (VbIntrinsicNames.IsIntrinsic(identifier))
                    throw new NotImplementedException(
                        identifier + " is a VB6 intrinsic that HexIDE does not implement yet");
                // Under a DefType it reads as that type's zero, typed as a Dim's would be (measured, #169: `i9` under DefInt
                // is an Integer 0, `z9` under DefStr "").
                unclaimedRead = varOrProcCall;
                return currentModule.PrePass.DefaultTypeOf(identifier) is { } defaultType
                    ? (VbNumeric.IsDeclarableScalar(defaultType) ? new Vb6Value(defaultType).AsFixedType() : new Vb6Value(defaultType))
                    : Vb6Value.Variant;
            }

            return variable;
        }
        else if (ctx.iCS_S_MembersCall() is { } membersCall)
        {
            // `k.Items!key`: the chain, then its default member given the name (#1471).
            if (membersCall.dictionaryCallStmt() is { } trailingBang)
            {
                var head = await ResolveImplicitCallWithoutBang(ctx, membersCall);
                return await Bang(head, trailingBang);
            }

            // A namespace qualifier (`Module1.Foo`, `VBA.Abs`, `VBA.Math.Abs`) — resolved before treating the
            // leading part as a value, but a same-named variable shadows it (VB6 precedence).
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart
                // Only whether there is one, as a statement's qualifier asks: it must not make a form's instance (#822).
                && (interpreter.EnumIsQualifier(leadPart.GetText(), env, currentModule)
                    || !interpreter.ExecutionContext.TryInspectVariable(env, leadPart.GetText(), out _))
                && interpreter.TryResolveQualifier(leadPart.GetText(), out var qualifier))
            {
                return await ResolveQualifiedMemberValue(qualifier, membersCall.iCS_S_MemberCall(), ctx);
            }

            // A leading dot (`.Member`) has no leading part — resolve against the innermost With target.
            Vb6Value variable;
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } varOrProCall)
                variable = await EvaluateValue(varOrProCall);
            else if (membersCall.iCS_S_ProcedureOrArrayCall() is { } leadProcOrArray)
                // `Command1(i).Caption` — the lead is an indexed call (a control-array element or an array); resolve
                // it to the element value, then read the member off it below.
                variable = (Vb6Value)(await EvaluateProcedureOrArrayCall(leadProcOrArray))!;
            else
                variable = withTargets.Count > 0
                    ? withTargets.Peek()
                    : throw new VBRunTimeException(ctx, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);

            // Every member in the chain, folded left to right. `a.b.c` is `a`, then `b` on that, then `c` on
            // that — each step a lookup on a value that exists by the time it is needed, which is execution
            // rather than analysis. Object chains used to stop at one dot. (#173)
            return await ResolveMemberChain(variable, membersCall.iCS_S_MemberCall(), 0, ctx);
        }
        else if (ctx.iCS_S_ProcedureOrArrayCall() is { } procOrArrayCall)
        {
            return await EvaluateProcedureOrArrayCall(procOrArrayCall);
        }
        // `!key` alone, inside `With c` (measured, #1471).
        else if (ctx.iCS_S_DictionaryCall() is { } withBang)
        {
            return await Bang(withTargets.Count > 0
                ? withTargets.Peek()
                : throw new VBRunTimeException(ctx, VBStandardError.ObjectVariableOrWithBlockVariableNotSet), withBang.dictionaryCallStmt());
        }
        else
        {
            throw new NotImplementedException($"{ctx} is not supported");
        }
    }

    // A members call's value, as ResolveImplicitCall gives it, for one that ends in `!name`.
    private async Task<Vb6Value> ResolveImplicitCallWithoutBang(VB6Parser.ImplicitCallStmt_InStmtContext ctx, VB6Parser.ICS_S_MembersCallContext membersCall)
    {
        if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart
            && (interpreter.EnumIsQualifier(leadPart.GetText(), env, currentModule)
                    || !interpreter.ExecutionContext.TryInspectVariable(env, leadPart.GetText(), out _))
            && interpreter.TryResolveQualifier(leadPart.GetText(), out _))
            throw new NotImplementedException($"`{ctx.GetText()}` is supported only on a Collection");
        Vb6Value variable;
        if (membersCall.iCS_S_VariableOrProcedureCall() is { } varOrProCall)
            variable = await EvaluateValue(varOrProCall);
        else if (membersCall.iCS_S_ProcedureOrArrayCall() is { } leadProcOrArray)
            variable = (Vb6Value)(await EvaluateProcedureOrArrayCall(leadProcOrArray))!;
        else
            variable = withTargets.Count > 0
                ? withTargets.Peek()
                : throw new VBRunTimeException(ctx, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        return await ResolveMemberChain(variable, membersCall.iCS_S_MemberCall(), 0, ctx);
    }

    /// <summary>
    /// <c>x!name</c>: the default member of <c>x</c>, given the name as a String, which on a Collection is its item by key,
    /// <c>c!key</c> as <c>c("key")</c> and <c>c![key]</c> the same (measured, #1471). On a form it is Controls' Item
    /// (measured, #2066). Other objects are refused.
    /// </summary>
    private async Task<Vb6Value> Bang(Vb6Value target, VB6Parser.DictionaryCallStmtContext bang)
    {
        var key = BangKey(bang);
        return target.Value switch
        {
            VbCollection items => items.Default([new Vb6Value(key)]),
            ImageListImages images => images.Item(new Vb6Value(key)),
            FormControls controls => controls.Item([new Vb6Value(key)]),
            // Forms has no keys, so `Forms!Form1` is 13 (measured, #2496).
            VbForms forms => forms.Item([new Vb6Value(key)]),
            VbObject { ClassDef.Kind: InterpreterModuleKind.Form } form =>
                await interpreter.Loading((Form: form, Key: key),
                    static s => FormMembers.ControlsItem(s.Form, [new Vb6Value(s.Key)])),
            null when target.Type == Vb6Value.ValueType.Object
                => throw new VBRunTimeException(bang, VBStandardError.ObjectVariableOrWithBlockVariableNotSet),
            _ => throw new NotImplementedException($"`!{key}` is supported only on a Collection and a form's Controls"),
        };
    }

    /// <summary>The name after <c>!</c>, as the String the default member is given: <c>![a key]</c> without its brackets.</summary>
    internal static string BangKey(VB6Parser.DictionaryCallStmtContext bang)
    {
        if (bang.typeHint() != null)
            throw new NotImplementedException($"A type character after `!{bang.ambiguousIdentifier().GetText()}` is not implemented (#742)");
        var key = bang.ambiguousIdentifier().GetText();
        return key.Length >= 2 && key[0] == '[' && key[^1] == ']' ? key[1..^1] : key;
    }

    private const string TypeCharacterMismatch = "Type-declaration character does not match declared data type";

    // A character on a call to the program's own Function, `F&()` for `Function F%()`, must be the one its return type is
    // declared by: a mismatch, or one on a Variant Function, is VB6's compile error (measured, #740).
    private static void RefuseMismatchedCharacter(ProcedureInfo procedure, VB6Parser.TypeHintContext hint)
    {
        if (procedure.ReturnsVariant || procedure.ReturnType != BaseTypeMapper.Map(hint))
            throw new VBCompileErrorException(TypeCharacterMismatch);
    }

    // A variable, a constant or an Enum member read through a type character, `s$`, `i%`, `a%(1)` (#742): the character is a
    // check, not a conversion. The name is read as it would be without it, and its type must be the character's: the declared
    // one, a Const's value's, an Enum member's Long, a declared array's element type. A Variant, an object, a Byte, a Boolean
    // and a Date take no character, so a read of one through any character is VB6's compile error (measured,
    // docs/vb6-fidelity-oracle/a-name-read-with-a-type-character.md).
    private void RefuseMismatchedVariableCharacter(int location, Vb6Value value, VB6Parser.TypeHintContext hint)
    {
        if (CharacterTypeOf(location, value) != BaseTypeMapper.Map(hint))
            throw new VBCompileErrorException(TypeCharacterMismatch);
    }

    private Vb6Value.ValueType? CharacterTypeOf(int location, Vb6Value value)
    {
        var state = interpreter.ExecutionContext.State;
        if (state.IsVariant(location))
            return null;
        if (state.IsConstant(location) || state.IsReadOnly(location))
            return value.Type;
        if (value.Type.IsArray && value.Value is VBArray array)
            return array.ElementType;
        return state.DeclaredTypeOf(location);
    }

    // No procedure, intrinsic, constant, member of the form or of the library has the name, so a read through a character
    // declares it, as an assignment through one would.
    private bool NothingClaims(string name)
        => !FormMembers.TryBare(interpreter.ExecutionContext, env, name, out _, out _)
           && !interpreter.BuiltIns.TryGetBuiltInConstant(name, out _)
           && !interpreter.TryResolveProcedure(name, currentModule, out _, out _)
           && interpreter.GlobalMember(name) is null
           && !interpreter.EnumIsQualifier(name, env, currentModule)
           && !interpreter.TryResolveQualifier(name, out _)
           && !VbIntrinsicNames.IsIntrinsic(name)
           && !name.Equals("Clipboard", StringComparison.OrdinalIgnoreCase);

    // The name is the program's own Function, and no variable in scope hides it but the Function's own return slot, so that
    // `RI%(n - 1)` inside `RI%` is a recursive call.
    private bool CallsOwnFunction(string name)
        => interpreter.TryResolveProcedure(name, currentModule, out _, out var procedure) && procedure.IsFunction
           && (!env.TryGetVariableLocation(name, out _) || string.Equals(env.ReturnName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The library's functions that have a <c>$</c> form, every one measured to compile and answer a String.</summary>
    private static readonly HashSet<string> DollarForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chr", "ChrB", "ChrW", "Command", "CurDir", "Dir", "Environ", "Error", "Format", "Hex", "Input", "LCase", "Left", "LeftB", "LTrim", "Mid",
        "MidB", "Oct", "Right", "RightB", "RTrim", "Space", "Str", "String", "Trim", "UCase",
    };

    /// <summary>The <c>$</c> forms read with no parentheses, as a variable is: measured for each.</summary>
    private static readonly HashSet<string> BareDollarForms = new(StringComparer.OrdinalIgnoreCase) { "Command", "CurDir", "Dir" };

    /// <summary>Library functions on which VB6 refuses a <c>$</c> at compile time (measured).</summary>
    private static readonly HashSet<string> DollarRefused = new(StringComparer.OrdinalIgnoreCase)
    {
        "Array", "Asc", "AscW", "Choose", "IIf", "InStrRev", "Val",
    };

    /// <summary>Library functions on which VB6 ignores a <c>$</c>, the result keeping its usual type (measured).</summary>
    private static readonly HashSet<string> DollarIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "CLng", "CVar", "Fix", "InStr", "InStrB", "Int", "Len", "LenB", "Sgn",
    };

    /// <summary>Resolve a parenthesised call on an identifier — a control-array element (<c>Command1(i)</c>), a
    /// local array element (<c>arr(i)</c>), a user Function, or an intrinsic — as a VALUE. Shared by expression
    /// position and the leading part of a member chain (<c>Command1(i).Caption</c>).</summary>
    public async Task<object?> EvaluateProcedureOrArrayCall(VB6Parser.ICS_S_ProcedureOrArrayCallContext procOrArrayCall)
    {
        var groups = ParenGroups(procOrArrayCall);
        if (groups.Count <= 1)
            return await EvaluateCallOrElement(procOrArrayCall, groups.Count == 1 ? groups[0] : null);
        var later = new LaterGroups(this, groups, procOrArrayCall);
        var whole = await EvaluateCallOrElement(procOrArrayCall, groups[0], later.Evaluate) as Vb6Value?
            ?? throw new NotImplementedException($"indexing {procOrArrayCall.GetText()} is not implemented (#1476)");
        return IndexAll(whole, await later.Values(), procOrArrayCall);
    }

    /// <summary>
    /// Each parenthesised group of a call or index, in order, an empty <c>()</c> as null: <c>T3()(1)</c> is two groups, the
    /// first empty. The grammar gives an argsCall only to a group with something in it, so argsCall() alone cannot tell
    /// <c>T3()(1)</c>, the result of T3 indexed, from <c>T3(1)</c>.
    /// </summary>
    internal static List<VB6Parser.ArgsCallContext?> ParenGroups(VB6Parser.ICS_S_ProcedureOrArrayCallContext call)
    {
        var groups = new List<VB6Parser.ArgsCallContext?>();
        VB6Parser.ArgsCallContext? current = null;
        var open = false;
        foreach (var child in call.children ?? [])
        {
            if (child is ITerminalNode { Symbol.Type: VB6Parser.LPAREN })
            {
                open = true;
                current = null;
            }
            else if (open && child is VB6Parser.ArgsCallContext group)
                current = group;
            else if (open && child is ITerminalNode { Symbol.Type: VB6Parser.RPAREN })
            {
                groups.Add(current);
                open = false;
            }
        }
        return groups;
    }

    /// <summary>
    /// The index groups after the first, <c>Split(s)(0)</c> and <c>a(1)(0)</c> (#1476), evaluated once: the last group first,
    /// each group's subscripts left to right. VB6 evaluates them after all but the last of the first group's arguments or
    /// subscripts, then that last one (measured: <c>F2(a1, a2)(i1)(i2)</c> is a1, i2, i1, a2, and <c>a(i1)(i2)(i3)</c> is i3,
    /// i2, i1), which the argument loops do by calling <see cref="Evaluate"/> before their last.
    /// </summary>
    internal sealed class LaterGroups(ExpressionExecutor executor, IReadOnlyList<VB6Parser.ArgsCallContext?> groups,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        private List<Vb6Value>[]? values;

        public async Task Evaluate()
        {
            if (values is not null)
                return;
            var evaluated = new List<Vb6Value>[groups.Count - 1];
            for (var g = groups.Count - 1; g >= 1; g--)
                evaluated[g - 1] = groups[g] is { } group
                    ? await executor.EvaluateCallArgs(group)
                    : throw new NotImplementedException($"an empty index group, as in {ctx.GetText()}, is not implemented (#1476)");
            values = evaluated;
        }

        public async Task<List<Vb6Value>[]> Values()
        {
            await Evaluate();
            return values!;
        }
    }

    private static Vb6Value IndexAll(Vb6Value value, IEnumerable<List<Vb6Value>> groups, Antlr4.Runtime.ParserRuleContext ctx)
    {
        foreach (var group in groups)
            value = IndexResult(value, group, ctx);
        return value;
    }

    /// <summary>
    /// A value indexed as VB6 indexes a call's result (measured, #1476):
    /// <list type="bullet">
    /// <item>An array gives its element, and 9 out of range or for the wrong number of subscripts.</item>
    /// <item>A Collection gives its Item.</item>
    /// <item>A typed scalar, such as a Function's <c>As Integer</c> result, does not compile: "Type mismatch". A Variant
    /// holding a scalar is 13 at run time.</item>
    /// </list>
    /// An object's own default member is #174's, and Nothing is unmeasured: both are refused.
    /// </summary>
    internal static Vb6Value IndexResult(Vb6Value value, IReadOnlyList<Vb6Value> indices, Antlr4.Runtime.ParserRuleContext ctx)
    {
        if (value.Value is VBArray array)
        {
            try
            {
                return ElementAsRead(value, array, array.GetValue(Subscripts(indices)));
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(ctx, VBStandardError.SubscriptOutOfRange);
            }
        }
        if (value.Value is Components.InBox.TabStripTabs tabs)
        {
            if (indices.Count != 1)
                throw AvaloniaInterop.TabStripMembers.Refused("Tabs index arguments");
            return tabs.Item(indices[0]);
        }
        if (value.Value is ToolbarItems toolbarItems)
            return toolbarItems.Item(indices);
        if (value.Value is StatusBarPanels panels)
            return new Vb6Value(panels.Item(indices));
        if (value.Value is Components.InBox.TreeNodes nodes)
            return nodes.Default(indices);
        if (value.Value is VbCollection items)
            return items.Default(indices);
        if (value.Value is ImageListImages images)
            return images.Item(indices[0]);
        if (value.Type == Vb6Value.ValueType.Object || value.Value is VbObject)
            throw new NotImplementedException(value.Value is null
                ? $"indexing Nothing, as in {ctx.GetText()}, is not implemented: what VB6 raises is unmeasured (#1476)"
                : $"indexing an object, as in {ctx.GetText()}, reads its default member, which is not implemented (#174)");
        if (value.HasFixedType)
            throw new VBCompileErrorException("Type mismatch");
        throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch);
    }

    // A procedure with no parameters, given arguments, is called with none and its result indexed with them, where it
    // returns a Variant or an object: `F(1)` is `F()(1)`, and an As Collection one's Item (measured, #1476). A typed return
    // given arguments does not compile, which is #741's.
    private static bool IndexesItsResult(ProcedureInfo procedure, VB6Parser.ArgsCallContext? args) =>
        args is { } given && given.argCall().Length > 0 && procedure.Parameters.Count == 0
        && (procedure.ReturnsVariant || procedure.ReturnsObject);

    // The call or element of `name(first group)`, with no later index groups: EvaluateProcedureOrArrayCall's single group.
    internal async Task<object?> EvaluateCallOrElement(VB6Parser.ICS_S_ProcedureOrArrayCallContext procOrArrayCall,
        VB6Parser.ArgsCallContext? argsCtx, Func<Task>? beforeLast = null)
    {
        if (procOrArrayCall.dictionaryCallStmt() != null)
            throw new NotImplementedException($"dictionaryCall not supported");

        if (procOrArrayCall.ambiguousIdentifier() == null)
            throw new NotImplementedException($"only proc call supportedhere");

        // A type-declaration character on the call (#742). `$` is the one a program reaches first, the library's string
        // functions written `Trim$` and `Left$` (#1071). The name is looked up without it by the ordinary search, and the
        // `$` is then checked against what was found (measured, docs/vb6-fidelity-oracle/a-type-character-on-a-name-the-program-also-declares-left-beside-a-user-left.md). The other characters
        // are checked against the program's own Function's return type (measured, #740), or an array's element type, `a%(1)`
        // (measured, #742), and still stop the run on anything else.
        var name = procOrArrayCall.ambiguousIdentifier().GetText();
        var dollar = false;
        VB6Parser.TypeHintContext? ownHint = null, variableHint = null;
        if (procOrArrayCall.typeHint() is { } hint)
        {
            if (hint.GetText() == "$")
                dollar = true;
            else if (CallsOwnFunction(name))
                ownHint = hint;
            else if (env.TryGetVariableLocation(name, out _))
                variableHint = hint;
            else
                throw new NotImplementedException($"the type-declaration character {hint.GetText()} on a call is not implemented (#742)");
        }

        // A control array indexed by element (`Command1(i)`) — the shared name resolves to a ControlArrayGroup
        // value; return the element control. A missing element is Err 340 (oracle-verified against vb6.exe).
        if (!interpreter.TryReadNow(env, name, out var named, out var maybeGroup, out var namedNeed))
            (named, maybeGroup) = await interpreter.ReadVariableAfter(env, name, namedNeed!);
        if (named && argsCtx is null && ParenGroups(procOrArrayCall).Count > 0
            && !string.Equals(env.ReturnName, name, StringComparison.OrdinalIgnoreCase)
            && env.TryGetVariableLocation(name, out var wholeArrayLocation))
        {
            if (interpreter.ExecutionContext.State.IsDeclaredArray(wholeArrayLocation)
                && !env.VariantParameters.Contains(name))
            {
                // Empty parentheses name the declared array itself, retaining its caller slot for ByRef (#2474).
                if (procOrArrayCall.typeHint() is { } arrayHint)
                    RefuseMismatchedVariableCharacter(wholeArrayLocation, maybeGroup, arrayHint);
                return maybeGroup;
            }
            if (interpreter.ExecutionContext.State.IsVariant(wholeArrayLocation))
                throw new VBCompileErrorException("Expected Sub, Function, or Property");
        }
        if (variableHint is not null && !maybeGroup.Type.IsArray)
            throw new NotImplementedException($"`{name}{variableHint.GetText()}(…)` is not implemented: {name} is not an array (#742)");
        if (named && maybeGroup.Value is Components.InBox.TabStripTabs tabs)
        {
            var indices = await EvaluateCallArgs(argsCtx);
            if (indices.Count != 1)
                throw AvaloniaInterop.TabStripMembers.Refused("Tabs index arguments");
            return tabs.Item(indices[0]);
        }
        // A Collection's default member, Item: `c(1)` and `c("key")` (#1471). `c(1)(2)` indexes the Item (#1476).
        if (named && maybeGroup.Value is ToolbarItems toolbarItems)
            return toolbarItems.Item(await EvaluateCallArgs(argsCtx));
        if (named && maybeGroup.Value is Components.InBox.TreeNodes nodes)
        {
            if (dollar) throw new NotImplementedException();
            return nodes.Default(await EvaluateCallArgs(argsCtx));
        }
        if (named && maybeGroup.Value is VbCollection items)
        {
            if (dollar)
                throw new NotImplementedException($"{name}$ on a Collection is not implemented (#742)");
            return await AsBound(maybeGroup, "Item", async () => items.Default(await EvaluateMemberArgs(items, "Item", argsCtx)));
        }
        if (named && maybeGroup.Value is StatusBarPanels panels)
            return new Vb6Value(panels.Item(await EvaluateCallArgs(argsCtx)));
        if (named && maybeGroup.Value is ImageListImages images)
            return images.Item((await EvaluateCallArgs(argsCtx))[0]);
        // A form's Controls held in a variable, `o(1)` (#1060).
        if (named && maybeGroup.Value is FormControls heldControls)
        {
            if (dollar)
                throw new NotImplementedException($"{name}$ on a form's Controls is not implemented (#742)");
            return heldControls.Item(await EvaluateCallArgs(argsCtx));
        }
        // Forms held in a variable, `o(0)` (#2496).
        if (named && maybeGroup.Value is VbForms heldForms)
        {
            if (dollar)
                throw new NotImplementedException($"{name}$ on Forms is not implemented (#742)");
            return heldForms.Item(await EvaluateCallArgs(argsCtx));
        }
        // Indexing a variable declared As Collection that holds Nothing is 91 (measured, #1471).
        if (named && maybeGroup.Type == Vb6Value.ValueType.Object && maybeGroup.Value is null
            && maybeGroup.DeclaredAs is { } declaredAs && IsCollectionType(declaredAs))
            throw new VBRunTimeException(procOrArrayCall, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
        if (named && maybeGroup.Value is ControlArrayGroup group)
        {
            if (dollar)
                throw new NotImplementedException($"{name}$ on a control array is not implemented (#742)");
            var idxArgs = await EvaluateCallArgs(argsCtx);
            if (idxArgs.Count != 1)
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.SubscriptOutOfRange);
            var elementIndex = ControlArrayIndex(idxArgs[0]);
            if (!group.TryGetElement(elementIndex, out var element))
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.ControlArrayElementIsMissing(elementIndex));
            return new Vb6Value(element);
        }

        // Resolution order (VB6): a local ARRAY variable → user procedure → intrinsic. A non-array
        // variable of the same name (e.g. a Function's own return slot) is not array-indexing — it falls
        // through so `Fact(n - 1)` inside `Fact` is a recursive call, and `Zero()` finds the Function.
        if (await interpreter.ReadVariable(env, name) is (true, var variable) && variable.Type.IsArray)
        {
            if (procOrArrayCall.typeHint() is { } elementHint)
            {
                if (!env.TryGetVariableLocation(name, out var arrayLocation))
                    throw new NotImplementedException($"{name}{elementHint.GetText()} on an array is not implemented (#742)");
                RefuseMismatchedVariableCharacter(arrayLocation, variable, elementHint);
            }
            if (variable.Value is not VBArray array)
                throw new VBCompileErrorException("Array expected");
            var indices = await EvaluateCallArgs(argsCtx, beforeLast);
            try
            {
                return ElementAsRead(variable, array, array.GetValue(Subscripts(indices)));
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.SubscriptOutOfRange);
            }
        }
        // A declared scalar claims its name before the library does (#741). Variant and object slots keep
        // their indexing/default-member paths; the return slot must allow Fact(n - 1) inside Fact.
        if (argsCtx is not null && named && !string.Equals(env.ReturnName, name, StringComparison.OrdinalIgnoreCase)
            && !env.VariantParameters.Contains(name) && env.TryGetVariableLocation(name, out var scalarLocation)
            && interpreter.ExecutionContext.State.DeclaredTypeOf(scalarLocation) is { } scalarType
            && VbNumeric.IsDeclarableScalar(scalarType))
            throw new VBCompileErrorException("Expected array");

        // In a form's own code, a member of the form (#995). A method is called: `TextWidth("abc")`. A property called with
        // arguments is VBA's function of the name only when the arguments fit it, `Left("abc", 1)`, and never a project
        // procedure's (measured). Any other is refused: VB6 gives "Wrong number of arguments" where the property's type
        // takes no index, and reaches a default member where it does (`Controls(0)`), which HexIDE has none of yet.
        if (FormMembers.TryBare(interpreter.ExecutionContext, env, name, out var form, out var formMember))
        {
            if (ownHint is not null)
                throw new NotImplementedException($"{name}{ownHint.GetText()} on a member of the form is not implemented (#742)");
            var formArgs = await EvaluateCallArgs(argsCtx);
            // `Left$("abc", 1)` in a form's code is VBA's Left$, a String, as `Left("abc", 1)` is VBA's Left (measured).
            if (dollar && !FormMembers.CallIsVbas(name, formArgs.Count))
                throw new NotImplementedException($"{name}$ on a member of the form is not implemented (#742)");
            if (formMember.IsMethod)
                return await interpreter.Loading((Form: form, Name: name, Args: formArgs), static s => FormMembers.Call(s.Form, s.Name, s.Args));
            if (FormMembers.IsControls(name))
            {
                if (dollar)
                    throw new NotImplementedException($"{name}$ on a member of the form is not implemented (#742)");
                return await interpreter.Loading((Form: form, Args: formArgs), static s => FormMembers.ControlsItem(s.Form, s.Args));
            }
            if (FormMembers.CallIsVbas(name, formArgs.Count) && await EvaluateFunction(name, formArgs) is { } vbaResult)
                return dollar ? DollarResult(vbaResult, procOrArrayCall) : vbaResult;
            throw new NotImplementedException($"{form.ClassName}'s {name} with arguments is not implemented{FormMembers.TrackedBy(name)}");
        }
        if (interpreter.TryResolveProcedure(name, currentModule, out _, out var procedure))
        {
            // The program's own Trim answers Trim$, and only a String Function matches the `$`: a Variant one, declared
            // with no As clause, is the mismatch (measured, TU_01 and TV_01).
            if (dollar && (procedure.ReturnType != Vb6Value.ValueType.String || procedure.ReturnsVariant))
                throw new VBCompileErrorException(TypeCharacterMismatch);
            if (ownHint is not null)
                RefuseMismatchedCharacter(procedure, ownHint);
            if (IndexesItsResult(procedure, argsCtx))
            {
                var index = await EvaluateCallArgs(argsCtx, beforeLast);
                var whole = await interpreter.CallProcedure(name, [], currentModule, callerEnv: env, callerFrames: stmtFrames)
                    ?? throw new VBSubOrFunctionNotDefinedException(name);
                return IndexResult(whole, index, procOrArrayCall);
            }
            var callArgs = await ResolveCallArgs(argsCtx, beforeLast);
            if (await interpreter.CallProcedure(name, callArgs, currentModule, callerEnv: env, callerFrames: stmtFrames) is { } fnResult)
                return fnResult;
            throw new VBSubOrFunctionNotDefinedException(name); // a Sub used where a value is required
        }
        if (!dollar && interpreter.GlobalMember(name) is { } global)
            return await interpreter.CallLibraryMember(global, name, await EvaluateCallArgs(argsCtx))
                ?? throw new VBSubOrFunctionNotDefinedException(name);
        if (!dollar && name.Equals("CallByName", StringComparison.OrdinalIgnoreCase))
            return await interpreter.CallByName(await ResolveCallArgs(argsCtx), stmtFrames);
        // `Forms(i)`, where nothing the program declares claims the name: Forms' default member, Item (#2496).
        if (!dollar && interpreter.UnclaimedLibraryObject(name) is { Value: VbForms forms })
            return forms.Item(await EvaluateCallArgs(argsCtx));
        if (dollar && DollarRefused.Contains(name))
            throw new VBCompileErrorException(TypeCharacterMismatch);
        if (dollar && !DollarForms.Contains(name) && !DollarIgnored.Contains(name))
            throw new NotImplementedException($"{name}$ is not implemented: what VB6 does with a $ on {name} is not measured (#742)");
        // A library function's later index groups: before its one argument, `Split(t)(i)` being i then t, and after its two,
        // `Split(s, d)(i)` being s, d, i (measured, #1476). No rule fits both, so each count does as measured.
        if (beforeLast is not null && ArgSlots(argsCtx).Count() <= 1)
            await beforeLast();
        var args = await EvaluateIntrinsicArgs(name, argsCtx);
        if (await EvaluateFunction(name, args) is { } builtInResult)
            return dollar && DollarForms.Contains(name) ? DollarResult(builtInResult, procOrArrayCall) : builtInResult;

        throw new VBSubOrFunctionNotDefinedException(name);
    }

    // A `$` form is its plain form with the result made a String, so where the plain form passes a Null through, the `$`
    // form raises 94 (measured on all 23). The String is typed, where the plain form's is a Variant's: "9" from `Mid$` is
    // compared as text against `Case Is > 10` and takes the Case (measured for the fourteen that answer text, #1134).
    private static Vb6Value DollarResult(Vb6Value result, Antlr4.Runtime.ParserRuleContext ctx) =>
        result.IsNull
            ? throw new VBRunTimeException(ctx, VBStandardError.InvalidUseOfNull)
            : new Vb6Value(VbDisplay.ToText(result)).AsFixedType();

    /// <summary>
    /// A field of an object, given an index: <c>Form1.Text1(0)</c>, a control array named through its form, picks its
    /// element as a bare <c>Text1(0)</c> does, and a missing one is 340, its message naming the index (measured, #1386).
    /// <para>
    /// A class's Variant holding an array, <c>k.V(1)</c>, is its element through its declared class and 451 through
    /// Object (measured, #1435). Other fields and bindings remain refused.
    /// </para>
    /// </summary>
    private Vb6Value FieldElement(Vb6Value receiver, VbObject obj, string name, Vb6Value field, List<Vb6Value> indexes,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        if (IsVariantArrayField(obj, name, field))
        {
            if (string.Equals(receiver.DeclaredAs, obj.ClassName, StringComparison.OrdinalIgnoreCase))
                return IndexResult(field, indexes, ctx);
            if (string.Equals(receiver.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase))
                throw new VBRunTimeException(ctx, VBStandardError.ObjectNotACollection);
        }
        if (field.Value is not ControlArrayGroup group)
            throw new NotImplementedException(
                $"{obj.ClassName}.{name}(...) is not implemented: only a control array is indexed through its object (#1435)");
        if (indexes.Count != 1)
            throw new VBRunTimeException(ctx, VBStandardError.SubscriptOutOfRange);
        var index = ControlArrayIndex(indexes[0]);
        return group.TryGetElement(index, out var element)
            ? new Vb6Value(element)
            : throw new VBRunTimeException(ctx, VBStandardError.ControlArrayElementIsMissing(index));
    }

    internal bool IsVariantArrayField(VbObject obj, string name, Vb6Value field) =>
        field.Value is VBArray && obj.ClassDef.Kind == InterpreterModuleKind.Class
        && obj.InstanceEnv.TryGetVariableLocation(name, out var slot)
        && interpreter.ExecutionContext.State.IsVariant(slot);

    // Resolve a namespace-qualified member reference as a VALUE (expression context): `Module1.Foo(args)`,
    // `Module1.PublicConst`, `VBA.Abs(x)`, `VBA.Math.Abs(x)`, `VBA.vbCrLf`. Library-module segments
    // (`Math`/`Strings`/…) are transparent and skipped permissively; multi-level object-graph chains are not
    // supported here (that is the deferred general member-chain wall).

    /// <summary>
    /// Resolve ONE member on a value: `.Name` applied to whatever the previous step produced.
    ///
    /// Extracted so a chain can fold it. It used to be inline and shaped for exactly one dot, which is the
    /// whole of why `obj.a.b` was refused — not anything the CST could not represent. (#173)
    /// </summary>
    private async Task<Vb6Value> ResolveOneMember(
        Vb6Value variable, VB6Parser.ICS_S_MemberCallContext member, Antlr4.Runtime.ParserRuleContext ctx,
        Func<Task>? beforeLast = null)
    {
        if (variable.Value is VBTabStrip strip && AvaloniaInterop.TabStripMembers.IsOwn(MemberName(member)))
            return AvaloniaInterop.TabStripMembers.Read(strip, MemberName(member), await EvaluateCallArgs(MemberArgs(member)));
        if (variable.Value is VBToolbar toolbar)
        {
            if (MemberArgs(member) is null && MemberName(member).ToLowerInvariant() is "hotimagelist" or "disabledimagelist"
                && toolbar.TryGetProperty(MemberName(member), out var imageList)) return imageList;
            VBToolbar.RefusePending(MemberName(member));
            if (MemberName(member).Equals("Buttons", StringComparison.OrdinalIgnoreCase) && MemberArgs(member) is { } indexes)
                return toolbar.Buttons.Item(await EvaluateCallArgs(indexes));
        }
        if (variable.Value is ToolbarItem button && MemberName(member).Equals("ButtonMenus", StringComparison.OrdinalIgnoreCase)
            && MemberArgs(member) is { } menuIndexes && button.TryGetProperty("ButtonMenus", out var menuCollection))
            return ((ToolbarItems)menuCollection.Value!).Item(await EvaluateCallArgs(menuIndexes));
        if (variable.Value is BasicInterpreter.LibraryObject managed && MemberArgs(member) is null
            && managed.TryGetProperty(MemberName(member), out var property))
            return property;
        if (variable.Value is BuiltinControls.VBStatusBar statusBar && MemberName(member).Equals("Panels", StringComparison.OrdinalIgnoreCase))
            return MemberArgs(member) is { } panelArgs
                ? new Vb6Value(statusBar.Panels.Item(await EvaluateCallArgs(panelArgs))) : new Vb6Value(statusBar.Panels);
        if (variable.Value is VBImageList && MemberName(member).ToLowerInvariant() is "name" or "tag" or "parent" or "object")
        {
            // These four are the invisible control's extender, answered by the shared control bindings.
        }
        else if (variable.Value is ICSharpAsyncProxy proxy)
            return await AsBound(variable, MemberName(member), async () =>
                await interpreter.CallProxy(proxy, MemberName(member), await EvaluateMemberArgs(proxy, MemberName(member), MemberArgs(member)))
                    ?? throw new VBSubOrFunctionNotDefinedException(MemberName(member)));
        // A class instance member (`obj.Method(args)` or `obj.Field`) — method dispatch wins over a field of
        // the same name. Nothing.Member → Error 91.
        if (variable.Type == Vb6Value.ValueType.Object)
        {
            if (variable.Value is not VbObject vobj)
                throw new VBRunTimeException(ctx, VBStandardError.ObjectVariableOrWithBlockVariableNotSet);
            // Through an interface-typed name, `Draw` means `IFoo_Draw`, and nothing else the class declares
            // is reachable at all — so resolve what to look up before any of the three lookups below. (#186)
            //
            // Folding a chain gives the right scope for free: only the FIRST hop's value came from a slot
            // read, so only it carries a declared class. `x.A.B` views `A` through the interface and then
            // resolves `B` against whatever A returned, which is what VB6 does.
            var memberName = VbInterface.ResolveMember(variable, vobj, MemberName(member))
                ?? throw new VBMethodOrDataMemberNotFoundException(MemberName(member), variable.Type);
            if (vobj.ClassDef.PrePass.Procedures.TryGetValue(memberName, out var method))
            {
                if (IndexesItsResult(method, MemberArgs(member)))
                {
                    var index = await EvaluateCallArgs(MemberArgs(member), beforeLast);
                    var whole = await interpreter.RunProcedure(vobj.ClassDef, method, [], vobj.InstanceEnv, variable, stmtFrames)
                        ?? throw new VBSubOrFunctionNotDefinedException(memberName);
                    return IndexResult(whole, index, ctx);
                }
                var callArgs = await ResolveCallArgs(MemberArgs(member), beforeLast);
                return await interpreter.RunProcedure(vobj.ClassDef, method, callArgs, vobj.InstanceEnv, variable, stmtFrames)
                    ?? throw new VBSubOrFunctionNotDefinedException(memberName);   // a Sub used as a value
            }
            // A read (`= x.P`) dispatches the Property Get accessor (Function-like: returns via its name).
            if (vobj.ClassDef.PrePass.Properties.TryGetValue(memberName, out var prop) && prop.Get is { } getter)
            {
                if (IndexesItsResult(getter, MemberArgs(member)))
                {
                    var index = await EvaluateCallArgs(MemberArgs(member), beforeLast);
                    var whole = await interpreter.RunProcedure(vobj.ClassDef, getter, [], vobj.InstanceEnv, variable, stmtFrames)
                        ?? throw new VBSubOrFunctionNotDefinedException(memberName);
                    return IndexResult(whole, index, ctx);
                }
                var getArgs = await ResolveCallArgs(MemberArgs(member), beforeLast);
                return await interpreter.RunProcedure(vobj.ClassDef, getter, getArgs, vobj.InstanceEnv, variable, stmtFrames)
                    ?? throw new VBSubOrFunctionNotDefinedException(memberName);
            }
            if (!interpreter.TryReadNow(vobj.InstanceEnv, memberName, out var isField, out var fieldVal, out var isFieldNeed))
                (isField, fieldVal) = await interpreter.ReadVariableAfter(vobj.InstanceEnv, memberName, isFieldNeed!);
            if (isField)
                return MemberArgs(member) is { } fieldArgs
                    ? FieldElement(variable, vobj, memberName, fieldVal, await EvaluateCallArgs(fieldArgs), ctx)
                    : fieldVal;
            // A form's built-in members are its window's: `Me.Caption`, `Form1.Width` (#822). They come after the form's
            // own members, which VB6 refuses to let clash with them. Using one loads the form, as a control does.
            if (await interpreter.Loading((Form: vobj, Member: memberName), static s => s.Form.BuiltInMembersFor(s.Member)) is { } formWindow)
            {
                // A VB6 Form member HexIDE can't read is refused as not implemented: 438 is what VB6 says of a member the
                // Form doesn't have, which this one does (#995). A method is the window's to call.
                if (vobj.ClassDef.Kind == InterpreterModuleKind.Form && FormMembers.IsMember(memberName, out var formMember))
                {
                    if (formMember.IsMethod)
                        return FormMembers.Call(vobj, memberName, await EvaluateCallArgs(MemberArgs(member)));
                    if (MemberArgs(member) == null)
                        return FormMembers.Read(vobj, memberName);
                    if (FormMembers.IsControls(memberName))
                        return FormMembers.ControlsItem(vobj, await EvaluateCallArgs(MemberArgs(member)));
                }
                return await ResolveOneMember(new Vb6Value(formWindow), member, ctx);
            }
            throw new VBMethodOrDataMemberNotFoundException(memberName, variable.Type);
        }

        if (variable.Value is VBTreeView capacityTree && MemberName(member).Equals("GetVisibleCount", StringComparison.OrdinalIgnoreCase))
        {
            if ((await EvaluateCallArgs(MemberArgs(member))).Count != 0) throw new NotImplementedException();
            return new Vb6Value(capacityTree.GetVisibleCount());
        }

        if (variable.Value is VBTreeView treeView && MemberName(member).Equals("Nodes", StringComparison.OrdinalIgnoreCase)
            && MemberArgs(member) is { } nodeIndexes)
            return treeView.Nodes.Default(await EvaluateCallArgs(nodeIndexes));

        if (variable.Value is VBSSTab tab && SSTabMembers.IsIndexed(MemberName(member))
            && MemberArgs(member) is { } tabIndexes)
            return SSTabMembers.ReadIndexed(tab, MemberName(member), await EvaluateCallArgs(tabIndexes));

        // A list's List(i) and a ListBox's Selected(i), the control members here that take an index (#821, #1395).
        if (variable.Value is Control listControl && ListMembers.IsList(listControl) && MemberArgs(member) is { } listIndexes)
        {
            if (MemberName(member).Equals("List", StringComparison.OrdinalIgnoreCase))
                return ListMembers.ReadItem(listControl, await EvaluateCallArgs(listIndexes));
            if (listControl is VBListBox listBox && MemberName(member).Equals("Selected", StringComparison.OrdinalIgnoreCase))
                return ListMembers.ReadSelected(listBox, await EvaluateCallArgs(listIndexes));
        }

        var memberIdentifier = member.GetText().TrimStart('.') ?? throw new VBRunTimeException(ctx, VBStandardError.ObjectRequired, "Null member name");

        // A CSharp property bag (e.g. Err.Number) resolves its own properties by name.
        if (variable.Value is ICSharpPropertyBag propertyBag && propertyBag.TryGetProperty(memberIdentifier, out var bagValue))
            return bagValue;

        if (variable.Type != Vb6Value.ValueType.Control ||
            variable.Value is not Control control)
            throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
        // Every member of a control Controls.Remove took out is 735, its Name included (measured, #1556).
        VBProps.RefuseIfRemoved(control);

        if (control is VBTimer && TimerMembers.TryGet(control, memberIdentifier, out var timerValue))
            return timerValue;

        AvaloniaInterop.UserControlMembers.Check(variable, memberIdentifier, reading: true);
        if (AvaloniaInterop.UserControlMembers.TryGet(control, memberIdentifier, out var userControlValue))
            return userControlValue;

        if (AvaloniaInterop.UserControlContainers.TryGet(control, memberIdentifier, out var containerValue))
            return containerValue;

        if (AvaloniaInterop.RichTextBoxMembers.TryGet(control, memberIdentifier, out var richTextValue))
            return richTextValue;

        // An in-box control's own members, and the extender's it lacks, by its member table (#1082).
        if ((AvaloniaInterop.StatusBarMembers.TryGet(control, memberIdentifier, out var inBoxValue)
             ?? AvaloniaInterop.ProgressBarMembers.TryGet(control, memberIdentifier, out inBoxValue)) is { } inBox)
            return inBox ? inBoxValue : throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);

        if (AvaloniaInterop.SSTabMembers.TryGet(control, memberIdentifier, out var tabValue))
            return tabValue;

        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            if (AvaloniaInteroperability.TryGet(control, prop, out var value))
                return value;
        }
        // FontName, which is no property of its own here but the name of the control's font (#1141).
        if (FontMembers.TryGet(control, memberIdentifier, out var fontMember))
            return fontMember;
        // A TextBox's SelStart, SelLength and SelText, which exist only at run time (#1150).
        if (TextBoxMembers.TryGet(control, memberIdentifier, out var textBoxMember))
            return textBoxMember;
        // A list's ListCount (#821), and a ListBox's SelCount and Text (#1395).
        if (ListMembers.TryGet(control, memberIdentifier, out var listMember))
            return listMember;
        // Any control's Name, and an array element's Index (#1431).
        if (IdentityMembers.TryGet(control, memberIdentifier, out var identityMember))
            return identityMember;

        throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
    }

    /// <summary>
    /// Fold every remaining member onto a value, left to right.
    ///
    /// A UDT hands the rest of the chain to <see cref="GetUdtField"/> to walk its owned fields and indices.
    /// Other members resolve a step at a time — so a chain may change kind as it goes (an object field
    /// holding a UDT, a UDT field holding an object) without any step needing to know what follows it.
    /// </summary>
    internal async Task<Vb6Value> ResolveMemberChain(
        Vb6Value current, VB6Parser.ICS_S_MemberCallContext[] members, int from,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        for (var i = from; i < members.Length; i++)
        {
            if (current.Value is VbUdt)
                return await GetUdtField(current, members[i..], ctx);
            // A member reached through a Variant is late-bound, and its result is a Variant's whatever the member is
            // declared as (measured, #987: `vo.P * 7000` with `Property Get P() As Integer` returning 5 is 35000 through
            // `Dim vo`, and 6 through `Dim o As Foo`; `With vo: .P` is the same).
            var lateBound = current.HeldAsVariant;
            current = await ResolveMemberIndexed(current, members[i], ctx);
            if (lateBound)
                current = current.AsVariant();
        }
        return current;
    }

    private async Task<object?> ResolveQualifiedMemberValue(
        BasicInterpreter.QualifierTarget qualifier,
        VB6Parser.ICS_S_MemberCallContext[] members,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        int idx = 0;
        if (qualifier.Kind == BasicInterpreter.QualifierKind.Library)
            while (idx < members.Length - 1 && BasicInterpreter.IsIntrinsicModuleSegment(MemberName(members[idx])))
                idx++;   // skip transparent VBA.<module>. segments

        // The qualifier is simply the FIRST step of an ordinary chain: `Module1.p.In1.Z` is Module1 -> p
        // -> In1 -> Z, and everything after the first resolves exactly as it would unqualified — measured,
        // to four levels. This used to refuse anything past one member while the very same chain worked
        // without the qualifier, because the two were treated as different problems when only the first
        // step differs in kind. (#173)
        async Task<object?> ThenTheRest(Vb6Value head) =>
            idx + 1 < members.Length
                ? await ResolveMemberChain(head, members, idx + 1, ctx)
                : head;

        var member = members[idx];
        var memberName = MemberName(member);
        var argsCtx = MemberArgs(member);

        // The PROJECT level is transparent: it names the one project in scope, so it changes nothing about
        // what is found and is simply stepped over. `Project1.Module1.MyEnum.Foo` is then resolved exactly
        // as `Module1.MyEnum.Foo` would be, and a bare `Project1.Foo` as `Foo`.
        if (qualifier.Kind == BasicInterpreter.QualifierKind.Project)
        {
            // The next segment may itself be a qualifier (a module, or an enum) — `Project1.Module1.…` and
            // `Project1.MyEnum.Foo` — so re-enter with it and the rest of the chain.
            if (idx + 1 < members.Length && interpreter.TryResolveQualifier(memberName, out var inner))
                return await ResolveQualifiedMemberValue(inner, members[(idx + 1)..], ctx);
            // Otherwise it is an ordinary project-level name: `Project1.Foo`, a hoisted enum member or a
            // module-level Public. It is PROJECT-wide, so the search is too — the current module first,
            // then every other standard module. A member hoisted by Module2's enum is not in Module1's
            // env, and looking only there is what made this fail on the first run.
            var (inProject, projectMember) = await interpreter.ReadVariable(env, memberName);
            if (inProject)
                return await ThenTheRest(projectMember);
            foreach (var m in interpreter.Modules.All)
            {
                if (m.Kind != InterpreterModuleKind.Standard) continue;
                if (await interpreter.ReadVariable(m.ModuleEnv, memberName) is (true, var fromModule))
                    return await ThenTheRest(fromModule);
            }
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Library)
        {
            // A library-qualified name is an intrinsic function, or a constant IN THAT LIBRARY.
            //
            // The library level SELECTS — it is not transparent. `VBRUN.vbCancel` is 0 and `VBA.vbCancel`
            // is 2, because vbCancel is declared by both VBRUN.DragConstants and VBA.VbMsgBoxResult with
            // different values. This branch used to resolve the bare name and step over the library, so
            // it answered 2 for both: a wrong value. The reason given at the time was that no table of
            // the libraries' enum names existed and inventing one would be pre-execution analysis —
            // correct, and the operative word was inventing. VB6InBoxLibraries is measured from the real
            // type libraries, so the level can now be honoured instead of skipped.
            var library = qualifier.Name!;
            while (true)
            {
                if (memberName.Equals("CallByName", StringComparison.OrdinalIgnoreCase))
                    return await ThenTheRest(await interpreter.CallByName(await ResolveCallArgs(argsCtx), stmtFrames));
                var args = argsCtx != null ? await EvaluateIntrinsicArgs(memberName, argsCtx) : new List<Vb6Value>();
                if (await EvaluateFunction(memberName, args) is { } fn)
                    return await ThenTheRest(fn);
                if (args.Count == 0 && VB6InBoxLibraries.TryInLibrary(library, memberName, out var c))
                    return await ThenTheRest(c);

                // A middle segment is stepped over only if it genuinely belongs here: an intrinsic module
                // of the VBA library (`VBA.Math.Abs` is `VBA.Abs`), or a container this library declares
                // (`VBRUN.AlignConstants.vbAlignBottom`). Anything else is refused rather than skipped —
                // `VBRUN.VbMsgBoxResult.vbCancel` and `stdole.vbCancel` are both measured ILLEGAL, and
                // skipping is what used to make them quietly succeed.
                var isStepOver = idx + 1 < members.Length
                    && (BasicInterpreter.IsIntrinsicModuleSegment(memberName)
                        || VB6InBoxLibraries.ContainerBelongsTo(library, memberName));
                if (!isStepOver)
                    throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);

                // Inside a named container the lookup is strict on both levels, so a member of a DIFFERENT
                // container of the same library is not found through this one.
                if (VB6InBoxLibraries.TryContainer(memberName, out var container))
                {
                    var inner = MemberName(members[idx + 1]);
                    if (MemberArgs(members[idx + 1]) == null
                        && VB6InBoxLibraries.TryInLibraryContainer(library, container.Name, inner, out var cv))
                    {
                        idx++;
                        return await ThenTheRest(cv);
                    }
                    throw new VBMethodOrDataMemberNotFoundException(inner, Vb6Value.ValueType.EmptyVariant);
                }

                idx++;
                memberName = MemberName(members[idx]);
                argsCtx = MemberArgs(members[idx]);
            }
        }

        if (qualifier.Kind == BasicInterpreter.QualifierKind.InBoxContainer)
        {
            // `DragConstants.vbCancel` -> 0, `VbMsgBoxResult.vbCancel` -> 2. The container selects on its
            // own; all 79 container names are unique across the four libraries. Strict: `DragConstants
            // .vbYes` is measured illegal, so a member of another container is refused here.
            if (argsCtx == null
                && VB6InBoxLibraries.TryInContainer(qualifier.Name!, memberName, out var cv))
                return await ThenTheRest(cv);
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        // A referenced control library (#1082): `MSComctlLib.ccOrientationVertical`, and through one of its enums,
        // `MSComctlLib.ScrollingConstants.ccScrollingSmooth`, which is strict as the in-box libraries' enums are.
        if (qualifier.Kind == BasicInterpreter.QualifierKind.RuntimeLibrary)
        {
            var library = qualifier.RuntimeLibrary!;
            if (interpreter.GlobalMember(memberName, library) is { } global)
                return await ThenTheRest(await interpreter.CallLibraryMember(global, memberName, await EvaluateCallArgs(argsCtx))
                    ?? throw new VBSubOrFunctionNotDefinedException(memberName));
            if (argsCtx == null && library.TryBare(memberName, out var c))
                return await ThenTheRest(c);
            if (argsCtx == null && idx + 1 < members.Length && library.ContainerName(memberName) is { } container)
            {
                var inner = MemberName(members[idx + 1]);
                if (MemberArgs(members[idx + 1]) == null && library.TryInContainer(container, inner, out var cv))
                {
                    idx++;
                    return await ThenTheRest(cv);
                }
                throw new VBMethodOrDataMemberNotFoundException(inner, Vb6Value.ValueType.EmptyVariant);
            }
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        // `ScrollingConstants.ccScrollingStandard`: one of a referenced control library's enums (#1082).
        if (qualifier.Kind == BasicInterpreter.QualifierKind.ControlLibraryContainer)
        {
            if (argsCtx == null && qualifier.RuntimeLibrary!.TryInContainer(qualifier.Name!, memberName, out var cv))
                return await ThenTheRest(cv);
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Enum)
        {
            // MyEnum.Member -> the member's Long value.
            if (qualifier.EnumMembers!.TryGetValue(memberName, out var enumValue))
                return await ThenTheRest(new Vb6Value(enumValue));
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        // Module-qualified: a Public procedure (private is visible only when qualifying the current module),
        // else a module-level Public variable/const.
        // A character on the member, `Module2.pS$`, is checked as it is on the bare name (measured, #742).
        var module = qualifier.Module!;
        var memberHint = member.iCS_S_VariableOrProcedureCall()?.typeHint() ?? member.iCS_S_ProcedureOrArrayCall()?.typeHint();
        if (module.PrePass.Procedures.TryGetValue(memberName, out var proc)
            && (!proc.IsPrivate || ReferenceEquals(module, currentModule)))
        {
            if (memberHint is not null)
                RefuseMismatchedCharacter(proc, memberHint);
            var callArgs = argsCtx != null ? await ResolveCallArgs(argsCtx) : new List<CallArg>();
            if (await interpreter.RunProcedure(module, proc, callArgs, callerFrames: stmtFrames) is { } result)
                return await ThenTheRest(result);
            throw new VBSubOrFunctionNotDefinedException(memberName);   // a Sub used where a value is required
        }
        // `Module1.MyEnum.Member` — the three-part form, measured legal. Unlike the library case this one
        // CAN be validated exactly, because the module's own enums are collected in its pre-pass: the
        // middle segment must name an enum this module declares, and the last must be one of its members.
        // `Module1.Member` already worked, because a member is hoisted as a module-level name.
        if (module.PrePass.Enums.TryGetValue(memberName, out var enumTable)
            && interpreter.EnumIsQualifier(memberName, module.ModuleEnv, module))
        {
            if (idx + 1 == members.Length)
                throw new VBCompileErrorException("Expected variable or procedure, not enum type");
            var innerName = MemberName(members[idx + 1]);
            if (!enumTable.TryGetValue(innerName, out var innerValue))
                throw new VBMethodOrDataMemberNotFoundException(innerName, Vb6Value.ValueType.EmptyVariant);
            var innerResult = new Vb6Value(innerValue);
            return idx + 2 < members.Length
                ? await ResolveMemberChain(innerResult, members, idx + 2, ctx)
                : innerResult;
        }

        if (await interpreter.ReadVariable(module.ModuleEnv, memberName) is (true, var v))
        {
            if (memberHint is not null && module.ModuleEnv.TryGetVariableLocation(memberName, out var memberLocation))
                RefuseMismatchedVariableCharacter(memberLocation, v, memberHint);
            // #965: the argument list indexes the module's array, as on an unqualified read. Private foreign
            // reads and non-array calls are unmeasured here and retain their existing behaviour.
            if (argsCtx != null && v.Value is VBArray
                && module.PrePass.PublicVariables.Contains(memberName))
                v = IndexResult(v, await EvaluateCallArgs(argsCtx), ctx);
            return await ThenTheRest(v);
        }

        throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
    }

    /// <summary>
    /// An array element as it reads (#987, measured): a Variant's when the array holds Variants (`Dim v(1)`, Array()) or
    /// was reached through a Variant (`va(0)`, with `va = a` and `a` an Integer array), and typed when a declared
    /// scalar-element array is read through its own name. The element's stored flag does not decide it: a Variant
    /// element holding the literal 5 is a Variant (`w(0) * 7000` is 35000), and so is anything else a Variant array holds.
    /// An element of an object or UDT array is left as it is.
    /// </summary>
    private static Vb6Value ElementAsRead(Vb6Value holder, VBArray array, Vb6Value element) =>
        holder.HeldAsVariant || array.ElementType is not { } type || type == Vb6Value.ValueType.EmptyVariant ? element.AsVariant()
        : VbNumeric.IsDeclarableScalar(type) ? element.AsFixedType()
        : element;

    // Read a UDT field chain (`e.City`, `e.Address.City`) — navigate owned bags, returning the final field's
    // value (a nested-UDT field returns the live reference; the immediate consumer, a Let/ByVal, deep-copies it).
    private async Task<Vb6Value> GetUdtField(Vb6Value root, VB6Parser.ICS_S_MemberCallContext[] members, Antlr4.Runtime.ParserRuleContext ctx)
    {
        var current = root;
        foreach (var m in members)
        {
            if (current.Value is not VbUdt bag)
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch, "Member access on a non-UDT value");
            var fieldName = MemberName(m);
            current = await interpreter.Loading((Bag: bag, Name: fieldName, Context: ctx),
                static s => s.Bag.TryGet(s.Name, out var value) ? value
                    : throw new VBRunTimeException(s.Context, VBStandardError.MethodOrDataMemberNotFound, s.Name));
            if (MemberArgs(m) is { } args)
                current = IndexResult(current, await EvaluateCallArgs(args), ctx);
        }
        return current;
    }

    // The identifier of a member-call segment (`.Foo` or `.Foo(args)`). A ProcedureOrArrayCall head can be a
    // baseType token (e.g. `.Currency`) rather than an ambiguousIdentifier, so the inner accessor needs its own
    // null-guard — otherwise it NREs instead of falling through to the clean "malformed" error.
    internal static string MemberName(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier().GetText()
           ?? m.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
           ?? throw new VBCompileErrorException("Malformed or unsupported qualified member call");

    // The first argsCall of a `.Foo(args)` member segment, or null for a bare `.Foo`. A later one indexes the result, which
    // ResolveMemberIndexed applies (#1476): it used to be dropped, with the first, so `k.Echo("m n")(1)` called Echo with none.
    private static VB6Parser.ArgsCallContext? MemberArgs(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_ProcedureOrArrayCall() is { } p && ParenGroups(p) is [var first, ..] ? first : null;

    // A member segment with index groups after its first, `k.Items()(1)` (#1476): the later groups first, the last of them
    // first, then the member with its first group, then its result indexed with each, as for a bare name.
    private async Task<Vb6Value> ResolveMemberIndexed(Vb6Value current, VB6Parser.ICS_S_MemberCallContext member,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        if (current.Value is VBImageList && member.iCS_S_VariableOrProcedureCall()?.dictionaryCallStmt() is { } imageBang)
            return await Bang(await ResolveOneMember(current, member, ctx), imageBang);
        if (member.iCS_S_ProcedureOrArrayCall() is not { } call || ParenGroups(call) is not { Count: > 1 } groups)
            return await ResolveOneMember(current, member, ctx);
        var later = new LaterGroups(this, groups, ctx);
        var whole = await ResolveOneMember(current, member, ctx, later.Evaluate);
        return IndexAll(whole, await later.Values(), ctx);
    }

    public override async Task<object?> VisitVsAmp(VB6Parser.VsAmpContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return Concatenate(leftValue, rightValue);
    }

    /// <summary><c>&amp;</c> on two values, as <see cref="VisitVsAmp"/> and an <c>#If</c>'s expression apply it.</summary>
    internal static Vb6Value Concatenate(Vb6Value leftValue, Vb6Value rightValue)
    {
        if (leftValue.IsNull && rightValue.IsNull)
            return Vb6Value.Null;
        // Null and Empty both concatenate as "" (Empty.Value is null, so the old code NPE'd on it).
        if (leftValue.IsNull)
            return new Vb6Value(AmpString(rightValue));
        if (rightValue.IsNull)
            return new Vb6Value(AmpString(leftValue));
        // Typed when both sides are, as `"1" & "0"` is, and a Variant's when either is, as `Chr(49) & "0"` is (measured,
        // #987: a Select Case on each against `Case Is > 9`).
        var joined = new Vb6Value(AmpString(leftValue) + AmpString(rightValue));
        return leftValue.HasFixedType && rightValue.HasFixedType ? joined.AsFixedType() : Typed(leftValue, rightValue, joined);
    }

    private static string AmpString(Vb6Value v) => VbDisplay.ToText(v);

    // An operand of Like as text: an array is 13 and Nothing 91, as a conversion's are (measured).
    private static string LikeOperand(Vb6Value v) => VbDisplay.ToText(VB6BuiltIns.RefuseNonScalar(v));

    /// <summary>An operator's result, a Variant's when either operand is (measured, #987: `v + 1` with `v` holding 4, and
    /// `i + v` with `i` an Integer, are Variant selectors), and otherwise as the operator produced it, which is typed when
    /// both operands are (#122).</summary>
    private static Vb6Value Typed(Vb6Value left, Vb6Value right, Vb6Value result) =>
        left.HeldAsVariant || right.HeldAsVariant ? result.AsVariant() : result;

    // `+` and `-`, one level of the grammar, so taken left to right (#1074).
    public override async Task<object?> VisitVsAdd(VB6Parser.VsAddContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return AddOrSubtract(leftValue, rightValue, context.MINUS() != null, context);
    }

    /// <summary><c>+</c> or <c>-</c> on two values, as <see cref="VisitVsAdd"/> and an <c>#If</c>'s expression apply them.</summary>
    internal static Vb6Value AddOrSubtract(Vb6Value leftValue, Vb6Value rightValue, bool subtract, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (subtract)
            return Typed(leftValue, rightValue, VbNumeric.Sub(leftValue, rightValue, context));
        if (leftValue.Type == Vb6Value.ValueType.String && rightValue.Type == Vb6Value.ValueType.String)
            return new Vb6Value((string)leftValue.Value! + (string)rightValue.Value!);   // VB6: String + String concatenates
        // A String beside Empty is the String, both ways round: `"6" + Empty` and `Empty + "6"` are the String "6"
        // (measured, #1047). Any other operand beside a String reads the String as a number.
        if (leftValue.Type == Vb6Value.ValueType.String && rightValue.Type == Vb6Value.ValueType.EmptyVariant)
            return new Vb6Value((string)leftValue.Value!);
        if (leftValue.Type == Vb6Value.ValueType.EmptyVariant && rightValue.Type == Vb6Value.ValueType.String)
            return new Vb6Value((string)rightValue.Value!);
        return Typed(leftValue, rightValue, VbNumeric.Add(leftValue, rightValue, context));
    }

    // `*` and `/`, one level of the grammar (#1074).
    public override async Task<object?> VisitVsMult(VB6Parser.VsMultContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return MultiplyOrDivide(leftValue, rightValue, context.DIV() != null, context);
    }

    /// <summary><c>*</c> or <c>/</c> on two values, as <see cref="VisitVsMult"/> and an <c>#If</c>'s expression apply them.</summary>
    internal static Vb6Value MultiplyOrDivide(Vb6Value leftValue, Vb6Value rightValue, bool divide, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, divide
            ? VbNumeric.RealDivide(leftValue, rightValue, context)
            : VbNumeric.Mul(leftValue, rightValue, context));
    }

    public override async Task<object?> VisitVsMod(VB6Parser.VsModContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return Modulo(leftValue, rightValue, context);
    }

    internal static Vb6Value Modulo(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.Modulo(leftValue, rightValue, context));
    }

    public async override Task<object?> VisitVsDiv(VB6Parser.VsDivContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return IntegerDivide(leftValue, rightValue, context);
    }

    internal static Vb6Value IntegerDivide(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.IntDivide(leftValue, rightValue, context));
    }

    public override async Task<object?> VisitVsPow(VB6Parser.VsPowContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return Power(leftValue, rightValue, context);
    }

    internal static Vb6Value Power(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.Power(leftValue, rightValue, context));
    }

    // Every comparison is one level of the grammar, Like and Is included, so `1 < 2 = True` is (1 < 2) = True and
    // `Nothing Is Nothing = True` is True (measured, #1074).
    public override async Task<object?> VisitVsCompare(VB6Parser.VsCompareContext context)
    {
        if (context.LIKE() != null)
        {
            // Null on either side is Null; anything else is taken as the text & would make of it (#1547).
            var (text, pattern) = await GetTwoValues(context.valueStmt());
            if (text.IsNull || pattern.IsNull)
                return Vb6Value.Null;
            return new Vb6Value(VbLike.Matches(LikeOperand(text), LikeOperand(pattern),
                currentModule?.PrePass.OptionCompareText == true));
        }
        if (context.IS() != null)
        {
            // Reference identity on the underlying object (Nothing = null). One line covers a Is b, x Is Nothing,
            // and Nothing Is Nothing — NOT Vb6Value equality (which would run numeric coercion).
            var leftObject = await EvaluateValue(context.valueStmt(0));
            var rightObject = await EvaluateValue(context.valueStmt(1));
            if (leftObject.Type == Vb6Value.ValueType.Error || rightObject.Type == Vb6Value.ValueType.Error)
                throw new VBRunTimeException(context, VBStandardError.ObjectRequired);
            return new Vb6Value(ReferenceEquals(leftObject.Value, rightObject.Value));
        }
        // Option Compare is the module's where the comparison is written: a Binary module's function called from a Text
        // module compares as Binary (measured, #1567).
        var compareText = currentModule?.PrePass.OptionCompareText == true;
        if (context.EQ() != null || context.NEQ() != null)
        {
            var (left, right) = await GetTwoValuesSameTypesOrNull(context.valueStmt());
            if (left.Type == Vb6Value.ValueType.Null || right.Type == Vb6Value.ValueType.Null)
                return Vb6Value.Null;
            var equal = StringCompare(left, right, compareText) is int same ? same == 0 : left.Equals(right);
            return new Vb6Value(equal == (context.EQ() != null));
        }
        var order = context.LT() != null ? Order.Less
            : context.GT() != null ? Order.Greater
            : context.LEQ() != null ? Order.LessOrEqual
            : Order.GreaterOrEqual;
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        // Two strings compare ordinally under Option Compare Binary (oracle-verified: "B" < "a", "10" < "9"), and as text
        // under Option Compare Text. A string-vs-number pair is left to the numeric path below (numeric string coerces; a
        // non-numeric string vs a number is Type Mismatch, both already faithful, and the same in either mode).
        if (StringCompare(leftValue, rightValue, compareText) is int sc)
            return (Vb6Value)Holds(order, sc, 0);
        if (leftValue.Type == Vb6Value.ValueType.Error && rightValue.Type == Vb6Value.ValueType.Error)
            return (Vb6Value)Holds(order, (ushort)leftValue.Value!, (ushort)rightValue.Value!);
        if (TryUnpack(leftValue, rightValue, out int leftInt, out int rightInt))
            return (Vb6Value)Holds(order, leftInt, rightInt);
        if (TryUnpack(leftValue, rightValue, out float leftFloat, out float rightFloat))
            return (Vb6Value)Holds(order, leftFloat, rightFloat);
        if (TryUnpack(leftValue, rightValue, out double leftDouble, out double rightDouble))
            return (Vb6Value)Holds(order, leftDouble, rightDouble);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    private enum Order { Less, Greater, LessOrEqual, GreaterOrEqual }

    // The operator itself, on the operands' own type, as each comparison had it: a NaN compares false every way.
    private static bool Holds<T>(Order order, T left, T right) where T : System.Numerics.IComparisonOperators<T, T, bool> =>
        order switch
        {
            Order.Less => left < right,
            Order.Greater => left > right,
            Order.LessOrEqual => left <= right,
            _ => left >= right,
        };

    // The comparison operators' order when BOTH operands are String: ordinal under Option Compare Binary (verified against
    // vb6.exe), and vb6.exe's text compare under Option Compare Text (VbTextCompare). Returns null when the pair is not two
    // strings, so the caller falls through to the numeric path (a numeric string vs a number coerces; a non-numeric string
    // vs a number is a Type Mismatch, both already faithful).
    private static int? StringCompare(Vb6Value left, Vb6Value right, bool text)
        => left.Type == Vb6Value.ValueType.String && right.Type == Vb6Value.ValueType.String
            ? left.TryCompareTo(right, text)
            : null;

    public override async Task<object?> VisitVsAnd(VB6Parser.VsAndContext context)
    {
        // The RAW pair, not the coerced one. GetTwoValuesSameTypes promotes both operands to a common
        // numeric type and throws on any mix it cannot unify — which loses the per-operand width the
        // result ladder is defined over (Byte with Integer is Integer; Byte with Byte stays Byte) and
        // rejects the ordinary `True And 2` and `"12" And 10` outright. Bitwise operators do not coerce
        // to a common type; they reduce each operand to bits independently. (#166)
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return And(leftValue, rightValue, context);
    }

    internal static Vb6Value And(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (TryBitwise(leftValue, rightValue, static (a, b) => a & b, out var and)) return and;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsOr(VB6Parser.VsOrContext context)
    {
        // The RAW pair, not the coerced one. GetTwoValuesSameTypes promotes both operands to a common
        // numeric type and throws on any mix it cannot unify — which loses the per-operand width the
        // result ladder is defined over (Byte with Integer is Integer; Byte with Byte stays Byte) and
        // rejects the ordinary `True And 2` and `"12" And 10` outright. Bitwise operators do not coerce
        // to a common type; they reduce each operand to bits independently. (#166)
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return Or(leftValue, rightValue, context);
    }

    internal static Vb6Value Or(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (TryBitwise(leftValue, rightValue, static (a, b) => a | b, out var or)) return or;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsXor(VB6Parser.VsXorContext context)
    {
        // The RAW pair, not the coerced one. GetTwoValuesSameTypes promotes both operands to a common
        // numeric type and throws on any mix it cannot unify — which loses the per-operand width the
        // result ladder is defined over (Byte with Integer is Integer; Byte with Byte stays Byte) and
        // rejects the ordinary `True And 2` and `"12" And 10` outright. Bitwise operators do not coerce
        // to a common type; they reduce each operand to bits independently. (#166)
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        return Xor(leftValue, rightValue, context);
    }

    internal static Vb6Value Xor(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (TryBitwise(leftValue, rightValue, static (a, b) => a ^ b, out var xor)) return xor;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsNegation(VB6Parser.VsNegationContext context)
    {
        return Negation(await EvaluateScalar(context.valueStmt()), context);
    }

    internal static Vb6Value Negation(Vb6Value value, Antlr4.Runtime.ParserRuleContext context)
    {
        if (value.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        var negated = VbNumeric.Negate(value, context);
        return value.HeldAsVariant ? negated.AsVariant() : negated;
    }

    public override async Task<object?> VisitVsNot(VB6Parser.VsNotContext context)
    {
        return Not(await EvaluateScalar(context.valueStmt()), context);
    }

    internal static Vb6Value Not(Vb6Value value, Antlr4.Runtime.ParserRuleContext context)
    {
        if (value.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        // Not keeps its operand's OWN width rather than promoting: Not CByte(5) is 250, an eight-bit
        // complement, not -6. (#166)
        if (VbBitwise.TryUnpack(value, out var bits, out var width))
            return VbBitwise.Not(bits, width);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsEqv(VB6Parser.VsEqvContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());   // raw — see VisitVsAnd
        return Eqv(leftValue, rightValue, context);
    }

    internal static Vb6Value Eqv(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (TryBitwise(leftValue, rightValue, static (a, b) => ~(a ^ b), out var eqv)) return eqv;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsImp(VB6Parser.VsImpContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());   // raw — see VisitVsAnd
        return Imp(leftValue, rightValue, context);
    }

    internal static Vb6Value Imp(Vb6Value leftValue, Vb6Value rightValue, Antlr4.Runtime.ParserRuleContext context)
    {
        if (leftValue.Type == Vb6Value.ValueType.Null &&
            rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (leftValue.Type == Vb6Value.ValueType.Null && rightValue.Value is bool rbool)
            return rbool ? (Vb6Value)true : Vb6Value.Null;
        if (rightValue.Type == Vb6Value.ValueType.Null && leftValue.Value is bool lbool)
            return lbool ? Vb6Value.Null : (Vb6Value)true;
        if (TryBitwise(leftValue, rightValue, static (a, b) => ~a | b, out var imp)) return imp;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override Task<object?> VisitVsAddressOf(VB6Parser.VsAddressOfContext context) => throw new NotImplementedException("ADDRESSOF is not implemented");

    public override async Task<object?> VisitVsStruct(VB6Parser.VsStructContext context)
    {
        if (context.valueStmt().Length != 1)
            throw new NotImplementedException("Only single element supported");

        return await Visit(context.valueStmt(0));
    }

    public override async Task<object?> VisitVsNew(VB6Parser.VsNewContext context)
    {
        // `New ClassName` — the class name is a nested bare identifier; extract it SYNTACTICALLY (evaluating it
        // would try to resolve a variable and throw). `Dim o As New C1` makes its object where it is read (#435).
        var call = (context.valueStmt() as VB6Parser.VsICSContext)?.implicitCallStmt_InStmt();
        if (call is not null && interpreter.LibraryClassFor(call.GetText()) is { } libraryClass)
            return interpreter.AdoptNewTemp(interpreter.NewLibraryObject(libraryClass), stmtFrames);
        // `New Collection`, and `New VBA.Collection`, the library's own (#1471).
        if (call?.iCS_S_MembersCall() is { } qualified && IsCollectionType(qualified.GetText()))
            return interpreter.AdoptNewTemp(new Vb6Value(interpreter.NewCollection()), stmtFrames);
        var className = call?.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
            ?? throw new VBCompileErrorException("Expected a class name after New");
        if (IsCollectionType(className) && !interpreter.Modules.TryGet(className, out _))
            return interpreter.AdoptNewTemp(new Vb6Value(interpreter.NewCollection()), stmtFrames);
        // VB6 compiles New ErrObject but refuses its creation at run time (#2627).
        if (className.Equals("ErrObject", StringComparison.OrdinalIgnoreCase) && !interpreter.Modules.TryGet(className, out _))
            throw new VBRunTimeException(context, new VBStandardError(-2147221164, "Class not registered"));
        var created = Vb6Value.NewObject(await interpreter.NewObject(className));
        // Hold the transient in the current statement frame so a New that is never stored (consumed by a built-in
        // arg, a condition, a member read) still terminates at statement-end; a subsequent Set / ByVal-bind
        // AddRefs again so the destination out-lives the flush by exactly one net reference.
        return interpreter.AdoptNewTemp(created, stmtFrames);
    }

    /// <summary>Whether a type name is VB6's Collection: <c>Collection</c> or <c>VBA.Collection</c>.</summary>
    internal static bool IsCollectionType(string name) =>
        name.Equals("Collection", StringComparison.OrdinalIgnoreCase) || name.Equals("VBA.Collection", StringComparison.OrdinalIgnoreCase);

    public override async Task<object?> VisitVsTypeOf(VB6Parser.VsTypeOfContext context)
    {
        var operandStmt = context.typeOfStmt().valueStmt();
        var targetType = context.typeOfStmt().type();

        // Grammar greediness: `TypeOf p Is Clock` parses the operand as the comparison `p Is Clock` with NO
        // Is-type clause. Recover by splitting the comparison into the real operand + the type name.
        Vb6Value operand;
        string targetName;
        if (targetType == null && operandStmt is VB6Parser.VsCompareContext vsIs && vsIs.IS() != null)
        {
            operand = await EvaluateValue(vsIs.valueStmt(0));
            targetName = vsIs.valueStmt(1).GetText();
        }
        else
        {
            operand = await EvaluateValue(operandStmt);
            targetName = targetType?.complexType()?.GetText() ?? targetType?.baseType()?.GetText()
                ?? throw new VBCompileErrorException("Expected a type after TypeOf … Is");
        }

        // A live instance matches its own class (or the catch-all `Object`); Nothing / non-object → False.
        // (VB6 raises an error for TypeOf on a non-object — a documented simplification here.)
        // A Collection is a Collection, a VBA.Collection and an Object (measured, #1471).
        if (operand.Value is VbCollection)
            return new Vb6Value(IsCollectionType(targetName) || string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase));
        // A control is its class, with its library or without, a Control and an Object: a TextBox is TextBox, VB.TextBox,
        // Control and Object, and a CommonDialog MSComDlg.CommonDialog (measured, #1060). A control array and a form's
        // Controls are an Object, and Controls is not a Collection.
        if (operand.Value is Avalonia.Controls.Control control)
        {
            if (interpreter.ControlClassFor(targetName) is { } controlClass)
                return new Vb6Value(control is VBProgressBar progress
                    && controlClass.Equals(progress.Library.TypeName("ProgressBar"), StringComparison.OrdinalIgnoreCase));
            var className = VB6BuiltIns.ControlTypeName(control);
            // An in-box control's library is the one it was read under: MSComctlLib's ProgressBar or ComctlLib's (#1082).
            var library = control switch
            {
                VBCommonDialog => "MSComDlg",
                VBProgressBar bar => bar.Library.Name,
                VBToolbar => "MSComctlLib",
                VBStatusBar => "MSComctlLib",
                VBImageList => "MSComctlLib",
                VBUpDown => "MSComCtl2",
                _ => "VB",
            };
            return new Vb6Value(string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, "Control", StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, className, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, $"{library}.{className}", StringComparison.OrdinalIgnoreCase));
        }
        if (operand.Value is ControlArrayGroup or FormControls or VbForms)
            return new Vb6Value(string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase));
        if (operand.Value is VbObject vo)
            return new Vb6Value(string.Equals(vo.ClassName, targetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase)
                // A form is a Form too, and not a Control (measured, #1060).
                || vo.ClassDef.Kind == InterpreterModuleKind.Form && string.Equals(targetName, "Form", StringComparison.OrdinalIgnoreCase)
                // …or an interface the class implements. A class that does NOT implement it is False, not an
                // error — measured, and the reason this stays a plain widening of the same expression.
                || VbInterface.Implements(vo.ClassDef, targetName));
        return new Vb6Value(false);
    }

    // `name:=value` is a named argument, which a call to a procedure or an intrinsic places (#1465). Reaching here, it was
    // given to something that does not place them yet: a control's or a form's method, or an object's (#1474).
    public override Task<object?> VisitVsAssign(VB6Parser.VsAssignContext context) =>
        throw new NotImplementedException("A named argument (" + context.implicitCallStmt_InStmt().GetText()
            + ":=) is not implemented here yet (#1474)");

    public override async Task<object?> VisitVsMid(VB6Parser.VsMidContext context)
    {
        var args = await EvaluateCallArgs(context.midStmt().argsCall());
        return await interpreter.BuiltIns.EvaluateBuiltInFunction("Mid", args);
    }

    /// <summary>
    /// Apply a bitwise operator under VB6's rules: each operand reduced to 32 bits on its own terms,
    /// the result reported at the width the ladder gives. False when either operand is not something
    /// VB6 accepts, which the caller turns into a type mismatch.
    ///
    /// This replaced a pair of rungs that unpacked to <c>int</c> or to <c>bool</c> and rejected
    /// everything else — so a Long or floating operand, which is to say any <c>&amp;H…&amp;</c> mask and
    /// therefore most code that touches the Windows API, was a spurious type mismatch. The ladder is
    /// measured against vb6.exe, not assumed; see <see cref="VbBitwise"/>. (#166)
    /// </summary>
    private static bool TryBitwise(Vb6Value left, Vb6Value right, Func<long, long, long> op, out Vb6Value result)
    {
        result = default;
        if (!VbBitwise.TryUnpack(left, out var leftBits, out var leftWidth)) return false;
        if (!VbBitwise.TryUnpack(right, out var rightBits, out var rightWidth)) return false;
        result = VbBitwise.Pack(op(leftBits, rightBits), VbBitwise.Combine(leftWidth, rightWidth));
        return true;
    }


    public override async Task<object?> VisitBlockStmt(VB6Parser.BlockStmtContext context)
    {
        return await base.VisitBlockStmt(context);
    }
}
