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
    private readonly ContributedProjects _types = new();
    private readonly ProjectService _service;
    private IAddinProjectChanges? _changes;

    public ContributedProjectTests()
    {
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

        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ContributedProject.OpenError" => "Could not open {0} with {1}: {2}",
            "Str.ContributedProject.NoReader" => "Str.ContributedProject.NoReader {0}",
            var key => key + " {0} {1}",
        });
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        _service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            Substitute.For<IEventBus>(), projects, _recent,
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), strings,
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
    }, "RDCore for HexIDE");

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
        project.ContributedBy.Should().Be("RDCore projects");
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
        _windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Path.Join(_dir, "Ledger.vbp"));
        _recent.ClearReceivedCalls();
        project.Modules[0].UpdateCode("Sub Main()\r\n    Debug.Print 2\r\nEnd Sub\r\n");

        await _service.SaveAllProjects(saveAs: true);

        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        _recent.DidNotReceive().Add(Arg.Any<string>());
        File.ReadAllText(Path.Join(_dir, "src", "Main.bas")).Should().Contain("Debug.Print 2");
        File.ReadAllText(ProjectFile).Should().Be(before);
        File.Exists(Path.Join(_dir, "Ledger.vbp")).Should().BeFalse();
        project.AbsolutePath.Should().Be(ProjectFile);
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
        await WhenSettled(() => added.Count == 1 && project.RelatedDocuments.Count == 0 && project.References.Count == 0);

        added.Should().Equal("Helpers");
        deleted.Should().BeEmpty();
        project.Modules.Single(module => module.Name == "Startup").AbsolutePath.Should().Be(Path.Join(_dir, "src", "Program.bas"));
        _changes.MemberRemoved(Path.Join(_dir, "src", "Helpers.bas"));
        await WhenSettled(() => deleted.Count == 1);
        deleted.Should().Equal("Helpers");
    }

    // The reports are applied on the UI thread, posted; this runs its queue until they are.
    private static async Task WhenSettled(Func<bool> settled)
    {
        for (var turns = 0; !settled() && turns < 200; turns++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }
        settled().Should().BeTrue("the add-in's reports were applied");
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
    public async Task A_group_s_contributed_project_is_read_by_its_add_in_and_one_no_add_in_reads_is_refused()
    {
        using var type = Contribute();
        File.WriteAllText(Path.Join(_dir, "Other.xproj"), "Type=Exe\r\nModule=Startup; src\\Main.bas\r\n");
        var group = Path.Join(_dir, "Group1.vbg");
        File.WriteAllText(group, "VBGROUP 5.0\r\nProject=.rdproj\r\nProject=Other.xproj\r\n");

        await _service.OpenProject(group);

        _loaded.Should().ContainSingle().Which.ContributedBy.Should().Be("RDCore projects",
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
        _windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Path.Join(_dir, "Ledger.exe"));

        await ((Func<Task>)(() => _service.MakeProject(project))).Should().ThrowAsync<OperationCanceledException>();

        await _windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        await _windows.Received(1).MessageBox(Arg.Is<string>(text => text.StartsWith("Str.ContributedProject.NotMade Ledger RDCore projects")),
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
        var interpreter = Substitute.For<IDebugController>();
        var runner = new HexIDE.IDE.ProjectRunnerService(events, _windows, projects, Substitute.For<ILocalizationService>(),
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

        ((Action)(() => _types.Register(Reading("*.vbp"), "Other add-in"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading(".rdproj"), "Other add-in"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading("*.rdproj"), "Other add-in"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => _types.Register(Reading("*.*"), "Other add-in"))).Should().Throw<ArgumentException>();
        ((Action)(() => _types.Register(Reading(), "Other add-in"))).Should().Throw<ArgumentException>();
        using var fine = _types.Register(Reading("*.mypro"), "Other add-in");
        _types.Find(Path.Join(_dir, "x.mypro"))!.Origin.Should().Be("Other add-in");
    }

    [Fact]
    public void A_type_contributed_by_an_initialisation_that_fails_is_taken_out_with_it()
    {
        using (new AddinInitializationScope())
            Contribute();

        _types.Types.Should().BeEmpty();
    }
}
