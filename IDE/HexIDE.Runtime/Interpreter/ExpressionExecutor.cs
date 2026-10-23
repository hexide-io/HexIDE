using System;
using System.Collections.Generic;
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
        return await interpreter.CallBuiltIn(name, args) is { } result ? IntrinsicAsRead(name, args, result) : (Vb6Value?)null;
    }

    // Which intrinsics VB6 types and which return a Variant, measured one by one (#987, native and p-code: a Select Case
    // on each result against `Case Is < N.4`, or `Case Is > 9` for text, and its product past an Integer's or a Long's
    // ceiling). A name not listed was not run and reads as it always has. CSng was measured with #234: two CSng results
    // whose sum is past a Single's ceiling raise Err 6, where two Variant Singles widen to Double.
    // AscW and ChrW, and Str (" 19" is read as 19 against `Case Is > 10`), were measured with #1134.
    private static readonly HashSet<string> TypedIntrinsics = new(StringComparer.OrdinalIgnoreCase)
        { "Sgn", "Asc", "AscW", "CInt", "CLng", "CByte", "CSng", "CStr", "UBound", "LBound", "Replace", "StrReverse" };

    private static readonly HashSet<string> VariantIntrinsics = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hour", "Minute", "Second", "Day", "Month", "Year", "Weekday", "IIf", "Choose", "Switch",
        "Mid", "Left", "Right", "Trim", "LTrim", "LCase", "UCase", "Hex", "Oct", "String", "Format", "Chr", "ChrW",
        "Str",
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
            return Vb6Value.NewCurrency(AsDecimal(v));
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

    public async Task<List<Vb6Value>> EvaluateCallArgs(VB6Parser.ArgsCallContext? context)
    {
        List<Vb6Value> callArgs = new();
        foreach (var arg in ArgSlots(context))
        {
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

        return callArgs;
    }

    // Like EvaluateCallArgs, but also captures each argument's caller slot when it is a bare lvalue, so a
    // ByRef parameter can alias it. Used for user-procedure calls (builtins take values only).
    public async Task<List<CallArg>> ResolveCallArgs(VB6Parser.ArgsCallContext? context)
    {
        var result = new List<CallArg>();
        var named = false;
        foreach (var arg in ArgSlots(context))
        {
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
        return result;
    }

    // One argument, with what a ByRef parameter needs to alias it. A call-site ByVal keyword (or a non-lvalue like a literal
    // or `(x)`) forces a copy.
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
        var values = await EvaluateCallArgs(context);
        var slots = ArgSlots(context);
        var result = new VB6BuiltIns.IntrinsicArgumentValues(values.Count);
        for (var i = 0; i < values.Count; i++)
            result.AddArgument(values[i], slots[i] is null);
        return result;
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

    private int? TryGetArgLocation(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is VB6Parser.VsICSContext ics
            && ics.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall() is { } vp
            && vp.typeHint() == null && vp.dictionaryCallStmt() == null
            && env.TryGetVariableLocation(vp.ambiguousIdentifier().GetText(), out var loc)
            && !interpreter.ExecutionContext.State.IsConstant(loc)
            && !interpreter.ExecutionContext.State.IsReadOnly(loc))
            return loc;
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
            || call.typeHint() != null || call.dictionaryCallStmt() != null
            || call.argsCall().Length != 1
            || await interpreter.ReadVariable(env, id.GetText()) is not (true, var holder)
            || !holder.Type.IsArray || holder.Value is not VBArray array)
            return null;
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
    public override async Task<object?> VisitVsLiteral(VB6Parser.VsLiteralContext literalContext)
    {
        var result = await VisitVsLiteralCore(literalContext);
        return result is Vb6Value v ? v.AsFixedType() : result;
    }

    private async Task<object?> VisitVsLiteralCore(VB6Parser.VsLiteralContext literalContext)
    {
        if (literalContext.literal().STRINGLITERAL() is { } stringliteral)
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
        if (literalContext.literal().INTEGERLITERAL() is { } integerliteral)
            return ClassifyIntegerLiteral(integerliteral.GetText());
        if (literalContext.literal().OCTALLITERAL() is { } octalliteral)
            return ClassifyRadixLiteral(octalliteral.GetText(), 8);
        if (literalContext.literal().DOUBLELITERAL() is { } doubleliteral)
        {
            var text = doubleliteral.GetText();
            char suffix = text[^1];
            if (suffix is '#' or '!' or '&' or '@' or '%')
            {
                var body = text[..^1];
                return suffix switch
                {
                    '!' => new Vb6Value(float.Parse(body)),
                    '&' => new Vb6Value((long)Math.Round(double.Parse(body), MidpointRounding.ToEven)),   // & forces Long
                    '@' => Vb6Value.NewCurrency(decimal.Parse(body)),                                     // @ forces Currency
                    '%' => new Vb6Value((int)Math.Round(double.Parse(body), MidpointRounding.ToEven)),    // % forces Integer
                    _ => new Vb6Value(double.Parse(body)),                                                // #
                };
            }
            // VB6: an unsuffixed floating-point literal (with a '.' or exponent) defaults to Double.
            return new Vb6Value(double.Parse(text));
        }
        if (literalContext.literal().TRUE() is { })
        {
            Vb6Value val = new Vb6Value(true);
            return val;
        }
        if (literalContext.literal().FALSE() is { })
        {
            Vb6Value val = new Vb6Value(false);
            return val;
        }
        if (literalContext.literal().NULL() is { })
        {
            return Vb6Value.Null;
        }
        if (literalContext.literal().NOTHING() is { })
        {
            return Vb6Value.Nothing;   // a null object reference
        }
        if (literalContext.literal().EMPTY_() is { })
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
        if (literalContext.literal().DATELITERAL() is { } dateliteral)
        {
            // VB6 date literals are culture-independent, US month/day/year — parse invariantly. A time-only literal is
            // that time on the zero date, not today (#771).
            var inner = dateliteral.GetText().Trim('#');
            return new Vb6Value(VB6BuiltIns.OnZeroDateIfTimeOnly(DateTime.Parse(inner,
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.NoCurrentDateDefault)));
        }
        if (literalContext.literal().HEXLITERAL() is { } hexliteral)
            return ClassifyRadixLiteral(hexliteral.GetText(), 16);   // VB6 &H is numeric, not a colour
        else
        {
            throw new NotImplementedException($"{literalContext.literal().GetChild(0)} literal is not supported");
        }
    }

    // VB6 whole-number literal typing: a type-char suffix wins (& Long, @ Currency, ! Single, # Double);
    // an exponent makes it Double; otherwise Integer if it fits Int16, else Long if it fits Int32, else Double.
    internal static Vb6Value ClassifyIntegerLiteral(string text)
    {
        char suffix = text[^1];
        if (suffix is '&' or '@' or '!' or '#' or '%')
        {
            var body = text[..^1];
            return suffix switch
            {
                '&' => new Vb6Value(long.TryParse(body, out var lp) ? lp : (long)double.Parse(body)),
                '@' => Vb6Value.NewCurrency(decimal.Parse(body)),
                '!' => new Vb6Value(float.Parse(body)),
                '%' => new Vb6Value(int.Parse(body)),   // Integer (magnitude ctor keeps it Integer in Int16 range)
                _ => new Vb6Value(double.Parse(body)),   // '#'
            };
        }
        if (text.IndexOf('e') >= 0 || text.IndexOf('E') >= 0)
            return new Vb6Value(double.Parse(text));   // exponent without a dot -> Double (e.g. 1e5)
        if (long.TryParse(text, out var lv))
            return lv >= int.MinValue && lv <= int.MaxValue ? new Vb6Value((int)lv) : new Vb6Value((double)lv);
        return new Vb6Value(double.Parse(text));
    }

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
            if (!bangFound)
                throw new NotImplementedException($"`{context.GetText()}` is supported only on a Collection");
            return Bang(bangTarget, bang);
        }
        var identifier = await ExtractIdentifier(context);
        RefuseAmbiguousEnumMember(identifier);
        // Through Loading: a form's name or control may need the form made or loaded first (#822).
        if (!interpreter.TryReadNow(env, identifier, out var found, out var var, out var foundNeed))
            (found, var) = await interpreter.ReadVariableAfter(env, identifier, foundNeed!);
        if (found)
            return var;
        if (identifier.Equals("Clipboard", StringComparison.OrdinalIgnoreCase))
        {
            // A function named Clipboard can supply the member-chain's object too, not just a bare value.
            if (interpreter.TryResolveProcedure(identifier, currentModule, out _, out var proc) && proc.IsFunction
                && await interpreter.CallProcedure(identifier, [], currentModule, callerEnv: env, callerFrames: stmtFrames) is { } result)
                return result;
            return new Vb6Value(interpreter.Clipboard);
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
            // `Command$` with no parentheses, the library's function with no arguments and its result a String (measured).
            // Any other name with a type character is still refused (#742).
            if (varOrProcCall.typeHint() is { } hint)
            {
                var hinted = varOrProcCall.ambiguousIdentifier().GetText();
                if (hint.GetText() == "$" && BareDollarForms.Contains(hinted)
                    && !env.TryGetVariableLocation(hinted, out _)
                    && !interpreter.TryResolveProcedure(hinted, currentModule, out _, out _)
                    && await EvaluateFunction(hinted, []) is { } bareResult)
                    return DollarResult(bareResult, varOrProcCall);
                throw new NotImplementedException("Type hint is not supported");
            }
            if (varOrProcCall.dictionaryCallStmt() != null)
                return await VisitICS_S_VariableOrProcedureCall(varOrProcCall);
            var identifier = varOrProcCall.ambiguousIdentifier().GetText();
            RefuseAmbiguousEnumMember(identifier);

            if (!interpreter.TryReadNow(env, identifier, out var found, out var variable, out var foundNeed))
                (found, variable) = await interpreter.ReadVariableAfter(env, identifier, foundNeed!);
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
                // ...or a zero-argument intrinsic (Now, Date, Time, Timer, Rnd, ...). Consulted last, so a
                // variable/constant/user Function of the same name still wins above.
                if (await EvaluateFunction(identifier, []) is { } builtInFn)
                    return builtInFn;
                if (identifier.Equals("Clipboard", StringComparison.OrdinalIgnoreCase))
                    return new Vb6Value(interpreter.Clipboard);
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
                return Bang(head, trailingBang);
            }

            // A namespace qualifier (`Module1.Foo`, `VBA.Abs`, `VBA.Math.Abs`) — resolved before treating the
            // leading part as a value, but a same-named variable shadows it (VB6 precedence).
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart
                // Only whether there is one, as a statement's qualifier asks: it must not make a form's instance (#822).
                && !interpreter.ExecutionContext.TryInspectVariable(env, leadPart.GetText(), out _)
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
            return Bang(withTargets.Count > 0
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
            && !interpreter.ExecutionContext.TryInspectVariable(env, leadPart.GetText(), out _)
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
    /// <c>c!key</c> as <c>c("key")</c> and <c>c![key]</c> the same (measured, #1471). No other object's default member
    /// takes it yet, so anything else is refused.
    /// </summary>
    private static Vb6Value Bang(Vb6Value target, VB6Parser.DictionaryCallStmtContext bang)
    {
        var key = BangKey(bang);
        return target.Value switch
        {
            VbCollection items => items.Default([new Vb6Value(key)]),
            null when target.Type == Vb6Value.ValueType.Object
                => throw new VBRunTimeException(bang, VBStandardError.ObjectVariableOrWithBlockVariableNotSet),
            _ => throw new NotImplementedException($"`!{key}` is supported only on a Collection"),
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

    /// <summary>The library's functions that have a <c>$</c> form, every one measured to compile and answer a String.</summary>
    private static readonly HashSet<string> DollarForms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chr", "ChrB", "ChrW", "Command", "CurDir", "Environ", "Error", "Format", "Hex", "LCase", "Left", "LeftB", "LTrim", "Mid",
        "MidB", "Oct", "Right", "RightB", "RTrim", "Space", "Str", "String", "Trim", "UCase",
    };

    /// <summary>The <c>$</c> forms read with no parentheses, as a variable is: measured for each.</summary>
    private static readonly HashSet<string> BareDollarForms = new(StringComparer.OrdinalIgnoreCase) { "Command" };

    /// <summary>Library functions on which VB6 refuses a <c>$</c> at compile time (measured).</summary>
    private static readonly HashSet<string> DollarRefused = new(StringComparer.OrdinalIgnoreCase)
    {
        "Array", "Asc", "AscW", "Choose", "IIf", "InStrRev", "Val",
    };

    /// <summary>Library functions on which VB6 ignores a <c>$</c>, the result keeping its usual type (measured).</summary>
    private static readonly HashSet<string> DollarIgnored = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "CLng", "Fix", "InStr", "InStrB", "Int", "Len", "LenB", "Sgn",
    };

    /// <summary>Resolve a parenthesised call on an identifier — a control-array element (<c>Command1(i)</c>), a
    /// local array element (<c>arr(i)</c>), a user Function, or an intrinsic — as a VALUE. Shared by expression
    /// position and the leading part of a member chain (<c>Command1(i).Caption</c>).</summary>
    public async Task<object?> EvaluateProcedureOrArrayCall(VB6Parser.ICS_S_ProcedureOrArrayCallContext procOrArrayCall)
    {
        if (procOrArrayCall.dictionaryCallStmt() != null)
            throw new NotImplementedException($"dictionaryCall not supported");

        if (procOrArrayCall.ambiguousIdentifier() == null)
            throw new NotImplementedException($"only proc call supportedhere");

        // A type-declaration character on the call (#742). `$` is the one a program reaches first, the library's string
        // functions written `Trim$` and `Left$` (#1071). The name is looked up without it by the ordinary search, and the
        // `$` is then checked against what was found (measured, docs/vb6-fidelity-oracle.md). The other characters still
        // stop the run.
        var dollar = false;
        if (procOrArrayCall.typeHint() is { } hint)
        {
            if (hint.GetText() != "$")
                throw new NotImplementedException($"the type-declaration character {hint.GetText()} on a call is not implemented (#742)");
            dollar = true;
        }

        if (procOrArrayCall.argsCall().Length > 1)
            throw new NotImplementedException("only a single argsCall is supported");
        var argsCtx = procOrArrayCall.argsCall().Length == 1 ? procOrArrayCall.argsCall(0) : null;

        var name = procOrArrayCall.ambiguousIdentifier().GetText();

        // A control array indexed by element (`Command1(i)`) — the shared name resolves to a ControlArrayGroup
        // value; return the element control. A missing element is Err 340 (oracle-verified against vb6.exe).
        if (!interpreter.TryReadNow(env, name, out var named, out var maybeGroup, out var namedNeed))
            (named, maybeGroup) = await interpreter.ReadVariableAfter(env, name, namedNeed!);
        // A Collection's default member, Item: `c(1)` and `c("key")` (#1471). `c(1)(2)` is #1476, refused above.
        if (named && maybeGroup.Value is VbCollection items)
        {
            if (dollar)
                throw new NotImplementedException($"{name}$ on a Collection is not implemented (#742)");
            return await AsBound(maybeGroup, "Item", async () => items.Default(await EvaluateMemberArgs(items, "Item", argsCtx)));
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
            if (dollar)
                throw new NotImplementedException($"{name}$ on an array is not implemented (#742)");
            if (variable.Value is not VBArray array)
                throw new VBCompileErrorException("Array expected");
            var indices = await EvaluateCallArgs(argsCtx);
            try
            {
                return ElementAsRead(variable, array, array.GetValue(Subscripts(indices)));
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.SubscriptOutOfRange);
            }
        }
        // In a form's own code, a member of the form (#995). A method is called: `TextWidth("abc")`. A property called with
        // arguments is VBA's function of the name only when the arguments fit it, `Left("abc", 1)`, and never a project
        // procedure's (measured). Any other is refused: VB6 gives "Wrong number of arguments" where the property's type
        // takes no index, and reaches a default member where it does (`Controls(0)`), which HexIDE has none of yet.
        if (FormMembers.TryBare(interpreter.ExecutionContext, env, name, out var form, out var formMember))
        {
            var formArgs = await EvaluateCallArgs(argsCtx);
            // `Left$("abc", 1)` in a form's code is VBA's Left$, a String, as `Left("abc", 1)` is VBA's Left (measured).
            if (dollar && !FormMembers.CallIsVbas(name, formArgs.Count))
                throw new NotImplementedException($"{name}$ on a member of the form is not implemented (#742)");
            if (formMember.IsMethod)
                return await interpreter.Loading((Form: form, Name: name, Args: formArgs), static s => FormMembers.Call(s.Form, s.Name, s.Args));
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
            var callArgs = await ResolveCallArgs(argsCtx);
            if (await interpreter.CallProcedure(name, callArgs, currentModule, callerEnv: env, callerFrames: stmtFrames) is { } fnResult)
                return fnResult;
            throw new VBSubOrFunctionNotDefinedException(name); // a Sub used where a value is required
        }
        if (dollar && DollarRefused.Contains(name))
            throw new VBCompileErrorException(TypeCharacterMismatch);
        if (dollar && !DollarForms.Contains(name) && !DollarIgnored.Contains(name))
            throw new NotImplementedException($"{name}$ is not implemented: what VB6 does with a $ on {name} is not measured (#742)");
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
    /// Any other field given one is refused. A class's Variant holding an array, <c>k.V(1)</c>, is its element early-bound
    /// and 451 late-bound, which HexIDE can't tell apart; the index used to be dropped, which read the whole array.
    /// </para>
    /// </summary>
    private static Vb6Value FieldElement(VbObject obj, string name, Vb6Value field, List<Vb6Value> indexes,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
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
        Vb6Value variable, VB6Parser.ICS_S_MemberCallContext member, Antlr4.Runtime.ParserRuleContext ctx)
    {
        if (variable.Value is ICSharpAsyncProxy proxy)
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
                var callArgs = MemberArgs(member) is { } ac ? await ResolveCallArgs(ac) : new List<CallArg>();
                return await interpreter.RunProcedure(vobj.ClassDef, method, callArgs, vobj.InstanceEnv, variable, stmtFrames)
                    ?? throw new VBSubOrFunctionNotDefinedException(memberName);   // a Sub used as a value
            }
            // A read (`= x.P`) dispatches the Property Get accessor (Function-like: returns via its name).
            if (vobj.ClassDef.PrePass.Properties.TryGetValue(memberName, out var prop) && prop.Get is { } getter)
            {
                if (MemberArgs(member) != null)
                    throw new NotImplementedException("Parameterized property access is not supported");
                return await interpreter.RunProcedure(vobj.ClassDef, getter, [], vobj.InstanceEnv, variable, stmtFrames)
                    ?? throw new VBSubOrFunctionNotDefinedException(memberName);
            }
            if (!interpreter.TryReadNow(vobj.InstanceEnv, memberName, out var isField, out var fieldVal, out var isFieldNeed))
                (isField, fieldVal) = await interpreter.ReadVariableAfter(vobj.InstanceEnv, memberName, isFieldNeed!);
            if (isField)
                return MemberArgs(member) is { } fieldArgs
                    ? FieldElement(vobj, memberName, fieldVal, await EvaluateCallArgs(fieldArgs), ctx)
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
                }
                return await ResolveOneMember(new Vb6Value(formWindow), member, ctx);
            }
            throw new VBMethodOrDataMemberNotFoundException(memberName, variable.Type);
        }

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

        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            // A Timer's or scroll bar's colour, which VB6 does not have and which would read as black. Refused until
            // #801 resolves a property the control does not declare, rather than read wrong.
            if (AvaloniaInteroperability.IsUnsetColour(control, prop))
                throw new NotImplementedException(
                    $"Reading {memberIdentifier} from this control is not implemented: VB6 gives it no {memberIdentifier} (#801)");
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
    /// A UDT hands the rest of the chain to <see cref="GetUdtField"/>, which already walked its own bags.
    /// Everything else resolves a step at a time — so a chain may change kind as it goes (an object field
    /// holding a UDT, a UDT field holding an object) without any step needing to know what follows it.
    /// </summary>
    internal async Task<Vb6Value> ResolveMemberChain(
        Vb6Value current, VB6Parser.ICS_S_MemberCallContext[] members, int from,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        for (var i = from; i < members.Length; i++)
        {
            if (current.Value is VbUdt)
                return (Vb6Value)GetUdtField(current, members[i..], ctx)!;
            // A member reached through a Variant is late-bound, and its result is a Variant's whatever the member is
            // declared as (measured, #987: `vo.P * 7000` with `Property Get P() As Integer` returning 5 is 35000 through
            // `Dim vo`, and 6 through `Dim o As Foo`; `With vo: .P` is the same).
            var lateBound = current.HeldAsVariant;
            current = await ResolveOneMember(current, members[i], ctx);
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

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Enum)
        {
            // MyEnum.Member -> the member's Long value.
            if (qualifier.EnumMembers!.TryGetValue(memberName, out var enumValue))
                return await ThenTheRest(new Vb6Value(enumValue));
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        // Module-qualified: a Public procedure (private is visible only when qualifying the current module),
        // else a module-level Public variable/const.
        var module = qualifier.Module!;
        if (module.PrePass.Procedures.TryGetValue(memberName, out var proc)
            && (!proc.IsPrivate || ReferenceEquals(module, currentModule)))
        {
            var callArgs = argsCtx != null ? await ResolveCallArgs(argsCtx) : new List<CallArg>();
            if (await interpreter.RunProcedure(module, proc, callArgs, callerFrames: stmtFrames) is { } result)
                return await ThenTheRest(result);
            throw new VBSubOrFunctionNotDefinedException(memberName);   // a Sub used where a value is required
        }
        if (await interpreter.ReadVariable(module.ModuleEnv, memberName) is (true, var v))
            return await ThenTheRest(v);

        // `Module1.MyEnum.Member` — the three-part form, measured legal. Unlike the library case this one
        // CAN be validated exactly, because the module's own enums are collected in its pre-pass: the
        // middle segment must name an enum this module declares, and the last must be one of its members.
        // `Module1.Member` already worked, because a member is hoisted as a module-level name.
        if (idx + 1 < members.Length && module.PrePass.Enums.TryGetValue(memberName, out var enumTable))
        {
            var innerName = MemberName(members[idx + 1]);
            if (!enumTable.TryGetValue(innerName, out var innerValue))
                throw new VBMethodOrDataMemberNotFoundException(innerName, Vb6Value.ValueType.EmptyVariant);
            var innerResult = new Vb6Value(innerValue);
            return idx + 2 < members.Length
                ? await ResolveMemberChain(innerResult, members, idx + 2, ctx)
                : innerResult;
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
    private static object? GetUdtField(Vb6Value root, VB6Parser.ICS_S_MemberCallContext[] members, Antlr4.Runtime.ParserRuleContext ctx)
    {
        var current = root;
        foreach (var m in members)
        {
            if (current.Value is not VbUdt bag)
                throw new VBRunTimeException(ctx, VBStandardError.TypeMismatch, "Member access on a non-UDT value");
            var fieldName = MemberName(m);
            if (!bag.TryGet(fieldName, out current))
                throw new VBRunTimeException(ctx, VBStandardError.MethodOrDataMemberNotFound, fieldName);
        }
        return current;
    }

    // The identifier of a member-call segment (`.Foo` or `.Foo(args)`). A ProcedureOrArrayCall head can be a
    // baseType token (e.g. `.Currency`) rather than an ambiguousIdentifier, so the inner accessor needs its own
    // null-guard — otherwise it NREs instead of falling through to the clean "malformed" error.
    private static string MemberName(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier().GetText()
           ?? m.iCS_S_ProcedureOrArrayCall()?.ambiguousIdentifier()?.GetText()
           ?? throw new VBCompileErrorException("Malformed or unsupported qualified member call");

    // The single argsCall of a `.Foo(args)` member segment, or null for a bare `.Foo`.
    private static VB6Parser.ArgsCallContext? MemberArgs(VB6Parser.ICS_S_MemberCallContext m)
        => m.iCS_S_ProcedureOrArrayCall() is { } p && p.argsCall().Length == 1 ? p.argsCall(0) : null;

    public override async Task<object?> VisitVsAmp(VB6Parser.VsAmpContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
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

    /// <summary>An operator's result, a Variant's when either operand is (measured, #987: `v + 1` with `v` holding 4, and
    /// `i + v` with `i` an Integer, are Variant selectors), and otherwise as the operator produced it, which is typed when
    /// both operands are (#122).</summary>
    private static Vb6Value Typed(Vb6Value left, Vb6Value right, Vb6Value result) =>
        left.HeldAsVariant || right.HeldAsVariant ? result.AsVariant() : result;

    // `+` and `-`, one level of the grammar, so taken left to right (#1074).
    public override async Task<object?> VisitVsAdd(VB6Parser.VsAddContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (context.MINUS() != null)
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
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, context.DIV() != null
            ? VbNumeric.RealDivide(leftValue, rightValue, context)
            : VbNumeric.Mul(leftValue, rightValue, context));
    }

    public override async Task<object?> VisitVsMod(VB6Parser.VsModContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.Modulo(leftValue, rightValue, context));
    }

    public async override Task<object?> VisitVsDiv(VB6Parser.VsDivContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.IntDivide(leftValue, rightValue, context));
    }

    public override async Task<object?> VisitVsPow(VB6Parser.VsPowContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return Typed(leftValue, rightValue, VbNumeric.Power(leftValue, rightValue, context));
    }

    // Every comparison is one level of the grammar, Like and Is included, so `1 < 2 = True` is (1 < 2) = True and
    // `Nothing Is Nothing = True` is True (measured, #1074).
    public override async Task<object?> VisitVsCompare(VB6Parser.VsCompareContext context)
    {
        if (context.LIKE() != null)
            throw new NotImplementedException("Like is not implemented");
        if (context.IS() != null)
        {
            // Reference identity on the underlying object (Nothing = null). One line covers a Is b, x Is Nothing,
            // and Nothing Is Nothing — NOT Vb6Value equality (which would run numeric coercion).
            var leftObject = await EvaluateValue(context.valueStmt(0));
            var rightObject = await EvaluateValue(context.valueStmt(1));
            return new Vb6Value(ReferenceEquals(leftObject.Value, rightObject.Value));
        }
        if (context.EQ() != null || context.NEQ() != null)
        {
            var (left, right) = await GetTwoValuesSameTypesOrNull(context.valueStmt());
            if (left.Type == Vb6Value.ValueType.Null || right.Type == Vb6Value.ValueType.Null)
                return Vb6Value.Null;
            return new Vb6Value(left.Equals(right) == (context.EQ() != null));
        }
        var order = context.LT() != null ? Order.Less
            : context.GT() != null ? Order.Greater
            : context.LEQ() != null ? Order.LessOrEqual
            : Order.GreaterOrEqual;
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        // Two strings compare ordinally (VB6 default Option Compare Binary — oracle-verified: "B" < "a", "10" < "9").
        // A string-vs-number pair is left to the numeric path below (numeric string coerces; a non-numeric string
        // vs a number is Type Mismatch — both already faithful).
        if (StringCompare(leftValue, rightValue) is int sc)
            return (Vb6Value)Holds(order, sc, 0);
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

    // Ordinal string comparison for the relational operators when BOTH operands are String (VB6 default Option
    // Compare Binary — verified against vb6.exe). Returns null when the pair is not two strings, so the caller
    // falls through to the numeric path (a numeric string vs a number coerces; a non-numeric string vs a number is
    // a Type Mismatch — both already faithful). `Option Compare Text` (case-insensitive) is a separate wall.
    private static int? StringCompare(Vb6Value left, Vb6Value right)
        => left.Type == Vb6Value.ValueType.String && right.Type == Vb6Value.ValueType.String
            ? left.TryCompareTo(right)
            : null;

    public override async Task<object?> VisitVsAnd(VB6Parser.VsAndContext context)
    {
        // The RAW pair, not the coerced one. GetTwoValuesSameTypes promotes both operands to a common
        // numeric type and throws on any mix it cannot unify — which loses the per-operand width the
        // result ladder is defined over (Byte with Integer is Integer; Byte with Byte stays Byte) and
        // rejects the ordinary `True And 2` and `"12" And 10` outright. Bitwise operators do not coerce
        // to a common type; they reduce each operand to bits independently. (#166)
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
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
        if (TryBitwise(leftValue, rightValue, static (a, b) => a ^ b, out var xor)) return xor;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsNegation(VB6Parser.VsNegationContext context)
    {
        var value = await EvaluateScalar(context.valueStmt());
        if (value.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        var negated = VbNumeric.Negate(value, context);
        return value.HeldAsVariant ? negated.AsVariant() : negated;
    }

    public override async Task<object?> VisitVsNot(VB6Parser.VsNotContext context)
    {
        var value = await EvaluateScalar(context.valueStmt());
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
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (TryBitwise(leftValue, rightValue, static (a, b) => ~(a ^ b), out var eqv)) return eqv;
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsImp(VB6Parser.VsImpContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());   // raw — see VisitVsAnd
        if (leftValue.Type == Vb6Value.ValueType.Null &&
            rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (leftValue.Type == Vb6Value.ValueType.Null && TryUnpack(rightValue, out bool rbool))
            return rbool ? (Vb6Value)true : Vb6Value.Null;
        if (rightValue.Type == Vb6Value.ValueType.Null && TryUnpack(leftValue, out bool lbool))
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
        // `New Collection`, and `New VBA.Collection`, the library's own (#1471).
        if (call?.iCS_S_MembersCall() is { } qualified && IsCollectionType(qualified.GetText()))
            return interpreter.AdoptNewTemp(new Vb6Value(interpreter.NewCollection()), stmtFrames);
        var className = call?.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
            ?? throw new VBCompileErrorException("Expected a class name after New");
        if (IsCollectionType(className) && !interpreter.Modules.TryGet(className, out _))
            return interpreter.AdoptNewTemp(new Vb6Value(interpreter.NewCollection()), stmtFrames);
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
        if (operand.Value is VbObject vo)
            return new Vb6Value(string.Equals(vo.ClassName, targetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase)
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
