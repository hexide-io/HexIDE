using System.IO;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Recovery;
using HexIDE.Runtime.Components;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// Offering back what a crashed run left (hexide-io/HexIDE#499 task group 2): where each project's work is offered,
/// what restoring it puts in the IDE, and what is left on disk afterwards.
/// </summary>
/// <remarks>
/// Each test is two IDE sessions: the first opens and edits a project and is "killed" by writing its capture to a run
/// nobody ends, and the second starts, finds that run and offers it. Both use a real <see cref="ProjectService"/> and
/// <see cref="FileBaselineStore"/>: whether a restored document reads as edited is the store's answer, not a flag.
/// </remarks>
public sealed class RecoveryOffersTests : IDisposable
{
    // A start time no running process has, so the crashed run reads as over though its id is this process's.
    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-restore-" + Guid.NewGuid().ToString("N"));
    private readonly string root;
    private readonly List<RecoveryDialogViewModel> shown = [];
    private readonly List<string> messages = [];
    private readonly Queue<Func<RecoveryDialogViewModel, Task>> answers = new();
    private MessageBoxResult confirm = MessageBoxResult.Yes;

    public RecoveryOffersTests()
    {
        Directory.CreateDirectory(dir);
        root = Path.Join(dir, "Recovery");
    }

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private static Task Restore(RecoveryDialogViewModel dialog) => dialog.RestoreSelectedAsync();

    private static Task Discard(RecoveryDialogViewModel dialog) => dialog.DiscardSelectedAsync();

    private static Task Later(RecoveryDialogViewModel dialog) => Task.CompletedTask;

    /// <summary>One IDE: a project service with its own store and loaded projects.</summary>
    private sealed class Session
    {
        public readonly List<ProjectDefinition> Loaded = [];
        public readonly FileBaselineStore Store = new();
        public readonly IProjectManager Manager = Substitute.For<IProjectManager>();
        public readonly EventBus Bus = new();
        public readonly IWindowManager Windows = Substitute.For<IWindowManager>();
        public readonly ProjectService Service;
        public readonly List<BaseEditorWindowViewModel> Tabs = [];
        public readonly IDocumentDockService Dock = Substitute.For<IDocumentDockService>();
        public readonly EditorService Editors;
        public readonly ContributedProjects Types;

