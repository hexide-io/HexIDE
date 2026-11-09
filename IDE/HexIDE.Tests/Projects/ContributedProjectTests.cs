using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Runtime.Debugging;
using HexIDE.Projects;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A project type an add-in contributes: Open Project reads its file through the add-in into a project HexIDE edits the
/// code of, never writes the file of, and changes the membership of only as the add-in reports.
/// </summary>
public sealed class ContributedProjectTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-contributed-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly IRecentProjectsService _recent = Substitute.For<IRecentProjectsService>();
    private readonly ContributedProjects _types;
    private readonly HeldReads _reads = new();
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IEventBus _events = Substitute.For<IEventBus>();
    private readonly FileBaselineStore _baselines = new();
    private readonly ProjectService _service;
    private IAddinProjectChanges? _changes;

    public ContributedProjectTests()
    {
        _types = new ContributedProjects { ReadFile = _reads.Read };
        Directory.CreateDirectory(Path.Join(_dir, "src", "Classes"));
        Directory.CreateDirectory(Path.Join(_dir, "docs"));
        File.WriteAllText(ProjectFile, """{ "ProjectInfo": { "Name": "Ledger" } }""");
        // Named in the file, not by it: a module's name is its VB_Name.
        File.WriteAllText(Path.Join(_dir, "src", "Main.bas"), "Attribute VB_Name = \"Startup\"\r\nSub Main()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(_dir, "src", "Classes", "Account.cls"),
            "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Account\"\r\nPublic Balance As Currency\r\n");
        File.WriteAllText(Path.Join(_dir, "Sheet1.doccls"),
            "Attribute VB_Name = \"Sheet1\"\r\nAttribute VB_Base = \"0{00020820-0000-0000-C000-000000000046}\"\r\nPrivate Sub Worksheet_Activate()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(_dir, "README.md"), "# Ledger");

        var projects = _projects;
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.Project.AlreadyOpen" => "Projet {0} déjà ouvert.",
            "Str.ContributedProject.OpenError" => "Could not open {0} with {1}: {2}",
            "Str.ContributedProject.NoReader" => "Str.ContributedProject.NoReader {0}",
            var key => key + " {0} {1}",
        });
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        _service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            _events, projects, _recent,
            Substitute.For<IReferenceLibraryService>(), sidecar, _baselines, strings,
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService()),
            contributed: _types);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    private string ProjectFile => Path.Join(_dir, ".rdproj");

    private AddinProjectContent Content() => new("Ledger")
    {
        Members =
        [
            new(Path.Join(_dir, "src", "Main.bas"), AddinMemberKind.StandardModule),
            new(Path.Join(_dir, "src", "Classes", "Account.cls"), AddinMemberKind.ClassModule),
            new(Path.Join(_dir, "Sheet1.doccls"), AddinMemberKind.DocumentModule),
            new(Path.Join(_dir, "README.md"), AddinMemberKind.Other),
        ],
        References = [new("Scripting", Guid.Parse("420b2830-e718-11cf-893d-00a0c9054228"), 1, 0, @"C:\Windows\System32\scrrun.dll")],
        Folders = ["src", "src/Classes", "docs"],
    };

    private IDisposable Contribute(Func<string, AddinProjectContent>? read = null) => _types.Register(new AddinProjectType("RDCore projects")
    {
        FileNames = [".rdproj"],
        OpenAsync = (path, changes, _) =>
        {
            _changes = changes;
            return Task.FromResult((read ?? (_ => Content()))(path));
        },
    }, RDCore);

    private static readonly AddinOrigin RDCore = new("rdcore", "RDCore for HexIDE");
    private static readonly AddinOrigin Other = new("other", "Other add-in");

    private async Task<ProjectDefinition> Opened()
    {
        await _service.OpenProject(ProjectFile);
        return _loaded.Should().ContainSingle().Subject;
    }

    [AvaloniaFact]
    public async Task A_contributed_project_file_is_read_by_its_add_in_into_a_project_with_its_modules_references_and_folders()
    {
        using var type = Contribute();

        var project = await Opened();

        project.Name.Should().Be("Ledger");
        project.ContributedBy.Should().Be("rdcore", "an add-in is known by its manifest id");
        project.ContributorTitle.Should().Be("RDCore for HexIDE");
        project.AbsolutePath.Should().Be(ProjectFile);
        project.Modules.Select(module => (module.Name, module.Kind)).Should().Equal(
            ("Startup", ModuleKind.StandardModule), ("Account", ModuleKind.ClassModule), ("Sheet1", ModuleKind.ClassModule));
        project.Modules[0].Code.Should().StartWith("Sub Main()", "the header is kept apart, as a .vbp's module's is");
        project.Modules[2].Code.Should().StartWith("Private Sub Worksheet_Activate()",
            "a document module is its host document's code, kept with its header, for reading and editing");
        project.RelatedDocuments.Should().ContainSingle().Which.Name.Should().Be("README.md");
        project.References.Should().ContainSingle().Which.Should().Be(
            new HexIDE.Runtime.ProjectElements.VbReference("{420B2830-E718-11CF-893D-00A0C9054228}", "1.0", 0,
                @"C:\Windows\System32\scrrun.dll", "Scripting"));
        project.ListedFolders.Should().Equal("src", "src/Classes", "docs");
    }

    [AvaloniaFact]
    public async Task Open_Project_offers_a_type_s_whole_file_name_as_that_name_not_as_a_pattern()
    {
        using var type = Contribute();
        Avalonia.Platform.Storage.FilePickerOpenOptions? offered = null;
        _windows.OpenFilePickerAsync(Arg.Do<Avalonia.Platform.Storage.FilePickerOpenOptions>(options => offered = options))
            .Returns((IReadOnlyList<string>?)null);

        await ((Func<Task>)(() => _service.OpenProject())).Should().ThrowAsync<OperationCanceledException>();

        offered!.FileTypeFilter!.Single(filter => filter.Name == "RDCore projects").Patterns.Should().Equal(".rdproj");
        offered.FileTypeFilter!.First().Patterns.Should().Contain(".rdproj").And.NotContain("*.rdproj");
    }

    [AvaloniaFact]
    public async Task Its_listed_folders_show_in_the_Project_Explorer_an_empty_one_included()
    {
        using var type = Contribute();
        var project = await Opened();

        var tree = HexIDE.Tools.ProjectTreeBuilder.BuildChildren([], _dir, listedFolders: project.ListedFolders);

        tree.OfType<HexIDE.Tools.DirectoryViewModel>().Select(folder => folder.Name).Should().BeEquivalentTo(["docs", "src"]);
    }

    [AvaloniaFact]
    public async Task Saving_writes_an_edited_module_and_never_the_project_file()
    {
        using var type = Contribute();
        var project = await Opened();
        var before = File.ReadAllText(ProjectFile);
        project.Modules[0].UpdateCode("Sub Main()\r\n    Debug.Print 1\r\nEnd Sub\r\n");

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(Path.Join(_dir, "src", "Main.bas")).Should().Contain("Debug.Print 1");
        File.ReadAllText(ProjectFile).Should().Be(before, "the project file is its add-in's program's to write");
        File.Exists(Path.Join(_dir, ".vbp")).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Save_As_saves_the_edited_modules_and_puts_the_project_file_nowhere_new()
    {
        using var type = Contribute();
        var project = await Opened();
        var before = File.ReadAllText(ProjectFile);
        _windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Path.Join(_dir, "Ledger.vbp"));
        _recent.ClearReceivedCalls();
        project.Modules[0].UpdateCode("Sub Main()\r\n    Debug.Print 2\r\nEnd Sub\r\n");

        await _service.SaveAllProjects(saveAs: true);

        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>());
        _recent.DidNotReceive().Add(Arg.Any<string>());
        File.ReadAllText(Path.Join(_dir, "src", "Main.bas")).Should().Contain("Debug.Print 2");
        File.ReadAllText(ProjectFile).Should().Be(before);
        File.Exists(Path.Join(_dir, "Ledger.vbp")).Should().BeFalse();
        project.AbsolutePath.Should().Be(ProjectFile);
    }

    [AvaloniaFact]
    public async Task Its_properties_references_and_components_are_refused_with_a_reason_from_every_route()
    {
        using var type = Contribute();
        var project = await Opened();

        await _service.EditProjectProperties(project);
        await _service.EditProjectReferences(project);
        await _service.EditProjectComponents(project);

        await _windows.DidNotReceive().ShowDialog(Arg.Any<IDialog>());
        await _windows.Received(3).MessageBox(Arg.Is<string>(text => text == "Str.ContributedProject.SettingsReadOnly Ledger RDCore for HexIDE"),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task Every_add_is_refused_at_the_service_with_the_reason_whichever_route_reaches_it()
    {
        using var type = Contribute();
        var project = await Opened();
        var stray = Path.Join(_dir, "Stray.bas");
        File.WriteAllText(stray, "Attribute VB_Name = \"Stray\"\r\n");
        const string reason = "Str.ContributedProject.MembershipReadOnly Ledger RDCore for HexIDE";

        // As the Project Explorer, the menus and Add File add: someone is told why.
        Func<Task>[] interactive =
        [
            () => _service.AddNewForm(project, "Form1"),
            () => _service.AddNewModule(project, "Module1", ModuleKind.StandardModule),
            () => _service.AddNewUserControl(project, "UserControl1"),
            () => _service.AddNewPropertyPage(project, "PropertyPage1"),
            () => _service.AddExistingForm(project, stray),
            () => _service.AddExistingModule(project, stray, ModuleKind.StandardModule),
            () => _service.AddExistingRelatedDocument(project, stray),
        ];
        foreach (var add in interactive)
            await add.Should().ThrowAsync<OperationCanceledException>();
        // As automation's add_file adds: the reason is handed back.
        await ((Func<Task>)(() => _service.AddNewModule(project, "Class1", ModuleKind.ClassModule, interactive: false)))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(reason);
        // As crash recovery would add back a member the add-in no longer lists.
        _service.AddRecovered(project, new HexIDE.Recovery.UnsavedDocument("Lost", nameof(ModuleKind.StandardModule), null,
            Vb6TextEncoding.Utf8WithBom, new HexIDE.Recovery.UnsavedFile("Lost.bas", "Attribute VB_Name = \"Lost\"\r\n"))).Should().BeNull();

        project.Forms.Should().BeEmpty();
        project.Modules.Select(module => module.Name).Should().Equal("Startup", "Account", "Sheet1");
        project.RelatedDocuments.Should().ContainSingle();
        await _windows.Received(interactive.Length).MessageBox(reason,
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task Removing_a_member_is_refused_with_a_reason()
    {
        using var type = Contribute();
        var project = await Opened();

        await _service.RemoveDocument(HexIDE.Runtime.ProjectElements.DocumentIdentity.For(project.Modules[0]));

        project.Modules.Should().HaveCount(3, "its membership is the add-in's program's to change");
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.Contains("Str.ContributedProject.MembershipReadOnly")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task What_the_add_in_reports_changes_the_project_as_the_same_change_to_a_vbp_would()
    {
        using var type = Contribute();
        var project = await Opened();
        var added = new List<string>();
        var deleted = new List<string>();
        project.ModuleAdded += (_, module) => added.Add(module.Name);
        project.ModuleDeleted += (_, module) => deleted.Add(module.Name);
        File.WriteAllText(Path.Join(_dir, "src", "Helpers.bas"), "Attribute VB_Name = \"Helpers\"\r\n");

        _changes!.MemberAdded(new(Path.Join(_dir, "src", "Helpers.bas"), AddinMemberKind.StandardModule));
        _changes.MemberMoved(Path.Join(_dir, "src", "Main.bas"), Path.Join(_dir, "src", "Program.bas"));
        _changes.MemberRemoved(Path.Join(_dir, "README.md"));
        _changes.ReferencesChanged([]);
        Dispatcher.UIThread.RunJobs();

        added.Should().Equal("Helpers");
        deleted.Should().BeEmpty();
        project.RelatedDocuments.Should().BeEmpty();
        project.References.Should().BeEmpty();
        project.Modules.Single(module => module.Name == "Startup").AbsolutePath.Should().Be(Path.Join(_dir, "src", "Program.bas"));
        _changes.MemberRemoved(Path.Join(_dir, "src", "Helpers.bas"));
        Dispatcher.UIThread.RunJobs();
        deleted.Should().Equal("Helpers");
    }

    [AvaloniaFact]
    public async Task Reports_are_applied_in_order_and_a_member_is_added_once_and_never_under_a_name_taken()
    {
        using var type = Contribute();
        var project = await Opened();
        string Module(string file, string name)
        {
            var path = Path.Join(_dir, "src", file);
            File.WriteAllText(path, $"Attribute VB_Name = \"{name}\"\r\n");
            return path;
        }
        var helpers = Module("Helpers.bas", "Helpers");
        var temporary = Module("Scratch.bas", "Scratch");
        var copy = Module("AccountCopy.bas", "Account");
        var last = Module("Last.bas", "Last");
        _reads.Hold(temporary);

        _changes!.MemberAdded(new(helpers, AddinMemberKind.StandardModule));
        _changes.MemberAdded(new(helpers, AddinMemberKind.StandardModule));
        _changes.MemberAdded(new(temporary, AddinMemberKind.StandardModule));
        _changes.MemberRemoved(temporary);
        _changes.MemberAdded(new(copy, AddinMemberKind.StandardModule));
        _changes.MemberAdded(new(last, AddinMemberKind.StandardModule));
        Dispatcher.UIThread.RunJobs();

        project.Modules.Select(module => module.Name).Should().Equal(["Startup", "Account", "Sheet1", "Helpers"],
            "the reports after an add wait for its file to be read");
        _reads.Release(temporary);
        Dispatcher.UIThread.RunJobs();

        project.Modules.Select(module => module.Name).Should().Equal("Startup", "Account", "Sheet1", "Helpers", "Last");
        // The one name taken is Account's: a file reported twice is the same member, not one that takes its own name.
        await _windows.Received(1).MessageBox(Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text == "Str.ContributedProject.NameTaken AccountCopy.bas Account"),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task A_member_taken_out_is_unloaded_as_Remove_unloads_one_with_its_edits_saved_if_wanted()
    {
        using var type = Contribute();
        var project = await Opened();
        var startup = project.Modules[0];
        var account = project.Modules[1];
        startup.UpdateCode("Sub Main()\r\n    Debug.Print 3\r\nEnd Sub\r\n");
        _windows.MessageBox(Arg.Is<string>(text => text.StartsWith("Str.ContributedProject.RemovedWithChanges")),
                Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>())
            .Returns(MessageBoxResult.Yes);

        _changes!.MemberRemoved(Path.Join(_dir, "src", "Main.bas"));
        _changes.MemberRemoved(Path.Join(_dir, "src", "Classes", "Account.cls"));
        Dispatcher.UIThread.RunJobs();

        project.Modules.Should().ContainSingle();

        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.StartsWith("Str.ContributedProject.RemovedWithChanges")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
        await _windows.Received(1).MessageBox("Str.ContributedProject.RemovedWithChanges Startup Ledger",
            Arg.Any<string?>(), MessageBoxButtons.YesNo, Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
        File.ReadAllText(Path.Join(_dir, "src", "Main.bas")).Should().Contain("Debug.Print 3");
        _events.Received(1).Publish(Arg.Is<ModuleUnloadedEvent>(e => e.Module == startup));
        _events.Received(1).Publish(Arg.Is<ModuleUnloadedEvent>(e => e.Module == account));
    }

    [AvaloniaFact]
    public async Task A_member_moved_keeps_its_baselines_so_its_edits_stay_unsaved_and_its_new_name_is_announced()
    {
        using var type = Contribute();
        var project = await Opened();
        var startup = project.Modules[0];
        var account = project.Modules[1];
        startup.UpdateCode("Sub Main()\r\n    Debug.Print 4\r\nEnd Sub\r\n");
        var (main, program) = (Path.Join(_dir, "src", "Main.bas"), Path.Join(_dir, "src", "Program.bas"));
        var (accountFile, ledger) = (Path.Join(_dir, "src", "Classes", "Account.cls"), Path.Join(_dir, "src", "Ledger.cls"));
        File.Move(main, program);
        File.Move(accountFile, ledger);

        _changes!.MemberMoved(main, program);
        _changes.MemberMoved(accountFile, ledger);
        Dispatcher.UIThread.RunJobs();

        account.AbsolutePath.Should().Be(ledger);
        _service.HasUnsavedChanges(startup).Should().BeTrue("its edits were not saved, wherever its file is");
        _service.HasUnsavedChanges(account).Should().BeFalse("its file holds what HexIDE read, now at its new path");
        _baselines.Matches(ledger, File.ReadAllBytes(ledger)).Should().BeTrue();
        _baselines.TryGet(accountFile).Should().BeNull();
        _events.Received(1).Publish(Arg.Is<ModuleMovedEvent>(e => e.Module == startup));
        _events.Received(1).Publish(Arg.Is<ModuleMovedEvent>(e => e.Module == account));
    }

    [AvaloniaFact]
    public async Task A_member_whose_name_another_has_is_left_out_of_the_open_and_named()
    {
        File.WriteAllText(Path.Join(_dir, "src", "Startup.bas"), "Attribute VB_Name = \"Startup\"\r\n");
        using var type = Contribute(_ => Content() with
        {
            Members = [.. Content().Members, new(Path.Join(_dir, "src", "Startup.bas"), AddinMemberKind.StandardModule)],
        });

        var project = await Opened();

        project.Modules.Should().ContainSingle(module => module.Name == "Startup")
            .Which.AbsolutePath.Should().Be(Path.Join(_dir, "src", "Main.bas"));
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.Contains("Str.ContributedProject.NameTaken Startup.bas Startup")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task The_add_in_is_told_when_the_project_is_closed_or_its_open_failed_and_later_reports_are_ignored()
    {
        using (Contribute(_ => throw new InvalidDataException("not a project file")))
            await _service.OpenProject(ProjectFile);
        _changes!.Closed.IsCancellationRequested.Should().BeTrue("the open failed");

        using var type = Contribute();
        var project = await Opened();
        var changes = _changes;
        changes.Closed.IsCancellationRequested.Should().BeFalse();
        // An add-in's handler that fails must not stop the unload that tells it, or the exit beyond.
        changes.Closed.Register(() => throw new InvalidOperationException("an add-in's own failure"));
        _loaded.Remove(project);
        ((Action)(() => _projects.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project))).Should().NotThrow();

        changes.Closed.IsCancellationRequested.Should().BeTrue("the project was closed");
        _loaded.Add(project);
        changes.MemberRemoved(Path.Join(_dir, "src", "Main.bas"));
        changes.ReferencesChanged([]);
        Dispatcher.UIThread.RunJobs();
        project.Modules.Should().HaveCount(3, "a report after the project closed is ignored");
        project.References.Should().ContainSingle();
    }

    // A member's file read completes at once, so the reports posted to the UI thread are all applied by the RunJobs that
    // runs them. A read held completes only when the test releases it, and RunJobs then applies what waited for it: the
    // test, not the thread pool, decides the order the reads finish in.
    private sealed class HeldReads
    {
        private readonly Dictionary<string, TaskCompletionSource> _held = new(StringComparer.OrdinalIgnoreCase);

        public void Hold(string path) => _held[path] = new TaskCompletionSource();

        public void Release(string path) => _held[path].SetResult();

        public async Task<byte[]> Read(string path)
        {
            if (_held.TryGetValue(path, out var held))
                await held.Task;
            return File.ReadAllBytes(path);
        }
    }

    [AvaloniaFact]
    public async Task A_project_type_taken_out_is_no_longer_read_and_a_failed_read_says_why()
    {
        var type = Contribute(_ => throw new InvalidDataException("not a project file"));

        await _service.OpenProject(ProjectFile);

        _loaded.Should().BeEmpty();
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.Contains("not a project file")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
        type.Dispose();
        _types.Find(ProjectFile).Should().BeNull();
    }

    [AvaloniaFact]
    public async Task A_contributed_file_whose_add_in_is_not_loaded_is_refused_not_read_as_a_vbp()
    {
        // As the recent list would reopen it, with no project type contributed for it now.
        await _service.OpenProject(ProjectFile);

        _loaded.Should().BeEmpty();
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.StartsWith("Str.ContributedProject.NoReader")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task A_contributed_project_already_open_shows_the_localized_message()
    {
        using var type = Contribute();
        await Opened();

        Func<Task> reopen = () => _service.OpenProject(ProjectFile);

        await reopen.Should().ThrowAsync<OperationCanceledException>();
        _loaded.Should().ContainSingle();
        await _windows.Received(1).MessageBox("Projet .rdproj déjà ouvert.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [WindowsOnlyFact]
    public async Task A_contributed_project_already_open_is_refused_when_opened_again_with_different_drive_and_folder_case()
    {
        // The host's rule for the same file, as a .vbp's open has it: on Windows, the same project under another letter case.
        using var type = Contribute();
        await Opened();
        var alternate = Path.Join(_dir.ToUpperInvariant(), ".rdproj");
        alternate = char.ToLowerInvariant(alternate[0]) + alternate[1..];
        alternate.Should().NotBe(ProjectFile);

        Func<Task> reopen = () => _service.OpenProject(alternate);

        await reopen.Should().ThrowAsync<OperationCanceledException>();
        _loaded.Should().ContainSingle();
        await _windows.Received(1).MessageBox("Projet .rdproj déjà ouvert.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task A_group_s_contributed_project_is_read_by_its_add_in_and_one_no_add_in_reads_is_refused()
    {
        using var type = Contribute();
        File.WriteAllText(Path.Join(_dir, "Other.xproj"), "Type=Exe\r\nModule=Startup; src\\Main.bas\r\n");
        var group = Path.Join(_dir, "Group1.vbg");
        File.WriteAllText(group, "VBGROUP 5.0\r\nProject=.rdproj\r\nProject=Other.xproj\r\n");

        await _service.OpenProject(group);

        _loaded.Should().ContainSingle().Which.ContributedBy.Should().Be("rdcore",
            "a group names a project file as Open Project does, and is read the same way");
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text == "Str.ContributedProject.NoReader Other.xproj"),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    [AvaloniaFact]
    public async Task Make_is_refused_with_a_reason_before_it_asks_where_to_put_the_program()
    {
        using var type = Contribute();
        var project = await Opened();
        var before = File.ReadAllText(ProjectFile);
        _windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(Path.Join(_dir, "Ledger.exe"));

        await ((Func<Task>)(() => _service.MakeProject(project))).Should().ThrowAsync<OperationCanceledException>();

        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>());
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.StartsWith("Str.ContributedProject.NotMade Ledger RDCore for HexIDE")),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
        File.ReadAllText(ProjectFile).Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void HexIDE_s_interpreter_starts_only_a_project_HexIDE_read_itself(bool contributed)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Ledger") { ContributedBy = contributed ? "RDCore projects" : null };
        var projects = Substitute.For<IProjectManager>();
        projects.StartupProject.Returns(project);
        var events = Substitute.For<IEventBus>();
        var interpreter = Substitute.For<IDebugGate>();
        var runner = HexIDE.IDE.TestRunners.WithInterpreter(events, _windows, projects, Substitute.For<ILocalizationService>(),
            interpreter, Substitute.For<IBreakpointService>(), new WatchService(), new RunScope());

        runner.CanStartDefaultProject.Should().Be(!contributed);
        if (!contributed)
            return;
        runner.RunProject(project);
        events.DidNotReceive().Publish(Arg.Any<ApplyAllUnsavedChangesEvent>());
        interpreter.DidNotReceive().Reset();
    }

    [Fact]
    public void A_type_cannot_take_HexIDE_s_own_files_or_another_type_s()
    {
        using var first = Contribute();
        AddinProjectType Reading(params string[] names) => new("Other") { FileNames = names, OpenAsync = (_, _, _) => throw new NotImplementedException() };

        ((Action)(() => _types.Register(Reading("*.vbp"), Other))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading(".rdproj"), Other))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading("*.rdproj"), Other))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading("*.*"), Other))).Should().Throw<ArgumentException>();
        ((Action)(() => _types.Register(Reading(), Other))).Should().Throw<ArgumentException>();
        using var fine = _types.Register(Reading("*.mypro"), Other);
        _types.Find(Path.Join(_dir, "x.mypro"))!.Origin.Should().Be("Other add-in");
    }

    [Fact]
    public void A_type_reads_the_file_names_it_was_registered_with_whatever_its_list_holds_later()
    {
        var names = new List<string> { "*.mypro" };
        using var type = _types.Register(new AddinProjectType("Other") { FileNames = names, OpenAsync = (_, _, _) => throw new NotImplementedException() }, Other);

        names[0] = "*.vbp";
        names.Add(".rdproj");

        _types.Find(Path.Join(_dir, "x.mypro")).Should().NotBeNull();
        _types.Find(Path.Join(_dir, "x.vbp")).Should().BeNull("a type never takes HexIDE's own files, however it is changed");
        using var rdcore = Contribute();
        _types.Find(ProjectFile)!.Origin.Should().Be("RDCore for HexIDE");
    }

    [AvaloniaFact]
    public async Task An_open_in_progress_is_called_off_when_its_type_is_taken_out()
    {
        var reading = new TaskCompletionSource<AddinProjectContent>();
        var token = CancellationToken.None;
        var type = _types.Register(new AddinProjectType("RDCore projects")
        {
            FileNames = [".rdproj"],
            OpenAsync = (_, changes, cancellation) =>
            {
                _changes = changes;
                token = cancellation;
                cancellation.Register(() => reading.TrySetCanceled(cancellation));
                return reading.Task;
            },
        }, RDCore);

        var open = _service.OpenProject(ProjectFile);
        token.IsCancellationRequested.Should().BeFalse();
        type.Dispose();

        // The deadline only bounds an open that is never called off, which then fails as a TimeoutException.
        await ((Func<Task>)(() => open.WaitAsync(TimeSpan.FromSeconds(30)))).Should().ThrowAsync<OperationCanceledException>();
        token.IsCancellationRequested.Should().BeTrue();
        _loaded.Should().BeEmpty();
        _changes!.Closed.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void A_type_contributed_by_an_initialisation_that_fails_is_taken_out_with_it()
    {
        using (new AddinInitializationScope())
            Contribute();

        _types.Types.Should().BeEmpty();
    }
}
