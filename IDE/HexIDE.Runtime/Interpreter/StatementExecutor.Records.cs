namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// <c>Get</c> and <c>Put</c> on Binary and Random files (#1646), in the bytes <see cref="VbRecordFormat"/> describes. What
/// an operand is decides its bytes, as VB6's compiler decides it: a variable, field or element by its declaration, and any
/// other expression by its type, which HexIDE knows for a literal, a typed variable's value, a call declared to return a
/// type, and an intrinsic measured for it. Where it doesn't know, a Put is refused: a Variant and its value written raw are
/// different files.
/// </summary>
public partial class StatementExecutor
{

    public override async Task<ControlFlow> VisitPutStmt(VB6Parser.PutStmtContext context)
    {
        var files = interpreter.BuiltIns.Files;
        var values = context.valueStmt();
        var number = await FileNumber(values[0]);
        long? at = values.Length == 3 ? await RecordNumber(values[1]) : null;
        var target = values[^1];
        var (arg, operand) = await RecordOperandOf(target, forGet: false);
        var random = files.RecordMode(number) == VbFileMode.Random;
        var value = arg.Location is { } location ? interpreter.ExecutionContext.State[location] : arg.Ref?.Read() ?? arg.Value;
        var writer = new VbRecordFormat.Writer();
        try
        {
            switch (operand.Kind)
            {
                case RecordOperandKind.Udt:
                    VbRecordFormat.WriteUdt(writer, (VbUdt)value.Value!);
                    break;
                case RecordOperandKind.Array:
                    var array = value.Value as VBArray ?? new VBArray();
                    VbRecordFormat.WriteArray(writer, array, descriptor: random && array.IsDynamic);
                    break;
                case RecordOperandKind.Variant:
                    VbRecordFormat.WriteVariant(writer, value);
                    break;
                default:
                    VbRecordFormat.WriteTyped(writer, value, operand.Type!, prefixed: random);
                    break;
            }
        }
        catch (RecordObjectInVariant partial)
        {
            files.Put(number, at, partial.Written, move: false);
            throw new VBRunTimeException(target, VBStandardError.TypeMismatch);
        }
        files.Put(number, at, writer.ToArray());
        return ControlFlow.Nothing;
    }

    public override async Task<ControlFlow> VisitGetStmt(VB6Parser.GetStmtContext context)
    {
        var files = interpreter.BuiltIns.Files;
        var values = context.valueStmt();
        var number = await FileNumber(values[0]);
        long? at = values.Length == 3 ? await RecordNumber(values[1]) : null;
        var target = values[^1];
        var (arg, operand) = await RecordOperandOf(target, forGet: true);
        var random = files.RecordMode(number) == VbFileMode.Random;
        var state = interpreter.ExecutionContext.State;
        var current = arg.Location is { } location ? state[location] : arg.Ref!.Read();
        var read = files.BeginGet(number, at);
        try
        {
            switch (operand.Kind)
            {
                case RecordOperandKind.Udt:
                    VbRecordFormat.ReadUdtInto(read, (VbUdt)current.Value!);
                    files.EndGet(read);
                    break;
                case RecordOperandKind.Array:
                    var array = current.Value as VBArray ?? new VBArray();
                    if (!(random && array.IsDynamic))
                    {
                        // A Binary file's array, or a fixed one, is read into as it stands: one with no bounds yet reads
                        // nothing (measured).
                        VbRecordFormat.ReadElementsInto(read, array);
                        files.EndGet(read);
                        break;
                    }
                    // A Random file's dynamic array takes the bounds its descriptor gives, whatever it had (measured). The
                    // record is read before the array is checked: an array locked by a ByRef element is 10, after which
                    // the file has moved on (measured).
                    var bounds = VbRecordFormat.ReadDescriptor(read);
                    var element = array.ElementType ?? Vb6Value.ValueType.EmptyVariant;
                    if (element == Vb6Value.ValueType.UserDefinedType)
                        throw new NotImplementedException("Get of a Random file's record into a dynamic array of a user-defined Type is not implemented (#1646, #178)");
                    VBArray replaced;
                    if (bounds.Count == 0)
                    {
                        replaced = new VBArray(element, [(0, 0)]) { DeclaredClass = array.DeclaredClass };
                        replaced.Free();
                    }
                    else
                    {
                        replaced = new VBArray(element, bounds) { DeclaredClass = array.DeclaredClass };
                        VbRecordFormat.ReadElementsInto(read, replaced);
                    }
                    files.EndGet(read);
                    if (array.IsLocked)
                        throw new VBRunTimeException(target, VBStandardError.ThisArrayIsFixedOrTemporarilyLocked);
                    StoreRecordValue(arg, current.WithArray(replaced));
                    break;
                case RecordOperandKind.Variant:
                    var variant = VbRecordFormat.ReadVariant(read);
                    files.EndGet(read);
                    StoreRecordValue(arg, variant);
                    break;
                default:
                    var length = operand.Type == Vb6Value.ValueType.String ? VbDisplay.ToText(current).Length : 0;
                    var typed = VbRecordFormat.ReadTyped(read, operand.Type!, prefixed: random, length);
                    files.EndGet(read);
                    StoreRecordValue(arg, typed);
                    break;
            }
        }
        catch (RecordGetFailure failure)
        {
            if (failure.ClearTo is { } cleared)
                StoreRecordValue(arg, cleared);
            throw new VBRunTimeException(target, failure.Error);
        }
        return ControlFlow.Nothing;
    }

