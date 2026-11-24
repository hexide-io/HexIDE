using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class FlatScrollBarRuntimeTests
{
    private static Vb6Value Input(object value) => value switch
    {
        string text => new Vb6Value(text), double number => new Vb6Value(number),
        int number => new Vb6Value(number), _ => throw new InvalidOperationException(),
    };
    private static Control Create() => FlatScrollBarComponentClass.Instance.Instantiate(
        new ComponentInstance(FlatScrollBarComponentClass.Instance, "Flat1"));
    private static ICSharpPropertyBag Bag(Control control) => (ICSharpPropertyBag)control;
    private static object? Read(Control control, string name)
    {
        Bag(control).TryGetProperty(name, out var value).Should().BeTrue();
        return value.Value;
    }

    [AvaloniaFact]
    public void Defaults_and_Integer_members()
    {
        var bar = Create();
        new[] { "Value", "Min", "Max", "SmallChange", "LargeChange", "Orientation", "Appearance", "Arrows" }
            .Select(n => Convert.ToInt32(Read(bar, n))).Should().Equal(0, 0, 32767, 1, 1, 1, 1, 0);
        Read(bar, "_Value").Should().Be(0);
        Read(bar, "MousePointer").Should().Be(0L);
        bar.Focusable.Should().BeFalse();
        ControlLibraries.MSComCtl2.TryInContainer("OrientationConstants", "cc2OrientationHorizontal", out var horizontal).Should().BeTrue();
        horizontal.Value.Should().Be(1L);
        ControlLibraries.MSComCtl2.TryBare("fsbFlat", out var flat).Should().BeTrue();
        flat.Value.Should().Be(1L);
        ControlLibraries.MSComCtl2.TryBare("cc2RightDown", out var right).Should().BeTrue();
        right.Value.Should().Be(2L);
        Bag(bar).TryGetProperty("Value", out var value).Should().BeTrue();
        value.Type.Should().Be(Vb6Value.ValueType.Integer);
        foreach (var pointer in new[] { 0, 1, 2, 3 })
        {
            Bag(bar).TrySetProperty("MousePointer", new Vb6Value(pointer)).Should().BeTrue();
            Read(bar, "MousePointer").Should().Be((long)pointer);
        }
        Bag(bar).TrySetProperty("Enabled", new Vb6Value(0));
        bar.IsEnabled.Should().BeFalse();
        Bag(bar).TrySetProperty("Enabled", new Vb6Value(2));
        bar.IsEnabled.Should().BeTrue();
        Bag(bar).TrySetProperty("Value", new Vb6Value(12));
        Read(bar, "_Value").Should().Be(12);
        var pointerOutsideProbe = () => Bag(bar).TrySetProperty("MousePointer", new Vb6Value(32768L));
        pointerOutsideProbe.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
    }

    [AvaloniaFact]
    public void Range_clamps_silently_and_Value_changes_synchronously_only_once()
    {
        var bar = Create();
        VBProps.SetName(bar, "Flat1");
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = bar };
        Bag(bar).TrySetProperty("Value", new Vb6Value(60));
        root.Calls.Select(c => c.Name).Should().Equal("Flat1_Change");
        Bag(bar).TrySetProperty("Value", new Vb6Value(60));
        Bag(bar).TrySetProperty("Max", new Vb6Value(50));
        Read(bar, "Value").Should().Be(50);
        Bag(bar).TrySetProperty("Max", new Vb6Value(100));
        Bag(bar).TrySetProperty("Min", new Vb6Value(200));
        Read(bar, "Value").Should().Be(100);
        root.Calls.Should().HaveCount(1);
        Bag(bar).TrySetProperty("Value", new Vb6Value(150));
        Read(bar, "Value").Should().Be(150);
        root.Calls.Should().HaveCount(2);
    }

    [AvaloniaTheory]
    [InlineData("Value", "12", 12)]
    [InlineData("Value", 12.6, 13)]
    [InlineData("Min", -32768, -32768)]
    [InlineData("Max", 32767, 32767)]
    [InlineData("SmallChange", 32767, 32767)]
    [InlineData("LargeChange", 1, 1)]
    [InlineData("Appearance", 2, 2)]
    [InlineData("Arrows", 2, 2)]
    public void Measured_assignments(string name, object input, int expected)
    {
        var bar = Create();
        Bag(bar).TrySetProperty(name, Input(input)).Should().BeTrue();
        Convert.ToInt32(Read(bar, name)).Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("Value", -1, 380)]
    [InlineData("Value", "abc", 13)]
    [InlineData("Min", -32769, 6)]
    [InlineData("Max", 32768, 6)]
    [InlineData("SmallChange", 0, 380)]
    [InlineData("LargeChange", -1, 380)]
    [InlineData("LargeChange", 32768, 6)]
    [InlineData("Orientation", 2, 380)]
    [InlineData("Orientation", -1, 380)]
    [InlineData("Appearance", 3, 380)]
    [InlineData("Arrows", -1, 380)]
    [InlineData("MousePointer", -1, 380)]
    [InlineData("hWnd", 0, 383)]
    public void Measured_errors(string name, object input, int error)
    {
        var bar = Create();
        var act = () => Bag(bar).TrySetProperty(name, Input(input));
        act.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(error);
    }

    [AvaloniaFact]
    public void Orientation_swaps_dimensions_only_when_changed()
    {
        var bar = Create();
        bar.Width = 160;
        bar.Height = 17;
        Bag(bar).TrySetProperty("Orientation", new Vb6Value(0));
        (bar.Width, bar.Height).Should().Be((17, 160));
        Bag(bar).TrySetProperty("Orientation", new Vb6Value(1));
        (bar.Width, bar.Height).Should().Be((160, 17));
    }

    [AvaloniaFact]
    public async Task Interpreter_default_member_and_missing_members()
    {
        var bar = Create();
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar));
        var result = await ContainerRuntimeHarness.Run(context, environment, """
            Flat1 = 12
            Dim v As Variant
            v = Flat1
            Debug.Print v, TypeName(v)
            Debug.Print TypeOf Flat1 Is MSComCtl2.FlatScrollBar
            On Error Resume Next
            Debug.Print Flat1.TabStop
            Debug.Print Err.Number
            Err.Clear
            Dim o As Object
            Set o = Flat1
            o.SetFocus
            Debug.Print Err.Number
            Debug.Print Flat1.ToolTipText
            Flat1.Move 0, 0, 0, 0
            Debug.Print Flat1.Width, Flat1.Height
            """);
        result.Select(v => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().Equal("12", "Integer", "True", "438", "438", "", "30", "30");
        context.AllocVariable(environment, "EarlyFlat", new Vb6Value(bar), declaredClass: "MSComCtl2.FlatScrollBar");
        var early = () => ContainerRuntimeHarness.Run(context, environment, "EarlyFlat.SetFocus");
        await early.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [AvaloniaTheory]
    [InlineData("hWnd")]
    [InlineData("MouseIcon")]
    public void Unimplemented_members_refuse(string member)
    {
        var bar = Create();
        var act = () => Read(bar, member);
        act.Should().Throw<NotImplementedException>().WithMessage("*FlatScrollBar*not implemented*");
    }

    // VB6 SP6 native and p-code: #1097's conversions comment, oracle PR #5507.
    [AvaloniaTheory]
    [InlineData("Value", "Empty", 0, 0)]
    [InlineData("Value", "True", 380, 37)]
    [InlineData("Value", "False", 0, 0)]
    [InlineData("Value", "\"7\"", 0, 7)]
    [InlineData("Value", "CByte(7)", 0, 7)]
    [InlineData("Value", "CCur(7.5)", 0, 8)]
    [InlineData("Value", "CDate(7.5)", 0, 8)]
    [InlineData("Value", "CDec(7.5)", 0, 8)]
    [InlineData("Value", "7.5", 0, 8)]
    [InlineData("Value", "CSng(2.5)", 0, 2)]
    [InlineData("Value", "\"abc\"", 13, 37)]
    [InlineData("Value", "40000&", 6, 37)]
    [InlineData("Value", "Null", 94, 37)]
    [InlineData("Min", "Empty", 0, 0)]
    [InlineData("Min", "True", 0, -1)]
    [InlineData("Min", "False", 0, 0)]
    [InlineData("Min", "\"7\"", 0, 7)]
    [InlineData("Min", "CByte(7)", 0, 7)]
    [InlineData("Min", "CCur(7.5)", 0, 8)]
    [InlineData("Min", "CDate(7.5)", 0, 8)]
    [InlineData("Min", "CDec(7.5)", 0, 8)]
    [InlineData("Min", "7.5", 0, 8)]
    [InlineData("Min", "CSng(2.5)", 0, 2)]
    [InlineData("Min", "\"abc\"", 13, 0)]
    [InlineData("Min", "40000&", 6, 0)]
    [InlineData("Min", "Null", 94, 0)]
    [InlineData("Max", "Empty", 0, 0)]
    [InlineData("Max", "True", 0, -1)]
    [InlineData("Max", "False", 0, 0)]
    [InlineData("Max", "\"7\"", 0, 7)]
    [InlineData("Max", "CByte(7)", 0, 7)]
    [InlineData("Max", "CCur(7.5)", 0, 8)]
    [InlineData("Max", "CDate(7.5)", 0, 8)]
    [InlineData("Max", "CDec(7.5)", 0, 8)]
    [InlineData("Max", "7.5", 0, 8)]
    [InlineData("Max", "CSng(2.5)", 0, 2)]
    [InlineData("Max", "\"abc\"", 13, 100)]
    [InlineData("Max", "40000&", 6, 100)]
    [InlineData("Max", "Null", 94, 100)]
    [InlineData("SmallChange", "Empty", 380, 1)]
    [InlineData("SmallChange", "True", 380, 1)]
    [InlineData("SmallChange", "False", 380, 1)]
    [InlineData("SmallChange", "\"7\"", 0, 7)]
    [InlineData("SmallChange", "CByte(7)", 0, 7)]
    [InlineData("SmallChange", "CCur(7.5)", 0, 8)]
    [InlineData("SmallChange", "CDate(7.5)", 0, 8)]
    [InlineData("SmallChange", "CDec(7.5)", 0, 8)]
    [InlineData("SmallChange", "7.5", 0, 8)]
    [InlineData("SmallChange", "CSng(2.5)", 0, 2)]
    [InlineData("SmallChange", "\"abc\"", 13, 1)]
    [InlineData("SmallChange", "40000&", 6, 1)]
    [InlineData("SmallChange", "Null", 94, 1)]
    [InlineData("LargeChange", "Empty", 380, 1)]
    [InlineData("LargeChange", "True", 380, 1)]
    [InlineData("LargeChange", "False", 380, 1)]
    [InlineData("LargeChange", "\"7\"", 0, 7)]
    [InlineData("LargeChange", "CByte(7)", 0, 7)]
    [InlineData("LargeChange", "CCur(7.5)", 0, 8)]
    [InlineData("LargeChange", "CDate(7.5)", 0, 8)]
    [InlineData("LargeChange", "CDec(7.5)", 0, 8)]
    [InlineData("LargeChange", "7.5", 0, 8)]
    [InlineData("LargeChange", "CSng(2.5)", 0, 2)]
    [InlineData("LargeChange", "\"abc\"", 13, 1)]
    [InlineData("LargeChange", "40000&", 6, 1)]
    [InlineData("LargeChange", "Null", 94, 1)]
    public async Task Measured_conversions(string member, string expression, int error, int expected)
    {
        var bar = Create();
        Bag(bar).TrySetProperty("Max", new Vb6Value(100));
        Bag(bar).TrySetProperty("Value", new Vb6Value(37));
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar), declaredClass: "MSComCtl2.FlatScrollBar");
        var result = await ContainerRuntimeHarness.Run(context, environment, $"""
            On Error Resume Next
            Flat1.{member} = {expression}
            Debug.Print Err.Number
            Debug.Print Flat1.{member}
            """);
        Convert.ToInt32(result[0].Value).Should().Be(error);
        Convert.ToInt32(result[1].Value).Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("0", false, 0)]
    [InlineData("Empty", false, 0)]
    [InlineData("CSng(0)", false, 0)]
    [InlineData("\"False\"", false, 0)]
    [InlineData("1", true, 0)]
    [InlineData("-1", true, 0)]
    [InlineData("2", true, 0)]
    [InlineData("1.5", true, 0)]
    [InlineData("CByte(1)", true, 0)]
    [InlineData("CCur(1)", true, 0)]
    [InlineData("\"True\"", true, 0)]
    [InlineData("\"abc\"", true, 13)]
    [InlineData("Null", true, 94)]
    public async Task Measured_Enabled_conversions(string expression, bool expected, int error)
    {
        var bar = Create();
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar), declaredClass: "MSComCtl2.FlatScrollBar");
        var result = await ContainerRuntimeHarness.Run(context, environment, $"""
            On Error Resume Next
            Flat1.Enabled = {expression}
            Debug.Print Err.Number
            """);
        Convert.ToInt32(result.Single().Value).Should().Be(error);
        bar.IsEnabled.Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("Value")]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("SmallChange")]
    [InlineData("LargeChange")]
    [InlineData("Enabled")]
    public async Task Object_Null_store_raises_13_and_keeps_property(string member)
    {
        var bar = Create();
        var before = Read(bar, member);
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar));
        var result = await ContainerRuntimeHarness.Run(context, environment, $"""
            Dim o As Object
            Set o = Flat1
            On Error Resume Next
            o.{member} = Null
            Debug.Print Err.Number
            """);
        Convert.ToInt32(result.Single().Value).Should().Be(13);
        Read(bar, member).Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(99)]
    public void MousePointer_updates_stored_and_visible_cursor(int pointer)
    {
        var bar = Create();
        Bag(bar).TrySetProperty("MousePointer", new Vb6Value(pointer)).Should().BeTrue();
        Read(bar, "MousePointer").Should().Be((long)pointer);
        VBCursorTypeExtensions.PointerOf(bar.Cursor).Should().Be((VBCursorType)pointer);
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(98)]
    [InlineData(100)]
    public void Invalid_MousePointer_keeps_value_and_cursor(int pointer)
    {
        var bar = Create();
        Bag(bar).TrySetProperty("MousePointer", new Vb6Value(2));
        var cursor = bar.Cursor;
        var act = () => Bag(bar).TrySetProperty("MousePointer", new Vb6Value(pointer));
        act.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
        Read(bar, "MousePointer").Should().Be(2L);
        bar.Cursor.Should().BeSameAs(cursor);
    }

    [AvaloniaFact]
    public void Saved_MousePointer_sets_cursor()
    {
        var bar = (VBFlatScrollBar)Create();
        bar.LoadProperties([("MousePointer", new Vb6Value(11.0))]);
        Read(bar, "MousePointer").Should().Be(11L);
        VBCursorTypeExtensions.PointerOf(bar.Cursor).Should().Be((VBCursorType)11);
    }

    [AvaloniaFact]
    public async Task Hidden_Value_uses_Value_and_raises_Change_once()
    {
        var bar = Create();
        VBProps.SetName(bar, "Flat1");
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = bar };
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar));
        var result = await ContainerRuntimeHarness.Run(context, environment, """
            Flat1.Value = 37
            Debug.Print Flat1.[_Value]
            Flat1.[_Value] = 12
            Debug.Print Flat1.Value
            Flat1.[_Value] = 12
            """);
        result.Select(v => Convert.ToInt32(v.Value)).Should().Equal(37, 12);
        root.Calls.Select(c => c.Name).Should().Equal("Flat1_Change", "Flat1_Change");
    }

    [AvaloniaFact]
    public void Unmeasured_Refresh_arguments_keep_the_existing_refusal()
    {
        var bar = Create();
        var act = () => bar.Call("Refresh", [new Vb6Value(1)]);
        act.Should().Throw<NotImplementedException>().WithMessage("FlatScrollBar Refresh is not implemented (#1097)");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_succeeds(bool lateBound)
    {
        var bar = Create();
        var context = new ModuleExecutionContext();
        var environment = new ExecutionEnvironment();
        context.AllocVariable(environment, "Flat1", new Vb6Value(bar), declaredClass: "MSComCtl2.FlatScrollBar");
        var result = await ContainerRuntimeHarness.Run(context, environment, lateBound ? """
            Dim o As Object
            Set o = Flat1
            o.Refresh
            Debug.Print Err.Number
            """ : """
            Flat1.Refresh
            Debug.Print Err.Number
            """);
        Convert.ToInt32(result.Single().Value).Should().Be(0);
    }
}
