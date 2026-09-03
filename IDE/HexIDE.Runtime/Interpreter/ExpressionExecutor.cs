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
        return await interpreter.BuiltIns.EvaluateBuiltInFunction(name, args);
    }

    public async Task<(Vb6Value, Vb6Value)> GetTwoValues(VB6Parser.ValueStmtContext[] context)
    {
        if (context.Length != 2)
            throw new Exception("This should only be called for two operands instructions");
        var left = await EvaluateValue(context[0]);
        var right = await EvaluateValue(context[1]);
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
        foreach (var arg in ArgSlots(context))
        {
            // A blank slot has no lvalue to alias, so it can never be ByRef.
            if (arg is null)
            {
                result.Add(new CallArg(Vb6Value.Missing, null));
                continue;
            }
            if (arg.PARAMARRAY() != null)
                throw new NotImplementedException("ParamArray arguments are not yet supported");
            var value = await EvaluateValue(arg.valueStmt());
            // A call-site ByVal keyword (or a non-lvalue like a literal / (x)) forces a copy.
            int? location = arg.BYVAL() != null ? null : TryGetArgLocation(arg.valueStmt());
            result.Add(new CallArg(value, location));
        }
        return result;
    }

    private int? TryGetArgLocation(VB6Parser.ValueStmtContext valueStmt)
    {
        if (valueStmt is VB6Parser.VsICSContext ics
            && ics.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall() is { } vp
            && vp.typeHint() == null && vp.dictionaryCallStmt() == null
            && env.TryGetVariableLocation(vp.ambiguousIdentifier().GetText(), out var loc))
            return loc;
        return null;
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
            var str = stringliteral.GetText().Substring(1, stringliteral.GetText().Length - 2);
            Vb6Value val = new Vb6Value(str);
            return val;
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
            // VB6 date literals are culture-independent, US month/day/year — parse invariantly.
            var inner = dateliteral.GetText().Trim('#');
            return new Vb6Value(DateTime.Parse(inner, System.Globalization.CultureInfo.InvariantCulture));
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
        var identifier = await ExtractIdentifier(context);
        RefuseAmbiguousEnumMember(identifier);
        if (interpreter.ExecutionContext.TryGetVariable(env, identifier, out var var))
            return var;
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
            if (varOrProcCall.typeHint() != null)
                throw new NotImplementedException("Type hint is not supported");
            if (varOrProcCall.dictionaryCallStmt() != null)
                throw new NotImplementedException("dictionaryCallStmt is not supported");
            var identifier = varOrProcCall.ambiguousIdentifier().GetText();
            RefuseAmbiguousEnumMember(identifier);

            if (!interpreter.ExecutionContext.TryGetVariable(env, identifier, out var variable))
            {
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
                // Nothing else claims the name, so VB6 makes it a variable: reading an undeclared name is
                // legal and yields Empty (measured). LAST, after procedures and intrinsics — a mistyped
                // function call must not become a silent Empty. (#171)
                //
                // Returned WITHOUT allocating. Creating here would make evaluating an expression mutate
                // the environment, and the debugger evaluates watch expressions on its own schedule — a
                // watch on an undeclared name would create it and fire a change the program never made.
                // That is not hypothetical: it broke WatchBreakTests, which caught the observer changing
                // what it observed. The write path allocates; a read only reports.
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
                return Vb6Value.Variant;
            }

            return variable;
        }
        else if (ctx.iCS_S_MembersCall() is { } membersCall)
        {
            if (membersCall.dictionaryCallStmt() != null)
                throw new NotImplementedException($"dictionaryCall not supported");

            // A namespace qualifier (`Module1.Foo`, `VBA.Abs`, `VBA.Math.Abs`) — resolved before treating the
            // leading part as a value, but a same-named variable shadows it (VB6 precedence).
            if (membersCall.iCS_S_VariableOrProcedureCall() is { } leadPart
                && !interpreter.ExecutionContext.TryGetVariable(env, leadPart.GetText(), out _)
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
        else
        {
            throw new NotImplementedException($"{ctx} is not supported");
        }
    }

    /// <summary>Resolve a parenthesised call on an identifier — a control-array element (<c>Command1(i)</c>), a
    /// local array element (<c>arr(i)</c>), a user Function, or an intrinsic — as a VALUE. Shared by expression
    /// position and the leading part of a member chain (<c>Command1(i).Caption</c>).</summary>
    public async Task<object?> EvaluateProcedureOrArrayCall(VB6Parser.ICS_S_ProcedureOrArrayCallContext procOrArrayCall)
    {
        if (procOrArrayCall.dictionaryCallStmt() != null)
            throw new NotImplementedException($"dictionaryCall not supported");

        if (procOrArrayCall.ambiguousIdentifier() == null)
            throw new NotImplementedException($"only proc call supportedhere");

        if (procOrArrayCall.typeHint() != null)
            throw new NotImplementedException($"typehint not supported");

        if (procOrArrayCall.argsCall().Length > 1)
            throw new NotImplementedException("only a single argsCall is supported");
        var argsCtx = procOrArrayCall.argsCall().Length == 1 ? procOrArrayCall.argsCall(0) : null;

        var name = procOrArrayCall.ambiguousIdentifier().GetText();

        // A control array indexed by element (`Command1(i)`) — the shared name resolves to a ControlArrayGroup
        // value; return the element control. A missing element is Err 340 (oracle-verified against vb6.exe).
        if (interpreter.ExecutionContext.TryGetVariable(env, name, out var maybeGroup)
            && maybeGroup.Value is ControlArrayGroup group)
        {
            var idxArgs = await EvaluateCallArgs(argsCtx);
            if (idxArgs.Count != 1)
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.SubscriptOutOfRange);
            if (!group.TryGetElement(AsType<int>(idxArgs[0]), out var element))
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.ControlArrayElementDoesntExist);
            return new Vb6Value(element);
        }

        // Resolution order (VB6): a local ARRAY variable → user procedure → intrinsic. A non-array
        // variable of the same name (e.g. a Function's own return slot) is not array-indexing — it falls
        // through so `Fact(n - 1)` inside `Fact` is a recursive call, and `Zero()` finds the Function.
        if (interpreter.ExecutionContext.TryGetVariable(env, name, out var variable) && variable.Type.IsArray)
        {
            if (variable.Value is not VBArray array)
                throw new VBCompileErrorException("Array expected");
            var indices = await EvaluateCallArgs(argsCtx);
            try
            {
                return array.GetValue(AsType<int>(indices));
            }
            catch (IndexOutOfRangeException)
            {
                throw new VBRunTimeException(procOrArrayCall, VBStandardError.SubscriptOutOfRange);
            }
        }
        if (interpreter.TryResolveProcedure(name, currentModule, out _, out _))
        {
            var callArgs = await ResolveCallArgs(argsCtx);
            if (await interpreter.CallProcedure(name, callArgs, currentModule, callerEnv: env, callerFrames: stmtFrames) is { } fnResult)
                return fnResult;
            throw new VBSubOrFunctionNotDefinedException(name); // a Sub used where a value is required
        }
        var args = await EvaluateCallArgs(argsCtx);
        if (await EvaluateFunction(name, args) is { } builtInResult)
            return builtInResult;

        throw new VBSubOrFunctionNotDefinedException(name);
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
            if (interpreter.ExecutionContext.TryGetVariable(vobj.InstanceEnv, memberName, out var fieldVal))
                return fieldVal;
            throw new VBMethodOrDataMemberNotFoundException(memberName, variable.Type);
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
            if (AvaloniaInteroperability.TryGet(control, prop, out var value))
                return value;
        }

        throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
    }

    /// <summary>
    /// Fold every remaining member onto a value, left to right.
    ///
    /// A UDT hands the rest of the chain to <see cref="GetUdtField"/>, which already walked its own bags.
    /// Everything else resolves a step at a time — so a chain may change kind as it goes (an object field
    /// holding a UDT, a UDT field holding an object) without any step needing to know what follows it.
    /// </summary>
    private async Task<Vb6Value> ResolveMemberChain(
        Vb6Value current, VB6Parser.ICS_S_MemberCallContext[] members, int from,
        Antlr4.Runtime.ParserRuleContext ctx)
    {
        for (var i = from; i < members.Length; i++)
        {
            if (current.Value is VbUdt)
                return (Vb6Value)GetUdtField(current, members[i..], ctx)!;
            current = await ResolveOneMember(current, members[i], ctx);
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
            if (interpreter.ExecutionContext.TryGetVariable(env, memberName, out var projectMember))
                return await ThenTheRest(projectMember);
            foreach (var m in interpreter.Modules.All)
            {
                if (m.Kind != InterpreterModuleKind.Standard) continue;
                if (interpreter.ExecutionContext.TryGetVariable(m.ModuleEnv, memberName, out var fromModule))
                    return await ThenTheRest(fromModule);
            }
            throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);
        }

        if (qualifier.Kind == BasicInterpreter.QualifierKind.Library)
        {
            // A library-qualified name resolves against the intrinsic registry (function) or a builtin
            // constant. If neither matches and there are more segments, this one is an ENUM level and is
            // transparent — `VBRUN.AlignConstants.vbAlignBottom` and `VBRUN.vbAlignBottom` are both
            // measured legal, so the middle segment is genuinely optional, exactly as the intrinsic-module
            // level already was (`VBA.Math.Abs` is `VBA.Abs`).
            //
            // Membership is not validated, for the same reason IsIntrinsicModuleSegment does not validate
            // its own: there is no table of the libraries' enum names here, and inventing one would be
            // pre-execution analysis. An unrecognised middle segment is therefore skipped rather than
            // checked, which costs a missing error and never a wrong value.
            while (true)
            {
                var args = argsCtx != null ? await EvaluateCallArgs(argsCtx) : new List<Vb6Value>();
                if (await EvaluateFunction(memberName, args) is { } fn)
                    return await ThenTheRest(fn);
                if (args.Count == 0 && interpreter.BuiltIns.TryGetBuiltInConstant(memberName, out var c))
                    return await ThenTheRest(c);
                if (idx + 1 >= members.Length)
                    throw new VBMethodOrDataMemberNotFoundException(memberName, Vb6Value.ValueType.EmptyVariant);

                idx++;
                memberName = MemberName(members[idx]);
                argsCtx = MemberArgs(members[idx]);
            }
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
        if (interpreter.ExecutionContext.TryGetVariable(module.ModuleEnv, memberName, out var v))
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
        return new Vb6Value(AmpString(leftValue) + AmpString(rightValue));
    }

    private static string AmpString(Vb6Value v) => v.Value?.ToString() ?? "";

    public override async Task<object?> VisitVsAdd(VB6Parser.VsAddContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        if (leftValue.Type == Vb6Value.ValueType.String && rightValue.Type == Vb6Value.ValueType.String)
            return new Vb6Value((string)leftValue.Value! + (string)rightValue.Value!);   // VB6: String + String concatenates
        return VbNumeric.Add(leftValue, rightValue, context);
    }

    public override async Task<object?> VisitVsMinus(VB6Parser.VsMinusContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return VbNumeric.Sub(leftValue, rightValue, context);
    }

    public override async Task<object?> VisitVsMult(VB6Parser.VsMultContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return VbNumeric.Mul(leftValue, rightValue, context);
    }

    public override async Task<object?> VisitVsMod(VB6Parser.VsModContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return VbNumeric.Modulo(leftValue, rightValue, context);
    }

    public async override Task<object?> VisitVsDiv(VB6Parser.VsDivContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return context.DIV().GetText() == "/"
            ? VbNumeric.RealDivide(leftValue, rightValue, context)
            : VbNumeric.IntDivide(leftValue, rightValue, context);
    }

    public override async Task<object?> VisitVsPow(VB6Parser.VsPowContext context)
    {
        var (leftValue, rightValue) = await GetTwoValues(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return VbNumeric.Power(leftValue, rightValue, context);
    }

    public override async Task<object?> VisitVsEq(VB6Parser.VsEqContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypesOrNull(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return new Vb6Value(leftValue.Equals(rightValue));
    }

    public override async Task<object?> VisitVsNeq(VB6Parser.VsNeqContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypesOrNull(context.valueStmt());
        if (leftValue.Type == Vb6Value.ValueType.Null || rightValue.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return new Vb6Value(!leftValue.Equals(rightValue));
    }

    public override async Task<object?> VisitVsLt(VB6Parser.VsLtContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        // Two strings compare ordinally (VB6 default Option Compare Binary — oracle-verified: "B" < "a", "10" < "9").
        // A string-vs-number pair is left to the numeric path below (numeric string coerces; a non-numeric string
        // vs a number is Type Mismatch — both already faithful).
        if (StringCompare(leftValue, rightValue) is int sc)
            return (Vb6Value)(sc < 0);
        if (TryUnpack(leftValue, rightValue, out int leftInt, out int rightInt))
            return (Vb6Value)(leftInt < rightInt);
        if (TryUnpack(leftValue, rightValue, out float leftFloat, out float rightFloat))
            return (Vb6Value)(leftFloat < rightFloat);
        if (TryUnpack(leftValue, rightValue, out double leftDouble, out double rightDouble))
            return (Vb6Value)(leftDouble < rightDouble);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsGt(VB6Parser.VsGtContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        if (StringCompare(leftValue, rightValue) is int sc)
            return (Vb6Value)(sc > 0);
        if (TryUnpack(leftValue, rightValue, out int leftInt, out int rightInt))
            return (Vb6Value)(leftInt > rightInt);
        if (TryUnpack(leftValue, rightValue, out float leftFloat, out float rightFloat))
            return (Vb6Value)(leftFloat > rightFloat);
        if (TryUnpack(leftValue, rightValue, out double leftDouble, out double rightDouble))
            return (Vb6Value)(leftDouble > rightDouble);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsLeq(VB6Parser.VsLeqContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        if (StringCompare(leftValue, rightValue) is int sc)
            return (Vb6Value)(sc <= 0);
        if (TryUnpack(leftValue, rightValue, out int leftInt, out int rightInt))
            return (Vb6Value)(leftInt <= rightInt);
        if (TryUnpack(leftValue, rightValue, out float leftFloat, out float rightFloat))
            return (Vb6Value)(leftFloat <= rightFloat);
        if (TryUnpack(leftValue, rightValue, out double leftDouble, out double rightDouble))
            return (Vb6Value)(leftDouble <= rightDouble);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

    public override async Task<object?> VisitVsGeq(VB6Parser.VsGeqContext context)
    {
        var (leftValue, rightValue) = await GetTwoValuesSameTypes(context.valueStmt());
        if (StringCompare(leftValue, rightValue) is int sc)
            return (Vb6Value)(sc >= 0);
        if (TryUnpack(leftValue, rightValue, out int leftInt, out int rightInt))
            return (Vb6Value)(leftInt >= rightInt);
        if (TryUnpack(leftValue, rightValue, out float leftFloat, out float rightFloat))
            return (Vb6Value)(leftFloat >= rightFloat);
        if (TryUnpack(leftValue, rightValue, out double leftDouble, out double rightDouble))
            return (Vb6Value)(leftDouble >= rightDouble);
        throw new VBRunTimeException(context, VBStandardError.TypeMismatch);
    }

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
        var value = await EvaluateValue(context.valueStmt());
        if (value.Type == Vb6Value.ValueType.Null)
            return Vb6Value.Null;
        return VbNumeric.Negate(value, context);
    }

    public override async Task<object?> VisitVsNot(VB6Parser.VsNotContext context)
    {
        var value = await EvaluateValue(context.valueStmt());
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
        // would try to resolve a variable and throw). `As New` auto-instantiation is deferred (Phase 4).
        var className = (context.valueStmt() as VB6Parser.VsICSContext)
            ?.implicitCallStmt_InStmt()?.iCS_S_VariableOrProcedureCall()?.ambiguousIdentifier()?.GetText()
            ?? throw new VBCompileErrorException("Expected a class name after New");
        var created = Vb6Value.NewObject(await interpreter.NewObject(className));
        // Hold the transient in the current statement frame so a New that is never stored (consumed by a built-in
        // arg, a condition, a member read) still terminates at statement-end; a subsequent Set / ByVal-bind
        // AddRefs again so the destination out-lives the flush by exactly one net reference.
        interpreter.AdoptNewTemp(created, stmtFrames);
        return created;
    }

    public override async Task<object?> VisitVsTypeOf(VB6Parser.VsTypeOfContext context)
    {
        var operandStmt = context.typeOfStmt().valueStmt();
        var targetType = context.typeOfStmt().type();

        // Grammar greediness: `TypeOf p Is Clock` parses the operand as the vsIs expression `p Is Clock` with NO
        // Is-type clause. Recover by splitting the vsIs into the real operand + the type name.
        Vb6Value operand;
        string targetName;
        if (targetType == null && operandStmt is VB6Parser.VsIsContext vsIs)
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
        if (operand.Value is VbObject vo)
            return new Vb6Value(string.Equals(vo.ClassName, targetName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(targetName, "Object", StringComparison.OrdinalIgnoreCase)
                // …or an interface the class implements. A class that does NOT implement it is False, not an
                // error — measured, and the reason this stays a plain widening of the same expression.
                || VbInterface.Implements(vo.ClassDef, targetName));
        return new Vb6Value(false);
    }

    public override Task<object?> VisitVsAssign(VB6Parser.VsAssignContext context) => throw new NotImplementedException("Assign is not implemented");

    public override Task<object?> VisitVsLike(VB6Parser.VsLikeContext context) => throw new NotImplementedException("Like is not implemented");

    public override async Task<object?> VisitVsIs(VB6Parser.VsIsContext context)
    {
        // Reference identity on the underlying object (Nothing = null). One line covers a Is b, x Is Nothing,
        // and Nothing Is Nothing — NOT Vb6Value equality (which would run numeric coercion).
        var left = await EvaluateValue(context.valueStmt(0));
        var right = await EvaluateValue(context.valueStmt(1));
        return new Vb6Value(ReferenceEquals(left.Value, right.Value));
    }

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