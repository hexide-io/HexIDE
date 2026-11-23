using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class SelectionReadoutTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string message) { } }

    // Measured rows: #1641 and docs/vb6-fidelity-oracle/what-vb6s-properties-window-shows-a-position-in.md (#5233).
    // The fractional height deliberately follows the Properties window's 25.533, rather than the readout's 25.53.
    [AvaloniaTheory]
    [InlineData(1, "CommandButton", 240, 240, 1215, 495, "Control1", "240, 240", "1215 x 495")]
    [InlineData(3, "CommandButton", 255, 255, 1215, 495, "Control1", "17, 17", "81 x 33")]
    [InlineData(3, "TextBox", 247, 964, 1215, 383, "Control1", "16.467, 64.267", "81 x 25.533")]
    [InlineData(1, "ComboBox", 240, 360, 2400, 315, "Form1", "-60, -15", "4920 x 3675")]
    [InlineData(1, "ComboBox", 240, 360, 2400, 315, "Control1", "240, 360", "2400 x 315")]
    public void Selected_object_uses_the_Properties_windows_units(int scale, string type, int left, int top,
        int width, int height, string selected, string position, string size)
    {
        var source = $"""
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Form1"
               ClientHeight = 3090
               ClientLeft = 60
               ClientTop = 450
               ClientWidth = 4680
               ScaleMode = {scale}
               Begin VB.{type} Control1
                  Left = {left}
                  Top = {top}
                  Width = {width}
                  Height = {height}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new NullSink())!;
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        var focus = new FocusedProjectUtil(dock, Substitute.For<IProjectManager>());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock,
            new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        designer.SelectedComponent = designer.AllComponents.Single(c =>
            c.Instance.GetPropertyOrDefault(VBProperties.NameProperty) == selected);

        focus.FocusedComponentPosition.Should().Be(position);
        focus.FocusedComponentSize.Should().Be(size);
    }

    [Fact]
    public void Without_a_selection_both_readouts_are_zero()
    {
        var focus = new FocusedProjectUtil(Substitute.For<IDocumentDockService>(), Substitute.For<IProjectManager>());

        focus.FocusedComponentPosition.Should().Be("0, 0");
        focus.FocusedComponentSize.Should().Be("0, 0");
    }
}
