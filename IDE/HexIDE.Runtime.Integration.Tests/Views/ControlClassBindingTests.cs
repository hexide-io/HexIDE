using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #2265 and oracle PR #3956: "More bare names".
public class ControlClassBindingTests
{
    public static IEnumerable<object[]> Classes()
    {
        var rows = new (string Control, string Name, string Expression, string TypeName)[]
        {
            ("ListView", "ListView", "c", "ListView"),
            ("ListView", "ListItems", "c.ListItems", "IListItems"),
            ("ListView", "ListItem", "c.ListItems.Add", "IListItem"),
            ("ListView", "ColumnHeaders", "c.ColumnHeaders", "IColumnHeaders"),
            ("ListView", "ColumnHeader", "c.ColumnHeaders.Add", "IColumnHeader"),
            ("ListView", "ListSubItems", "c.ListItems.Add.ListSubItems", "IListSubItems"),
            ("ListView", "ListSubItem", "c.ListItems.Add.ListSubItems.Add", "IListSubItem"),
            ("TreeView", "TreeView", "c", "TreeView"),
            ("TreeView", "Nodes", "c.Nodes", "INodes"),
            ("TreeView", "Node", "c.Nodes.Add", "INode"),
            ("ProgressBar", "ProgressBar", "c", "ProgressBar"),
            ("StatusBar", "StatusBar", "c", "StatusBar"),
            ("StatusBar", "Panels", "c.Panels", "IPanels"),
            ("StatusBar", "Panel", "c.Panels.Add", "IPanel"),
            ("Toolbar", "Toolbar", "c", "Toolbar"),
            ("Toolbar", "Buttons", "c.Buttons", "IButtons"),
            ("Toolbar", "Button", "c.Buttons.Add", "IButton"),
            ("Toolbar", "ButtonMenus", "c.Buttons.Add.ButtonMenus", "IButtonMenus"),
            ("Toolbar", "ButtonMenu", "c.Buttons.Add.ButtonMenus.Add", "IButtonMenu"),
            ("TabStrip", "TabStrip", "c", "TabStrip"),
            ("TabStrip", "Tabs", "c.Tabs", "ITabs"),
            ("TabStrip", "Tab", "c.Tabs(1)", "ITab"),
        };
        foreach (var row in rows)
            foreach (var qualified in new[] { false, true })
                if (qualified || row.Name != "Tab")
                    yield return [row.Control, row.Name, row.Expression, row.TypeName, qualified];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Classes))]
    public async Task Typed_slots_and_TypeOf_keep_the_object_class(string control, string name,
        string expression, string typeName, bool qualified)
    {
        var form = $"VERSION 5.00\nBegin VB.Form Form1\n Begin MSComctlLib.{control} c\n End\n Begin VB.TextBox wrong\n End\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var target = qualified ? "MSComctlLib." + name : name;
        var values = new List<Vb6Value>();
        var interpreter = new BasicInterpreter(new Capture(values), ctx, env, $"""
            Dim x As {target}
            Debug.Print TypeName(x)
            Dim original As Object
            Set original = {expression}
            Set x = original
            Debug.Print TypeName(x), x Is original
            Debug.Print TypeOf x Is {target}, TypeOf x Is MSComctlLib.{name}
            Debug.Print TypeOf x Is ProgressBar
            On Error Resume Next
            Set x = wrong
            Debug.Print Err.Number, x Is original
            """);
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [ControlLibraries.MSComctlLib]));
        await interpreter.Execute();
        values.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal(
            "Nothing", typeName, "True", "True", "True", (name == "ProgressBar").ToString(), "13", "True");
    }

    // #2265's last slice, measured native and p-code in oracle PR #4023.
    [AvaloniaTheory]
    [InlineData("MSComCtl2", "DTPicker")]
    [InlineData("MSComCtl2", "MonthView")]
    [InlineData("MSComCtl2", "UpDown")]
    [InlineData("MSComCtl2", "Animation")]
    [InlineData("MSComCtl2", "FlatScrollBar")]
    [InlineData("TabDlg", "SSTab")]
    [InlineData("MSMask", "MaskEdBox")]
    [InlineData("RichTextLib", "RichTextBox")]
    [InlineData("MSComDlg", "CommonDialog")]
    [InlineData("MSComctlLib", "ImageCombo")]
    [InlineData("MSComctlLib", "ComboItems")]
    [InlineData("MSComctlLib", "ComboItem")]
    public async Task Remaining_measured_classes_keep_their_type(string libraryName, string name)
    {
        var library = ControlLibraries.All.Single(l => l.Name == libraryName);
        var control = name.StartsWith("Combo", StringComparison.Ordinal) ? "ImageCombo" : name;
        ModuleExecutionContext ctx;
        ExecutionEnvironment env;
        if (control is "ImageCombo" or "CommonDialog")
        {
            // ImageCombo still refuses at F5 (#1092); exercise its existing owner and collection model.
            // CommonDialog needs a host; no dialog is opened by this test.
            ctx = new ModuleExecutionContext();
            env = new ExecutionEnvironment();
            ctx.AllocVariable(env, "c", new Vb6Value(control == "ImageCombo"
                ? (Avalonia.Controls.Control)new HexIDE.Runtime.BuiltinControls.VBImageCombo(new EventPump(), () => Task.CompletedTask)
                : new HexIDE.Runtime.BuiltinControls.VBCommonDialog(new Capture([]))));
            ctx.AllocVariable(env, "wrong", new Vb6Value(new HexIDE.Runtime.BuiltinControls.VBTextBox()));
        }
        else
        {
            var form = $"VERSION 5.00\nBegin VB.Form Form1\n Begin {libraryName}.{control} c\n End\n Begin VB.TextBox wrong\n End\nEnd\nAttribute VB_Name = \"Form1\"\n";
            (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        }
        var expression = name switch { "ComboItems" => "c.ComboItems", "ComboItem" => "c.ComboItems.Add(, , \"Alpha\")", _ => "c" };
        var values = new List<Vb6Value>();
        var interpreter = new BasicInterpreter(new Capture(values), ctx, env, $"""
            Dim x As {name}, q As {libraryName}.{name}
            Debug.Print TypeName(x), TypeName(q)
            Dim original As Object
            Set original = {expression}
            Set x = original
            Set q = original
            Debug.Print TypeName(x), TypeName(q), x Is original, q Is original
            Debug.Print TypeOf x Is {name}, TypeOf x Is {libraryName}.{name}
            Debug.Print TypeOf wrong Is {name}
            On Error Resume Next
            Set x = wrong
            Debug.Print Err.Number, x Is original
            Err.Clear
            Set q = wrong
            Debug.Print Err.Number, q Is original
            """);
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [library]));
        await interpreter.Execute();
        var typeName = name.StartsWith("Combo", StringComparison.Ordinal) ? "I" + name : name;
        values.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal(
            "Nothing", "Nothing", typeName, typeName, "True", "True", "True", "True", "False", "13", "True", "13", "True");
    }

    [AvaloniaFact]
    public async Task First_library_Node_refuses_six_but_ButtonMenu_falls_through_to_six()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn("""
            VERSION 5.00
            Begin VB.Form Form1
             Begin MSComctlLib.TreeView tree
             End
             Begin MSComctlLib.Toolbar bar
             End
            End
            Attribute VB_Name = "Form1"
            """);
        var values = new List<Vb6Value>();
        var interpreter = new BasicInterpreter(new Capture(values), ctx, env, """
            Dim n As Node, q As MSComctlLib.Node, m As ButtonMenu
            Set q = tree.Nodes.Add
            Debug.Print TypeOf q Is Node, TypeOf q Is MSComctlLib.Node
            On Error Resume Next
            Set n = q
            Debug.Print Err.Number, TypeName(n)
            Err.Clear
            Set m = bar.Buttons.Add.ButtonMenus.Add
            Debug.Print Err.Number, TypeName(m), TypeOf m Is ButtonMenu
            """);
        // Only ListView/ListItem/ProgressBar were measured with both libraries; these follow the same lookup rule.
        interpreter.SetAppInfo(new AppInfo(ControlLibraries: [ControlLibraries.ComctlLib, ControlLibraries.MSComctlLib]));
        await interpreter.Execute();
        values.Select(v => v.Value?.ToString()).Should().Equal("False", "True", "13", "Nothing", "0", "IButtonMenu", "True");
    }

    private sealed class Capture(List<Vb6Value> values) : IBasicStandardLibrary
    {
        public void DebugPrint(Vb6Value value) => values.Add(value);
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Task.FromResult(default(MessageBoxResult));
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
    }
}
