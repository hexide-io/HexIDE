using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A new form or module gets its file when the project is saved, not when it is added, as VB6 does it.
///
/// <para>
/// Measured against the VB6 IDE (#489, <c>docs/vb6-fidelity-oracle/when-a-document-gets-a-file-and-what-a-dangling-startup-does.md</c>): no gesture that creates a document
/// writes a file, in an unsaved project or a saved one, and discarding an unsaved document leaves nothing behind.
/// HexIDE wrote each one at once, into the project's folder or, for a project never saved, a scratch folder under
/// <c>%TEMP%</c>. Its first Save Project then wrote the <c>.vbp</c> where the developer chose and left those members
/// where they were, so the project file reached back into <c>%TEMP%</c> for them
/// (<c>Module=Module1; ..\hexide_Project1_&lt;guid&gt;\Module1.bas</c>), and the next temp clean-up lost them
/// (hexide-io/HexIDE#500).
/// </para>
/// </summary>
public class WriteAtSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-writeatsave-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public WriteAtSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService(IUserSidecarService? realSidecar = null, IHeaderRefresher? refresher = null, IProjectManager? manager = null)
    {
        var projectManager = manager ?? Substitute.For<IProjectManager>();
        if (manager is null)
            projectManager.LoadedProjects.Returns(_ => loaded);
        // Every picker is answered with the chosen folder and the name it suggests, as a developer accepting
        // each suggestion there would.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionWriter(),
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            realSidecar ?? sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            refresher ?? Substitute.For<IHeaderRefresher>(),
            new Vb6ProjectFileFormat(), new ComponentRegistry());
    }

    /// <summary>A project as File ▸ New Project leaves it: no file and no folder.</summary>
    private ProjectDefinition Unsaved()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        loaded.Add(project);
        return project;
    }

    private static Task<DocumentIdentity> AddNew(ProjectService svc, ProjectDefinition project, string kind, string name) =>
        kind switch
        {
            "Form" => svc.AddNewForm(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "Module" => svc.AddNewModule(project, name, ModuleKind.StandardModule, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "Class" => svc.AddNewModule(project, name, ModuleKind.ClassModule, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "UserControl" => svc.AddNewUserControl(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            _ => svc.AddNewPropertyPage(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
        };

    // Measured in #498 and the oracle's when-a-document-gets-a-file-and-what-a-dangling-startup-does page.
    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    public async Task PathlessMemberPickerStartsInItsSavedProjectsFolder(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();
        project.AbsolutePath = Path.Join(dir, "Project1.vbp");
        await AddNew(svc, project, kind, "NewMember");

        await svc.SaveProject(project, saveAs: false);

        await windowManager.Received(1).SaveFilePickerAsync(
            Arg.Is<Avalonia.Platform.Storage.FilePickerSaveOptions>(o => o.SuggestedFileName == "NewMember." + (kind == "Form" ? "frm" : "bas")),
            dir);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsavedProjectCarriesOnlyThisSavesPreviousPickerDirectory(bool scratch)
    {
        var svc = MakeService();
        var project = Unsaved();
        await svc.AddNewForm(project, "Form1", interactive: false);
        await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        if (scratch)
        {
            project.WorkingDirectory = Path.Join(dir, "scratch");
            project.AbsolutePath = Path.Join(project.WorkingDirectory, "Project1.vbp");
        }
        var nextDirectory = Path.Join(dir, "next");
        Directory.CreateDirectory(nextDirectory);
        var calls = new List<(string? Name, string? Directory)>();
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                var name = ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName;
                calls.Add((name, ci.ArgAt<string?>(1)));
                return Task.FromResult<string?>(Path.Join(name == "Module1.bas" ? nextDirectory : dir, name));
            });

        await svc.SaveProject(project, saveAs: false);

        calls.Should().Equal(("Form1.frm", null), ("Module1.bas", dir), ("Project1.vbp", nextDirectory));
        project.WorkingDirectory.Should().Be(scratch ? Path.Join(dir, "scratch") : null);
        calls.Clear();
        var second = Unsaved();
        await svc.AddNewForm(second, "Form2", interactive: false);
        await svc.SaveProject(second, saveAs: false);
        calls[0].Should().Be(("Form2.frm", null));
    }

    [Fact]
    public async Task SavingPreservesFilesAtOldStagingNames()
    {
        var svc = MakeService();
        var project = Unsaved();
        await svc.AddNewForm(project, "Form1", interactive: false);
        await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        string[] names = ["Project1.vbp", "Form1.frm", "Module1.bas"];
        foreach (var name in names)
            File.WriteAllText(Path.Join(dir, name + ".tmp"), "user file");

        await svc.SaveProject(project, saveAs: false);

        foreach (var name in names)
        {
            File.Exists(Path.Join(dir, name)).Should().BeTrue();
            File.ReadAllText(Path.Join(dir, name + ".tmp")).Should().Be("user file");
        }
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("Class")]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task Adding_a_member_to_an_unsaved_project_writes_nothing(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull("a new document has no file until the project is saved");
        project.WorkingDirectory.Should().BeNull("Add chose no folder, because it wrote nothing");
        DocumentLookup.DocumentsOf(project).Should().Contain(added);
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("UserControl")]
    public async Task Adding_a_member_to_a_saved_project_writes_nothing_in_its_folder(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();
        project.AbsolutePath = Path.Join(dir, "Project1.vbp");

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull();
        Directory.GetFiles(dir).Should().BeEmpty("VB6 writes neither the new file nor the project file until it is saved");
    }

    [Theory]
    [InlineData("Form", false)]
    [InlineData("Module", false)]
    [InlineData("Class", false)]
    [InlineData("UserControl", false)]
    [InlineData("PropertyPage", false)]
    [InlineData("Form", true)]
    [InlineData("Module", true)]
    [InlineData("Class", true)]
    [InlineData("UserControl", true)]
    [InlineData("PropertyPage", true)]
    public async Task A_new_member_has_unsaved_changes_until_its_first_save(string kind, bool savedProject)
    {
        var svc = MakeService();
        var project = Unsaved();
        if (savedProject)
            await svc.SaveProject(project, saveAs: false);

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull();
        bool HasUnsavedChanges() => added.Form is { } form
            ? svc.HasUnsavedChanges(form)
            : svc.HasUnsavedChanges(added.Module!);
        HasUnsavedChanges().Should().BeTrue("a new document has no file yet (#597)");

        await svc.SaveProject(project, saveAs: false);

        added.AbsolutePath.Should().NotBeNull();
        File.Exists(added.AbsolutePath).Should().BeTrue();
        HasUnsavedChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData("Form", "VERSION 5.00\r\nBegin VB.Form Form1 ")]
    [InlineData("UserControl", "VERSION 5.00\r\nBegin VB.UserControl UserControl1 ")]
    [InlineData("PropertyPage", "VERSION 5.00\r\nBegin VB.PropertyPage PropertyPage1 ")]
    public async Task A_new_designer_document_holds_the_header_its_first_save_will_write(string kind, string opening)
    {
        // Stored at creation, which is the answer #885 asked for: the code window shows the file it will be, and a
        // later refresh has the header it replaces to compare against.
        var svc = MakeService();
        var project = Unsaved();

        var added = await AddNew(svc, project, kind, kind + "1");

        var designer = added.Module is { } module ? module.FormPart!.DesignerText : added.Form!.DesignerText;
        designer.Should().StartWith(opening);
    }

    [Fact]
    public async Task Saving_an_unsaved_project_puts_every_new_member_beside_its_project_file()
    {
        // The probe #500 records, as a regression test.
        var svc = MakeService();
        var project = Unsaved();
        var module = await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        var form = await svc.AddNewForm(project, "Form1", interactive: false);

        await svc.SaveProject(project, saveAs: false);

        module.AbsolutePath.Should().Be(Path.Join(dir, "Module1.bas"));
        form.AbsolutePath.Should().Be(Path.Join(dir, "Form1.frm"));
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo(["Form1.frm", "Module1.bas", "Project1.vbp"]);
        var vbp = File.ReadAllText(Path.Join(dir, "Project1.vbp"));
        vbp.Should().Contain("Form=Form1.frm\r\n").And.Contain("Module=Module1; Module1.bas\r\n");
        vbp.Should().NotContain("..", "a member written beside the project file is named without leaving its folder");
        project.WorkingDirectory.Should().BeNull("neither Add nor the save chose a scratch folder");
    }

    [Fact]
    public async Task A_new_member_is_saved_with_the_header_it_was_created_with()
    {
        var svc = MakeService();
        var project = Unsaved();
        var form = await svc.AddNewForm(project, "Form1", interactive: false);
        var header = form.DesignerText;

        await svc.SaveProject(project, saveAs: false);

        File.ReadAllText(form.AbsolutePath!).Should().StartWith(header!);
    }

    [AvaloniaFact]
    public async Task A_template_form_opens_with_the_header_its_first_save_writes()
    {
        var manager = new ProjectManager(Substitute.For<IEditorService>(), new EventBus());
        var service = MakeService(manager: manager);
        await service.CreateNewProject(IProjectTemplate.StandardEXE);
        var project = manager.LoadedProjects.Single();
        var form = project.Forms.Single();
        using var editor = TemplateEditor(form, new BookmarkService(), new BreakpointService());
        var header = form.DesignerText;

        header.Should().NotBeNullOrEmpty();
        editor.Document.Text.Should().StartWith(header!);
        editor.Document.Text.Should().StartWith("VERSION 5.00");
        await service.SaveProject(project, saveAs: false);

        File.ReadAllBytes(form.AbsolutePath!).Take(Vb6TextFile.Encode(header!, form.TextEncoding).Length)
            .Should().Equal(Vb6TextFile.Encode(header!, form.TextEncoding));
    }

    [AvaloniaFact]
    public async Task Template_marks_set_before_first_save_reopen_on_the_same_statement()
    {
        var projects = new ProjectManager(Substitute.For<IEditorService>(), new EventBus());
        await MakeService(manager: projects).CreateNewProject(IProjectTemplate.StandardEXE);
        var project = projects.LoadedProjects.Single();
        var form = project.Forms.Single();
        form.UpdateCode("Private Sub Form_Load()\r\n    Debug.Print 885\r\nEnd Sub\r\n");
        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        var manager = Substitute.For<IProjectManager>();
        var sidecar = new UserSidecarService(bookmarks, breakpoints, manager,
            new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader())
        {
            Debounce = token => Task.Delay(System.Threading.Timeout.Infinite, token),
        };
        await sidecar.LoadAsync(project);
        using var editor = TemplateEditor(form, bookmarks, breakpoints);
        var line = editor.Document.GetLineByOffset(editor.Document.Text.IndexOf("    Debug.Print", StringComparison.Ordinal)).LineNumber;
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [line]);
        bookmarks.SetBookmarks(document, [line]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        using var refresher = new HeaderRefresher(new Vb6ReadOnlyRegionWriter(), new EventBus(), dock,
            breakpoints, bookmarks, new Vb6ProjectFileFormat());

        await MakeService(sidecar, refresher).SaveProject(project, saveAs: false);
        await sidecar.SaveAsync(project);
        manager.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);
        var reopened = (await new ProjectLoader().LoadAsync(project.AbsolutePath!, new ThrowingSink())).Project;
        await sidecar.LoadAsync(reopened);
        using var reopenedEditor = TemplateEditor(reopened.Forms.Single(), bookmarks, breakpoints);
        var reopenedDocument = DocumentIdentity.For(reopened.Forms.Single());

        breakpoints.GetBreakpoints(reopenedDocument).Should().Equal(line);
        bookmarks.GetBookmarks(reopenedDocument).Should().Equal(line);
        var statement = reopenedEditor.Document.GetLineByNumber(line);
        reopenedEditor.Document.GetText(statement).Should().Be("    Debug.Print 885");
    }

    [AvaloniaTheory]
    [InlineData(ModuleKind.UserControl)]
    [InlineData(ModuleKind.PropertyPage)]
    public async Task Template_designer_modules_hold_their_header_before_the_project_is_added(ModuleKind kind)
    {
        var manager = new ProjectManager(Substitute.For<IEditorService>(), new EventBus());
        var template = new ProjectTemplate("Designer", "generic", true, name =>
        {
            var project = new ProjectDefinition(VBProjectType.EXE, name);
            project.AddModule(ModuleDefinition.NewDesigner(project, "Designer1", kind,
                HexIDE.Runtime.Components.FormComponentClass.ForRoot(
                    kind == ModuleKind.UserControl ? "VB.UserControl" : "VB.PropertyPage")));
            return project;
        });
        string? headerAtAdd = null;
        manager.ProjectLoaded += project => headerAtAdd = project.Modules.Single().FormPart!.DesignerText;
        var service = MakeService(manager: manager);

        await service.CreateNewProject(template);

        headerAtAdd.Should().NotBeNullOrEmpty();
        var module = manager.LoadedProjects.Single().Modules.Single();
        module.FormPart!.DesignerText.Should().Be(headerAtAdd);
    }

    private static CodeEditorViewModel TemplateEditor(FormDefinition form,
        BookmarkService bookmarks, BreakpointService breakpoints) =>
        new CodeEditorViewModel(Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(), Substitute.For<IProjectService>(), new EventBus(),
            Substitute.For<HexIDE.Lsp.ILspClient>(), Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(), bookmarks, breakpoints,
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(),
            new HexIDE.Localization.LocalizationService(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader()).Initialize(form);

    private sealed class ThrowingSink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    // #2678: --newproject writes the initial project into its recorded scratch folder.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saving_a_scratch_project_asks_for_members_before_the_project(bool saveAs)
    {
        var svc = MakeService();
        var project = Unsaved();
        var form = await svc.AddNewForm(project, "Form1", interactive: false);
        var scratch = ProjectService.ProjectFilesDirectory(project);
        try
        {
            await svc.SaveProjectToDirectory(project, scratch);
            var dialogs = new List<string>();
            windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
                .Returns(ci =>
                {
                    var name = ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName!;
                    dialogs.Add(name);
                    Directory.GetFiles(dir).Should().BeEmpty("all dialogs precede every write");
                    return Task.FromResult<string?>(Path.Join(dir, name));
                });

            await svc.SaveAllProjects(saveAs);

            dialogs.Should().Equal("Form1.frm", "Project1.vbp");
            Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo(["Form1.frm", "Project1.vbp"]);
            File.ReadAllText(Path.Join(dir, "Project1.vbp")).ReplaceLineEndings("\n")
                .Should().Contain("Form=Form1.frm\n").And.NotContain("..");
            form.AbsolutePath.Should().Be(Path.Join(dir, "Form1.frm"));
            svc.HasUnsavedChanges(form).Should().BeFalse();
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("UserControl")]
    public async Task Automation_persistence_writes_the_scratch_member_without_a_picker(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();
        var document = await AddNew(svc, project, kind, kind + "1");
        var scratch = ProjectService.ProjectFilesDirectory(project);
        try
        {
            await svc.SaveProjectToDirectory(project, scratch);
            var path = document.AbsolutePath!;
            const string code = "Private Sub Changed()\r\nEnd Sub\r\n";
            if (document.Form is { } form)
                form.UpdateCode(code);
            else
                document.Module!.UpdateCode(code);

            ScriptedFileDialogs.WouldShowPicker(path).Should().BeFalse();
            var saved = document.Form is { } savedForm
                ? await svc.SaveForm(savedForm, saveAs: false, interactive: false)
                : await svc.SaveModule(document.Module!, saveAs: false, interactive: false);

            saved.Should().BeTrue();
            document.AbsolutePath.Should().Be(path);
            File.ReadAllText(path).ReplaceLineEndings("\n").Should().Contain(code.ReplaceLineEndings("\n"));
            await windowManager.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>());
            Directory.GetFiles(dir).Should().BeEmpty();
            (document.Form is { } dirtyForm ? svc.HasUnsavedChanges(dirtyForm) : svc.HasUnsavedChanges(document.Module!))
                .Should().BeTrue("persistence does not give a scratch member a user-chosen home");
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Fact]
    public async Task Scratch_members_do_not_read_as_saved()
    {
        var svc = MakeService();
        var project = Unsaved();
        var form = await svc.AddNewForm(project, "Form1", interactive: false);
        var module = await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        var scratch = ProjectService.ProjectFilesDirectory(project);
        try
        {
            await svc.SaveProjectToDirectory(project, scratch);

            svc.HasUnsavedChanges(form).Should().BeTrue();
            svc.HasUnsavedChanges(module).Should().BeTrue();
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public async Task Cancelling_a_scratch_member_save_writes_nothing(bool saveAs, int cancelAt)
    {
        var svc = MakeService();
        var project = Unsaved();
        var form = await svc.AddNewForm(project, "Form1", interactive: false);
        var module = await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        var scratch = ProjectService.ProjectFilesDirectory(project);
        try
        {
            await svc.SaveProjectToDirectory(project, scratch);
            form.UpdateCode("Private Sub Changed()\r\nEnd Sub\r\n");
            var original = Directory.GetFiles(scratch).ToDictionary(path => path, File.ReadAllBytes);
            var dialogs = new List<string>();
            windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
                .Returns(ci =>
                {
                    var name = ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName!;
                    dialogs.Add(name);
                    return Task.FromResult<string?>(dialogs.Count == cancelAt ? null : Path.Join(dir, name));
                });

            Func<Task> save = () => svc.SaveAllProjects(saveAs);
            await save.Should().ThrowAsync<OperationCanceledException>();

            dialogs.Should().Equal(new[] { "Form1.frm", "Module1.bas" }.Take(cancelAt));
            Directory.GetFiles(dir).Should().BeEmpty();
            foreach (var (path, bytes) in original)
                File.ReadAllBytes(path).Should().Equal(bytes);
            form.AbsolutePath.Should().Be(Path.Join(scratch, "Form1.frm"));
            module.AbsolutePath.Should().Be(Path.Join(scratch, "Module1.bas"));
            project.AbsolutePath.Should().Be(Path.Join(scratch, "Project1.vbp"));
            svc.HasUnsavedChanges(form).Should().BeTrue();
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

}
