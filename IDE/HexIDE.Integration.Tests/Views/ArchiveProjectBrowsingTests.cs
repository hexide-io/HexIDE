using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;
using HexIDE.TwinBasic;

namespace HexIDE.Integration.Tests.Views;

public sealed class ArchiveProjectBrowsingTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-archive-ui-" + Guid.NewGuid());
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();
    private readonly IRecentProjectsService recent = Substitute.For<IRecentProjectsService>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly List<BaseEditorWindowViewModel> tabs = [];
    private readonly ArchiveProjectService archives;

    public ArchiveProjectBrowsingTests()
    {
        Directory.CreateDirectory(directory);
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        dock.OpenDocuments.Returns(tabs);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call => tabs.Add(call.Arg<BaseEditorWindowViewModel>()));
        dock.When(d => d.CloseDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call => tabs.Remove(call.Arg<BaseEditorWindowViewModel>()));
        dock.TryActivate<ArchiveDocumentViewModel>(Arg.Any<Func<ArchiveDocumentViewModel, bool>>())
            .Returns(call => tabs.OfType<ArchiveDocumentViewModel>().Any(call.Arg<Func<ArchiveDocumentViewModel, bool>>()));
        archives = new(dock, recent, localization);
    }

    [AvaloniaFact]
    public async Task Open_reload_and_close_keep_tabs_current_without_writing_files()
    {
        var path = Write("one.twinproj", "old");
        var before = File.ReadAllBytes(path);
        await archives.Open(path);
        await archives.Open(path);
        Assert.Single(archives.Projects);
        var entry = archives.Projects[0].Root.Children[0];
        archives.OpenDocument(entry);
        archives.OpenDocument(entry);
        var tab = Assert.IsType<ArchiveDocumentViewModel>(Assert.Single(tabs));
        Assert.Equal("old", tab.Document.Text);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Single(Directory.GetFiles(directory));
        recent.Received().Add(path);

        Write("one.twinproj", "replacement");
        await archives.Reload(Path.GetRelativePath(Environment.CurrentDirectory, path));
        Assert.Same(tab, Assert.Single(tabs));
        Assert.Equal("replacement", tab.Document.Text);
        File.WriteAllBytes(path, [1, 2, 3]);
        var snapshot = archives.Projects[0];
        await Assert.ThrowsAnyAsync<IOException>(() => archives.Reload(path));
        Assert.Same(snapshot, archives.Projects[0]);
        Assert.Equal("replacement", tab.Document.Text);

        await archives.Close(Path.GetRelativePath(Environment.CurrentDirectory, path));
        Assert.Empty(tabs);
        Assert.Empty(archives.Projects);
        archives.OpenDocument(entry);
        Assert.Empty(tabs);
    }

    [AvaloniaFact]
    public async Task Identical_member_names_in_two_projects_get_distinct_tabs_and_reload_closes_removed_entries()
    {
        var one = Write("one.twinproj", "one");
        var two = Write("two.twinproj", "two");
        await archives.Open(one);
        await archives.Open(two);
        foreach (var p in archives.Projects) archives.OpenDocument(p.Root.Children[0]);
        Assert.Equal(2, tabs.Count);
        Write("one.twinproj", "renamed", name: "new.twin");
        await archives.Reload(one);
        Assert.Equal("two", Assert.IsType<ArchiveDocumentViewModel>(Assert.Single(tabs)).Document.Text);
        await archives.Close();
        Assert.Empty(tabs);
    }

    [AvaloniaFact]
    public async Task Text_is_searchable_but_editor_replace_and_direct_mutation_are_read_only()
    {
        await archives.Open(Write("one.twinproj", "hello world hello"));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = Assert.IsType<ArchiveDocumentViewModel>(Assert.Single(tabs));
        dock.ActiveDocument.Returns(tab);
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            var editor = view.FindControl<TextEditor>("Editor")!;
            Assert.True(editor.IsReadOnly);
            var find = new FindReplaceViewModel(Substitute.For<IWindowManager>(), dock, localization)
                { SearchText = "world", ReplaceText = "changed" };
            find.FindNextCommand.Execute(null);
            Assert.Equal(6, tab.SelectionStart);
            Assert.Equal(5, editor.SelectionLength);
            find.ReplaceOneCommand.Execute(null);
            find.ReplaceAllCommand.Execute(null);
            Assert.Equal("hello world hello", tab.Document.Text);
            Assert.Throws<InvalidOperationException>(() => tab.Document.Insert(0, "bad"));
            Assert.Equal("hello world hello", tab.Document.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Opaque_and_hidden_entries_are_inspectable_without_text_guessing()
    {
        await archives.Open(Write("one.twinproj", "not a text preview", name: "form.tbform", flags: 1));
        var root = archives.Projects[0].Root;
        Assert.Empty(new ArchiveTreeNode(root).Children);
        Assert.Single(new ArchiveTreeNode(root, showHidden: true).Children);
        archives.OpenDocument(root.Children[0]);
        var tab = Assert.IsType<ArchiveDocumentViewModel>(Assert.Single(tabs));
        Assert.Empty(tab.Document.Text);
        Assert.Contains("UnsupportedType", tab.Notice);
    }

    [AvaloniaFact]
    public async Task Archive_focus_suppresses_the_unrelated_startup_project_and_tree_mutation()
    {
        var manager = Substitute.For<IProjectManager>();
        var project = new ProjectDefinition(VBProjectType.EXE, "VBProject");
        manager.StartupProject.Returns(project);
        manager.LoadedProjects.Returns(new[] { project });
        var focus = new FocusedProjectUtil(dock, manager, archives);
        var tree = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        await archives.Open(Write("one.twinproj", "source"));
        Assert.Null(focus.FocusedOrStartupProject);
        tree.SelectedItem = Assert.Single(tree.RootItems.OfType<ArchiveTreeNode>());
        Assert.False(tree.AddFormCommand.CanExecute(null));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        dock.ActiveDocument.Returns(tabs[0]);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        Assert.Null(focus.FocusedOrStartupProject);
        dock.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        Assert.Same(project, focus.FocusedOrStartupProject);
    }

    [AvaloniaFact]
    public async Task Application_composition_routes_open_to_archive_service()
    {
        var recentPath = UserDataPath.For("recent-projects.json");
        var savedRecent = File.Exists(recentPath) ? File.ReadAllBytes(recentPath) : null;
        var composition = new DISetup();
        var root = composition.Root;
        var path = Write("one.twinproj", "source");
        var bytes = File.ReadAllBytes(path);
        try
        {
            await composition.ProjectService.OpenProject(path);
            Assert.Empty(composition.ProjectManager.LoadedProjects);
            Assert.Single(root.ProjectExplorer.RootItems.OfType<ArchiveTreeNode>());
            await composition.ProjectService.SaveAllProjects(false);
            await composition.ProjectService.SaveAllProjects(true);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Single(Directory.GetFiles(directory));
            var vb = new ProjectDefinition(VBProjectType.EXE, "VBProject");
            composition.ProjectManager.AddProject(vb);
            Assert.False(composition.ProjectRunnerService.CanStartDefaultProject);
            composition.ProjectRunnerService.RunStartupProject();
            Assert.False(composition.ProjectRunnerService.IsRunning);
            Assert.False(root.AddFormCommand.CanExecute(null));
            root.AddFormCommand.Execute(null);
            Assert.Empty(vb.Forms);
            // Avoid a save prompt for the unrelated, newly created VB6 project.
            composition.ProjectManager.UnloadAllProjects();
            await composition.ProjectService.UnloadAllProjects();
            Assert.Empty(root.ProjectExplorer.RootItems);
        }
        finally
        {
            // Production wiring uses the real history service; retain the developer's entire history,
            // including entries that adding the fixture could have evicted from a full list.
            if (savedRecent != null) File.WriteAllBytes(recentPath, savedRecent);
            else File.Delete(recentPath);
        }
    }

    private string Write(string file, string text, string name = "main.twin", uint flags = 0)
    {
        var path = Path.Combine(directory, file);
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output, Encoding.UTF8);
        writer.Write(0xEA0BA51Cu);
        Header("Project", 0);
        writer.Write(1u);
        Header(name, flags);
        var bytes = Encoding.UTF8.GetBytes(text);
        writer.Write((uint)bytes.Length);
        writer.Write(bytes);
        writer.Write(0u);
        return path;

        void Header(string entryName, uint entryFlags)
        {
            writer.Write((short)1);
            var encoded = Encoding.UTF8.GetBytes(entryName);
            writer.Write((uint)encoded.Length);
            writer.Write(encoded);
            writer.Write(1ul);
            writer.Write(entryFlags);
            writer.Write((byte)0);
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
