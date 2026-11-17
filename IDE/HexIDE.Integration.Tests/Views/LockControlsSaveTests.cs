using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Lock Controls writes the form only when the form already has a file.
/// </summary>
/// <remarks>
/// A form with no file saves through the Save As picker. Toggling Lock Controls, or undoing or redoing the
/// toggle, therefore put a native Save As dialog in front of someone who had asked only to lock the controls,
/// and an automation client can neither see nor close that dialog. Measured live on the <c>Form1</c> of a
/// startup-dialog project: after a toggle and <c>invoke_designer_undo</c>, the process owned a visible
/// <c>#32770 | Save As</c> window. (#539 follow-up)
/// </remarks>
public class LockControlsSaveTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string BareForm = """
        VERSION 5.00
        Begin VB.Form Form1
        End
        Attribute VB_Name = "Form1"
        """;

    private static (FormEditViewModel Vm, FormDefinition Form, IProjectService Projects) Designer(string? path)
    {
        var projects = Substitute.For<IProjectService>();
        var form = new FormDeserializer().Deserialize(Project, BareForm, new NullSink())!;
        form.AbsolutePath = path;
        var vm = new FormEditViewModel(
            null!, Substitute.For<IEventBus>(), projects, null!, null!, null!, Substitute.For<ILocalizationService>());
        vm.Initialize(form);
        return (vm, form, projects);
    }

    [AvaloniaFact]
    public void Toggling_on_a_form_with_no_file_saves_nothing_and_keeps_the_change()
    {
        var (vm, form, projects) = Designer(path: null);

        vm.ToggleLockControls();

        form.LockControls.Should().BeTrue("the change is kept for the next save");
        projects.DidNotReceiveWithAnyArgs().SaveForm(default!, default);
    }

    [AvaloniaFact]
    public void Undoing_and_redoing_the_toggle_on_a_form_with_no_file_saves_nothing()
    {
        var (vm, form, projects) = Designer(path: null);
        vm.ToggleLockControls();

        vm.UndoStack.Undo();
        form.LockControls.Should().BeFalse();
        vm.UndoStack.Redo();
        form.LockControls.Should().BeTrue();

        projects.DidNotReceiveWithAnyArgs().SaveForm(default!, default);
    }

    [AvaloniaFact]
    public void Toggling_on_a_form_with_a_file_still_writes_it()
    {
        var (vm, form, projects) = Designer(path: @"C:\p\Form1.frm");

        vm.ToggleLockControls();

        projects.Received(1).SaveForm(form, false);
    }
}
