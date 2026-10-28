using System.IO;
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
        public readonly ProjectService Service;

        public Session()
        {
            Manager.LoadedProjects.Returns(_ => Loaded);
            Manager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(ci => Loaded.Add(ci.Arg<ProjectDefinition>()));
            var sidecar = Substitute.For<IUserSidecarService>();
            sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
            sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
            Service = new ProjectService(
                () => throw new InvalidOperationException("new-project dialog must not be reached"),
                Substitute.For<IWindowManager>(), Bus, Manager, Substitute.For<IRecentProjectsService>(),
                Substitute.For<IReferenceLibraryService>(), sidecar, Store, Substitute.For<ILocalizationService>(),
                Substitute.For<IHeaderRefresher>());
        }

        public ProjectDefinition Project => Loaded.Single();
    }

    private RecoveryOffers Offers(Session session, IEventBus? bus = null)
    {
        var recovery = Substitute.For<IRecoveryService>();
        recovery.RunFolder.Returns(Path.Join(root, "this-run"));

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
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);

        return new RecoveryOffers(recovery, session.Service, session.Manager, windows, localization, dock,
            session.Store, clock, bus ?? Substitute.For<IEventBus>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IHeaderRefresher>());
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
