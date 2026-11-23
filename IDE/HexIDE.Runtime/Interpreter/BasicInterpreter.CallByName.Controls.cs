using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

public partial class BasicInterpreter
{
    // SP6 native and p-code measurements: #4520, oracle PR #4759. Dispatch shares the parse-tree helpers.
    private async Task<Vb6Value> CallControlByName(Vb6Value receiver, string name, long kind, IReadOnlyList<CallArg> callArgs,
        Stack<List<Vb6Value>>? callerFrames)
    {
        if (kind is not (1 or 2 or 3 or 4 or 8))
            throw new NotImplementedException("CallByName call-type combination is not implemented (#4520)");
        var args = new List<Vb6Value>();
        for (var i = 3; i < callArgs.Count; i++) args.Add(callArgs[i].Value);
        if (receiver.Value is VbObject form)
        {
            if (name.Length == 0)
                throw new NotImplementedException("CallByName default member on a form is not implemented (#4520)");
            var (found, field) = await ReadVariable(form.InstanceEnv, name);
            if (found && field.Value is Control namedControl
                && string.Equals(VBProps.GetName(namedControl), name, StringComparison.OrdinalIgnoreCase))
            {
                if (kind is not (2 or 3)) throw CallByNameMissingMember();
                CallByNameArity(args.Count, 0, 0);
                return field;
            }
            if (found || form.ClassDef.PrePass.Procedures.ContainsKey(name) || form.ClassDef.PrePass.Properties.ContainsKey(name))
                throw new NotImplementedException("CallByName on a form's user-defined member is not implemented (#4520)");
            if (!FormMembers.IsMember(name, out var member)) throw CallByNameMissingMember();
            await Loading((Form: form, Member: name), static s => s.Form.BuiltInMembersFor(s.Member));
            if (member.IsMethod)
            {
                if (kind != 1) throw CallByNameMissingMember();
                CallByNameArity(args.Count, member.RequiredArgs, member.MaxArgs);
                if (name.Equals("Refresh", StringComparison.OrdinalIgnoreCase))
                    throw new NotImplementedException("CallByName Refresh result is unmeasured and not implemented (#991)");
                return FormMembers.Call(form, name, args);
            }
            if (kind is 2 or 3)
            {
                CallByNameArity(args.Count, 0, 0);
                return FormMembers.Read(form, name);
            }
            if (kind == 1 || kind == 8 && !member.CanSet) throw CallByNameMissingMember();
            CallByNameArity(args.Count, 1, 1);
            if (kind == 8)
            {
                if (!name.Equals("Font", StringComparison.OrdinalIgnoreCase) || args[0].Value is not ControlFont)
                    throw new NotImplementedException("CallByName Set on this object property is not implemented (#4520)");
                ControlFont.Set(form.BuiltInMembersFor(name)!, args[0]);
            }
            else
                FormMembers.Write(form, name, member, kind == 4
                    ? ObjectAsValue.Scalar(await ResolveDefaultMember(args[0], callerFrames: callerFrames), null) : args[0]);
            return new Vb6Value(Vb6Value.ValueType.EmptyVariant);
        }

        var control = (Control)receiver.Value!;
        if (control is not (VBTextBox or VBListBox or VBComboBox or VBPictureBox or VBCommonDialog))
            throw new NotImplementedException("CallByName on this control is not implemented (#4520)");
        receiver = new Vb6Value(control).ViewedAs("Object");
        if (name.Length == 0)
        {
            if (control is VBTextBox && kind == 2) name = "Text";
            else throw new NotImplementedException("CallByName default member on this control is not implemented (#4520)");
        }
        var method = AvaloniaMethodsInteroperability.CallByNameArity(control, name);
        if (method is not null)
        {
            if (kind != 1) throw CallByNameMissingMember();
            CallByNameArity(args.Count, method.Value.Min, method.Value.Max);
            if (name.Equals("Refresh", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException("CallByName Refresh result is unmeasured and not implemented (#991)");
            return await control.CallAsync(name, args);
        }
        if (kind == 1) throw CallByNameMissingMember();
        var indexed = ListMembers.IsList(control) && (name.Equals("List", StringComparison.OrdinalIgnoreCase)
            || control is VBListBox && name.Equals("Selected", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (kind is 2 or 3)
            {
                if (indexed && args.Count == 0)
                    throw new VBRunTimeException(VBStandardError.NeedPropertyArrayIndex);
                CallByNameArity(args.Count, indexed ? 1 : 0, indexed ? 1 : 0);
                return await ControlMembers.Read(receiver, name, indexed ? args : null);
            }
            if (kind == 8 && !name.Equals("Font", StringComparison.OrdinalIgnoreCase))
            {
                var property = await ControlMembers.Read(receiver, name, indexed ? args : null);
                if (property.Type == Vb6Value.ValueType.Control || property.Type == Vb6Value.ValueType.Object
                    || property.Type == Vb6Value.ValueType.CSharpProxyObject)
                    throw new NotImplementedException("CallByName Set on this object property is not implemented (#4520)");
                throw CallByNameMissingMember();
            }
            CallByNameArity(args.Count, indexed ? 2 : 1, indexed ? 2 : 1);
            var value = kind == 4
                ? ObjectAsValue.Scalar(await ResolveDefaultMember(args[^1], callerFrames: callerFrames), null) : args[^1];
            args.RemoveAt(args.Count - 1);
            if (kind == 8)
            {
                if (value.Value is not ControlFont)
                    throw new NotImplementedException("CallByName Font Set conversion is not implemented (#4520)");
                ControlFont.Set(control, value);
            }
            else if (ListMembers.IsList(control) && name.Equals("ListCount", StringComparison.OrdinalIgnoreCase))
                throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            else if (!ControlMembers.TryWrite(receiver, name, indexed ? args : null, value))
                throw CallByNameMissingMember();
            return new Vb6Value(Vb6Value.ValueType.EmptyVariant);
        }
        catch (VBMethodOrDataMemberNotFoundException)
        {
            throw CallByNameMissingMember();
        }
    }

    private static VBRunTimeException CallByNameMissingMember() =>
        new(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

    private static void CallByNameArity(int count, int min, int max)
    {
        if (count < min || count > max)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
    }
}
