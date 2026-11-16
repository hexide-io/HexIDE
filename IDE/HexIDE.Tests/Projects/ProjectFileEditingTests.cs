using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

public sealed class ProjectFileEditingTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-project-text-" + Guid.NewGuid().ToString("N"));
    private readonly EventBus events = new();
    private readonly ProjectManager manager;
    private readonly ProjectService service;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    private string ProjectPath => Path.Join(directory, "First.vbp");

    public ProjectFileEditingTests()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(ProjectPath, "Type=Exe\r\nModule=Code; Code.bas\r\nName=\"First\"\r\n");
        File.WriteAllText(Path.Join(directory, "Code.bas"), "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        manager = new ProjectManager(Substitute.For<IEditorService>(), events);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns(tabs);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString("Str.ProjectFile.ReplaceModel").Returns("Replace unsaved settings for {0}?");
        service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(), windows,
            events, manager, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(), strings, Substitute.For<IHeaderRefresher>(),
            new Vb6ProjectFileFormat(), new ComponentRegistry(), documents: documents);
    }

    private async Task<ProjectFileDocumentViewModel> Open()
    {
        await service.OpenProject(ProjectPath);
        var tab = new ProjectFileDocumentViewModel(new ProjectFileSource(manager.LoadedProjects.Single()), events, service);
        tabs.Add(tab);
        return tab;
    }

    private void AnswerChanges(bool? save)
    {
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            var dialog = call.Arg<SaveChangesViewModel>();
            var answer = false;
            dialog.CloseRequested += value => answer = value;
            if (save == true) dialog.Yes();
            else if (save == false) dialog.No();
            else dialog.Cancel();
            return Task.FromResult(answer);
        });
    }

    private static void Type(ProjectFileDocumentViewModel tab) => tab.Document.Insert(tab.Document.TextLength, "Title=\"X\"\r\n");

    [AvaloniaFact]
    public async Task Editing_has_its_own_undo_and_modified_mark()
    {
        var tab = await Open();
        tab.IsReadOnlyDocument.Should().BeFalse();
        Type(tab);
        tab.IsModified.Should().BeTrue();
        tab.Document.UndoStack.Undo();
        tab.IsModified.Should().BeFalse();
        tab.Document.UndoStack.Redo();
        tab.IsModified.Should().BeTrue();
        File.ReadAllText(ProjectPath).Should().NotContain("Title=");
    }

    [AvaloniaFact]
    public async Task Saving_the_tab_applies_exact_text_and_rebinds_the_same_tab()
    {
        var tab = await Open();
        var old = tab.Source.Owner;
        Type(tab);
        var text = tab.Document.Text;
        (await tab.SaveAsync()).Should().BeTrue();
        File.ReadAllBytes(ProjectPath).Should().Equal(Vb6TextFile.EncodeDocument(ProjectPath, text, ((ProjectDefinition)old).TextEncoding));
        manager.LoadedProjects.Should().ContainSingle().Which.Should().BeSameAs(tab.Source.Owner).And.NotBeSameAs(old);
        ((ProjectDefinition)tab.Source.Owner).UnknownPreSectionLines.Select(l => l.RawLine).Should().Contain("Title=\"X\"");
        tab.Document.Text.Should().Be(text);
        tab.IsModified.Should().BeFalse();
        tabs.Should().ContainSingle().Which.Should().BeSameAs(tab);
    }

    [AvaloniaFact]
    public async Task Refused_text_keeps_the_edit_and_writes_nothing()
    {
        var tab = await Open();
        var before = File.ReadAllBytes(ProjectPath);
        tab.Document.Insert(tab.Document.TextLength, "Module=Missing; missing.bas\r\n");
        var text = tab.Document.Text;
        (await tab.SaveAsync()).Should().BeFalse();
        File.ReadAllBytes(ProjectPath).Should().Equal(before);
        tab.Document.Text.Should().Be(text);
        tab.IsModified.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData("disk")]
    [InlineData("model")]
    [InlineData("member")]
    public async Task Cancel_before_apply_keeps_the_edit_and_writes_nothing(string question)
    {
        var tab = await Open();
        Type(tab);
        var project = (ProjectDefinition)tab.Source.Owner;
        if (question == "disk") File.AppendAllText(ProjectPath, "Title=\"External\"\r\n");
        if (question == "model") project.Name = "Unsaved";
        if (question == "member") project.Modules.Single().UpdateCode("' unsaved\r\n");
        AnswerChanges(null);
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(false);
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), MessageBoxButtons.OkCancel, MessageBoxIcon.Warning,
            Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>()).Returns(MessageBoxResult.Cancel);
        var before = File.ReadAllBytes(ProjectPath);
        var member = File.ReadAllBytes(Path.Join(directory, "Code.bas"));
        var save = () => tab.SaveAsync();
        await save.Should().ThrowAsync<OperationCanceledException>();
        tab.IsModified.Should().BeTrue();
        File.ReadAllBytes(ProjectPath).Should().Equal(before);
        File.ReadAllBytes(Path.Join(directory, "Code.bas")).Should().Equal(member);
        manager.LoadedProjects.Should().Contain(project);
        if (question == "model")
            await windows.Received().MessageBox("Replace unsaved settings for Unsaved?", Arg.Any<string?>(),
                MessageBoxButtons.OkCancel, MessageBoxIcon.Warning, Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>());
    }

    [AvaloniaTheory]
    [InlineData("project")]
    [InlineData("group")]
    [InlineData("exit")]
    [InlineData("close-project")]
    [InlineData("close-tab")]
    public async Task Other_save_and_close_paths_ask_and_cancel_writes_nothing(string action)
    {
        var tab = await Open();
        Type(tab);
        AnswerChanges(null);
        var before = File.ReadAllBytes(ProjectPath);
        var project = (ProjectDefinition)tab.Source.Owner;
        Func<Task> run = action switch
        {
            "project" => () => service.SaveProject(project, false),
            "group" => () => service.SaveAllProjects(false),
            "exit" => () => service.UnloadAllProjects(),
            "close-project" => () => service.UnloadProject(project),
            _ => async () => (await tab.ConfirmCloseAsync()).Should().BeFalse(),
        };
        if (action == "close-tab") await run();
        else await run.Should().ThrowAsync<OperationCanceledException>();
        await windows.Received(1).ShowDialog(Arg.Is<SaveChangesViewModel>(d => d.ChangedFiles.Single().Name == "First.vbp"));
        File.ReadAllBytes(ProjectPath).Should().Equal(before);
        tab.IsModified.Should().BeTrue();
        manager.LoadedProjects.Should().Contain(project);
    }

    [AvaloniaFact]
    public async Task Project_save_discard_leaves_the_file_as_it_was()
    {
        var tab = await Open();
        Type(tab);
        AnswerChanges(false);
        var before = File.ReadAllBytes(ProjectPath);
        await service.SaveProject((ProjectDefinition)tab.Source.Owner, false);
        await windows.Received(1).ShowDialog(Arg.Is<SaveChangesViewModel>(d => d.ChangedFiles.Single().Name == "First.vbp"));
        File.ReadAllBytes(ProjectPath).Should().Equal(before);
        tab.IsModified.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Project_save_apply_preserves_the_typed_bytes()
    {
        var tab = await Open();
        Type(tab);
        var text = tab.Document.Text;
        AnswerChanges(true);
        await service.SaveAllProjects(false);
        File.ReadAllBytes(ProjectPath).Should().Equal(Vb6TextFile.EncodeDocument(ProjectPath, text, ((ProjectDefinition)tab.Source.Owner).TextEncoding));
        tab.IsModified.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Disk_conflict_reload_or_keep_updates_the_same_tab(bool keep)
    {
        var tab = await Open();
        Type(tab);
        var typed = tab.Document.Text;
        File.AppendAllText(ProjectPath, "Title=\"External\"\r\n");
        var disk = File.ReadAllBytes(ProjectPath);
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var conflict = call.Arg<FileConflictViewModel>();
            if (keep) conflict.KeepAll();
            else conflict.ReloadAll();
            return Task.FromResult(true);
        });
        (await tab.SaveAsync()).Should().Be(keep);
        File.ReadAllBytes(ProjectPath).Should().Equal(keep
            ? Vb6TextFile.EncodeDocument(ProjectPath, typed, ((ProjectDefinition)tab.Source.Owner).TextEncoding) : disk);
        tab.IsModified.Should().BeFalse();
        tab.Document.Text.Should().Be(Vb6TextFile.Decode(File.ReadAllBytes(ProjectPath), ((ProjectDefinition)tab.Source.Owner).TextEncoding));
        tab.Source.Owner.Should().BeSameAs(manager.LoadedProjects.Single());
    }

    [AvaloniaTheory]
    [InlineData("First")]
    [InlineData("Second")]
    public async Task Applying_in_a_group_keeps_group_order_and_startup(string edited)
    {
        await service.OpenProject(ProjectPath);
        var second = Path.Join(directory, "Second.vbp");
        File.WriteAllText(second, "Type=Exe\r\nName=\"Second\"\r\n");
        await service.OpenProject(second);
        var group = manager.CurrentGroup;
        manager.StartupProject = manager.LoadedProjects.Single(p => p.Name == edited);
        var tab = new ProjectFileDocumentViewModel(new ProjectFileSource(manager.StartupProject), events, service);
        tabs.Add(tab);
        Type(tab);
        (await tab.SaveAsync()).Should().BeTrue();
        manager.CurrentGroup.Should().BeSameAs(group);
        manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
        manager.StartupProject.Should().BeSameAs(tab.Source.Owner);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_or_exit_during_reload_is_refused(bool saveReopenedProject)
    {
        var tab = await Open();
        Type(tab);
        Task? reentered = null;
        void Unloaded(ProjectDefinition project)
        {
            if (!saveReopenedProject) reentered = service.UnloadAllProjects();
        }
        void Loaded(ProjectDefinition project)
        {
            if (saveReopenedProject) reentered = service.SaveProject(project, false);
        }
        manager.ProjectLoaded += Loaded;
        manager.ProjectUnloaded += Unloaded;
        try { (await tab.SaveAsync()).Should().BeTrue(); }
        finally
        {
            manager.ProjectLoaded -= Loaded;
            manager.ProjectUnloaded -= Unloaded;
        }

        reentered.Should().NotBeNull();
        Func<Task> request = () => reentered!;
        await request.Should().ThrowAsync<OperationCanceledException>();
        manager.LoadedProjects.Should().ContainSingle().Which.Should().BeSameAs(tab.Source.Owner);
        await windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }

    [AvaloniaFact]
    public async Task Edits_arriving_during_apply_keep_their_buffer_undo_and_new_disk_baseline()
    {
        var tab = await Open();
        Type(tab);
        var saved = tab.Document.Text;
        var buffer = tab.Document;
        ((ProjectDefinition)tab.Source.Owner).Name = "Unsaved model";
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), MessageBoxButtons.OkCancel, MessageBoxIcon.Warning,
            Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>()).Returns(_ =>
        {
            asked.TrySetResult();
            return answer.Task;
        });
        var saving = tab.SaveAsync();
        await asked.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        tab.Document.Insert(tab.Document.TextLength, "Description=\"Later edit\"\r\n");
        var pending = tab.Document.Text;
        tab.CaretOffset = tab.SelectionStart = pending.Length;
        answer.SetResult(MessageBoxResult.Ok);

        (await saving).Should().BeTrue();

        File.ReadAllBytes(ProjectPath).Should().Equal(Vb6TextFile.EncodeDocument(ProjectPath, saved,
            ((ProjectDefinition)tab.Source.Owner).TextEncoding));
        tab.Document.Should().BeSameAs(buffer);
        tab.Document.Text.Should().Be(pending);
        tab.CaretOffset.Should().Be(pending.Length);
        tab.IsModified.Should().BeTrue();
        tab.Document.UndoStack.Undo();
        tab.IsModified.Should().BeFalse();
        tab.Document.UndoStack.Redo();
        tab.IsModified.Should().BeTrue();

        (await tab.SaveAsync()).Should().BeTrue();
        tab.IsModified.Should().BeFalse();
        ((ProjectDefinition)tab.Source.Owner).Description.Should().Be("Later edit");
        await windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }

    [AvaloniaTheory]
    [InlineData("group")]
    [InlineData("format")]
    [InlineData("writer")]
    public void Group_and_addin_files_remain_read_only(string kind)
    {
        var path = Path.Join(directory, "Other.vbp");
        File.WriteAllText(path, "Type=Exe\r\nName=\"Other\"\r\n");
        var project = new ProjectDefinition(VBProjectType.EXE, "Other")
        {
            AbsolutePath = path,
            FormFormat = kind == "format" ? Substitute.For<IFormFileFormat>() : null,
            ProjectFileWriter = kind == "writer" ? Substitute.For<IProjectFileWriter>() : null,
        };
        var source = kind == "group"
            ? new ProjectFileSource(new ProjectGroupDefinition("G") { AbsolutePath = path })
            : new ProjectFileSource(project);
        using var tab = new ProjectFileDocumentViewModel(source, events, service);
        tab.IsReadOnlyDocument.Should().BeTrue();
        tab.CanHoldUnsavedChanges.Should().BeFalse();
        Action edit = () => Type(tab);
        edit.Should().Throw<InvalidOperationException>();
    }

    public void Dispose()
    {
        foreach (var tab in tabs) tab.Dispose();
        Directory.Delete(directory, true);
    }
}