    // A record or byte number, a Long: 2.5 is 2 and 3.5 is 4, and "6" is 6 (measured).
    private async Task<long> RecordNumber(VB6Parser.ValueStmtContext context) =>
        Convert.ToInt64(VbNumeric.CoerceOnStore(await expressionExecutor.EvaluateValue(context), Vb6Value.ValueType.Long, context).Value,
            System.Globalization.CultureInfo.InvariantCulture);

    private void StoreRecordValue(CallArg arg, Vb6Value value)
    {
        if (arg.Location is { } location)
            interpreter.ExecutionContext.State[location] = value;
        else
            arg.Ref!.Write(value);
    }

    /// <summary>
    /// What a Put or Get operand is: a variable, field or element by its declaration; for Put, any other expression by the type
    /// HexIDE knows it has. Get needs something to store into, and a literal, a Const or a call is VB6's compile error
    /// "Variable required" (measured). An object variable is VB6's compile error for Get and Put alike (measured).
    /// </summary>
    private async Task<(CallArg Arg, RecordOperand Operand)> RecordOperandOf(VB6Parser.ValueStmtContext target, bool forGet)
    {
        var arg = await expressionExecutor.ResolveByRef(target);
        var state = interpreter.ExecutionContext.State;
        if (arg.Location is { } location)
        {
            if (forGet && (state.IsConstant(location) || state.IsReadOnly(location)))
                throw new VBCompileErrorException(VariableRequired);
            var held = state[location];
            if (held.Value is VbUdt)
                return (arg, new RecordOperand(RecordOperandKind.Udt));
            if (state.IsDeclaredArray(location))
                return (arg, new RecordOperand(RecordOperandKind.Array));
            if (state.DeclaredClassOf(location) is not null)
                throw new VBCompileErrorException(VbRecordFormat.ObjectRefused);
            return (arg, OperandOfType(state.DeclaredTypeOf(location)));
        }
        if (arg.Ref is { } reference)
        {
            if (reference.Read().Value is VbUdt)
                return (arg, new RecordOperand(RecordOperandKind.Udt));
            if (reference.IsVariant)
                return (arg, new RecordOperand(RecordOperandKind.Variant));
            return (arg, OperandOfType(reference.DeclaredType ?? Vb6Value.ValueType.Object));
        }
        if (forGet)
            throw new VBCompileErrorException(VariableRequired);
        var value = arg.Value;
        if (value.Value is VbUdt)
            return (arg, new RecordOperand(RecordOperandKind.Udt));
        // Null and Empty are only ever a Variant's, and a literal Null is written 01 00 (measured).
        if (value.Type == Vb6Value.ValueType.EmptyVariant || value.Type == Vb6Value.ValueType.Null || value.HeldAsVariant)
            return (arg, new RecordOperand(RecordOperandKind.Variant));
        if (value.HasFixedType && IsRecordScalar(value.Type))
            return (arg, new RecordOperand(RecordOperandKind.Typed, value.Type));
        throw new NotImplementedException(
            $"Put of {target.GetText()} is not implemented: whether VB6 writes it as a Variant or as its value was not measured (#1646)");
    }

    private static RecordOperand OperandOfType(Vb6Value.ValueType? declared)
    {
        if (declared is null || declared == Vb6Value.ValueType.EmptyVariant)
            return new RecordOperand(RecordOperandKind.Variant);
        if (IsRecordScalar(declared))
            return new RecordOperand(RecordOperandKind.Typed, declared);
        throw new VBCompileErrorException(VbRecordFormat.ObjectRefused);
    }

    private static bool IsRecordScalar(Vb6Value.ValueType type) =>
        !type.IsArray && (type == Vb6Value.ValueType.Integer || type == Vb6Value.ValueType.Long || type == Vb6Value.ValueType.Single
            || type == Vb6Value.ValueType.Double || type == Vb6Value.ValueType.Currency || type == Vb6Value.ValueType.Date
            || type == Vb6Value.ValueType.Boolean || type == Vb6Value.ValueType.Byte || type == Vb6Value.ValueType.String);
}
