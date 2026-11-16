using System.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.AvaloniaInterop;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Extension contract, including Long results: https://github.com/hexide-io/HexIDE/issues/1116.
public class ListSearchExtensionTests
{
    private readonly List<Vb6Value> debug = [];
    private readonly ModuleExecutionContext context = new();
    private readonly ExecutionEnvironment rootEnv = new();

    private void AssertDebugLog(Vb6Value[] expected) => debug.Should().Equal(expected);

    private sealed class CaptureLib(List<Vb6Value> output) : IBasicStandardLibrary
    {
        public void DebugPrint(Vb6Value value) => output.Add(value);
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Search_wraps_ignores_case_and_returns_Long_without_changing_selection(int style)
    {
        var list = MakeList(style);
        list.Items.Add("Alpha");
        list.Items.Add("alphabet");
        list.Items.Add("Beta");
        context.AllocVariable(rootEnv, "lst", new Vb6Value(list));
        await new BasicInterpreter(new CaptureLib(debug), context, rootEnv, """
            Debug.Print lst.FindString("ALP"), lst.FindString("alp", 0)
            Debug.Print lst.FindString("ALP", 1), lst.FindStringExact("ALPHA", 0)
            Debug.Print lst.FindStringExact("alp"), lst.FindString("missing")
            Debug.Print TypeName(lst.FindString("alp"))
            """).Execute();
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L), new Vb6Value(0L), new Vb6Value(0L),
            new Vb6Value(-1L), new Vb6Value(-1L), new Vb6Value("Long")]);
        list.SelectedIndex.Should().Be(-1);
    }

    [AvaloniaTheory]
    [InlineData(-1, "FindString", "", 449)]
    [InlineData(0, "FindStringExact", "", 449)]
    [InlineData(1, "FindString", "\"x\", \"bad\"", 13)]
    [InlineData(2, "FindStringExact", "\"x\", \"bad\"", 13)]
    public async Task Search_checks_required_argument_and_index_conversion(int style, string method, string args, long error)
    {
        context.AllocVariable(rootEnv, "lst", new Vb6Value(MakeList(style)));
        await new BasicInterpreter(new CaptureLib(debug), context, rootEnv,
            $"On Error Resume Next\nlst.{method} {args}\nDebug.Print Err.Number").Execute();
        AssertDebugLog([new Vb6Value(error)]);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // A serialization invariant of the extension contract, not a new VB6 measurement.
    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Searching_a_saved_control_does_not_change_its_form(int style)
    {
        var type = style < 0 ? "ListBox" : "ComboBox";
        var frm = $"VERSION 5.00\nBegin VB.Form Form1\n   Begin VB.{type} lst\n"
            + (style >= 0 ? $"      Style = {style}\n" : "")
            + "   End\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), frm, new NullSink())!;
        var serializer = new FormSerializer();
        var before = serializer.Serialize(form, "Form1.frm");
        var component = form.Components.Single(c => c.BaseClass is ListBoxComponentClass or ComboBoxComponentClass);
        var control = ((ComponentBaseClass)component.BaseClass).Instantiate(component);
        control.Call("AddItem", [new Vb6Value("Alpha")]);
        control.Call("FindString", [new Vb6Value("al")]).Should().Be(new Vb6Value(0L));
        control.Call("FindStringExact", [new Vb6Value("ALPHA")]).Should().Be(new Vb6Value(0L));
        serializer.Serialize(form, "Form1.frm").Should().BeEquivalentTo(before);
    }

    private static Avalonia.Controls.Primitives.SelectingItemsControl MakeList(int style) =>
        style < 0 ? new VBListBox() : new VBComboBox((HexIDE.Runtime.BuiltinTypes.VBComboStyle)style);
}
