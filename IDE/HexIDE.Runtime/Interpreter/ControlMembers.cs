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
    internal static async Task<Vb6Value> Read(Vb6Value variable, string memberIdentifier, IReadOnlyList<Vb6Value>? args,
        string? propertySpelling = null, Func<Task<List<Vb6Value>>>? evaluateArgs = null,
        Func<Task<List<Vb6Value>>>? evaluateMemberArgs = null)
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

        // A list's List(i) and a ListBox's Selected(i), the control members here that take an index (#821, #1395).
        if (variable.Value is Control listControl && ListMembers.IsList(listControl) && hasArgs)
        {
            if (memberIdentifier.Equals("List", StringComparison.OrdinalIgnoreCase))
                return ListMembers.ReadItem(listControl, await Arguments());
            if (listControl is VBListBox listBox && memberIdentifier.Equals("Selected", StringComparison.OrdinalIgnoreCase))
                return ListMembers.ReadSelected(listBox, await Arguments());
        }

        // Preserve the parser's existing scalar-property lookup (including any written parentheses).
        memberIdentifier = propertySpelling ?? memberIdentifier;
        var throughObject = variable.HeldAsVariant || string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase);
        if (variable.Value is VBCommonDialog dialog && dialog.TryGetProperty(memberIdentifier, out var dialogValue, throughObject))
            return dialogValue;

        CommonDialogMembers.Check(variable, memberIdentifier);

        // A CSharp property bag (e.g. Err.Number) resolves its own properties by name.
        if (variable.Value is ICSharpPropertyBag propertyBag && propertyBag.TryGetProperty(memberIdentifier, out var bagValue))
            return bagValue;

        if (variable.Type != Vb6Value.ValueType.Control ||
            variable.Value is not Control control)
            throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
        // Every member of a control Controls.Remove took out is 735, its Name included (measured, #1556).
        VBProps.RefuseIfRemoved(control);
        if (control is VBPictureBox && RuntimeDrawing.IsMethod(memberName))
        {
            if (memberName.Equals("Cls", StringComparison.OrdinalIgnoreCase))
                throw RuntimeDrawing.Refused("Cls used as a value");
            return RuntimeDrawing.Call(control, memberName, await Arguments());
        }
        if (control is VBPictureBox && RuntimeScale.IsConversion(memberName))
            return RuntimeScale.ConvertUnits(control, memberName,
                evaluateMemberArgs is not null ? await evaluateMemberArgs() : await Arguments());

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
        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            if (AvaloniaInteroperability.TryGet(control, prop, out var value))
            {
                // #4520 measured Visible during Form_Load, before the containing form is shown.
                if (ReferenceEquals(prop, VBProperties.VisibleProperty)
                    && FormControls.TryOf(control, out var owner) && owner.Form?.FormWindow is { } window
                    && FormRoot.View(window) is { IsShown: false })
                    return new Vb6Value(false);
                return value;
            }
        }
        if (RuntimeScale.TryGet(control, memberIdentifier, out var scaleMember))
            return scaleMember;
        // FontName, which is no property of its own here but the name of the control's font (#1141).
        if (FontMembers.TryGet(control, memberIdentifier, out var fontMember))
            return fontMember;
        // A TextBox's SelStart, SelLength and SelText, which exist only at run time (#1150).
        if (TextBoxMembers.TryGet(control, memberIdentifier, out var textBoxMember))
            return textBoxMember;
        // A list's ListCount (#821), and a ListBox's SelCount and Text (#1395).
        if (ListMembers.TryGet(control, memberIdentifier, out var listMember))
            return listMember;
        if (ContainerMembers.TryGet(control, memberIdentifier, out var containerMember))
            return containerMember;
        // Any control's Name, and an array element's Index (#1431).
        if (IdentityMembers.TryGet(control, memberIdentifier, out var identityMember))
            return identityMember;

        throw new VBMethodOrDataMemberNotFoundException(memberIdentifier, variable.Type);
    }

    internal static bool TryWrite(Vb6Value variable, string memberIdentifier, IReadOnlyList<Vb6Value>? args, Vb6Value value)
    {
        var control = (Control)variable.Value!;
        var throughObject = variable.HeldAsVariant || string.Equals(variable.DeclaredAs, "Object", StringComparison.OrdinalIgnoreCase);
        if (control is VBCommonDialog dialog && dialog.TrySetProperty(memberIdentifier, value, throughObject)) return true;
        if (control is ICSharpPropertyBag bag && bag.TrySetProperty(memberIdentifier, value)) return true;
        VBProps.RefuseIfRemoved(control);
        if (ListMembers.IsList(control) && args is not null)
        {
            if (memberIdentifier.Equals("List", StringComparison.OrdinalIgnoreCase))
            {
                ListMembers.WriteItem(control, args, value);
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
        if (SliderMembers.TrySet(control, memberIdentifier, value) is { } slider)
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
        var props = VBProperties.PropertiesByName.GetValueOrDefault(memberIdentifier, []);

        foreach (var prop in props)
        {
            if (AvaloniaInteroperability.TrySet(control, prop, value))
                return true;
        }
        if (RuntimeScale.TrySet(control, memberIdentifier, value))
            return true;
        // FontName sets the control's font by name (#1141).
        if (FontMembers.TrySet(control, memberIdentifier, value))
            return true;
        // A TextBox's SelStart, SelLength and SelText (#1150).
        if (TextBoxMembers.TrySet(control, memberIdentifier, value))
            return true;
        // A list's ListCount, which is read-only (#821), and a ListBox's Text (#1395).
        if (ListMembers.TrySet(control, memberIdentifier, value))
            return true;
        // Any control's Name and Index, which are read-only at run time (#1431).
        if (IdentityMembers.TrySet(control, memberIdentifier))
            return true;

        return false;
    }
}
