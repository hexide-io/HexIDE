using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Projects;

/// <summary>
/// An add-in's project type that writes its own project file (#3698): what it offers, where and when its writer is asked,
/// and the guards around the write. The writer here renders a format nothing like VB6's: a header line, then each member's
/// path from the project's folder, one to a line.
/// </summary>
public sealed class ContributedProjectFileTests : IDisposable
{
    private static readonly AddinOrigin Provider = new("provider", "A provider");
    private const string Module = "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n";

    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-contributed-project-file-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly ContributedProjects _types = new() { ReadFile = path => Task.FromResult(File.ReadAllBytes(path)) };
    private readonly StandInWriter _writer = new();
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly ProjectService _service;
    private IAddinProjectChanges? _changes;

    public ContributedProjectFileTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CodeFile, Module);
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\n");

        var projects = _projects;
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        projects.When(m => m.UnloadProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Remove(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ContributedProject.SavedInPlace" => "{0} is saved where it was opened, because {1} writes its project file only there.",
            "Str.ContributedProject.RenderRefused" => "{0} wasn't written by {1}.",
            "Str.ContributedProject.MemberOverProjectFile" => "{0} would go over {1}.",
            "Str.ContributedProject.MembershipReadOnly" => "{0} is {1}'s to change.",
            "Str.ContributedProject.WithdrawWithChanges" => "{0} has unsaved files: {1}",
            "Str.ContributedProject.WithdrawnWithChanges" => "{0} has gone, and these weren't saved: {1}",
            var key => key.StartsWith("Str.ContributedProject.", StringComparison.Ordinal) ? key + " {0} {1}" : key,
        });
        _windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(),
            Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>()).Returns(Task.FromResult(MessageBoxResult.Ok));
        _service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), strings,
            Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry(), contributed: _types);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string ProjectFile => Path.Join(_dir, "Ledger.sproj");
    private string CodeFile => Path.Join(_dir, "Code.bas");

    private IDisposable Contribute(IProjectFileWriter? writer) =>
        _types.Register(new AddinProjectType("Stand-in projects")
        {
            FileNames = ["*.sproj"],
            ProjectFile = writer,
            OpenAsync = (path, changes, _) =>
            {
                _changes = changes;
                return Task.FromResult(new AddinProjectContent("Ledger") { Members = Listed(path) });
            },
        }, Provider);

    private IDisposable Contribute() => Contribute(_writer);

    // What the add-in reads: each line after the first is a member, from the project's folder.
    private static AddinProjectMember[] Listed(string path) =>
        [.. File.ReadAllLines(path).Skip(1).Where(line => line.Length > 0).Select(line =>
        {
            var full = Path.GetFullPath(Path.Join(Path.GetDirectoryName(path)!, line));
            return new AddinProjectMember(full, Path.GetExtension(full).ToLowerInvariant() switch
            {
                ".bas" => AddinMemberKind.StandardModule,
                ".cls" => AddinMemberKind.ClassModule,
                _ => AddinMemberKind.Other,
            });
        })];

    private async Task<ProjectDefinition> Opened()
    {
        await _service.OpenProject(ProjectFile);
        return _loaded.Should().ContainSingle().Subject;
    }

    // Whether the project's own file is edited, as crash recovery sees it.
    private bool ProjectFileEdited() => _service.CaptureUnsavedWork().SingleOrDefault()?.ProjectFile is not null;

    private string? ProjectFileCopy() => _service.CaptureUnsavedWork().SingleOrDefault()?.ProjectFile?.Text;

    private void PickForSave(string path) =>
        _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns(path);

    private void AnswerConflict(Action<FileConflictViewModel> choose) =>
        _windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var conflict = call.Arg<FileConflictViewModel>();
            var chosen = true;
            conflict.CloseRequested += result => chosen = result;
            choose(conflict);
            return Task.FromResult(chosen);
        });

    private Task ToldOf(string text) => _windows.Received().MessageBox(Arg.Is<string>(said => said.Contains(text)),
        Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());

    // ── What a type offers ─────────────────────────────────────────────────────────────────────

    [AvaloniaTheory]
    [InlineData(false, ProjectFileParts.None, false, false)]
    [InlineData(true, ProjectFileParts.None, true, false)]
    [InlineData(true, ProjectFileParts.Members, true, true)]
    [InlineData(true, ProjectFileParts.Members | ProjectFileParts.References | ProjectFileParts.Properties, true, true)]
    public async Task A_type_offers_its_project_file_with_what_its_writer_carries(bool hasWriter, ProjectFileParts carries,
        bool writesProjectFile, bool changesMembership)
    {
        _writer.Carries = carries;
        using var type = Contribute(hasWriter ? _writer : null);

        var offers = (await Opened()).Offers;

        offers.WritesProjectFile.Should().Be(writesProjectFile);
        offers.ChangesMembership.Should().Be(changesMembership);
        offers.SetsReferences.Should().Be(hasWriter && carries.HasFlag(ProjectFileParts.References));
        offers.MovesProjectFile.Should().BeFalse();
        offers.SetsProperties.Should().BeFalse("Project Properties comes with #3698's third change");
        offers.SetsComponents.Should().BeFalse("Components is VB6's project-file text, slice 10's");
        offers.Makes.Should().BeFalse();
        offers.Runs.Should().BeFalse();
    }

    [Fact]
    public void Built_in_projects_offer_moving_their_project_file() =>
        ProjectOffers.Everything.MovesProjectFile.Should().BeTrue();

    [AvaloniaFact]
    public async Task References_are_saved_where_the_writer_carries_them()
    {
        _writer.Carries |= ProjectFileParts.References;
        using var type = Contribute();
        var project = await Opened();
        await _service.EditProjectReferences(project);
        await _windows.Received(1).ShowDialog(Arg.Any<ReferencesViewModel>());
        project.SetReferences([new VbReference("probe", "1.0", 0, null, "Probe library")]);

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\nref Probe library\r\n");
    }

    [AvaloniaFact]
    public async Task References_are_refused_where_the_writer_does_not_carry_them()
    {
        using var type = Contribute();
        await _service.EditProjectReferences(await Opened());

        await ToldOf("Str.ContributedProject.SettingsReadOnly");
        await _windows.DidNotReceive().ShowDialog(Arg.Any<ReferencesViewModel>());
    }

    [AvaloniaFact]
    public async Task Save_project_as_moves_only_the_vbp_and_names_the_project_saved_in_place()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        var vbp = Path.Join(_dir, "Probe.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nName=\"Probe\"\r\n");
        await _service.OpenProject(vbp);
        var moved = Path.Join(_dir, "Moved.vbp");
        PickForSave(moved);

        await _service.SaveAllProjects(true);

        await _windows.Received(1).SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
        File.Exists(moved).Should().BeTrue();
        project.AbsolutePath.Should().Be(ProjectFile);
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\n");
        await ToldOf("Ledger is saved where it was opened, because A provider writes its project file only there.");
    }

    [AvaloniaFact]
    public async Task Save_project_as_does_not_name_a_project_without_a_writer()
    {
        using var type = Contribute(null);
        await Opened();
        var before = File.ReadAllBytes(ProjectFile);
        var vbp = Path.Join(_dir, "Probe.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nName=\"Probe\"\r\n");
        await _service.OpenProject(vbp);
        var moved = Path.Join(_dir, "Moved.vbp");
        PickForSave(moved);

        await _service.SaveAllProjects(true);

        File.Exists(moved).Should().BeTrue();
        File.ReadAllBytes(ProjectFile).Should().Equal(before);
        await _windows.DidNotReceive().MessageBox(Arg.Is<string>(said => said.Contains("is saved where it was opened")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task Save_project_as_with_a_dismissed_picker_writes_nothing_and_names_no_project()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        var before = File.ReadAllBytes(ProjectFile);
        var vbp = Path.Join(_dir, "Probe.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nName=\"Probe\"\r\n");
        var vbpBefore = File.ReadAllBytes(vbp);
        await _service.OpenProject(vbp);
        _windows.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>()).Returns((string?)null);

        var save = () => _service.SaveAllProjects(true);

        await save.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllBytes(ProjectFile).Should().Equal(before);
        File.ReadAllBytes(vbp).Should().Equal(vbpBefore);
        await _windows.DidNotReceive().MessageBox(Arg.Is<string>(said => said.Contains("is saved where it was opened")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task A_project_opens_unedited_and_its_file_is_written_in_its_writer_s_encoding()
    {
        _writer.Encoding = new UTF8Encoding(true);
        using var type = Contribute();

        var project = await Opened();

        project.TextEncoding.Should().Be(new Vb6TextEncoding(65001, true));
        ProjectFileEdited().Should().BeFalse("a fresh render of what it read is what it rendered at open");
    }

    // ── Membership ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_module_added_is_saved_and_the_project_file_lists_it()
    {
        _writer.Encoding = new UTF8Encoding(true);
        using var type = Contribute();
        var project = await Opened();
        await _service.AddNewModule(project, "Extra", ModuleKind.StandardModule);
        PickForSave(Path.Join(_dir, "Extra.bas"));

        ProjectFileEdited().Should().BeTrue();
        await _service.SaveProject(project, saveAs: false);

        File.Exists(Path.Join(_dir, "Extra.bas")).Should().BeTrue();
        File.ReadAllBytes(ProjectFile).Should().Equal([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("ledger\r\nCode.bas\r\nExtra.bas\r\n")]);
        ProjectFileEdited().Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_member_removed_is_left_out_of_the_project_file_and_its_own_file_is_kept()
    {
        using var type = Contribute();
        var project = await Opened();

        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Be("ledger\r\n");
        File.ReadAllText(CodeFile).Should().Be(Module);
    }

    [AvaloniaFact]
    public async Task A_type_whose_file_carries_no_members_refuses_an_add_and_a_remove()
    {
        _writer.Carries = ProjectFileParts.None;
        using var type = Contribute();
        var project = await Opened();

        var add = () => _service.AddNewModule(project, "Extra", ModuleKind.StandardModule, interactive: false);
        await add.Should().ThrowAsync<InvalidOperationException>();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));

        project.Modules.Should().ContainSingle("a member the project file can't drop isn't dropped");
        await ToldOf("is A provider's to change");
    }

    // ── Where the project file is written ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task Save_project_as_writes_the_project_file_where_it_was_opened_and_asks_for_nowhere_else()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        var other = Path.Join(_dir, "other");
        PickForSave(Path.Join(other, "Ledger.sproj"));

        await _service.SaveProject(project, saveAs: true);

        File.ReadAllText(ProjectFile).Should().Be("ledger\r\n");
        Directory.Exists(other).Should().BeFalse();
        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>(), Arg.Any<string?>());
    }

    [AvaloniaFact]
    public async Task Writing_the_project_to_another_folder_is_refused_before_anything_is_written()
    {
        using var type = Contribute();
        var project = await Opened();
        var other = Path.Join(_dir, "elsewhere");

        var write = () => _service.SaveProjectToDirectory(project, other);

        await write.Should().ThrowAsync<RenderRefusal>("its project file is written only where it was opened");
        Directory.Exists(other).Should().BeFalse();
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\n");
    }

    [AvaloniaFact]
    public async Task A_module_saved_as_its_project_s_own_file_is_refused_before_anything_is_written()
    {
        using var type = Contribute();
        var project = await Opened();
        PickForSave(ProjectFile);

        var save = () => _service.SaveModule(project.Modules.Single(), saveAs: true);

        await save.Should().ThrowAsync<MemberOverProjectFileRefusal>();
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\n");
        await ToldOf("Code would go over Ledger.sproj.");
    }

    [AvaloniaFact]
    public async Task A_vbp_project_s_module_saved_over_its_vbp_is_refused_too()
    {
        var vbp = Path.Join(_dir, "Probe.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nModule=Code; Code.bas\r\nName=\"Probe\"\r\n");
        var before = File.ReadAllBytes(vbp);
        await _service.OpenProject(vbp);
        var project = _loaded.Should().ContainSingle().Subject;
        PickForSave(vbp);

        var save = () => _service.SaveModule(project.Modules.Single(), saveAs: true);

        await save.Should().ThrowAsync<MemberOverProjectFileRefusal>();
        File.ReadAllBytes(vbp).Should().Equal(before);
    }

    [AvaloniaFact]
    public async Task No_member_is_ever_the_project_file_whichever_route_names_it()
    {
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\nLedger.sproj\r\n");
        using var type = Contribute();
        var project = await Opened();
        await ToldOf("is the project's own file");

        _changes!.MemberAdded(new AddinProjectMember(ProjectFile, AddinMemberKind.Other));
        _changes.MemberMoved(CodeFile, ProjectFile);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        project.RelatedDocuments.Should().BeEmpty();
        project.Modules.Single().AbsolutePath.Should().Be(CodeFile);
    }

    // ── A writer that refuses ──────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_writer_that_throws_at_a_save_leaves_every_file_as_it_was()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.AddNewModule(project, "Extra", ModuleKind.StandardModule);
        PickForSave(Path.Join(_dir, "Extra.bas"));
        _writer.Throws = new IOException("the ledger is locked");

        var save = () => _service.SaveProject(project, saveAs: false);

        await save.Should().ThrowAsync<RenderRefusal>();
        File.Exists(Path.Join(_dir, "Extra.bas")).Should().BeFalse("the project file is rendered before anything is written");
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\n");
        await ToldOf("the ledger is locked");
    }

    [AvaloniaFact]
    public async Task A_writer_that_throws_keeps_the_last_answer_and_the_last_copy()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        var copy = ProjectFileCopy();

        _writer.Throws = new IOException("gone quiet");

        copy.Should().Be("ledger\r\n");
        ProjectFileEdited().Should().BeTrue("the last render that answered said it was edited");
        ProjectFileCopy().Should().Be(copy);
    }

    [AvaloniaFact]
    public async Task A_writer_that_fails_at_open_is_never_asked_to_write_that_session()
    {
        _writer.Throws = new IOException("not today");
        using var type = Contribute();
        var project = await Opened();
        _writer.Throws = null;
        var renders = _writer.Renders;
        await _service.AddNewModule(project, "Extra", ModuleKind.StandardModule);
        PickForSave(Path.Join(_dir, "Extra.bas"));

        var save = () => _service.SaveProject(project, saveAs: false);

        await save.Should().ThrowAsync<RenderRefusal>();
        _writer.Renders.Should().Be(renders, "with no baseline, nothing can tell what its file would change");
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\n");
        await ToldOf("not today");
    }

    // ── Withdrawal ─────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_writer_is_never_called_once_its_type_is_withdrawn()
    {
        var type = Contribute();
        var project = await Opened();
        // Edited and captured, so every route after this has something it would render if it could.
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        _service.CaptureUnsavedWork();
        type.Dispose();
        var renders = _writer.Renders;

        _service.CaptureUnsavedWork();
        await _service.SaveProject(project, saveAs: false);
        await _service.ConfirmAddinWithdrawal("provider");

        _writer.Renders.Should().Be(renders);
        project.Offers.WritesProjectFile.Should().BeFalse();
        project.Offers.ChangesMembership.Should().BeFalse();
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\n");
    }

    [AvaloniaFact]
    public async Task Turning_the_add_in_off_asks_about_an_unsaved_project_file_and_Save_writes_it()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        _windows.MessageBox(Arg.Is<string>(said => said.Contains("has unsaved files")), Arg.Any<string?>(),
            MessageBoxButtons.YesNoCancel, Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>())
            .Returns(Task.FromResult(MessageBoxResult.Yes));

        var mayGo = await _service.ConfirmAddinWithdrawal("provider");

        mayGo.Should().BeTrue();
        await ToldOf("A provider has unsaved files:   • Ledger");
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\n");
    }

    [AvaloniaFact]
    public async Task An_add_in_that_goes_with_nobody_asked_leaves_its_project_file_s_last_copy_and_says_so()
    {
        var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        var copy = ProjectFileCopy();

        type.Dispose();
        Dispatcher.UIThread.RunJobs();

        await ToldOf("A provider has gone, and these weren't saved:   • Ledger");
        ProjectFileCopy().Should().Be(copy, "crash recovery keeps the copy it last took");
    }

    // ── A file changed outside HexIDE ──────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_file_changed_outside_with_nothing_of_HexIDE_s_to_write_is_left_as_it_is()
    {
        using var type = Contribute();
        var project = await Opened();
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\nTheirs.bas\r\n");

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        await _windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }

    [AvaloniaFact]
    public async Task A_file_changed_outside_is_asked_about_and_Keep_my_changes_overwrites_it()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        AnswerConflict(conflict => conflict.KeepAll());

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Be("ledger\r\n");
        await _windows.Received(1).ShowDialog(Arg.Is<FileConflictViewModel>(conflict =>
            conflict.CanCancel && conflict.Files.Single().Name == "Ledger.sproj"));
    }

    [AvaloniaFact]
    public async Task A_file_changed_outside_and_Cancel_leaves_every_file_as_it_was()
    {
        using var type = Contribute();
        var project = await Opened();
        await _service.AddNewModule(project, "Extra", ModuleKind.StandardModule);
        PickForSave(Path.Join(_dir, "Extra.bas"));
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        AnswerConflict(conflict => conflict.Cancel());

        var save = () => _service.SaveProject(project, saveAs: false);

        await save.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        File.Exists(Path.Join(_dir, "Extra.bas")).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_file_changed_outside_and_Reload_opens_the_project_again_as_the_file_now_says()
    {
        File.WriteAllText(Path.Join(_dir, "Theirs.bas"), Module.Replace("Code", "Theirs"));
        using var type = Contribute();
        var project = await Opened();
        await _service.RemoveDocument(DocumentIdentity.For(project.Modules.Single()));
        File.WriteAllText(ProjectFile, "ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        AnswerConflict(conflict => conflict.ReloadAll());

        var save = () => _service.SaveProject(project, saveAs: false);

        await save.Should().ThrowAsync<OperationCanceledException>("a reload saves nothing");
        File.ReadAllText(ProjectFile).Should().Be("ledger\r\nCode.bas\r\nTheirs.bas\r\n");
        _loaded.Should().ContainSingle().Which.Modules.Select(module => module.Name).Should().Equal("Code", "Theirs");
    }

    // ── Echoes of HexIDE's own write ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task A_report_the_project_already_matches_changes_nothing()
    {
        using var type = Contribute();
        var project = await Opened();

        _changes!.MemberRemoved(Path.Join(_dir, "Gone.bas"));
        _changes.MemberAdded(new AddinProjectMember(CodeFile, AddinMemberKind.StandardModule));
        _changes.ReferencesChanged([]);
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(50);
        Dispatcher.UIThread.RunJobs();

        project.Modules.Should().ContainSingle();
        ProjectFileEdited().Should().BeFalse();
    }

    // ── The language server ────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task The_language_server_is_given_no_project_text_for_a_project_file_an_add_in_writes()
    {
        using var type = Contribute();
        await Opened();

        var workspace = new ProjectLspWorkspace(() => _projects, new Vb6ProjectFileFormat());

        workspace.Projects.Should().ContainSingle().Which.ProjectText
            .Should().BeNull("the text would be the add-in's format, which a VB6 server doesn't read");
    }

    // ── The guard ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void No_product_code_renders_a_project_file_past_its_project_s_writer() =>
        ProductSource.FilesMatching(new Regex(@"\b_?format\s*\.\s*RenderProject\b"),
                // The one call, which a project's writer goes before, and the language server's text, which is a .vbp's only.
                "ProjectService.ProjectFile.cs", "ProjectLspWorkspace.cs")
            .Should().BeEmpty("a project an add-in opened has its file written by that add-in's writer, or by nothing");

    // A writer for the stand-in format: a header line, then each member's path from the project's folder.
    private sealed class StandInWriter : IProjectFileWriter
    {
        public ProjectFileParts Carries { get; set; } = ProjectFileParts.Members;

        public Encoding Encoding { get; set; } = new UTF8Encoding(false);

        public Exception? Throws { get; set; }

        public int Renders { get; private set; }

        public string RenderProject(ProjectDefinition project, string path, Func<object, string?>? pathOf)
        {
            Renders++;
            if (Throws is not null)
                throw Throws;
            var folder = Path.GetDirectoryName(path)!;
            var lines = new List<string> { "ledger" };
            foreach (var member in project.Forms.Cast<object>().Concat(project.Modules).Concat(project.RelatedDocuments))
            {
                var at = member switch
                {
                    FormDefinition form => form.AbsolutePath,
                    ModuleDefinition module => module.AbsolutePath,
                    RelatedDocumentDefinition document => document.AbsolutePath,
                    _ => null,
                } ?? pathOf?.Invoke(member);
                if (at is not null)
                    lines.Add(Path.GetRelativePath(folder, at));
            }
            foreach (var reference in project.References)
                lines.Add("ref " + reference.Name);
            return string.Join("\r\n", lines) + "\r\n";
        }
    }
}
