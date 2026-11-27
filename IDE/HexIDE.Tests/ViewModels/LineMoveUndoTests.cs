using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class LineMoveUndoTests
{
    private static FormEditViewModel Open(FormDefinition form)
    {
        var localization = new LocalizationService();
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), InBoxRegistry.Create(), localization);
        return new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(form);
    }

    private static FormEditViewModel OpenTwipsForm(params ComponentInstance[] controls)
    {
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        return Open(new FormDefinition(TestHelpers.CreateProject(), [root, .. controls], ""));
    }

    private static ComponentInstance TwipsLine() =>
        new ComponentInstance(LineComponentClass.Instance, "Line1")
            .SetProperty(VBProperties.X1Property, 100d).SetProperty(VBProperties.Y1Property, 100d)
            .SetProperty(VBProperties.X2Property, 200d).SetProperty(VBProperties.Y2Property, 200d);

    private static (double X1, double Y1, double X2, double Y2) Endpoints(ComponentInstance instance) =>
        (instance[VBProperties.X1Property], instance[VBProperties.Y1Property],
            instance[VBProperties.X2Property], instance[VBProperties.Y2Property]);

    private static void Drag(FormEditViewModel designer, ComponentInstanceViewModel control, double dx, double dy)
    {
        designer.BeginDrag([control]);
        control.Left += dx;
        control.Top += dy;
        designer.EndDrag();
    }

    [AvaloniaFact]
    public void Undo_and_redo_restore_a_twips_lines_exact_endpoints_after_a_fractional_move()
    {
        using var designer = OpenTwipsForm(TwipsLine());
        var line = designer.Components.Single();

        Drag(designer, line, 3.25, 1.5);
        Endpoints(line.Instance).Should().Be((148.75, 122.5, 248.75, 222.5));

        designer.UndoStack.Undo();
        Endpoints(line.Instance).Should().Be((100d, 100d, 200d, 200d));

        designer.UndoStack.Redo();
        Endpoints(line.Instance).Should().Be((148.75, 122.5, 248.75, 222.5));
    }

    [AvaloniaFact]
    public void A_line_drag_is_described_as_a_move()
    {
        using var designer = OpenTwipsForm(TwipsLine());

        Drag(designer, designer.Components.Single(), 3.25, 1.5);

        designer.UndoStack.UndoDescription.Should().Be("Move: Line1");
    }

    [AvaloniaFact]
    public void Undo_and_redo_restore_a_pixel_lines_exact_endpoints_after_a_fractional_move()
    {
        var form = new FormDeserializer().Deserialize(TestHelpers.CreateProject(),
            """
            VERSION 5.00
            Begin VB.Form Form1
               ClientHeight = 3090
               ClientWidth = 4680
               ScaleMode = 3
               Begin VB.Line Line1
                  X1 = 40
                  Y1 = 60
                  X2 = 200
                  Y2 = 60
               End
            End
            Attribute VB_Name = "Form1"
            """.ReplaceLineEndings("\r\n"), new NullSink())!;
        using var designer = Open(form);
        var line = designer.Components.Single();

        Drag(designer, line, 3.25, 1.5);
        Endpoints(line.Instance).Should().Be((43.25, 61.5, 203.25, 61.5));

        designer.UndoStack.Undo();
        Endpoints(line.Instance).Should().Be((40d, 60d, 200d, 60d));

        designer.UndoStack.Redo();
        Endpoints(line.Instance).Should().Be((43.25, 61.5, 203.25, 61.5));
    }

    [AvaloniaFact]
    public void A_subpixel_move_of_a_line_with_the_same_rounded_box_can_be_undone()
    {
        using var designer = OpenTwipsForm(TwipsLine());
        var line = designer.Components.Single();
        var box = (line.Left, line.Top, line.Width, line.Height);

        Drag(designer, line, 0.1, 0);
        Endpoints(line.Instance).Should().Be((101.5, 100d, 201.5, 200d));
        (line.Left, line.Top, line.Width, line.Height).Should().Be(box);

        designer.UndoStack.CanUndo.Should().BeTrue();
        designer.UndoStack.Undo();
        Endpoints(line.Instance).Should().Be((100d, 100d, 200d, 200d));
    }

    [AvaloniaFact]
    public void A_line_and_a_button_drag_share_one_move_step()
    {
        var button = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        using var designer = OpenTwipsForm(TwipsLine(), button);
        var line = designer.Components.Single(c => c.IsLine);
        var command = designer.Components.Single(c => c.Instance == button);
        var start = (command.Left, command.Top);

        designer.BeginDrag([line, command]);
        line.Left += 3.25;
        line.Top += 1.5;
        command.Left += 3.25;
        command.Top += 1.5;
        designer.EndDrag();

        designer.UndoStack.UndoDescription.Should().Be("Move: 2 controls");
        designer.UndoStack.Undo();
        Endpoints(line.Instance).Should().Be((100d, 100d, 200d, 200d));
        (command.Left, command.Top).Should().Be(start);
        designer.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public void Undo_restores_a_twips_line_after_a_whole_pixel_move()
    {
        using var designer = OpenTwipsForm(TwipsLine());
        var line = designer.Components.Single();

        Drag(designer, line, 3, 0);
        Endpoints(line.Instance).Should().Be((145d, 100d, 245d, 200d));

        designer.UndoStack.Undo();
        Endpoints(line.Instance).Should().Be((100d, 100d, 200d, 200d));
    }

    [AvaloniaFact]
    public void Undo_and_redo_leave_omitted_endpoints_on_the_untouched_axis_unset()
    {
        var instance = new ComponentInstance(LineComponentClass.Instance, "Line1")
            .SetProperty(VBProperties.X1Property, 100d).SetProperty(VBProperties.X2Property, 200d);
        using var designer = OpenTwipsForm(instance);

        Drag(designer, designer.Components.Single(), 3, 0);

        designer.UndoStack.Undo();
        instance.TryGetBoxedProperty(VBProperties.Y1Property, out _).Should().BeFalse();
        instance.TryGetBoxedProperty(VBProperties.Y2Property, out _).Should().BeFalse();

        designer.UndoStack.Redo();
        instance.TryGetBoxedProperty(VBProperties.Y1Property, out _).Should().BeFalse();
        instance.TryGetBoxedProperty(VBProperties.Y2Property, out _).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Undo_restores_a_button_after_a_move()
    {
        using var designer = OpenTwipsForm(new ComponentInstance(CommandButtonComponentClass.Instance, "Command1"));
        var button = designer.Components.Single();
        var start = (button.Left, button.Top, button.Width, button.Height);

        Drag(designer, button, 3.25, 1.5);
        (button.Left, button.Top).Should().Be((start.Left + 3.25, start.Top + 1.5));
        designer.UndoStack.UndoDescription.Should().Be("Move: Command1");

        designer.UndoStack.Undo();
        (button.Left, button.Top, button.Width, button.Height).Should().Be(start);
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string message) { } }
}
