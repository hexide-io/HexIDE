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
    public ControlLocalsTests() => AvaloniaTestSetup.EnsureInitialized();

    [Fact]
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

    [Fact]
    public void ReadProperties_AreNameSorted()
    {
        var props = AvaloniaInteroperability.ReadProperties(new VBTextBox());
        props.Should().NotBeEmpty();
        props.Select(p => p.Name).Should().BeInAscendingOrder();
    }
}
