using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Phase 2.6 — property-boundary colour interop. Now that VB6 &amp;H literals evaluate as numbers, assigning a
/// numeric OLE_COLOR to a colour property (as `ctrl.BackColor = &amp;HFF` does at runtime) must convert to a
/// VBColor. Verifies AvaloniaInteroperability.TrySet does that on a real (headless) control.
/// </summary>
public class HexColorInteropTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static Control SpawnLabel()
    {
        var form = new FormDeserializer().Deserialize(Project, """
            VERSION 5.00
            Begin VB.UserControl MyCtrl
               Begin VB.Label Label1
                  Caption = "Hi"
               End
            End
            Attribute VB_Name = "MyCtrl"
            """, NullSink.Instance)!;
        var canvas = VBLoader.SpawnComponentsForDesigner(form);
        return (Control)canvas.Children[0];
    }

    [AvaloniaFact]
    public void NumericHexColor_AssignedToBackColor_ConvertsToVBColor()
    {
        var label = SpawnLabel();

        // `Label1.BackColor = &HFF` — &HFF now evaluates to the number 255, not a colour literal.
        AvaloniaInteroperability.TrySet(label, VBProperties.BackColorProperty, new Vb6Value(0xFF))
            .Should().BeTrue();

        AvaloniaInteroperability.TryGet(label, VBProperties.BackColorProperty, out var readBack)
            .Should().BeTrue();
        readBack.Value.Should().BeOfType<VBColor>();
        var color = (VBColor)readBack.Value!;
        (color.R, color.G, color.B).Should().Be(((byte)255, (byte)0, (byte)0));   // &HFF -> red
    }
}
