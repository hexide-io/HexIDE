using System.IO;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

// VB6 SP6 save/update measurements: https://github.com/hexide-io/HexIDE/pull/5201
// Multiple-copy save measurement: https://github.com/hexide-io/HexIDE/pull/5268
// Extra loss guards: https://github.com/hexide-io/HexIDE/issues/2756 (Team Desk decision).
public sealed class SharedFileSaveTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("hexide-shared-").FullName;
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly EventBus events = new();
    private readonly IDocumentDockService documents = Substitute.For<IDocumentDockService>();
    private readonly ProjectManager manager;
    private readonly ProjectService service;
    private readonly List<string> questions = [];
    private readonly Queue<MessageBoxResult> answers = new();
    private string path = "";
    private object[] copies = [];

    public SharedFileSaveTests()
    {
        manager = new ProjectManager(Substitute.For<IEditorService>(), events);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + " {0} {1}");
        strings.GetString("Str.Tool.ProtocolInspector.ReplaceFiles").Returns("Str.Tool.ProtocolInspector.ReplaceFiles {0}");
        strings.GetString("Str.Dialog.SharedFile.MultipleModified").Returns("Str.Dialog.SharedFile.MultipleModified {0}");
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(),
            Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<MessageBoxLayout>()).Returns(call =>
        {
            questions.Add(call.ArgAt<string>(0));
            return call.Arg<MessageBoxButtons>() == MessageBoxButtons.YesNo && answers.Count > 0
                ? answers.Dequeue() : MessageBoxResult.No;
        });
        service = new ProjectService(() => throw new InvalidOperationException(), windows, events, manager,
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(), strings,
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), documents: documents);
    }

    public void Dispose() => Directory.Delete(root, true);

    private async Task Open(string extension)
    {
        path = Path.Join(root, "Shared." + extension);
        var header = extension switch
        {
            "frm" => "VERSION 5.00\r\nBegin VB.Form Shared\r\n   Caption = \"Shared\"\r\n   ClientHeight = 1500\r\n   ClientWidth = 3000\r\nEnd\r\n",
            "cls" => "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1\r\nEND\r\n",
            _ => ""
        };
        File.WriteAllText(path, header + "Attribute VB_Name = \"Shared\"\r\n'original\r\n");
        var line = extension == "frm" ? "Form=..\\Shared.frm"
            : $"{(extension == "cls" ? "Class" : "Module")}=Shared; ..\\Shared.{extension}";
        foreach (var name in new[] { "P1", "P2" })
        {
            Directory.CreateDirectory(Path.Join(root, name));
            File.WriteAllText(Path.Join(root, name, name + ".vbp"),
                $"Type=Exe\r\n{line}\r\nName=\"{name}\"\r\n");
        }
        File.WriteAllText(Path.Join(root, "Together.vbg"),
            "VBGROUP 5.0\r\nProject=P1\\P1.vbp\r\nStartupProject=P2\\P2.vbp\r\n");
        await service.OpenProject(Path.Join(root, "Together.vbg"));
        copies = manager.LoadedProjects.Select(p => extension == "frm" ? (object)p.Forms.Single() : p.Modules.Single()).ToArray();
        copies.Should().HaveCount(2);
        copies[0].Should().NotBeSameAs(copies[1]);
        questions.Clear();
    }

    private string Code(int copy) => copies[copy] is FormDefinition form ? form.Code : ((ModuleDefinition)copies[copy]).Code;
    private void Edit(int copy, string text)
    {
        if (copies[copy] is FormDefinition form) form.UpdateCode("'" + text + "\r\n");
        else ((ModuleDefinition)copies[copy]).UpdateCode("'" + text + "\r\n");
    }
    private Task<bool> Save(int copy, bool interactive = true) => copies[copy] is FormDefinition form
        ? service.SaveForm(form, false, interactive) : service.SaveModule((ModuleDefinition)copies[copy], false, interactive);
    private bool Dirty(int copy) => copies[copy] is FormDefinition form
        ? service.HasUnsavedChanges(form) : service.HasUnsavedChanges((ModuleDefinition)copies[copy]);
    private void GroupIntact()
    {
        manager.CurrentGroup!.Name.Should().Be("Together");
        manager.CurrentGroup.AbsolutePath.Should().Be(Path.Join(root, "Together.vbg"));
        manager.LoadedProjects.Select(p => p.Name).Should().Equal("P1", "P2");
        manager.StartupProject.Should().BeSameAs(manager.LoadedProjects[1]);
        manager.LoadedProjects.Should().OnlyContain(project => !service.HasUnsavedChanges(project));
    }
    public static TheoryData<string, int> EachCopy => new() { { "bas", 0 }, { "bas", 1 }, { "cls", 0 }, { "cls", 1 }, { "frm", 0 }, { "frm", 1 } };
    public static TheoryData<string, bool> EachAnswer => new() { { "bas", false }, { "bas", true }, { "cls", false }, { "cls", true }, { "frm", false }, { "frm", true } };

    [Theory, MemberData(nameof(EachCopy))]
    public async Task Group_save_writes_only_the_changed_copy(string extension, int edited)
    {
        await Open(extension);
        Edit(edited, "edit");
        var written = new List<object>();
        using var saved = events.Subscribe<HexIDE.Events.DocumentSavedEvent>(e => written.Add((object?)e.Form ?? e.Module!));
        answers.Enqueue(MessageBoxResult.No);
        await service.SaveAllProjects(false);
        File.ReadAllText(path).Should().Contain("'edit");
        Code(1 - edited).Should().Contain("'original");
        written.Should().ContainSingle().Which.Should().BeSameAs(copies[edited]);
        Dirty(0).Should().BeFalse();
        Dirty(1).Should().BeFalse();
        GroupIntact();
    }

    [Theory, MemberData(nameof(EachAnswer))]
    public async Task Update_answer_controls_the_other_copy(string extension, bool update)
    {
        await Open(extension);
        Edit(0, "edit");
        answers.Enqueue(update ? MessageBoxResult.Yes : MessageBoxResult.No);
        (await Save(0)).Should().BeTrue();
        Code(1).Should().Contain(update ? "'edit" : "'original");
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.Update", StringComparison.Ordinal));
        Dirty(1).Should().BeFalse();
        if (update)
        {
            questions.Clear();
            Edit(1, "another edit");
            answers.Enqueue(MessageBoxResult.No);
            await Save(1);
            questions.Should().NotContain(q => q.StartsWith("Str.Dialog.SharedFile.DiskChanged", StringComparison.Ordinal));
        }
        GroupIntact();
    }

    [Theory, MemberData(nameof(EachAnswer))]
    public async Task Updating_a_dirty_copy_requires_its_own_consent(string extension, bool discard)
    {
        await Open(extension);
        Edit(0, "first");
        Edit(1, "second");
        answers.Enqueue(MessageBoxResult.Yes);
        answers.Enqueue(discard ? MessageBoxResult.Yes : MessageBoxResult.No);
        await Save(0);
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.DiscardChanges", StringComparison.Ordinal));
        Code(1).Should().Contain(discard ? "'first" : "'second");
        Dirty(1).Should().Be(!discard);
        File.ReadAllText(path).Should().Contain("'first");
    }

    [Theory, MemberData(nameof(EachAnswer))]
    public async Task Stale_copy_save_requires_overwrite_consent(string extension, bool overwrite)
    {
        await Open(extension);
        Edit(0, "first");
        answers.Enqueue(MessageBoxResult.No);
        await Save(0);
        Edit(1, "second");
        questions.Clear();
        answers.Enqueue(overwrite ? MessageBoxResult.Yes : MessageBoxResult.No);
        answers.Enqueue(MessageBoxResult.No);
        (await Save(1)).Should().Be(overwrite);
        File.ReadAllText(path).Should().Contain(overwrite ? "'second" : "'first");
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.DiskChanged", StringComparison.Ordinal));
        Dirty(1).Should().Be(!overwrite);
    }

    public static TheoryData<string, int> EachChoice => new()
    {
        { "bas", -1 }, { "bas", 0 }, { "bas", 1 },
        { "cls", -1 }, { "cls", 0 }, { "cls", 1 },
        { "frm", -1 }, { "frm", 0 }, { "frm", 1 }
    };

    [Theory, MemberData(nameof(EachChoice))]
    public async Task Group_save_selects_one_modified_copy_or_cancels_that_file(string extension, int choice)
    {
        var cancel = choice < 0;
        await Open(extension);
        Edit(0, "first");
        Edit(1, "second");
        var original = File.ReadAllBytes(path);
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            var vm = call.Arg<SaveChangesViewModel>();
            vm.ChangedFiles.Select(f => f.Name).Should().Equal("Shared." + extension + " (P1)", "Shared." + extension + " (P2)");
            vm.SelectedFiles.Clear();
            vm.SelectedFiles.Add(vm.ChangedFiles[cancel ? 0 : choice]);
            if (cancel) vm.Cancel(); else vm.Yes();
            return !cancel;
        });
        answers.Enqueue(MessageBoxResult.No);
        manager.LoadedProjects[1].Description = "saved too";
        await service.SaveAllProjects(false);
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.MultipleModified", StringComparison.Ordinal));
        if (cancel) File.ReadAllBytes(path).Should().Equal(original);
        else File.ReadAllText(path).Should().Contain(choice == 0 ? "'first" : "'second")
            .And.NotContain(choice == 0 ? "'second" : "'first");
        Code(1).Should().Contain("'second");
        File.ReadAllText(manager.LoadedProjects[1].AbsolutePath!).Should().Contain("saved too");
        GroupIntact();
    }

    [Theory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Selecting_both_copies_warns_again_without_writing(string extension)
    {
        await Open(extension);
        Edit(0, "first");
        Edit(1, "second");
        var original = File.ReadAllBytes(path);
        var shown = 0;
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            File.ReadAllBytes(path).Should().Equal(original);
            var vm = call.Arg<SaveChangesViewModel>();
            vm.SelectedFiles.Clear();
            foreach (var row in vm.ChangedFiles) vm.SelectedFiles.Add(row);
            if (++shown == 1) { vm.Yes(); return true; }
            vm.Cancel(); return false;
        });
        await service.SaveAllProjects(false);
        shown.Should().Be(2);
        questions.Count(q => q.StartsWith("Str.Dialog.SharedFile.MultipleModified", StringComparison.Ordinal)).Should().Be(2);
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Theory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Unchanged_shared_copies_do_not_write_over_external_content(string extension)
    {
        await Open(extension);
        File.AppendAllText(path, "'outside\r\n");
        var original = File.ReadAllBytes(path);
        await service.SaveAllProjects(false);
        File.ReadAllBytes(path).Should().Equal(original);
        questions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Automation_refuses_before_writing_when_an_update_question_is_due(string extension)
    {
        await Open(extension);
        Edit(0, "edit");
        var original = File.ReadAllBytes(path);
        Func<Task> save = () => Save(0, interactive: false);
        await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("*shared*updat*consent*");
        File.ReadAllBytes(path).Should().Equal(original);
        questions.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Update_refreshes_open_editors_and_preserves_a_declined_unsaved_buffer(string extension)
    {
        await Open(extension);
        using var first = ExternalFormChangeTests.Editor(service, events);
        using var second = ExternalFormChangeTests.Editor(service, events);
        if (copies[0] is FormDefinition form)
        {
            first.Initialize(form);
            second.Initialize((FormDefinition)copies[1]);
        }
        else
        {
            first.Initialize((ModuleDefinition)copies[0]);
            second.Initialize((ModuleDefinition)copies[1]);
        }
        documents.OpenDocuments.Returns(new BaseEditorWindowViewModel[] { first, second });
        first.Document.Text += "'first buffer edit\r\n";
        second.Document.Text += "'second buffer edit\r\n";
        answers.Enqueue(MessageBoxResult.Yes);
        answers.Enqueue(MessageBoxResult.No);
        await Save(0);
        questions.Should().Contain(q => q.StartsWith("Str.Dialog.SharedFile.DiscardChanges", StringComparison.Ordinal));
        second.BufferBody.Should().Contain("'second buffer edit").And.NotContain("'first buffer edit");
        questions.Clear();
        answers.Enqueue(MessageBoxResult.Yes);
        answers.Enqueue(MessageBoxResult.Yes);
        await Save(0);
        second.BufferBody.Should().Be(first.BufferBody);
        events.Publish(new HexIDE.Events.ApplyAllUnsavedChangesEvent());
        Code(1).Should().Be(Code(0));
        Dirty(1).Should().BeFalse();
        GroupIntact();
    }


    [Theory, MemberData(nameof(EachCopy))]
    public async Task Saving_one_project_offers_to_update_the_other_project(string extension, int saved)
    {
        await Open(extension);
        Edit(saved, "project edit");
        answers.Enqueue(MessageBoxResult.Yes);
        await service.SaveProject(manager.LoadedProjects[saved], false);
        File.ReadAllText(path).Should().Contain("'project edit");
        Code(1 - saved).Should().Be(Code(saved));
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.Update", StringComparison.Ordinal));
        GroupIntact();
    }

    [Theory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Saving_to_a_directory_also_offers_the_shared_file_update(string extension)
    {
        await Open(extension);
        Edit(0, "directory edit");
        answers.Enqueue(MessageBoxResult.Yes); // Existing output files.
        answers.Enqueue(MessageBoxResult.Yes); // Update the other copy.
        await service.SaveProjectToDirectory(manager.LoadedProjects[0], root);
        File.ReadAllText(path).Should().Contain("'directory edit");
        Code(1).Should().Be(Code(0));
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.Update", StringComparison.Ordinal));
    }

    [Theory, MemberData(nameof(EachAnswer))]
    public async Task Declined_shared_save_cancels_project_unload(string extension, bool wholeGroup)
    {
        await Open(extension);
        Edit(0, "first");
        answers.Enqueue(MessageBoxResult.No);
        await Save(0);
        Edit(1, "second");
        questions.Clear();
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            call.Arg<SaveChangesViewModel>().Yes();
            return true;
        });
        answers.Enqueue(MessageBoxResult.No);
        Func<Task> close = () => wholeGroup ? service.UnloadAllProjects()
            : service.UnloadProject(manager.LoadedProjects[1]);

        await close.Should().ThrowAsync<OperationCanceledException>();

        File.ReadAllText(path).Should().Contain("'first");
        Code(1).Should().Contain("'second");
        Dirty(1).Should().BeTrue();
        questions.Should().ContainSingle(q => q.StartsWith("Str.Dialog.SharedFile.DiskChanged", StringComparison.Ordinal));
        GroupIntact();
    }

    [Theory, MemberData(nameof(EachAnswer))]
    public async Task Unresolved_shared_choice_cancels_group_unload(string extension, bool cancel)
    {
        await Open(extension);
        Edit(0, "first");
        Edit(1, "second");
        var original = File.ReadAllBytes(path);
        manager.LoadedProjects[0].Description = "saved before cancelling close";
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            var vm = call.Arg<SaveChangesViewModel>();
            if (vm.ChangedFiles[0].Name.EndsWith(" (P1)", StringComparison.Ordinal))
            {
                if (cancel)
                {
                    vm.Cancel();
                    return false;
                }
                vm.SelectedFiles.Clear();
                vm.SelectedFiles.Add(vm.ChangedFiles[0]);
            }
            vm.Yes();
            return true;
        });
        answers.Enqueue(MessageBoxResult.No); // Keep the other project's unsaved copy after a chosen write.
        Func<Task> close = () => service.UnloadAllProjects();

        await close.Should().ThrowAsync<OperationCanceledException>();

        if (cancel) File.ReadAllBytes(path).Should().Equal(original);
        else File.ReadAllText(path).Should().Contain("'first").And.NotContain("'second");
        Code(0).Should().Contain("'first");
        Code(1).Should().Contain("'second");
        File.ReadAllText(manager.LoadedProjects[0].AbsolutePath!).Should().Contain("saved before cancelling close");
        GroupIntact();
    }


    // This result already passes on main: the old loop writes the last project's copy last.
    [Theory]
    [InlineData("bas"), InlineData("cls"), InlineData("frm")]
    public async Task Last_project_edit_is_retained_as_a_regression_guard(string extension)
    {
        await Open(extension);
        Edit(1, "last project edit");
        answers.Enqueue(MessageBoxResult.No);
        await service.SaveAllProjects(false);
        File.ReadAllText(path).Should().Contain("'last project edit");
    }

}
