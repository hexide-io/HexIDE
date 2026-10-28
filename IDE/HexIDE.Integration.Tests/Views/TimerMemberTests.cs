using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

// vb6.exe SP6, native and p-code: oracle "What a Timer has at run time" (#801).
public class TimerMemberTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Timer Timer1
              Enabled = 0
              Interval = 1000
              Tag = "t1"
           End
           Begin VB.Timer Timer2
              Index = 0
              Interval = 250
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("Visible")]
    [InlineData("MousePointer")]
    [InlineData("RightToLeft")]
    [InlineData("Font")]
    [InlineData("BackColor")]
    [InlineData("Container")]
    [InlineData("hWnd")]
    public async Task Absent_reads_raise_trappable_438_by_every_route(string member)
    {
        foreach (var target in new[] { "Timer1", "Timer2(0)", "o" })
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var result = await ContainerRuntimeHarness.Run(ctx, env,
                $"Dim o As Object\nSet o = Timer1\nOn Error Resume Next\nDim v\n"
                + (member is "Font" or "Container" ? "Set " : "")
                + $"v = {target}.{member}\nDebug.Print Err.Number\n");
            result.Single().Value?.ToString().Should().Be("438");
        }
    }

    // Keep the existing missing-member compile error for TabIndex (#1974, #801 review).
    [AvaloniaTheory]
    [InlineData("Timer1")]
    [InlineData("Timer2(0)")]
    public async Task TabIndex_is_refused_even_with_an_error_handler(string target)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var act = () => ContainerRuntimeHarness.Run(ctx, env,
            $"On Error Resume Next\nDebug.Print {target}.TabIndex\n");

        await act.Should().ThrowAsync<HexIDE.Runtime.Interpreter.VBMethodOrDataMemberNotFoundException>();
    }

    [AvaloniaTheory]
    [InlineData("Width = 100", "438")]
    [InlineData("Height = 100", "438")]
    [InlineData("Visible = False", "438")]
    [InlineData("Move 10, 20", "438")]
    [InlineData("Left = 50", "382")]
    [InlineData("Top = 70", "382")]
    public async Task Refused_writes_raise_measured_errors(string statement, string expected)
    {
        foreach (var target in new[] { "Timer1", "Timer2(0)", "o" })
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var result = await ContainerRuntimeHarness.Run(ctx, env,
                $"Dim o As Object\nSet o = Timer1\nOn Error Resume Next\n{target}.{statement}\nDebug.Print Err.Number\n");
            result.Single().Value?.ToString().Should().Be(expected);
        }
    }

    [AvaloniaFact]
    public async Task Interval_reads_as_Long_and_multiplies_without_Integer_overflow()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var result = await ContainerRuntimeHarness.Run(ctx, env,
            "Dim o As Object\nSet o = Timer1\nDebug.Print TypeName(Timer1.Interval)\nDebug.Print TypeName(o.Interval)\nDebug.Print TypeName(Timer2(0).Interval)\nDebug.Print Timer1.Interval * 40\n");
        result.Select(v => v.Value?.ToString()).Should().Equal("Long", "Long", "Long", "40000");
    }

    [AvaloniaTheory]
    [InlineData("Timer1", "False", "t1", "Timer1", "343")]
    [InlineData("Timer2(0)", "True", "", "Timer2", "0")]
    [InlineData("o", "False", "t1", "Timer1", "343")]
    public async Task Existing_members_keep_the_measured_values(string target, string enabled, string tag, string name, string indexError)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var result = await ContainerRuntimeHarness.Run(ctx, env,
            $"Dim o As Object\nSet o = Timer1\nOn Error Resume Next\nDebug.Print {target}.Enabled\nDebug.Print {target}.Tag\nDebug.Print {target}.Name\nDim v\nv = {target}.Index\nDebug.Print Err.Number\n");
        result.Select(v => v.Value?.ToString()).Should().Equal(enabled, tag, name, indexError);
    }

    [AvaloniaTheory]
    [InlineData("Left")]
    [InlineData("Top")]
    public async Task Designer_positions_still_refuse_reads(string member)
    {
        foreach (var target in new[] { "Timer1", "Timer2(0)", "o" })
        {
            var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
            var result = await ContainerRuntimeHarness.Run(ctx, env,
                $"Dim o As Object\nSet o = Timer1\nOn Error Resume Next\nDim v\nv = {target}.{member}\nDebug.Print Err.Number\nDebug.Print Err.Description\n");
            result.Select(v => v.Value?.ToString()).Should().Equal("393", $"'{member}' property cannot be read at run time");
        }
    }

    [AvaloniaTheory]
    [InlineData("BackColor")]
    [InlineData("ForeColor")]
    public void Unmeasured_scroll_bar_colours_refuse_reads_and_writes(string name)
    {
        var bar = new VBScrollBar();
        var property = name == "BackColor" ? HexIDE.Runtime.Components.VBProperties.BackColorProperty
            : HexIDE.Runtime.Components.VBProperties.ForeColorProperty;
        System.Action read = () => AvaloniaInteroperability.TryGet(bar, property, out _);
        System.Action write = () => AvaloniaInteroperability.TrySet(bar, property, new HexIDE.Runtime.Interpreter.Vb6Value(255));
        read.Should().Throw<System.NotImplementedException>().WithMessage("*#801*");
        write.Should().Throw<System.NotImplementedException>().WithMessage("*#801*");
    }

    [AvaloniaFact]
    public void Absent_properties_do_not_resolve_in_the_shared_binding_table()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(Form);
        var timer = (VBTimer)ContainerRuntimeHarness.Child(canvas, "Timer1");
        foreach (var name in new[] { "Width", "Height", "Visible", "Font", "BackColor", "ForeColor", "MousePointer", "RightToLeft" })
            foreach (var property in HexIDE.Runtime.Components.VBProperties.PropertiesByName[name])
                AvaloniaInteroperability.TryGet(timer, property, out _).Should().BeFalse(name);
    }
}
