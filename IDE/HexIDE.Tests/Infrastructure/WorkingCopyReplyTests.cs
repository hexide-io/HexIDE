using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.Localization;

namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class WorkingCopyReplyTests
{
    [Theory]
    [InlineData("set_control_property")]
    [InlineData("set_file_content")]
    [InlineData("add_control")]
    public void First_contact_explains_the_new_project_working_copy(string name)
    {
        var description = ToolSource.Tools.Single(t => t.Name == name).Description;
        description.Should().Contain("working copy").And.Contain("temp folder").And.Contain("without a picker");
        description.Length.Should().BeLessThanOrEqualTo(1900);
        description.Should().Contain("outside the project's folder").And.Contain("through links")
            .And.Contain("recovery file").And.Contain("how to restore");
    }

    [AvaloniaTheory]
    [InlineData(true, "property", false)]
    [InlineData(false, "property", false)]
    [InlineData(true, "source", false)]
    [InlineData(false, "source", false)]
    [InlineData(true, "control", false)]
    [InlineData(false, "control", false)]
    [InlineData(false, "property", true)]
    [InlineData(false, "source", true)]
    [InlineData(false, "control", true)]
    public async Task Write_reply_distinguishes_the_working_copy_from_a_chosen_file(bool scratch, string operation, bool saveFails)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-2707-" + Guid.NewGuid().ToString("N"));
        var form = project.Forms[0];
        form.AbsolutePath = Path.Combine(project.WorkingDirectory + (scratch ? "" : "-chosen"), "Form1.frm");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        var events = new EventBus();
        var windows = Substitute.For<IWindowManager>();
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, events,
            projects, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        var directory = Path.GetDirectoryName(form.AbsolutePath)!;
        await service.SaveProjectToDirectory(project, directory);
        try
        {
            var localization = Substitute.For<ILocalizationService>();
            var toolbox = new ToolBoxToolViewModel(dock, Substitute.For<IComponentRegistry>(), localization);
            using var designer = operation == "control"
                ? new FormEditViewModel(toolbox, events, service, Substitute.For<IEditorService>(), windows,
                    Substitute.For<ISettingsService>(), localization).Initialize(form)
                : null;
            if (designer is not null)
                dock.OpenDocuments.Returns([designer]);
            var writer = (IProjectService)service;
            if (saveFails)
            {
                writer = Substitute.For<IProjectService>();
                writer.SaveForm(form, false, interactive: false, rewrites: Arg.Any<SaveRewriteReport>())
                    .Returns(Task.FromException<bool>(new IOException("disk unavailable")));
            }
            using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
                null!, Substitute.For<IProjectRunnerService>(), writer, events, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            var tools = new HexIdeTools(context);
            var previousBytes = File.ReadAllBytes(form.AbsolutePath!);
            var companion = Path.ChangeExtension(form.AbsolutePath, ".frx");
            File.WriteAllBytes(companion, [0, 128, 255]);
            bool success;
            string? note;
            if (operation == "control")
            {
                var added = await tools.AddControlAsync("Form1", "CommandButton", 10, 10, 81, 33);
                success = added.Success;
                note = saveFails ? added.Error : added.Note;
            }
            else
            {
                var result = operation == "source"
                    ? await tools.SetFileContentAsync("Form1", "Option Explicit")
                    : await tools.SetControlPropertyAsync("Form1", "Form1", "Caption", "Go");
                success = result.Success;
                note = saveFails ? result.Error : result.Note;
            }
            success.Should().Be(!saveFails);
            var backup = Directory.GetFiles(directory, "Form1.frm.hexide-recovery-*").Should().ContainSingle().Subject;
            File.ReadAllBytes(backup).Should().Equal(previousBytes);
            note.Should().Contain(backup).And.Contain("To recover");
            var companionBackup = Directory.GetFiles(directory, "Form1.frx.hexide-recovery-*").Should().ContainSingle().Subject;
            File.ReadAllBytes(companionBackup).Should().Equal(new byte[] { 0, 128, 255 });
            note.Should().Contain(companionBackup);
            note.Should().Contain(form.AbsolutePath);
            if (saveFails)
            {
                note.Should().Contain("disk unavailable");
                File.ReadAllBytes(form.AbsolutePath!).Should().Equal(previousBytes);
                return;
            }
            if (scratch)
                note.Should().Contain("working copy").And.Contain("unsaved changes").And.Contain("Save Project");
            else
                note.Should().Contain("Wrote the file").And.NotContain("working copy");
            File.ReadAllText(form.AbsolutePath!).Should().Contain(operation switch
            {
                "source" => "Option Explicit", "control" => "Command1", _ => "Go"
            });
            service.HasUnsavedChanges(form).Should().Be(scratch);
            await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData("source", "outside")]
    [InlineData("source", "locked")]
    [InlineData("source", "nohome")]
    [InlineData("source", "picker")]
    [InlineData("source", "traversal")]
    [InlineData("property", "outside")]
    [InlineData("property", "locked")]
    [InlineData("property", "nohome")]
    [InlineData("property", "picker")]
    [InlineData("property", "traversal")]
    [InlineData("control", "outside")]
    [InlineData("control", "locked")]
    [InlineData("control", "nohome")]
    [InlineData("control", "picker")]
    [InlineData("control", "traversal")]
    public async Task Write_refuses_external_and_linked_targets_before_editing(string operation, string attack)
        => await CheckRefusal(operation, attack);

    public static bool CanCreateSymbolicLinks => !OperatingSystem.IsWindows();

    [AvaloniaTheory(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    [InlineData("source")]
    [InlineData("property")]
    [InlineData("control")]
    public async Task Write_refuses_linked_targets_before_editing(string operation)
        => await CheckRefusal(operation, "link");

    private static async Task CheckRefusal(string operation, string attack)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-2707-" + Guid.NewGuid().ToString("N"));
        var form = project.Forms[0];
        form.AbsolutePath = Path.Combine(project.WorkingDirectory, "Form1.frm");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        var events = new EventBus();
        var windows = Substitute.For<IWindowManager>();
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, events,
            projects, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        var directory = Path.GetDirectoryName(form.AbsolutePath)!;
        await service.SaveProjectToDirectory(project, directory);
        var outside = directory + "-outside";
        Directory.CreateDirectory(outside);
        var originalPath = form.AbsolutePath!;
        var unsafePath = Path.Combine(outside, "Victim.frm");
        File.WriteAllText(unsafePath, "user bytes");
        if (attack == "link")
        {
            var link = Path.Combine(directory, "linked");
            Directory.CreateSymbolicLink(link, outside);
            unsafePath = Path.Combine(link, "Victim.frm");
        }
        else if (attack == "traversal")
            unsafePath = Path.Combine(directory, "..", Path.GetFileName(outside), "Victim.frm");
        if (attack == "locked") unsafePath = originalPath;
        if (attack == "nohome") { project.AbsolutePath = null; project.WorkingDirectory = null; }
        using var locked = attack == "locked"
            ? new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        form.AbsolutePath = attack == "picker" ? null : unsafePath;
        if (attack == "picker") ScriptedFileDialogs.AnswerNextWith(unsafePath);
        var codeBefore = form.Code;
        var captionBefore = form.Components[0].GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.CaptionProperty);
        var countBefore = form.Components.Count;
        try
        {
            var localization = Substitute.For<ILocalizationService>();
            var toolbox = new ToolBoxToolViewModel(dock, Substitute.For<IComponentRegistry>(), localization);
            using var designer = operation == "control"
                ? new FormEditViewModel(toolbox, events, service, Substitute.For<IEditorService>(), windows,
                    Substitute.For<ISettingsService>(), localization).Initialize(form)
                : null;
            if (designer is not null)
                dock.OpenDocuments.Returns([designer]);
            using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
                null!, Substitute.For<IProjectRunnerService>(), service, events, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            var tools = new HexIdeTools(context);
            bool success;
            string? note;
            if (operation == "control")
            {
                var added = await tools.AddControlAsync("Form1", "CommandButton", 10, 10, 81, 33);
                success = added.Success;
                note = added.Error;
            }
            else
            {
                var result = operation == "source"
                    ? await tools.SetFileContentAsync("Form1", "Option Explicit")
                    : await tools.SetControlPropertyAsync("Form1", "Form1", "Caption", "Go");
                success = result.Success;
                note = result.Error;
            }
            success.Should().BeFalse();
            note.Should().Contain("Refused").And.Contain(unsafePath);
            File.ReadAllText(Path.Combine(outside, "Victim.frm")).Should().Be("user bytes");
            if (attack == "picker")
            {
                ScriptedFileDialogs.Pending.Should().Be(1);
                note.Should().Contain("clear_file_dialog_answers");
            }
            form.Code.Should().Be(codeBefore);
            form.Components.Count.Should().Be(countBefore);
            form.Components[0].GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.CaptionProperty).Should().Be(captionBefore);
        }
        finally { locked?.Dispose(); ScriptedFileDialogs.Clear(); Directory.Delete(directory, recursive: true); Directory.Delete(outside, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData("../Form1")]
    [InlineData("/Form1")]
    [InlineData(@"C:\Form1")]
    public async Task Direct_writers_refuse_path_arguments_by_name(string name)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var tools = new HexIdeTools(context);
        var source = await tools.SetFileContentAsync(name, "Option Explicit");
        var property = await tools.SetControlPropertyAsync(name, "Form1", "Caption", "Go");
        var control = await tools.AddControlAsync(name, "CommandButton", 0, 0, 81, 33);
        foreach (var error in new[] { source.Error, property.Error, control.Error })
            error.Should().Contain(name).And.Contain("not a rooted or relative path").And.Contain("Nothing was changed");
        source.Success.Should().BeFalse();
        property.Success.Should().BeFalse();
        control.Success.Should().BeFalse();
        var added = await tools.AddFileAsync(name, "Form");
        added.Success.Should().BeFalse();
        added.Error.Should().Contain(name).And.Contain("not a valid name");
        project.Forms.Should().ContainSingle();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Project_info_explains_a_project_without_a_home(bool scratch)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-2707");
        project.AbsolutePath = scratch ? Path.Combine(project.WorkingDirectory, "Project1.vbp") : null;
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var result = await new HexIdeTools(context).GetProjectInfoAsync(TestContext.Current.CancellationToken);
        result.Note.Should().Contain("Project1").And.Contain("no chosen home").And.Contain("Save Project");
    }
}
