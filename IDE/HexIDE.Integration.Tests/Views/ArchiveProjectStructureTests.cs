using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Tools;
using HexIDE.TwinBasic;

namespace HexIDE.Integration.Tests.Views;

public sealed class ArchiveProjectStructureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-structure-ui-" + Guid.NewGuid());
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    private readonly ArchiveProjectService archives;
    private readonly ProjectToolViewModel model;

    public ArchiveProjectStructureTests()
    {
        Directory.CreateDirectory(directory);
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        localization.GetString("Str.Archive.DeletePrompt").Returns("Permanently delete '{0}' from the project?");
        dock.OpenDocuments.Returns(tabs);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call => tabs.Add(call.Arg<BaseEditorWindowViewModel>()));
        dock.When(d => d.CloseDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call =>
        {
            tabs.Remove(call.Arg<BaseEditorWindowViewModel>());
            call.Arg<BaseEditorWindowViewModel>().Dispose();
        });
        archives = new(dock, Substitute.For<IRecentProjectsService>(), localization, windows,
            InBoxRegistry.LanguageEditing());
        model = new(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), localization, archives);
    }

    [AvaloniaFact]
    public async Task Menus_offer_only_measured_operations_and_bound_rename_keeps_the_open_tab()
    {
        var path = Write();
        await archives.Open(path);
        var view = new ProjectToolView { DataContext = model };
        view.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
        { Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml") });
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
        var menu = (MenuFlyout)tree.ContextFlyout!;
        try
        {
            foreach (var (name, module, folder) in new[] { ("Main.twin", true, false), ("Sources", false, true),
                ("Nested", false, true), ("Form.twin", false, false), ("Form.tbform", false, false), ("Settings", false, false) })
            {
                model.SelectedItem = Node(name);
                menu.ShowAt(tree);
                Dispatcher.UIThread.RunJobs();
                Item(menu, "Rename").IsVisible.Should().Be(module);
                Item(menu, "Delete").IsVisible.Should().Be(module);
                Item(menu, "NewFolder").IsVisible.Should().Be(folder);
                menu.Hide();
            }
            model.SelectedItem = Node("Main.twin");
            model.OpenSelected();
            var tab = tabs.OfType<ArchiveDocumentViewModel>().Single();
            tab.CaretOffset = 3;
            windows.InputBox(Arg.Any<string>(), Arg.Any<string?>(), "Main", false).Returns("Helpers");
            menu.ShowAt(tree);
            Dispatcher.UIThread.RunJobs();
            Item(menu, "Rename").Command!.Execute(null);
            for (var i = 0; i < 200 && tab.Title != "Helpers.twin"; i++)
                await Task.Delay(25, TestContext.Current.CancellationToken);
            tabs.Should().ContainSingle().Which.Should().BeSameAs(tab);
            tab.Title.Should().Be("Helpers.twin");
            model.SelectedItem.Should().BeOfType<ArchiveTreeNode>().Which.Entry.Identity.Should().Be(tab.Entry.Identity);
            archives.IsActive.Should().BeTrue();
            tab.CaretOffset.Should().Be(3);
            tab.IsModified.Should().BeFalse();
            Node("Helpers.twin").Should().NotBeNull();
            using var input = File.OpenRead(path);
            TwinBasicContainerReader.Read(input).Root.Children.OfType<TwinBasicDirectory>().Single().Children[0].Name.Should().Be("Helpers.twin");
        }
        finally { menu.Hide(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delete_waits_for_named_confirmation_and_closes_only_after_yes(bool yes)
    {
        var path = Write();
        await archives.Open(path);
        model.SelectedItem = Node("Main.twin");
        model.OpenSelected();
        var tab = tabs.Single();
        var before = File.ReadAllBytes(path);
        var answer = new TaskCompletionSource<MessageBoxResult>();
        windows.MessageBox(Arg.Any<string>(), "Str.Archive.Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            .Returns(call =>
            {
                call.ArgAt<string>(0).Should().Contain("Main.twin");
                File.ReadAllBytes(path).Should().Equal(before);
                return answer.Task;
            });
        var deleting = model.DeleteArchive();
        deleting.IsCompleted.Should().BeFalse();
        tabs.Should().Contain(tab);
        File.ReadAllBytes(path).Should().Equal(before);
        answer.SetResult(yes ? MessageBoxResult.Yes : MessageBoxResult.No);
        await deleting;
        tabs.Contains(tab).Should().Be(!yes);
        model.SelectedItem.Should().BeOfType<ArchiveTreeNode>().Which.Name.Should().Be(yes ? "Sources" : "Main.twin");
        archives.IsActive.Should().BeTrue();
        File.ReadAllBytes(path).SequenceEqual(before).Should().Be(!yes);
        windows.ReceivedCalls().Should().ContainSingle(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
    }

    [AvaloniaTheory]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("folder")]
    public async Task Unsaved_source_refuses_each_command_before_prompt_or_write(string operation)
    {
        var path = Write();
        await archives.Open(path);
        model.SelectedItem = Node("Main.twin");
        model.OpenSelected();
        ((ArchiveDocumentViewModel)tabs.Single()).Document.Insert(0, "edited");
        var before = File.ReadAllBytes(path);
        if (operation == "folder") model.SelectedItem = Node("Sources");
        await Command(operation);
        File.ReadAllBytes(path).Should().Equal(before);
        await windows.Received(1).MessageBox("Str.Archive.StructureUnsaved", icon: MessageBoxIcon.Warning);
        windows.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(IWindowManager.InputBox));
    }

    [AvaloniaTheory]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("folder")]
    public async Task Edits_arriving_during_the_prompt_also_refuse_the_write(string operation)
    {
        var path = Write();
        await archives.Open(path);
        model.SelectedItem = Node("Main.twin");
        model.OpenSelected();
        var tab = (ArchiveDocumentViewModel)tabs.Single();
        var before = File.ReadAllBytes(path);
        var name = new TaskCompletionSource<string?>();
        var answer = new TaskCompletionSource<MessageBoxResult>();
        windows.InputBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), false).Returns(name.Task);
        windows.MessageBox(Arg.Any<string>(), "Str.Archive.Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning).Returns(answer.Task);
        if (operation == "folder") model.SelectedItem = Node("Sources");
        var pending = Command(operation);
        pending.IsCompleted.Should().BeFalse();
        tab.Document.Insert(0, "edited");
        name.SetResult("Fresh");
        answer.SetResult(MessageBoxResult.Yes);
        await pending;
        File.ReadAllBytes(path).Should().Equal(before);
        tabs.Should().Contain(tab);
        tab.IsModified.Should().BeTrue();
        windows.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox)
            && Equals(c.GetArguments()[0], "Str.Archive.StructureUnsaved"));
    }

    [AvaloniaFact]
    public async Task New_folder_writes_immediately_and_can_contain_another_folder()
    {
        var path = Write();
        await archives.Open(path);
        model.SelectedItem = Node("Sources");
        windows.InputBox(Arg.Any<string>(), "Str.Archive.NewFolder", "", false).Returns("Fresh");
        await model.NewArchiveFolder();
        Node("Fresh").Entry.Archive!.Source.Category.Should().Be(0);
        model.SelectedItem.Should().BeOfType<ArchiveTreeNode>().Which.Name.Should().Be("Fresh");
        Node("Sources").IsExpanded.Should().BeTrue();
        windows.InputBox(Arg.Any<string>(), "Str.Archive.NewFolder", "", false).Returns("Child");
        await model.NewArchiveFolder();
        using var input = File.OpenRead(path);
        var sources = TwinBasicContainerReader.Read(input).Root.Children.OfType<TwinBasicDirectory>().Single();
        sources.Children[0].Name.Should().Be("Fresh");
        ((TwinBasicDirectory)sources.Children[0]).Children[0].Name.Should().Be("Child");
    }

    [AvaloniaFact]
    public async Task Office_module_has_no_structural_commands()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent != null && !Directory.Exists(Path.Combine(root.FullName, "corpus"))) root = root.Parent;
        await archives.Open(Path.Combine(root.FullName, "corpus", "office", "m365-x64", "basic.xlsm"));
        foreach (var node in model.RootItems.OfType<ArchiveTreeNode>().SelectMany(Walk))
        {
            model.SelectedItem = node;
            model.CanRenameArchive.Should().BeFalse();
            model.CanDeleteArchive.Should().BeFalse();
            model.CanAddArchiveFolder.Should().BeFalse();
        }
    }

    [AvaloniaFact]
    public async Task Structural_edits_keep_the_saved_two_project_group()
    {
        await using var owned = new RealComposition();
        var c = owned.Composition;
        _ = c.Root;
        File.WriteAllText(Path.Combine(directory, "One.vbp"), "Type=Exe\r\nName=\"One\"\r\n");
        File.WriteAllText(Path.Combine(directory, "Two.vbp"), "Type=Exe\r\nName=\"Two\"\r\n");
        var groupPath = Path.Combine(directory, "KeepThisGroup.vbg");
        var bytes = Encoding.UTF8.GetBytes("VBGROUP 5.0\r\nProject=One.vbp\r\nStartupProject=Two.vbp\r\n");
        File.WriteAllBytes(groupPath, bytes);
        await c.ProjectService.OpenProject(groupPath);
        var group = c.ProjectManager.CurrentGroup!;
        var projects = c.ProjectManager.LoadedProjects.ToArray();
        var startup = c.ProjectManager.StartupProject;
        var title = c.Root.ProjectExplorer.Title;
        var service = new ArchiveProjectService(c.DocumentDockService, Substitute.For<IRecentProjectsService>(), localization,
            windows, InBoxRegistry.LanguageEditing());
        var explorer = new ProjectToolViewModel(c.ProjectManager, Substitute.For<IEventBus>(), c.ProjectService,
            Substitute.For<IEditorService>(), localization, service);
        try
        {
            await service.Open(Write());
            ArchiveTreeNode Find(string name) => explorer.RootItems.OfType<ArchiveTreeNode>().SelectMany(Walk).Single(n => n.Name == name);
            explorer.SelectedItem = Find("Sources");
            windows.InputBox(Arg.Any<string>(), "Str.Archive.NewFolder", "", false).Returns("Fresh");
            await explorer.NewArchiveFolder();
            Find("Fresh").Should().NotBeNull();
            explorer.SelectedItem = Find("Main.twin");
            explorer.OpenSelected();
            var tab = c.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
            windows.InputBox(Arg.Any<string>(), "Str.Archive.Rename", "Main", false).Returns("Helpers");
            await explorer.RenameArchive();
            tab.Title.Should().Be("Helpers.twin");
            explorer.SelectedItem = Find("Helpers.twin");
            windows.MessageBox(Arg.Any<string>(), "Str.Archive.Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning).Returns(MessageBoxResult.Yes);
            await explorer.DeleteArchive();
            c.DocumentDockService.OpenDocuments.Should().NotContain(tab);
            explorer.SelectedItem.Should().BeOfType<ArchiveTreeNode>().Which.Name.Should().Be("Sources");
            service.IsActive.Should().BeTrue();
            c.ProjectManager.CurrentGroup.Should().BeSameAs(group);
            group.Name.Should().Be("KeepThisGroup");
            group.AbsolutePath.Should().Be(groupPath);
            c.ProjectManager.LoadedProjects.Should().Equal(projects);
            c.ProjectManager.StartupProject.Should().BeSameAs(startup);
            c.Root.ProjectExplorer.Title.Should().Be(title);
            File.ReadAllBytes(groupPath).Should().Equal(bytes);
        }
        finally { await service.Close(); c.ProjectManager.UnloadAllProjects(); }
    }

    private Task Command(string operation) => operation == "rename" ? model.RenameArchive()
        : operation == "delete" ? model.DeleteArchive() : model.NewArchiveFolder();
    private static MenuItem Item(MenuFlyout menu, string key) => menu.Items.OfType<MenuItem>().Single(item =>
        item.Name == "Archive" + key);
    private ArchiveTreeNode Node(string name) => model.RootItems.OfType<ArchiveTreeNode>().SelectMany(Walk).Single(n => n.Name == name);
    private static IEnumerable<ArchiveTreeNode> Walk(ArchiveTreeNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
    private string Write()
    {
        var path = Path.Combine(directory, "project.twinproj");
        using var output = File.Create(path);
        TwinBasicContainerWriter.Write(new(1, new("Project", 0, 0, 0,
            [new TwinBasicDirectory("Sources", 0, 0, 3, [
                new TwinBasicFile("Main.twin", 3, 0, 0, [.. Encoding.UTF8.GetBytes("original")], []),
                new TwinBasicDirectory("Nested", 0, 0, 0, []),
                new TwinBasicFile("Form.twin", 0, 0, 0, [], []),
                new TwinBasicFile("Form.tbform", 0, 0, 0, [], [])]),
             new TwinBasicFile("Settings", 0, 0, 4, [], [])])), output);
        return path;
    }
    public void Dispose()
    {
        foreach (var tab in tabs) tab.Dispose();
        Directory.Delete(directory, true);
    }
}
