using System.IO;
using System.Text;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Recovery;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// What crash recovery backs up (hexide-io/HexIDE#499 task group 1): every unsaved document, rendered as its save would
/// write it, whether or not it has a file, and whatever an editor or designer holds ahead of the model.
/// </summary>
/// <remarks>
/// A real <see cref="FileBaselineStore"/> and a real <see cref="EventBus"/>, as the save-prompt tests use: a substituted
/// store makes every document vacuously dirty, and a substituted bus would never reach the editors' flush.
/// </remarks>
public sealed class UnsavedWorkCaptureTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-recovery-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = [];
    private readonly EventBus bus = new();
    private readonly List<IDisposable> created = [];

    public UnsavedWorkCaptureTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        foreach (var vm in created) vm.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            bus,
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
    }

    /// <summary>A saved project of a form and two modules, opened and saved again so every baseline is this build's.</summary>
    private async Task<(ProjectService Service, ProjectDefinition Project)> OpenSaved()
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Module2.bas"),
            "Attribute VB_Name = \"Module2\"\r\nPublic Sub Untouched()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Test.vbp"),
            "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nModule=Module2; Module2.bas\r\nName=\"Test\"\r\n");

        var service = MakeService();
        await service.OpenProject(Path.Join(dir, "Test.vbp"));
        var project = loaded.Single();
        await service.SaveProject(project, saveAs: false);
        return (service, project);
    }

    private T Made<T>(T vm) where T : IDisposable
    {
        created.Add(vm);
        return vm;
    }

    private CodeEditorViewModel CodeWindow(ProjectService service, ModuleDefinition module) => Made(new CodeEditorViewModel(
        Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), service,
        bus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
        Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
        Substitute.For<IDebugController>(), Substitute.For<HexIDE.Debugging.IRunScope>(),
        Substitute.For<ILocalizationService>()).Initialize(module));

    private FormEditViewModel Designer(IProjectService service, FormDefinition form) => Made(new FormEditViewModel(
        new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ILocalizationService>()),
        bus, service, null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>())
        .Initialize(form));

    [Fact]
    public async Task A_project_with_nothing_unsaved_is_not_captured()
    {
        var (service, _) = await OpenSaved();

        service.CaptureUnsavedWork().Should().BeEmpty();
    }

    [Fact]
    public async Task An_edited_module_is_captured_as_its_save_then_writes_it()
    {
        var (service, project) = await OpenSaved();
        var module = project.Modules.Single(m => m.Name == "Module1");
        module.UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");

        var captured = service.CaptureUnsavedWork().Should().ContainSingle().Subject;

        captured.Path.Should().Be(project.AbsolutePath);
        captured.ProjectFile.Should().BeNull("the project file would not change");
        captured.ProjectFileHash.Should().Be(FileHasher.Hash(File.ReadAllBytes(project.AbsolutePath!)),
            "a moved project is matched by its project file as this run last read or wrote it");
        captured.Documents.Select(d => d.Name).Should().Equal("Form1", "Module1", "Module2");
        captured.Documents.Single(d => d.Name == "Module2").Content.Should().BeNull("Module2 has nothing unsaved");
        captured.Documents.Single(d => d.Name == "Form1").Content.Should().BeNull();
        var content = captured.Documents.Single(d => d.Name == "Module1").Content!;
        content.FileName.Should().Be("Module1.bas");

        await service.SaveProject(project, saveAs: false);
        content.Text.Should().Be(File.ReadAllText(module.AbsolutePath!, Encoding.Latin1),
            "the backup holds exactly what the save went on to write");
    }

    [Fact]
    public async Task A_project_only_opened_is_fingerprinted_by_its_file_as_read()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Test.vbp"), "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        var service = MakeService();
        await service.OpenProject(Path.Join(dir, "Test.vbp"));
        loaded.Single().Modules[0].UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");

        service.CaptureUnsavedWork().Single().ProjectFileHash
            .Should().Be(FileHasher.Hash(File.ReadAllBytes(Path.Join(dir, "Test.vbp"))),
                "a project edited and never saved this run is the case a moved project most needs matching for");
    }

    [Fact]
    public async Task A_form_with_no_file_yet_is_captured_and_named_in_the_project_file()
    {
        var (service, project) = await OpenSaved();
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form2"));

        var captured = service.CaptureUnsavedWork().Should().ContainSingle().Subject;

        var form = captured.Documents.Single(d => d.Name == "Form2");
        form.Path.Should().BeNull();
        form.Kind.Should().Be("Form");
        form.Content!.FileName.Should().Be("Form2.frm");
        form.Content.Text.Should().Contain("Begin VB.Form Form2");
        captured.ProjectFile!.FileName.Should().Be("Test.vbp");
        captured.ProjectFile.Text.Should().Contain("Form=Form2.frm",
            "a save lists the new form, and a project file without it would lose it on restore");
    }

    [Fact]
    public void A_project_that_was_never_saved_is_captured_whole()
    {
        var project = TestHelpers.CreateProjectWithForm("Scratch");
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        loaded.Add(project);
        var service = MakeService();

        var captured = service.CaptureUnsavedWork().Should().ContainSingle().Subject;

        captured.Path.Should().BeNull();
        captured.ProjectFileHash.Should().BeNull();
        captured.ProjectFile!.FileName.Should().Be("Scratch.vbp");
        captured.ProjectFile.Text.Should().Contain("Form=Form1.frm").And.Contain("Module=Module1; Module1.bas");
        captured.Documents.Should().AllSatisfy(d => d.Content.Should().NotBeNull("none of them has a file"));
        captured.Documents.Single(d => d.Name == "Module1").Content!.FileName.Should().Be("Module1.bas");
    }

    [Fact]
    public async Task A_form_companion_is_captured_under_the_name_its_text_cites()
    {
        var (service, project) = await OpenSaved();
        project.Forms[0].Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });

        var form = service.CaptureUnsavedWork().Single().Documents.Single(d => d.Name == "Form1").Content!;

        form.CompanionFileName.Should().Be("Form1.frx");
        form.Companion.Should().NotBeNullOrEmpty();
        form.Text.Should().Contain("\"Form1.frx\":");
    }

    [Fact]
    public async Task A_capture_and_its_backup_write_nothing_beside_the_project()
    {
        var (service, project) = await OpenSaved();
        project.Modules[0].UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form2"));
        var before = Listing(dir);

        var root = Path.Join(Path.GetTempPath(), "hexide-recovery-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            RecoveryRun.Begin(root, 4242, DateTime.UtcNow).Write(service.CaptureUnsavedWork(), DateTime.UtcNow);

            Listing(dir).Should().Equal(before, "the project's folder, sidecar included, is only ever written by a save");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }

        static List<string> Listing(string folder) =>
        [
            .. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => path + " " + FileHasher.Hash(File.ReadAllBytes(path))),
        ];
    }

    [AvaloniaFact]
    public async Task A_code_window_s_unflushed_typing_is_captured()
    {
        var (service, project) = await OpenSaved();
        var module = project.Modules.Single(m => m.Name == "Module1");
        var window = CodeWindow(service, module);
        window.Document.Insert(window.Document.TextLength, "' typed and not saved\r\n");

        var content = service.CaptureUnsavedWork().Single().Documents.Single(d => d.Name == "Module1").Content;

        content!.Text.Should().Contain("' typed and not saved");
    }

    [AvaloniaFact]
    public async Task Typing_in_a_code_window_closed_without_saving_is_still_captured()
    {
        var (service, project) = await OpenSaved();
        var module = project.Modules.Single(m => m.Name == "Module1");
        var window = CodeWindow(service, module);
        window.Document.Insert(window.Document.TextLength, "' typed, then the window closed\r\n");
        window.Dispose();

        var content = service.CaptureUnsavedWork().Single().Documents.Single(d => d.Name == "Module1").Content;

        content!.Text.Should().Contain("' typed, then the window closed");
    }

    [AvaloniaFact]
    public async Task A_control_the_designer_holds_but_has_not_committed_is_captured()
    {
        var (service, project) = await OpenSaved();
        var form = project.Forms.Single();
        var designer = Designer(service, form);
        // Mid-gesture, the designer holds the control and the model doesn't have it yet.
        designer.BeginDrag([]);
        designer.SpawnControlAt(CommandButtonComponentClass.Instance, new Avalonia.Rect(10, 10, 80, 25));
        designer.HasComponentsNotInModel.Should().BeTrue("otherwise this test proves nothing about the flush");

        var content = service.CaptureUnsavedWork().Single().Documents.Single(d => d.Name == "Form1").Content;

        content!.Text.Should().Contain("Begin VB.CommandButton Command1");
    }

    [AvaloniaFact]
    public void A_capture_leaves_a_designer_that_is_not_ahead_alone()
    {
        var project = TestHelpers.CreateProjectWithForm();
        var form = project.Forms.Single();
        Designer(Substitute.For<IProjectService>(), form);
        var announced = 0;
        form.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(FormDefinition.Name)) announced++; };

        bus.Publish(new ApplyAllUnsavedChangesEvent { OnlyWhereAhead = true });
        announced.Should().Be(0, "a capture every few seconds must not re-announce every open form");

        bus.Publish(new ApplyAllUnsavedChangesEvent());
        announced.Should().Be(1, "a save's flush is unchanged");
    }
}
