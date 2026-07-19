using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class SpawnComponentsForDesignerTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;

    private const string CtlWithLabel = """
        VERSION 5.00
        Begin VB.UserControl MyCtrl
           Begin VB.Label Label1
              Caption         =   "Hello"
              Left            =   120
              Top             =   240
              Width           =   1215
              Height          =   255
           End
        End
        Attribute VB_Name = "MyCtrl"
        """;

    [AvaloniaFact]
    public void SpawnComponentsForDesigner_ReturnsCanvas()
    {
        var formPart = Deserialize(CtlWithLabel);
        var canvas = VBLoader.SpawnComponentsForDesigner(formPart);
        canvas.Should().NotBeNull();
        canvas.Should().BeOfType<Canvas>();
    }

    [AvaloniaFact]
    public void SpawnComponentsForDesigner_ChildControlsPositioned()
    {
        var formPart = Deserialize(CtlWithLabel);
        var canvas = VBLoader.SpawnComponentsForDesigner(formPart);

        canvas.Children.Should().HaveCount(1); // Label1 only; root UC (FormComponentClass) is skipped

        var label = canvas.Children[0];
        Canvas.GetLeft(label).Should().BeApproximately(120.0 / 15, 0.01);
        Canvas.GetTop(label).Should().BeApproximately(240.0 / 15, 0.01);
        label.Width.Should().BeApproximately(1215.0 / 15, 0.01);
        label.Height.Should().BeApproximately(255.0 / 15, 0.01);
    }

    [AvaloniaFact]
    public void SpawnComponentsForDesigner_EmptyFormPart_ReturnsEmptyCanvas()
    {
        var formPart = Deserialize("""
            VERSION 5.00
            Begin VB.UserControl MyCtrl
            End
            Attribute VB_Name = "MyCtrl"
            """);

        var canvas = VBLoader.SpawnComponentsForDesigner(formPart);

        canvas.Children.Should().BeEmpty();
    }
}
