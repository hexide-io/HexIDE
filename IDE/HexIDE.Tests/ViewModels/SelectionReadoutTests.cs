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

    private const string TwoButtons = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption = "Form1"
           ClientHeight = 3090
           ClientWidth = 4680
           ScaleMode = 1
           Begin VB.CommandButton Command1
              Left = 240
              Top = 240
              Width = 1215
              Height = 495
           End
           Begin VB.CommandButton Command2
              Left = 2400
              Top = 1200
              Width = 1215
              Height = 495
           End
        End
        Attribute VB_Name = "Form1"
        """;

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
        using var designer = OpenDesigner(source, out var focus);
        SelectControl(designer, selected);

        focus.FocusedComponentPosition.Should().Be(position);
        focus.FocusedComponentSize.Should().Be(size);
    }

    [AvaloniaFact]
    public void Changing_a_previously_selected_control_leaves_the_readouts_on_the_current_selection()
    {
        using var designer = OpenDesigner(TwoButtons, out var focus);
        var first = SelectControl(designer, "Command1");
        SelectControl(designer, "Command2");

        first.Left += 10;
        first.Width += 4;

        (focus.FocusedComponentPosition, focus.FocusedComponentSize).Should().Be(("2400, 1200", "1215 x 495"));
    }

    // The report's own sequence (#5806): one Line dragged, another selected, then Undo and Redo of the drag. Main read
    // the dragged Line's box, 198, 218 and 165 x 5, while Line1 stayed selected.
    [AvaloniaFact]
    public void Undoing_and_redoing_another_controls_move_leaves_the_readouts_on_the_current_selection()
    {
        using var designer = OpenDesigner("""
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 4500
               ClientWidth = 6000
               ScaleHeight = 300
               ScaleMode = 3
               ScaleWidth = 400
               Begin VB.Line Line1
                  X1 = 20
                  X2 = 120
                  Y1 = 30
                  Y2 = 30
               End
               Begin VB.Line Line2
                  X1 = 200
                  X2 = 360
                  Y1 = 220
                  Y2 = 220
               End
            End
            Attribute VB_Name = "Form1"
            """, out var focus);
        var dragged = SelectControl(designer, "Line2");
        designer.BeginDrag([dragged]);
        dragged.Left += 3;
        dragged.Top += 2;
        designer.EndDrag();
        var selected = SelectControl(designer, "Line1");
        var shown = (focus.FocusedComponentPosition, focus.FocusedComponentSize);

        designer.UndoStack.Undo();
        (focus.FocusedComponentPosition, focus.FocusedComponentSize).Should().Be(shown);
        designer.UndoStack.Redo();
        (focus.FocusedComponentPosition, focus.FocusedComponentSize).Should().Be(shown);
        designer.SelectedComponent.Should().BeSameAs(selected);
        shown.Should().Be(("18, 28", "105 x 5"));
    }

    [AvaloniaFact]
    public void Reselecting_a_control_shows_its_current_position_and_size()
    {
        using var designer = OpenDesigner(TwoButtons, out var focus);
        var first = SelectControl(designer, "Command1");
        SelectControl(designer, "Command2");
        first.Left += 10;
        first.Width += 4;

        SelectControl(designer, "Command1");

        focus.FocusedComponentPosition.Should().Be("390, 240");
        focus.FocusedComponentSize.Should().Be("1275 x 495");
    }

    [AvaloniaFact]
    public void Moving_and_resizing_the_selected_control_updates_the_readouts()
    {
        using var designer = OpenDesigner(TwoButtons, out var focus);
        var first = SelectControl(designer, "Command1");
        focus.FocusedComponentPosition.Should().Be("240, 240");
        focus.FocusedComponentSize.Should().Be("1215 x 495");

        first.Left += 10;
        first.Width += 4;

        focus.FocusedComponentPosition.Should().Be("390, 240");
        focus.FocusedComponentSize.Should().Be("1275 x 495");
    }

    [Fact]
    public void Without_a_selection_both_readouts_are_zero()
    {
        var focus = new FocusedProjectUtil(Substitute.For<IDocumentDockService>(), Substitute.For<IProjectManager>());

        focus.FocusedComponentPosition.Should().Be("0, 0");
        focus.FocusedComponentSize.Should().Be("0, 0");
    }

    private static FormEditViewModel OpenDesigner(string source, out FocusedProjectUtil focus)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source,
            new NullSink())!;
        var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        focus = new FocusedProjectUtil(dock, Substitute.For<IProjectManager>());
        dock.ActiveDocument.Returns(designer);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock,
            new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        return designer;
    }

    private static ComponentInstanceViewModel SelectControl(FormEditViewModel designer, string name)
    {
        var component = designer.AllComponents.Single(c => c.Instance.GetPropertyOrDefault(VBProperties.NameProperty) == name);
        designer.SelectedComponent = component;
        return component;
    }
}
