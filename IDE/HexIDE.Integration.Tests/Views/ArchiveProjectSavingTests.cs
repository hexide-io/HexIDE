using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.TwinBasic;

namespace HexIDE.Integration.Tests.Views;

public sealed class ArchiveProjectSavingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-archive-save-ui-" + Guid.NewGuid());
    public ArchiveProjectSavingTests() => Directory.CreateDirectory(directory);

    [AvaloniaFact]
    public async Task Typing_marks_modified_and_ordinary_Save_writes_and_clears_in_a_two_project_group()
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        _ = c.Root;
        File.WriteAllText(Path.Combine(directory, "One.vbp"), "Type=Exe\r\nName=\"One\"\r\n");
        File.WriteAllText(Path.Combine(directory, "Two.vbp"), "Type=Exe\r\nName=\"Two\"\r\n");
        var groupPath = Path.Combine(directory, "KeepThisGroup.vbg");
        var groupBytes = Encoding.UTF8.GetBytes("VBGROUP 5.0\r\nProject=One.vbp\r\nStartupProject=Two.vbp\r\n");
        File.WriteAllBytes(groupPath, groupBytes);
        await c.ProjectService.OpenProject(groupPath);
        var group = c.ProjectManager.CurrentGroup!;
        var one = c.ProjectManager.LoadedProjects[0];
        var two = c.ProjectManager.LoadedProjects[1];
        var groupTitle = c.Root.ProjectExplorer.Title;
        var path = Write("Main.twin");
        await c.ArchiveProjects.Open(path);
        c.ArchiveProjects.OpenDocument(c.ArchiveProjects.Projects.Single().Root.Children.Single());
        var tab = c.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.TextArea.Focus().Should().BeTrue();
            window.KeyTextInput("edited ");
            tab.IsModified.Should().BeTrue();
            tab.CanHoldUnsavedChanges.Should().BeTrue();
            tab.Document.Text.Should().Contain("edited ");
            c.Root.SaveFormCommand.CanExecute(null).Should().BeTrue();
            c.Root.SaveFormCommand.Execute(null);
            // The command's ordinary save does file I/O on a worker, then returns to the dispatcher.
            for (var i = 0; i < 200 && tab.IsModified; i++) await Task.Delay(25, TestContext.Current.CancellationToken);
            tab.IsModified.Should().BeFalse();
            using var input = File.OpenRead(path);
            var saved = TwinBasicContainerReader.Read(input);
            Encoding.UTF8.GetString(((TwinBasicFile)saved.Root.Children.Single()).Content.AsSpan()).Should().Be(tab.Document.Text);
            tab.Notice.Should().BeEmpty();
            c.ProjectManager.CurrentGroup.Should().BeSameAs(group);
            group.Name.Should().Be("KeepThisGroup");
            group.AbsolutePath.Should().Be(groupPath);
            File.ReadAllBytes(groupPath).Should().Equal(groupBytes);
            c.Root.ProjectExplorer.Title.Should().Be(groupTitle);
            c.ProjectManager.LoadedProjects.Should().Equal(one, two);
            c.ProjectManager.StartupProject.Should().BeSameAs(two);
        }
        finally
        {
            window.Close();
            c.ProjectManager.UnloadAllProjects();
            await c.ArchiveProjects.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("cancel", false)]
    [InlineData("discard", false)]
    [InlineData("save", false)]
    [InlineData("cancel", true)]
    [InlineData("discard", true)]
    [InlineData("save", true)]
    public async Task Modified_tab_or_project_close_asks_and_respects_the_answer(string answer, bool tabClose)
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        _ = c.Root;
        var dock = c.DocumentDockService;
        var windows = Substitute.For<IWindowManager>();
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var tabs = dock.OpenDocuments;
        var archives = new ArchiveProjectService(dock, Substitute.For<IRecentProjectsService>(), localization, windows,
            new HexIDE.Runtime.Interpreter.ConditionalCompilation.InterpreterConditionalCode());
        var path = Write("Main.twin");
        await archives.Open(path);
        archives.OpenDocument(archives.Projects.Single().Root.Children.Single());
        var tab = (ArchiveDocumentViewModel)tabs.Single();
        tab.IsReadOnlyDocument.Should().BeFalse();
        tab.Document.Insert(0, "edited");
        var asked = false;
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(call =>
        {
            asked = true;
            var prompt = call.Arg<SaveChangesViewModel>();
            prompt.ChangedFiles.Single().Name.Should().Contain(path).And.Contain("Main.twin");
            if (answer == "save") prompt.Yes();
            else if (answer == "discard") prompt.No();
            return Task.FromResult(answer != "cancel");
        });
        if (tabClose)
        {
            ((HexIDE.MainViewViewModel.DockFactory)c.Root.Layout.Factory!).CloseDockable(tab);
            for (var i = 0; i < 200 && answer != "cancel" && tabs.Contains(tab); i++)
                await Task.Delay(25, TestContext.Current.CancellationToken);
        }
        if (answer == "cancel")
        {
            if (!tabClose)
                await FluentActions.Awaiting(() => archives.Close(path)).Should().ThrowAsync<OperationCanceledException>();
            tabs.Should().Contain(tab);
            tab.IsModified.Should().BeTrue();
            archives.Projects.Should().ContainSingle();
        }
        else
        {
            if (!tabClose) await archives.Close(path);
            tabs.Should().BeEmpty();
            if (!tabClose) archives.Projects.Should().BeEmpty();
        }
        asked.Should().BeTrue();
        using var input = File.OpenRead(path);
        var content = ((TwinBasicFile)TwinBasicContainerReader.Read(input).Root.Children.Single()).Content;
        Encoding.UTF8.GetString(content.AsSpan()).Should().Be(answer == "save" ? "editedoriginal" : "original");
        tab.Dispose();
    }

    [AvaloniaFact]
    public async Task Non_source_entry_and_standalone_archive_preview_stay_read_only()
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        _ = c.Root;
        await c.ArchiveProjects.Open(Write("Form.tbform"));
        var entry = c.ArchiveProjects.Projects.Single().Root.Children.Single();
        c.ArchiveProjects.OpenDocument(entry);
        var tab = c.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
        tab.IsReadOnlyDocument.Should().BeTrue();
        FluentActions.Invoking(() => tab.Document.Insert(0, "bad")).Should().Throw<InvalidOperationException>();
        var sourcePath = Write("Main.twin");
        using var input = File.OpenRead(sourcePath);
        var source = ArchiveProjectSnapshot.Read(input, sourcePath).Root.Children.Single();
        using var standalone = new ArchiveDocumentViewModel(source, c.LocalizationService,
            new HexIDE.Runtime.Interpreter.ConditionalCompilation.InterpreterConditionalCode());
        standalone.IsReadOnlyDocument.Should().BeTrue();
        FluentActions.Invoking(() => standalone.Document.Insert(0, "bad")).Should().Throw<InvalidOperationException>();
        await c.ArchiveProjects.Close();
    }

    [AvaloniaFact]
    public async Task Cancelling_archive_close_keeps_the_loaded_two_project_group()
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        _ = c.Root;
        var windows = Substitute.For<IWindowManager>();
        var manager = Substitute.For<IProjectManager>();
        var one = new ProjectDefinition(VBProjectType.EXE, "One") { Offers = ProjectOffers.Nothing };
        var two = new ProjectDefinition(VBProjectType.EXE, "Two") { Offers = ProjectOffers.Nothing };
        var group = new ProjectGroupDefinition("SavedGroup") { AbsolutePath = Path.Combine(directory, "SavedGroup.vbg") };
        manager.LoadedProjects.Returns(new[] { one, two });
        manager.CurrentGroup = group;
        manager.StartupProject = two;
        var archives = new ArchiveProjectService(c.DocumentDockService, Substitute.For<IRecentProjectsService>(),
            c.LocalizationService, windows, new HexIDE.Runtime.Interpreter.ConditionalCompilation.InterpreterConditionalCode());
        await archives.Open(Write("Main.twin"));
        archives.OpenDocument(archives.Projects.Single().Root.Children.Single());
        var tab = c.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
        tab.IsReadOnlyDocument.Should().BeFalse();
        tab.Document.Insert(0, "edited");
        windows.ShowDialog(Arg.Any<SaveChangesViewModel>()).Returns(false);
        var service = new ProjectService(() => throw new InvalidOperationException(), windows,
            Substitute.For<IEventBus>(), manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<HexIDE.Sidecar.IUserSidecarService>(),
            new FileBaselineStore(), c.LocalizationService, Substitute.For<IHeaderRefresher>(),
            InBoxRegistry.Create(), archives);
        await FluentActions.Awaiting(service.UnloadAllProjects).Should().ThrowAsync<OperationCanceledException>();
        manager.DidNotReceive().UnloadAllProjects();
        manager.LoadedProjects.Should().Equal(one, two);
        manager.CurrentGroup.Should().BeSameAs(group);
        manager.StartupProject.Should().BeSameAs(two);
        group.Name.Should().Be("SavedGroup");
        group.AbsolutePath.Should().Be(Path.Combine(directory, "SavedGroup.vbg"));
        tab.IsModified.Should().BeTrue();
    }

    private string Write(string name)
    {
        var path = Path.Combine(directory, "project.twinproj");
        using var output = File.Create(path);
        TwinBasicContainerWriter.Write(new(1, new("Project", 1, 0, 0,
            [new TwinBasicFile(name, 3, 0, 4, [.. Encoding.UTF8.GetBytes("original")], [])])), output);
        return path;
    }

    public void Dispose() => Directory.Delete(directory, true);
}