        public Session(ContributedProjects? contributed = null, bool realManager = false)
        {
            Types = contributed ?? Vb6ProjectType.Alone();
            Dock.OpenDocuments.Returns(Tabs);
            Dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>(), Arg.Any<bool>()))
                .Do(ci => Tabs.Add(ci.Arg<BaseEditorWindowViewModel>()));
            Editors = new EditorService(Substitute.For<HexIDE.IDE.IWindowManager>(), Dock, null!, null!, null!, null!, () => Manager,
                Substitute.For<ILocalizationService>(), Bus, () => Service!);
            if (realManager)
                Manager = new ProjectManager(Editors, Bus);
            else
            {
                Manager.LoadedProjects.Returns(_ => Loaded);
                Manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(ci => Loaded.Add(ci.Arg<ProjectDefinition>()));
            }
            var sidecar = Substitute.For<IUserSidecarService>();
            sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
            sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
            Service = new ProjectService(                () => throw new InvalidOperationException("new-project dialog must not be reached"),
                Windows, Bus, Manager, Substitute.For<IRecentProjectsService>(),
                Substitute.For<IReferenceLibraryService>(), sidecar, Store, Substitute.For<ILocalizationService>(),
                Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), contributed: Types, documents: Dock);
        }

        public ProjectDefinition Project => Loaded.Single();
    }

    private RecoveryOffers Offers(Session session, IEventBus? bus = null, ContributedProjects? contributed = null, string? recoveryRoot = null)
    {
        var recovery = Substitute.For<IRecoveryService>();
        recovery.RunFolder.Returns(Path.Join(recoveryRoot ?? root, "this-run"));

        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(async ci =>
        {
            var dialog = (RecoveryDialogViewModel)ci.Arg<IDialog>();
            shown.Add(dialog);
            if (answers.TryDequeue(out var answer))
                await answer(dialog);
            return true;
        });
        windows.MessageBox(default!).ReturnsForAnyArgs(ci =>
        {
            messages.Add(ci.ArgAt<string>(0));
            return Task.FromResult(ci.ArgAt<MessageBoxButtons>(2) == MessageBoxButtons.YesNo ? confirm : MessageBoxResult.Ok);
        });

        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>() + ": {0}");
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        var dock = session.Dock;

        return new RecoveryOffers(recovery, session.Service, session.Manager, windows, localization, dock,
            session.Store, clock, bus ?? Substitute.For<IEventBus>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IHeaderRefresher>(), editors: session.Editors, contributed: contributed ?? session.Types);
    }

    /// <summary>Writes a saved project of a form and two modules into <paramref name="folder"/>, and names its file.</summary>
    private static string WriteProject(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Join(folder, "Form1.frm"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllText(Path.Join(folder, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(folder, "Module2.bas"),
            "Attribute VB_Name = \"Module2\"\r\nPublic Sub Untouched()\r\nEnd Sub\r\n");
        var vbp = Path.Join(folder, "Test.vbp");
        File.WriteAllText(vbp,
            "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nModule=Module2; Module2.bas\r\nName=\"Test\"\r\n");
        return vbp;
    }

    /// <summary>A first session that opens <paramref name="vbp"/> and saves it again, so every file is this build's.</summary>
    private static async Task<Session> Opened(string vbp)
    {
        var session = new Session();
        await session.Service.OpenProject(vbp);
        await session.Service.SaveProject(session.Project, saveAs: false);
        return session;
    }

    /// <summary>The first session's IDE dies with its work captured, and leaves the run's folder.</summary>
    private string Crash(Session session)
    {
        var run = RecoveryRun.Begin(root, Environment.ProcessId, LongAgo);
        run.Write(session.Service.CaptureUnsavedWork(), DateTime.UtcNow);
        return run.Folder;
    }

    private static Dictionary<string, byte[]> FilesIn(string folder) => Directory
        .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
        .ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes);

    private static ModuleDefinition Module(ProjectDefinition project, string name) => project.Modules.Single(m => m.Name == name);

    private static ProjectFileDocumentViewModel ProjectTab(Session session)
    {
        session.Editors.EditProjectFile(new ProjectFileSource(session.Project));
        return session.Tabs.OfType<ProjectFileDocumentViewModel>().Single();
    }

    [AvaloniaFact]
    public async Task Project_file_typing_is_captured_separately_and_round_trips()
    {
        var first = await Opened(WriteProject(dir));
        var tab = ProjectTab(first);
        first.Service.CaptureUnsavedWork().Should().BeEmpty("a clean tab is not captured");
        tab.Document.Text += "Title=\"Pending\"\r\n";
        var capture = first.Service.CaptureUnsavedWork().Single();
        capture.ProjectFile.Should().BeNull("typed text is not rendered model settings");
        capture.ProjectFileEdit.Should().Be(new UnsavedFile("Test.vbp", tab.Document.Text));
        var fingerprint = RecoveryRun.Fingerprint([capture]);
        RecoveryRun.Fingerprint([capture with { ProjectFileEdit = capture.ProjectFileEdit! with { Text = "later" } }])
            .Should().NotBe(fingerprint);
        first.Project.Name = "Model settings";
        var run = Crash(first);
        using var pending = PendingRun.Find(root, null, (_, _) => false, DateTime.UtcNow).Single();
        var read = pending.Read(pending.Projects.Single());
        read.ProjectFileEdit.Should().Be(capture.ProjectFileEdit);
        read.ProjectFile!.Text.Should().Contain("Model settings").And.NotContain("Pending");
        RecoveryRun.Read(run)!.Projects.Single().ProjectFileEdit.Should().NotBeNull();
        first.Project.Name = "Test";
        tab.Document.UndoStack.Undo();
        first.Service.CaptureUnsavedWork().Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_file_recovery_opens_a_modified_tab_without_applying_settings_or_writing(bool inGroup)
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        var tab = ProjectTab(first);
        tab.Document.Text = tab.Document.Text.Replace("Name=\"Test\"", "Name=\"Pending\"");
        Crash(first);
        File.AppendAllText(vbp, "Title=\"Changed outside\"\r\n");
        var bytes = File.ReadAllBytes(vbp);
        var second = new Session(realManager: true);
        var offers = Offers(second);
        var openPath = vbp;
        if (inGroup)
        {
            File.WriteAllText(Path.Join(dir, "Other.vbp"), "Type=Exe\r\nName=\"Other\"\r\n");
            openPath = Path.Join(dir, "Named.vbg");
            File.WriteAllText(openPath, "VBGROUP 5.0\r\nProject=Other.vbp\r\nProject=Test.vbp\r\nStartupProject=Test.vbp\r\n");
        }
        await second.Service.OpenProject(openPath);
        var project = second.Manager.LoadedProjects.Single(p => p.AbsolutePath == vbp);
        var group = second.Manager.CurrentGroup;
        var order = second.Manager.LoadedProjects.ToArray();
        var startup = second.Manager.StartupProject;
        var files = FilesIn(dir);
        string? warning = null;
        answers.Enqueue(dialog =>
        {
            warning = dialog.Items.Single().ChangedOnDisk;
            return Restore(dialog);
        });
        await offers.OfferForAsync(project);
        second.Manager.CurrentGroup.Should().BeSameAs(group);
        second.Manager.LoadedProjects.Should().Equal(order);
        second.Manager.StartupProject.Should().BeSameAs(startup);
        if (inGroup)
        {
            group!.Name.Should().Be("Named");
            group.AbsolutePath.Should().Be(openPath, "the group remains saved");
            second.Manager.LoadedProjects.Select(p => p.Name).Should().Equal("Other", "Test");
        }
        FilesIn(dir).Should().BeEquivalentTo(files);
        shown.Should().ContainSingle();
        warning.Should().Be("Str.Dialog.Recovery.ChangedOnDisk: Test.vbp");
        var restored = second.Tabs.OfType<ProjectFileDocumentViewModel>().Single();
        restored.Document.Text.Should().Be(tab.Document.Text);
        restored.IsModified.Should().BeTrue();
        project.Name.Should().Be("Test");
        File.ReadAllBytes(vbp).Should().Equal(bytes);
    }

    [AvaloniaFact]
    public async Task Project_file_backup_equal_to_disk_is_not_offered()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        var tab = ProjectTab(first);
        tab.Document.Text += "Title=\"Pending\"\r\n";
        var run = Crash(first);
        RecoveryRun.Read(run)!.Projects.Should().ContainSingle();
        File.WriteAllBytes(vbp, Vb6TextFile.Encode(tab.Document.Text, first.Project.TextEncoding));
        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);
        shown.Should().BeEmpty();
        second.Tabs.Should().BeEmpty();
        Directory.Exists(run).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task A_record_without_project_file_edit_still_restores_documents()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        ProjectTab(first).Document.Text += "Title=\"Pending\"\r\n";
        Module(first.Project, "Module1").UpdateCode("' recovered\r\n");
        var run = Crash(first);
        var recordPath = Path.Join(run, "run.json");
        var json = JsonNode.Parse(File.ReadAllText(recordPath))!;
        json["projects"]![0]!.AsObject().Remove("projectFileEdit").Should().BeTrue();
        File.WriteAllText(recordPath, json.ToJsonString());
        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);
        Module(second.Project, "Module1").Code.Should().Contain("' recovered");
        second.Tabs.Should().BeEmpty();
    }

    [Fact]
    public async Task A_saved_project_s_work_is_offered_as_it_opens_and_comes_back_unsaved()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        first.Project.Forms[0].Components[0].SetProperty(VBProperties.CaptionProperty, "Edited caption");
        var run = Crash(first);
        var onDisk = FilesIn(dir);

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        shown.Should().ContainSingle();
        Module(second.Project, "Module1").Code.Should().Contain("Public Sub Edited()");
        second.Project.Forms[0].Components[0].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("Edited caption");
        FilesIn(dir).Should().BeEquivalentTo(onDisk, "a restore puts the work in the IDE, and only a save writes it");
        second.Service.CaptureUnsavedWork().Single().Documents.Where(d => d.Content is not null).Select(d => d.Name)
            .Should().BeEquivalentTo(["Form1", "Module1"], "restored work reads as edited, so the next save writes it");
        Directory.Exists(run).Should().BeFalse("nothing in the run is left to offer");
        offers.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task A_backup_its_files_already_hold_is_not_offered_and_goes()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        var run = Crash(first);
        await first.Service.SaveProject(first.Project, saveAs: false);

        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        shown.Should().BeEmpty("restoring it would change nothing, as after a tick that landed inside a load");
        Directory.Exists(run).Should().BeFalse();
        offers.WaitingCount.Should().Be(0);
    }

    [Fact]
    public async Task Only_the_documents_whose_files_differ_from_their_backup_come_back_unsaved()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Module(first.Project, "Module2").UpdateCode("Public Sub AlsoEdited()\r\nEnd Sub\r\n");
        Crash(first);
        await first.Service.SaveModule(Module(first.Project, "Module2"), saveAs: false);

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        shown.Should().ContainSingle();
        second.Service.CaptureUnsavedWork().Single().Documents.Where(d => d.Content is not null).Select(d => d.Name)
            .Should().Equal(["Module1"], "Module2's file already holds its backup");
    }

    [Fact]
    public async Task A_document_the_project_no_longer_holds_is_added_back()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        var added = new ModuleDefinition(first.Project, "Module3", ModuleKind.StandardModule);
        added.UpdateCode("Public Sub NeverSaved()\r\nEnd Sub\r\n");
        first.Project.AddModule(added);
        File.WriteAllText(Path.Join(dir, "Module9.bas"), "Attribute VB_Name = \"Module9\"\r\nPublic Sub OnDisk()\r\nEnd Sub\r\n");
        await first.Service.AddExistingModule(first.Project, Path.Join(dir, "Module9.bas"), ModuleKind.StandardModule);
        Crash(first);

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        var module3 = Module(second.Project, "Module3");
        module3.AbsolutePath.Should().BeNull("it never had a file");
        module3.Code.Should().Contain("Public Sub NeverSaved()");
        Module(second.Project, "Module9").AbsolutePath.Should().Be(Path.Join(dir, "Module9.bas"),
            "a file added since the last save is added back from where it is");
    }

    [Fact]
    public async Task A_document_a_contributed_project_no_longer_lists_is_not_added_back()
    {
        File.WriteAllText(Path.Join(dir, ".rdproj"), "{}");
        File.WriteAllText(Path.Join(dir, "Main.bas"), "Attribute VB_Name = \"Main\"\r\nPublic Sub Kept()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Gone.bas"), "Attribute VB_Name = \"Gone\"\r\nPublic Sub Listed()\r\nEnd Sub\r\n");
        ContributedProjects Reading(params string[] members)
        {
            var types = new ContributedProjects();
            types.Register(new AddinProjectType("RDCore projects")
            {
                FileNames = [".rdproj"],
                OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Ledger")
                {
                    Members = [.. members.Select(name => new AddinProjectMember(Path.Join(dir, name), AddinMemberKind.StandardModule))],
                }),
            }, new AddinOrigin("rdcore", "RDCore for HexIDE"));
            return types;
        }
        var first = new Session(Reading("Main.bas", "Gone.bas"));
        await first.Service.OpenProject(Path.Join(dir, ".rdproj"));
        Module(first.Project, "Gone").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);

        // Its program took Gone out between the two runs, and left its file.
        var second = new Session(Reading("Main.bas"));
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(Path.Join(dir, ".rdproj"));
        await offers.OfferForAsync(second.Project);

        shown.Should().ContainSingle();
        second.Project.Modules.Select(module => module.Name).Should().Equal(["Main"], "its membership is its add-in's");
        await second.Windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }

    [Fact]
    public async Task A_never_saved_project_is_offered_at_start_and_restored_without_a_file()
    {
        var first = new Session();
        var project = TestHelpers.CreateProjectWithForm("Scratch");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Public Sub Typed()\r\nEnd Sub\r\n");
        project.AddModule(module);
        first.Loaded.Add(project);
        var run = Crash(first);
        var renderedAt = RecoveryRun.Read(run)!.Projects.Single().RenderedAt;

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        (await offers.OfferAtStartAsync()).Should().BeTrue();

        var restored = second.Project;
        restored.Name.Should().Be("Scratch");
        restored.AbsolutePath.Should().BeNull("it was never saved, and is not saved now");
        restored.Forms.Should().ContainSingle(f => f.Name == "Form1" && f.AbsolutePath == null);
        Module(restored, "Module1").AbsolutePath.Should().BeNull();
        Module(restored, "Module1").Code.Should().Contain("Public Sub Typed()");
        Directory.Exists(Path.GetDirectoryName(renderedAt)).Should().BeFalse("the files it was rebuilt from are gone");
        Directory.Exists(run).Should().BeFalse();
    }

    [Fact]
    public async Task A_never_saved_backup_waits_until_its_type_is_registered_again()
    {
        var first = new Session();
        first.Loaded.Add(TestHelpers.CreateProjectWithForm("Waiting"));
        var run = Crash(first);
        var second = new Session(new ContributedProjects());
        var offers = Offers(second, contributed: new ContributedProjects());

        (await offers.OfferAtStartAsync()).Should().BeFalse();
        shown.Should().BeEmpty();
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run).Should().BeTrue();

        // A later process has no earlier session's exclusive claim. Copy the persisted backup, without that claim,
        // into its store so this test can keep both sessions alive without changing the coordinator's lifetime.
        var laterRoot = Path.Join(dir, "LaterRecovery");
        var laterRun = Path.Join(laterRoot, Path.GetFileName(run));
        foreach (var file in Directory.EnumerateFiles(run, "*", SearchOption.AllDirectories)
                     .Where(file => Path.GetFileName(file) != "claim"))
        {
            var copy = Path.Join(laterRun, Path.GetRelativePath(run, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }
        var third = new Session(Vb6ProjectType.Alone());
        answers.Enqueue(Restore);
        (await Offers(third, recoveryRoot: laterRoot).OfferAtStartAsync()).Should().BeTrue();
        third.Project.Name.Should().Be("Waiting");
        third.Project.AbsolutePath.Should().BeNull();
        third.Project.Forms.Should().ContainSingle();
        Directory.Exists(laterRun).Should().BeFalse();
    }

    [Fact]
    public async Task A_saved_backup_without_its_type_is_listed_as_waiting_and_cannot_restore()
    {
        var first = await Opened(WriteProject(dir));
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        var run = Crash(first);
        var offers = Offers(new Session(new ContributedProjects()), contributed: new ContributedProjects());

        await offers.ShowPendingAsync();

        var dialog = shown.Should().ContainSingle().Subject;
        dialog.SelectedItem!.AccessibleName.Should().Contain("Str.Dialog.Recovery.WaitingForType: Test.vbp");
        dialog.RestoreCommand.CanExecute(null).Should().BeFalse();
        dialog.DiscardCommand.CanExecute(null).Should().BeTrue();
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run).Should().BeTrue();
        await dialog.RestoreSelectedAsync();
        messages.Should().BeEmpty("a disabled restore must not try opening the project");
        await dialog.DiscardSelectedAsync();
        offers.WaitingCount.Should().Be(0);
        Directory.Exists(run).Should().BeFalse();
    }

    [Fact]
    public async Task A_scratch_backup_without_its_type_is_kept_even_if_its_files_hold_it()
    {
        var (_, run) = await CrashedScratch(edited: false);
        var offers = Offers(new Session(new ContributedProjects()), contributed: new ContributedProjects());

        (await offers.OfferAtStartAsync()).Should().BeFalse();

        shown.Should().BeEmpty();
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run).Should().BeTrue();
    }

    [Fact]
    public async Task An_old_record_is_judged_by_its_path_without_a_type_field()
    {
        var run = RecoveryRun.Begin(root, Environment.ProcessId, LongAgo);
        // The original format has neither a type identifier nor RenderedAt.
        File.WriteAllText(Path.Join(run.Folder, "run.json"), $$"""
            {
              "format": 1, "processId": {{Environment.ProcessId}}, "processStart": "2000-01-01T00:00:00Z",
              "savedAt": "2000-01-01T00:01:00Z", "capture": "s1",
              "projects": [
                { "name": "Old", "path": "Old.vbp", "codePage": 1252, "utf8Bom": false, "documents": [] }
              ]
            }
            """);
        var offers = Offers(new Session(new ContributedProjects()), contributed: new ContributedProjects());

        await offers.ShowPendingAsync();

        shown.Single().SelectedItem!.AccessibleName.Should().Contain("Str.Dialog.Recovery.WaitingForType: Old.vbp");
        shown.Single().RestoreCommand.CanExecute(null).Should().BeFalse();
        Directory.Exists(run.Folder).Should().BeTrue();
    }

    [Fact]
    public async Task An_unsaved_backup_needs_a_type_with_a_format_for_its_RenderedAt()
    {
        var run = RecoveryRun.Begin(root, Environment.ProcessId, LongAgo);
        run.Write([new UnsavedProject("Other", null, null, new Vb6TextEncoding(1252),
            new UnsavedFile("Other.other", "content"), [], Path.Join(dir, "Other.other"))], DateTime.UtcNow);
        var types = Vb6ProjectType.Alone();
        types.Register(new AddinProjectType("Read only")
        {
            FileNames = ["*.other"],
            OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Other")),
        },
            new AddinOrigin("test.reader", "Reader"));
        var offers = Offers(new Session(types), contributed: types);

        await offers.OfferAtStartAsync();

        shown.Should().BeEmpty("a reader without a Format cannot rebuild an unsaved project");
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run.Folder).Should().BeTrue();
        await offers.ShowPendingAsync();
        shown.Single().SelectedItem!.AccessibleName.Should().Contain("Str.Dialog.Recovery.WaitingForType: Other.other");
        shown.Single().RestoreCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Opening_a_project_does_not_offer_or_resolve_a_backup_without_its_type()
    {
        var first = await Opened(WriteProject(dir));
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        var run = Crash(first);
        var second = await Opened(first.Project.AbsolutePath!);
        var offers = Offers(second, contributed: new ContributedProjects());

        await offers.OfferForAsync(second.Project);

        shown.Should().BeEmpty();
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run).Should().BeTrue();
    }

    [Fact]
    public async Task A_record_with_empty_RenderedAt_is_still_offered_without_a_type()
    {
        var run = RecoveryRun.Begin(root, Environment.ProcessId, LongAgo);
        run.Write([new UnsavedProject("Old", null, null, new Vb6TextEncoding(1252), null, [])], DateTime.UtcNow);
        var offers = Offers(new Session(new ContributedProjects()), contributed: new ContributedProjects());

        await offers.OfferAtStartAsync();

        shown.Single().RestoreCommand.CanExecute(null).Should().BeTrue();
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run.Folder).Should().BeTrue();
    }

    /// <summary>
    /// A first session whose project is in the scratch folder <c>--newproject</c> makes, with Module1 and the project
    /// file both captured as unsaved, and crashed. Unedited, they are captured all the same: every file in the scratch
    /// folder counts as unsaved (#2678).
    /// </summary>
    private async Task<(Session First, string Run)> CrashedScratch(bool edited = true)
    {
        var first = await Opened(WriteProject(dir));
        first.Project.WorkingDirectory = Path.GetDirectoryName(first.Project.AbsolutePath);
        if (edited)
        {
            Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
            first.Project.StartsAtSubMain = true;
        }
        var run = Crash(first);
        var recorded = RecoveryRun.Read(run)!.Projects.Single();
        recorded.Scratch.Should().BeTrue();
        recorded.ProjectFile.Should().NotBeNull("the project file is compared too");
        return (first, run);
    }

    [Fact]
    public async Task A_scratch_project_its_files_already_hold_is_not_offered_at_start()
    {
        // #2678 makes every document in a scratch project count as unsaved, so a killed --newproject run leaves a
        // backup whether or not anything was edited. Nothing was here, so the files hold every byte of it.
        var (_, run) = await CrashedScratch(edited: false);
        RecoveryRun.Read(run)!.Projects.Single().Documents.Should().Contain(d => d.File != null,
            "an unedited scratch document is captured too, which is why this rule exists");

        var offers = Offers(new Session());

        (await offers.OfferAtStartAsync()).Should().BeFalse();
        shown.Should().BeEmpty();
        Directory.Exists(run).Should().BeFalse();
    }

    [Fact]
    public async Task An_edited_scratch_project_is_still_offered_at_start()
    {
        var (_, run) = await CrashedScratch();

        var offers = Offers(new Session());
        await offers.OfferAtStartAsync();

        shown.Should().ContainSingle();
        Directory.Exists(run).Should().BeTrue();
    }

    [Fact]
    public async Task A_saved_project_s_work_waits_for_the_project_rather_than_being_offered_at_start()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);

        var offers = Offers(new Session());

        (await offers.OfferAtStartAsync()).Should().BeFalse();
        shown.Should().BeEmpty();
        offers.WaitingCount.Should().Be(1);
    }

    [Fact]
    public async Task Opening_a_project_offers_its_work_through_the_event()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);

        var second = new Session();
        var offered = new TaskCompletionSource();
        answers.Enqueue(dialog =>
        {
            offered.SetResult();
            return Task.CompletedTask;
        });
        Offers(second, second.Bus);
        await second.Service.OpenProject(vbp);

        await offered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        shown.Single().Items.Single().Name.Should().Be("Test");
    }

    [Fact]
    public async Task A_moved_project_is_found_by_its_project_file()
    {
        var original = Path.Join(dir, "A");
        var first = await Opened(WriteProject(original));
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);
        var moved = Path.Join(dir, "B");
        Directory.Move(original, moved);

        var second = new Session();
        var offers = Offers(second);
        string? origin = null;
        answers.Enqueue(dialog =>
        {
            origin = dialog.Items.Single().Origin;
            return Restore(dialog);
        });
        await second.Service.OpenProject(Path.Join(moved, "Test.vbp"));
        await offers.OfferForAsync(second.Project);

        origin.Should().StartWith("Str.Dialog.Recovery.MovedFrom: ").And.EndWith(Path.DirectorySeparatorChar + "A",
            "the dialog says where the work was left, so a copy can be told from the project it came from");
        Module(second.Project, "Module1").Code.Should().Contain("Public Sub Edited()");
    }

    [Fact]
    public async Task A_copy_beside_the_original_is_not_offered_the_original_s_work()
    {
        var original = Path.Join(dir, "A");
        var vbp = WriteProject(original);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);
        var copy = Path.Join(dir, "B");
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.EnumerateFiles(original))
            File.Copy(file, Path.Join(copy, Path.GetFileName(file)));

        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(Path.Join(copy, "Test.vbp"));
        await offers.OfferForAsync(second.Project);

        shown.Should().BeEmpty("the project the work belongs to is still where it was");
        offers.WaitingCount.Should().Be(1);
    }

    [Fact]
    public async Task A_file_changed_on_disk_since_the_backup_is_named_before_restoring()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        Crash(first);
        File.WriteAllText(Path.Join(dir, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\nPublic Sub ElsewhereSince()\r\nEnd Sub\r\n");

        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        shown.Single().Items.Single().ChangedOnDisk.Should().Be("Str.Dialog.Recovery.ChangedOnDisk: Module1");
    }

    [Fact]
    public async Task Discarding_asks_first_and_then_deletes_the_backup()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        var run = Crash(first);
        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(vbp);

        confirm = MessageBoxResult.No;
        answers.Enqueue(Discard);
        await offers.OfferForAsync(second.Project);

        messages.Should().ContainSingle(m => m.StartsWith("Str.Dialog.Recovery.DiscardConfirm"));
        shown.Single().Items.Should().ContainSingle("a discard not confirmed leaves the work on the list");
        Directory.Exists(run).Should().BeTrue();

        confirm = MessageBoxResult.Yes;
        await shown.Single().DiscardSelectedAsync();

        Directory.Exists(run).Should().BeFalse();
        offers.WaitingCount.Should().Be(0);
        Module(second.Project, "Module1").Code.Should().Contain("Public Sub Original()", "discarding restores nothing");
    }

    [Fact]
    public async Task Deciding_later_leaves_the_work_for_the_file_menu()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        Module(first.Project, "Module1").UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        var run = Crash(first);
        var second = new Session();
        var offers = Offers(second);
        await second.Service.OpenProject(vbp);

        answers.Enqueue(Later);
        await offers.OfferForAsync(second.Project);
        offers.WaitingCount.Should().Be(1);
        Directory.Exists(run).Should().BeTrue();

        answers.Enqueue(Restore);
        await offers.ShowPendingAsync();

        shown.Should().HaveCount(2);
        Module(second.Project, "Module1").Code.Should().Contain("Public Sub Edited()",
            "the project is open already, so its work goes straight into it");
        Directory.Exists(run).Should().BeFalse();
    }

    [Fact]
    public async Task The_file_menu_says_so_when_nothing_is_waiting()
    {
        var offers = Offers(new Session());

        await offers.ShowPendingAsync();

        shown.Should().BeEmpty();
        messages.Should().ContainSingle(m => m.StartsWith("Str.Dialog.Recovery.Nothing"));
    }

    [Fact]
    public async Task A_restore_that_cannot_be_made_keeps_the_work_and_says_so()
    {
        var first = new Session();
        first.Loaded.Add(TestHelpers.CreateProjectWithForm("Scratch"));
        var run = Crash(first);
        var renderedAt = RecoveryRun.Read(run)!.Projects.Single().RenderedAt;
        var occupied = Path.GetDirectoryName(renderedAt)!;
        Directory.CreateDirectory(occupied);
        File.WriteAllText(Path.Join(occupied, "someone-else.txt"), "not ours");
        try
        {
            var second = new Session();
            var offers = Offers(second);
            answers.Enqueue(Restore);

            (await offers.OfferAtStartAsync()).Should().BeFalse();

            second.Loaded.Should().BeEmpty();
            messages.Should().ContainSingle(m => m.StartsWith("Str.Dialog.Recovery.Failed"));
            shown.Single().Items.Should().ContainSingle("the work stays on the list to try again or discard");
            offers.WaitingCount.Should().Be(1);
            Directory.Exists(run).Should().BeTrue();
            File.ReadAllText(Path.Join(occupied, "someone-else.txt")).Should().Be("not ours");
        }
        finally
        {
            Directory.Delete(occupied, recursive: true);
        }
    }

    [Fact]
    public async Task A_saved_form_s_companion_comes_back_with_it()
    {
        var vbp = WriteProject(dir);
        var first = await Opened(vbp);
        first.Project.Forms[0].Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });
        Crash(first);

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        await second.Service.OpenProject(vbp);
        await offers.OfferForAsync(second.Project);

        second.Project.Forms[0].Components[0].GetPropertyOrDefault(VBProperties.IconProperty)
            .Should().Equal([1, 2, 3, 4], "the form's binary companion is backed up and read back with its text");
        File.Exists(Path.Join(dir, "Form1.frx")).Should().BeFalse("only a save writes the companion");
    }

    [Fact]
    public async Task A_never_saved_form_s_companion_comes_back_with_it()
    {
        var first = new Session();
        var project = TestHelpers.CreateProjectWithForm("Scratch");
        project.Forms[0].Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 5, 6, 7, 8 });
        first.Loaded.Add(project);
        Crash(first);

        var second = new Session();
        var offers = Offers(second);
        answers.Enqueue(Restore);
        (await offers.OfferAtStartAsync()).Should().BeTrue();

        second.Project.Forms[0].Components[0].GetPropertyOrDefault(VBProperties.IconProperty).Should().Equal([5, 6, 7, 8]);
    }

    [Fact]
    public async Task A_backup_naming_a_folder_outside_the_temporary_directory_is_not_written_to()
    {
        var outside = Path.Join(Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath())),
            "hexide-not-temp-" + Guid.NewGuid().ToString("N"));
        var backup = new UnsavedProject("Scratch", null, null, new Vb6TextEncoding(1252),
            new UnsavedFile("Scratch.vbp", "Type=Exe\r\nName=\"Scratch\"\r\n"), [], Path.Join(outside, "Scratch.vbp"));

        var restored = await new Session().Service.RestoreUnsavedProject(backup);

        restored.Should().BeNull();
        Directory.Exists(outside).Should().BeFalse();
    }
}
