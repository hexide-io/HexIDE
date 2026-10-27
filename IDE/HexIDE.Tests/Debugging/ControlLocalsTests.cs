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

    // A Timer's Left and Top raise 393 when read, as VB6's do (#2069). Expanding the Timer threw that out of the whole
    // expansion, so get_locals failed on any form holding one and the Locals window gave Timer1 no expander (#2289).
    [AvaloniaFact]
    public void A_property_whose_read_raises_shows_the_error_and_the_rest_are_still_listed()
    {
        var timer = new VBTimer { Interval = 250 };
        var node = DebugInspector.NodeFor("Timer1", new Vb6Value(timer), new ModuleExecutionContext());

        var props = node.Expand();

        props.Select(p => p.Name).Should().BeInAscendingOrder().And.Contain(new[] { "Enabled", "Interval", "Left", "Top" });
        foreach (var name in new[] { "Left", "Top" })
        {
            var raised = props.Single(p => p.Name == name);
            raised.Value.Should().Be("<Get not supported at runtime>");
            raised.TypeName.Should().BeEmpty();
            raised.HasChildren.Should().BeFalse();
        }
        props.Single(p => p.Name == "Interval").Value.Should().Be("250");
    }

    [AvaloniaFact]
    public void ReadProperties_reports_a_raising_read_as_its_error_with_no_value()
    {
        var props = AvaloniaInteroperability.ReadProperties(new VBTimer());

        var left = props.Single(p => p.Name == "Left");
        left.Error.Should().Be("Get not supported at runtime");
        props.Where(p => p.Name is not ("Left" or "Top")).Should().OnlyContain(p => p.Error == null);
    }

    // A program's read of a scroll bar's colour is refused (#801), but that refusal is the interpreter's, not the read
    // Locals makes, so its properties all read.
    [AvaloniaFact]
    public void A_scroll_bar_reads_every_property_without_an_error()
    {
        AvaloniaInteroperability.ReadProperties(new VBScrollBar()).Should().NotBeEmpty().And.OnlyContain(p => p.Error == null);
    }
}
