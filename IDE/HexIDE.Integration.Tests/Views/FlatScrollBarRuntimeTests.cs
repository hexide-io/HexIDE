using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

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
        var hidden = () => Read(bar, "_Value");
        hidden.Should().Throw<NotImplementedException>().WithMessage("*FlatScrollBar*not implemented*");
        var pointerOutsideProbe = () => Bag(bar).TrySetProperty("MousePointer", new Vb6Value(32768L));
        pointerOutsideProbe.Should().Throw<NotImplementedException>().WithMessage("*FlatScrollBar*not implemented*");
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
}
