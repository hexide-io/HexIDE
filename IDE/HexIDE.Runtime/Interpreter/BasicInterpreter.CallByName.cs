using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace HexIDE.Runtime.Interpreter;

public partial class BasicInterpreter
{
    // Keep the original CallArgs: RunProcedure's binder shares caller storage for ByRef parameters.
    internal async Task<Vb6Value> CallByName(IReadOnlyList<CallArg> callArgs, Stack<List<Vb6Value>>? callerFrames)
    {
        if (NamedArguments.Any(callArgs))
            await VB6BuiltIns.ArrangeIntrinsicArguments("CallByName", callArgs); // Measured compile error, #4145.
        foreach (var arg in callArgs)
        {
            arg.Value.RefuseOddBytes("CallByName");
            if (arg.Omitted)
                throw new NotImplementedException("CallByName omitted arguments are not implemented (#4520)");
        }
        if (callArgs.Count < 3)
            throw new NotImplementedException("CallByName with fewer than three arguments is not implemented (#4520)");
        var receiver = callArgs[0].Value;
        if (receiver.Type == Vb6Value.ValueType.Object && receiver.Value is null)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (receiver.Value is not (VbObject or VbCollection or VbErr)
            && receiver.Type != Vb6Value.ValueType.Object && receiver.Type != Vb6Value.ValueType.Control
            && receiver.Type != Vb6Value.ValueType.CSharpProxyObject)
            throw new VBRunTimeException(VBStandardError.ObjectRequired);
        if (callArgs[1].Value.IsNull)
            throw new VBRunTimeException(VBStandardError.InvalidUseOfNull);
        if (callArgs[1].Value.Type != Vb6Value.ValueType.String
            || (callArgs[2].Value.Type != Vb6Value.ValueType.Integer && callArgs[2].Value.Type != Vb6Value.ValueType.Long))
            throw new NotImplementedException("CallByName name/call-type conversion is not implemented (#4520)");
        var name = (string)callArgs[1].Value.Value!;
        var kind = Convert.ToInt64(callArgs[2].Value.Value);
        if (receiver.Value is VbObject obj && obj.ClassDef.Kind == InterpreterModuleKind.Class)
        {
            if (kind is 0 or 16 || name.Length == 0)
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            if (kind is not (1 or 2 or 3 or 4 or 8))
                throw new NotImplementedException("CallByName call-type combination is not implemented (#4520)");
            var memberArgs = new List<CallArg>();
            for (var i = 3; i < callArgs.Count; i++) memberArgs.Add(callArgs[i]);
            ProcedureInfo? procedure;
            if (obj.ClassDef.PrePass.Procedures.TryGetValue(name, out var method))
            {
                if (method.IsPrivate)
                    throw new NotImplementedException("CallByName private members are not implemented (#4520)");
                if (kind == 2)
                    throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
                if (kind != 1)
                    throw new NotImplementedException("CallByName method call type is not implemented (#4520)");
                procedure = method;
            }
            else if (obj.ClassDef.PrePass.Properties.TryGetValue(name, out var property))
            {
                procedure = kind switch { 2 => property.Get, 4 => property.Let, 8 => property.Set, _ => null };
                if (procedure is null || procedure.IsPrivate)
                    throw new NotImplementedException("CallByName property accessor is not implemented (#4520)");
            }
            else if (obj.ClassDef.PrePass.PublicVariables.Contains(name))
            {
                NoteMemberAccess(obj, name);
                var (_, field) = await ReadVariable(obj.InstanceEnv, name);
                if (kind is 2 or 3 && memberArgs.Count == 0 && VbNumeric.IsDeclarableScalar(field.Type))
                    return field;
                if (kind == 4 && memberArgs.Count == 1 && VbNumeric.IsDeclarableScalar(field.Type))
                {
                    ExecutionContext.TryUpdateVariable(obj.InstanceEnv, name, CopyForStore(memberArgs[0].Value));
                    await ReleaseRef(field);
                    return new Vb6Value(Vb6Value.ValueType.EmptyVariant);
                }
                throw new NotImplementedException("CallByName field assignment/indexing is not implemented (#4520)");
            }
            else if (obj.ClassDef.PrePass.PrivateVariables.Contains(name))
                throw new NotImplementedException("CallByName private members are not implemented (#4520)");
            else
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

            for (var i = 0; i < procedure.Parameters.Count; i++)
            {
                var parameter = procedure.Parameters[i];
                if (parameter.ParamArray) break;
                if (i >= memberArgs.Count && !parameter.Optional)
                    throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
            }
            if (memberArgs.Count > procedure.Parameters.Count && !procedure.Parameters.Any(p => p.ParamArray))
                throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
            return await RunProcedure(obj.ClassDef, procedure, memberArgs, obj.InstanceEnv, receiver, callerFrames)
                ?? new Vb6Value(Vb6Value.ValueType.EmptyVariant);
        }
        if (receiver.Value is Control || receiver.Value is VbObject { ClassDef.Kind: InterpreterModuleKind.Form })
            return await CallControlByName(receiver, name, kind, callArgs, callerFrames);
        if (receiver.Value is VbCollection collection)
        {
            if (kind == 2 && (name.Equals("Count", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Item", StringComparison.OrdinalIgnoreCase)))
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            if (kind == 4 && name.Equals("Item", StringComparison.OrdinalIgnoreCase))
                throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
            if (kind == 1)
            {
                var add = name.Equals("Add", StringComparison.OrdinalIgnoreCase);
                var item = name.Equals("Item", StringComparison.OrdinalIgnoreCase);
                var count = name.Equals("Count", StringComparison.OrdinalIgnoreCase);
                var remove = name.Equals("Remove", StringComparison.OrdinalIgnoreCase);
                if (!add && !item && !count && !remove)
                    throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
                var values = new List<Vb6Value>();
                for (var i = 3; i < callArgs.Count; i++) values.Add(callArgs[i].Value);
                // CallByName's own arity error is 450, not late-bound Count's 451 (#4688).
                if ((count && values.Count != 0) || (item && values.Count == 0))
                    throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
                // Only the measured argument shapes in this slice; don't inherit Collection's direct-call arity errors.
                if ((add && values.Count is >= 1 and <= 3)
                    || ((item || remove) && values.Count == 1) || (count && values.Count == 0))
                    return await collection.CallAsync(name, values) ?? new Vb6Value(Vb6Value.ValueType.EmptyVariant);
            }
        }
        if (receiver.Value is VbErr err && name.Equals("Number", StringComparison.OrdinalIgnoreCase)
            && kind == 2 && callArgs.Count == 3)
            return new Vb6Value(err.Number);
        throw new NotImplementedException("CallByName for this member/call type is not implemented (#4520)");
    }
}
