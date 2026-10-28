using System.Linq;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests.Debugging;

/// <summary>
/// Debugger P8 / D7 — a live control value in the Locals tree expands to its readable VB6 properties. Needs a real
/// Avalonia control, so it lives in HexIDE.Tests (headless Avalonia), not the pure-interpreter runtime suite.
/// </summary>
public class ControlLocalsTests
{

    [AvaloniaFact]
    public void ControlValue_ExpandsToReadableProperties()
    {
        var button = new VBCommandButton();
        var node = DebugInspector.NodeFor("Command1", new Vb6Value(button), new ModuleExecutionContext());

        node.HasChildren.Should().BeTrue();   // no longer a leaf (D7)
        var props = node.Expand();
        props.Should().NotBeEmpty();
        // A button exposes the standard geometry properties.
        props.Select(p => p.Name).Should().Contain(new[] { "Left", "Top", "Width", "Height" });
    }

    [AvaloniaFact]
    public void ReadProperties_AreNameSorted()
    {
        var props = AvaloniaInteroperability.ReadProperties(new VBTextBox());
        props.Should().NotBeEmpty();
        props.Select(p => p.Name).Should().BeInAscendingOrder();
    }

    [AvaloniaFact]
    public void A_Timer_lists_only_its_runtime_properties()
    {
        var node = DebugInspector.NodeFor("Timer1", new Vb6Value(new VBTimer { Interval = 250 }), new ModuleExecutionContext());

        var props = node.Expand();

        props.Select(p => p.Name).Should().BeInAscendingOrder().And.Contain(new[] { "Enabled", "Interval", "Tag" });
        props.Select(p => p.Name).Should().NotContain(new[] { "Left", "Top", "Width", "Height", "Visible", "Font", "BackColor", "ForeColor", "MousePointer", "RightToLeft", "TabIndex", "ToolTipText" });
        props.Single(p => p.Name == "Interval").Value.Should().Be("250");
    }

    [AvaloniaFact]
    public void ReadProperties_omits_a_Timers_designer_positions()
    {
        var props = AvaloniaInteroperability.ReadProperties(new VBTimer());

        props.Select(p => p.Name).Should().NotContain(new[] { "Left", "Top" });
        props.Should().OnlyContain(p => p.Error == null);
    }

    [AvaloniaFact]
    public void A_scroll_bar_reads_every_property_without_an_error()
    {
        var props = AvaloniaInteroperability.ReadProperties(new VBScrollBar());

        props.Should().NotBeEmpty().And.OnlyContain(p => p.Error == null);
        props.Select(p => p.Name).Should().NotContain(new[] { "BackColor", "ForeColor" });
    }
}
