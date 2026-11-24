using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Interpreter;

// Shared by parse-tree access and CallByName (#4520).
internal static class ControlMembers
{
    // Part of #790: oracle/a-control-where-a-value-is-wanted-is-its-default-member.md,
    // "Each intrinsic control's default member". Both Let paths use the named property's dispatch.
    internal static string? DefaultMember(Vb6Value value) => value.Value switch
    {
        VBTextBox or VBListBox or VBComboBox => "Text",
        VBLabel or VBFrame => "Caption",
        VBCommandButton or VBCheckBox or VBOptionButton or VBScrollBar => "Value",
        VBShape => "Shape",
        _ => null,
    };

    internal static ValueTask<Vb6Value> ReadDefaultValue(Vb6Value value, string expression, bool intrinsicOnly = true)
    {
        if (DefaultMember(value) is { } member)
            return new(Read(value, member, null));
        if (value.Type == Vb6Value.ValueType.Control && value.Value is Control control
            && (!intrinsicOnly || ControlObjectType.IsIntrinsic(VB6BuiltIns.ControlTypeName(control))))
            throw new NotImplementedException($"`{expression}` reads a default member, which is not implemented (#790). "
                + "Name the property, or use Set to keep the reference.");
        return new(value);
    }

    // Measured late-bound list receivers, including Collection items and function results (#5135).
    // VB.Control declarations also reach dispatch with the bare Control view.
    private static bool IsLateBoundListReference(Vb6Value variable) =>
        variable.HeldAsVariant
            || string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase)
            || string.Equals(variable.DeclaredAs, "Control", StringComparison.OrdinalIgnoreCase);

    internal static async Task<Vb6Value> Read(Vb6Value variable, string memberIdentifier, IReadOnlyList<Vb6Value>? args,
        string? propertySpelling = null, Func<Task<List<Vb6Value>>>? evaluateArgs = null,
        Func<Task<List<Vb6Value>>>? evaluateMemberArgs = null, bool classifyMissing = false)
    {
        var memberName = memberIdentifier;
        var hasArgs = args is not null || evaluateArgs is not null;
        // The parse-tree path evaluates indexes only once it reaches the member that consumes them.
        async Task<IReadOnlyList<Vb6Value>> Arguments()
        {
            if (args is not null) return args;
            return evaluateArgs is not null ? await evaluateArgs() : [];
        }
        if (variable.Value is VBMSFlexGrid grid)
        {
            VBProps.RefuseIfRemoved(grid);
            return FlexGridMembers.Read(grid, memberIdentifier, await Arguments());
        }

        if (variable.Value is VBTreeView capacityTree && memberIdentifier.Equals("GetVisibleCount", StringComparison.OrdinalIgnoreCase))
        {
            if ((await Arguments()).Count != 0) throw new NotImplementedException();
            return new Vb6Value(capacityTree.GetVisibleCount());
        }

        if (variable.Value is VBTreeView treeView && memberIdentifier.Equals("Nodes", StringComparison.OrdinalIgnoreCase)
            && hasArgs)
            return treeView.Nodes.Default(await Arguments());

        if (variable.Value is VBSSTab tab && SSTabMembers.IsIndexed(memberIdentifier)
            && hasArgs)
            return SSTabMembers.ReadIndexed(tab, memberIdentifier, await Arguments());

        if (variable.Value is ItemsControl searchList && ListMembers.IsList(searchList) && ListMembers.IsExtension(searchList, memberIdentifier))
            return ListMembers.CallExtension(searchList, memberIdentifier, await Arguments());

        // A list's List(i) and ItemData(i), including a late-bound empty argument list (#5135).
        if (variable.Value is Control listControl && ListMembers.IsList(listControl))
        {
            var itemData = memberIdentifier.Equals("ItemData", StringComparison.OrdinalIgnoreCase);
            var item = memberIdentifier.Equals("List", StringComparison.OrdinalIgnoreCase);
            if (itemData || item)
            {
                var indexes = await Arguments();
                if (IsLateBoundListReference(variable) && indexes.Count == 0)
                {
                    VBProps.RefuseIfRemoved(listControl);
                    throw new VBRunTimeException(VBStandardError.NeedPropertyArrayIndex);
                }
                if (hasArgs)
                    return itemData ? ListMembers.ReadItemData(listControl, indexes) : ListMembers.ReadItem(listControl, indexes);
            }
            if (hasArgs && listControl is VBListBox listBox && memberIdentifier.Equals("Selected", StringComparison.OrdinalIgnoreCase))
                return ListMembers.ReadSelected(listBox, await Arguments());
        }

        // Preserve the parser's existing scalar-property lookup (including any written parentheses).
        memberIdentifier = propertySpelling ?? memberIdentifier;
        var throughObject = variable.HeldAsVariant || string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase);
        if (variable.Value is VBCommonDialog dialog && dialog.TryGetProperty(memberIdentifier, out var dialogValue, throughObject))
            return dialogValue;

        CommonDialogMembers.Check(variable, memberIdentifier);

        if (variable.Value is VBUserControl.RootObject root)
        {
            if (throughObject && FormMembers.IsCoordinateMember(memberName))
                throw new NotImplementedException($"UserControl.{memberIdentifier} through an Object variable is not implemented (#4693)");
            if (RuntimeScale.IsConversion(memberName))
                return RuntimeScale.ConvertUnits(root.Owner, memberName,
                    evaluateMemberArgs is not null ? await evaluateMemberArgs() : await Arguments());
        }

        // A CSharp property bag (e.g. Err.Number) resolves its own properties by name.
        if (variable.Value is ICSharpPropertyBag propertyBag && propertyBag.TryGetProperty(memberIdentifier, out var bagValue))
            return bagValue;

        if (variable.Type != Vb6Value.ValueType.Control ||
            variable.Value is not Control control)
            throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
        // Every member of a control Controls.Remove took out is 735, its Name included (measured, #1556).
        VBProps.RefuseIfRemoved(control);
        if (throughObject && (control is VBTextBox && RuntimeScale.IsConversion(memberName)
            || control is VBUserControl && FormMembers.IsCoordinateMember(memberName)))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        if (control is VBPictureBox && RuntimeDrawing.IsMethod(memberName))
        {
            if (memberName.Equals("Refresh", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException("Refresh used as a value is not implemented (#991)");
            if (memberName.Equals("Cls", StringComparison.OrdinalIgnoreCase))
                throw RuntimeDrawing.Refused("Cls used as a value");
            return RuntimeDrawing.Call(control, memberName, await Arguments());
        }
        if (control is VBPictureBox && RuntimeScale.IsConversion(memberName))
            return RuntimeScale.ConvertUnits(control, memberName,
                evaluateMemberArgs is not null ? await evaluateMemberArgs() : await Arguments(), throughObject);

        if (control is VBShape or VBLine && memberIdentifier.Equals("MousePointer", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

        if (control is VBTimer && TimerMembers.TryGet(control, memberIdentifier, out var timerValue,
                throughObject: string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase)))
            return timerValue;

        AvaloniaInterop.UserControlMembers.Check(variable, memberIdentifier, reading: true);
        if (AvaloniaInterop.UserControlMembers.TryGet(control, memberIdentifier, out var userControlValue))
            return userControlValue;

        if (AvaloniaInterop.UserControlContainers.TryGet(control, memberIdentifier, out var containerValue))
            return containerValue;

        if (AvaloniaInterop.RichTextBoxMembers.TryGet(control, memberIdentifier, out var richTextValue))
            return richTextValue;

        // An in-box control's own members, and the extender's it lacks, by its member table (#1082).
        if (AvaloniaInterop.SliderMembers.TryGet(control, memberIdentifier, out var sliderValue) is { } slider)
            return slider ? sliderValue : throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
        if ((AvaloniaInterop.StatusBarMembers.TryGet(control, memberIdentifier, out var inBoxValue)
             ?? AvaloniaInterop.ProgressBarMembers.TryGet(control, memberIdentifier, out inBoxValue)) is { } inBox)
            return inBox ? inBoxValue : throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);

        if (AvaloniaInterop.SSTabMembers.TryGet(control, memberIdentifier, out var tabValue))
            return tabValue;

        if (RuntimeDrawing.TryGet(control, memberIdentifier, out var drawingMember))
            return drawingMember;
        // An Image's Opacity, in a run that takes RDCore's additions (#4967).
        if (RDCoreMembers.TryGet(control, memberIdentifier, out var rdcoreMember))
            return rdcoreMember;
        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            // Visible before the form is shown, #4520's TextBox among them, is VBVisibility's: windowed controls only (#4829).
            if (AvaloniaInteroperability.TryGet(control, prop, out var value))
                return value;
        }
        if (control is not VBUserControl && RuntimeScale.TryGet(control, memberIdentifier, out var scaleMember))
            return scaleMember;
        // FontName, which is no property of its own here but the name of the control's font (#1141).
        if (FontMembers.TryGet(control, memberIdentifier, out var fontMember))
            return fontMember;
        // A TextBox's SelStart, SelLength and SelText, which exist only at run time (#1150).
        if (TextBoxMembers.TryGet(control, memberIdentifier, out var textBoxMember))
            return textBoxMember;
        // A list's ListCount (#821), and a ListBox's SelCount and Text (#1395).
        if (ListMembers.TryGet(control, memberIdentifier, out var listMember, IsLateBoundListReference(variable)))
            return listMember;
        if (ContainerMembers.TryGet(control, memberIdentifier, out var containerMember))
            return containerMember;
        // Any control's Name, and an array element's Index (#1431).
        if (IdentityMembers.TryGet(control, memberIdentifier, out var identityMember))
            return identityMember;

        if (classifyMissing) VB6ControlMembers.RefuseUnresolved(variable, memberName);
        throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
    }

    internal static bool TryWrite(Vb6Value variable, string memberIdentifier, IReadOnlyList<Vb6Value>? args, Vb6Value value)
    {
        var control = (Control)variable.Value!;
        var throughObject = variable.HeldAsVariant || string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase);
        if (control is VBCommonDialog dialog && dialog.TrySetProperty(memberIdentifier, value, throughObject)) return true;
        // FlatScrollBar's typed stores report Null differently through Object (#1097, oracle PR #5507).
        if (control is VBFlatScrollBar && throughObject && value.IsNull
            && memberIdentifier.ToLowerInvariant() is "value" or "_value" or "[_value]" or "min" or "max" or "smallchange" or "largechange" or "enabled")
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (control is ICSharpPropertyBag bag && bag.TrySetProperty(memberIdentifier, value)) return true;
        VBProps.RefuseIfRemoved(control);
        if (throughObject && control is VBUserControl && FormMembers.IsCoordinateMember(memberIdentifier))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        if (ListMembers.IsList(control) && args is not null)
        {
            if (memberIdentifier.Equals("ItemData", StringComparison.OrdinalIgnoreCase))
            {
                ListMembers.WriteItemData(control, args, value, IsLateBoundListReference(variable));
                return true;
            }
            if (memberIdentifier.Equals("List", StringComparison.OrdinalIgnoreCase))
            {
                ListMembers.WriteItem(control, args, value, IsLateBoundListReference(variable));
                return true;
            }
            if (control is VBListBox list && memberIdentifier.Equals("Selected", StringComparison.OrdinalIgnoreCase))
            {
                ListMembers.WriteSelected(list, args, value);
                return true;
            }
        }
        if (control is VBTimer)
            TimerMembers.CheckWrite(memberIdentifier);

        UserControlMembers.Check(variable, memberIdentifier);
        if (UserControlMembers.TrySet(control, memberIdentifier, value))
            return true;

        if (RichTextBoxMembers.TrySet(control, memberIdentifier, value))
            return true;

        // An in-box control's own members, and the extender's it lacks, by its member table (#1082).
        if (SliderMembers.TrySet(control, memberIdentifier, value, throughObject) is { } slider)
        {
            if (!slider) throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
            return true;
        }
        if ((StatusBarMembers.TrySet(control, memberIdentifier, value)
             ?? ProgressBarMembers.TrySet(control, memberIdentifier, value)) is { } inBox)
        {
            if (!inBox) return false;
            return true;
        }

        if (SSTabMembers.TrySet(control, memberIdentifier, value))
            return true;

        if (RuntimeDrawing.TrySet(control, memberIdentifier, value))
            return true;
        if (RDCoreMembers.TrySet(control, memberIdentifier, value))
            return true;
        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            if (AvaloniaInteroperability.TrySet(control, prop, value))
                return true;
        }
        if (control is not VBUserControl && RuntimeScale.TrySet(control, memberIdentifier, value))
            return true;
        // FontName sets the control's font by name (#1141).
        if (FontMembers.TrySet(control, memberIdentifier, value))
            return true;
        // A TextBox's SelStart, SelLength and SelText (#1150).
        if (TextBoxMembers.TrySet(control, memberIdentifier, value))
            return true;
        // A list's ListCount, which is read-only (#821), and a ListBox's Text (#1395).
        if (ListMembers.TrySet(control, memberIdentifier, value, IsLateBoundListReference(variable)))
            return true;
        // Any control's Name and Index, which are read-only at run time (#1431).
        if (IdentityMembers.TrySet(control, memberIdentifier))
            return true;

        return false;
    }
}
