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
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
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
        localization.GetString("Str.Archive.FolderReadyFormat").Returns("Ready: {0}");
        localization.GetString("Str.Archive.FolderErrorFormat").Returns("Stopped: {0}");
        archives = new(dock, recent, localization, windows);
    }

    [AvaloniaFact]
    public async Task Open_reload_and_close_keep_tabs_current_without_writing_files()
    {
        var path = Write("one.twinproj", "old");
        var before = File.ReadAllBytes(path);
        await archives.Open(path);
        await archives.Open(path);
        archives.Projects.Should().ContainSingle();
        var entry = archives.Projects[0].Root.Children[0];
        archives.OpenDocument(entry);
        archives.OpenDocument(entry);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        tab.Document.Text.Should().Be("old");
        File.ReadAllBytes(path).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
        recent.Received().Add(path);

        Write("one.twinproj", "replacement");
        await archives.Reload(Path.GetRelativePath(Environment.CurrentDirectory, path));
        tabs.Should().ContainSingle().Which.Should().BeSameAs(tab);
        tab.Document.Text.Should().Be("replacement");
        File.WriteAllBytes(path, [1, 2, 3]);
        var snapshot = archives.Projects[0];
        await FluentActions.Awaiting(() => archives.Reload(path)).Should().ThrowAsync<IOException>();
        archives.Projects[0].Should().BeSameAs(snapshot);
        tab.Document.Text.Should().Be("replacement");

        await archives.Close(Path.GetRelativePath(Environment.CurrentDirectory, path));
        tabs.Should().BeEmpty();
        archives.Projects.Should().BeEmpty();
        archives.OpenDocument(entry);
        tabs.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Identical_member_names_in_two_projects_get_distinct_tabs_and_reload_closes_removed_entries()
    {
        var one = Write("one.twinproj", "one");
        var two = Write("two.twinproj", "two");
        await archives.Open(one);
        await archives.Open(two);
        foreach (var p in archives.Projects) archives.OpenDocument(p.Root.Children[0]);
        tabs.Count.Should().Be(2);
        Write("one.twinproj", "renamed", name: "new.twin");
        await archives.Reload(one);
        tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which.Document.Text.Should().Be("two");
        await archives.Close();
        tabs.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Text_is_searchable_but_editor_replace_and_direct_mutation_are_read_only()
    {
        await archives.Open(Write("one.twinproj", "hello world hello"));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        dock.ActiveDocument.Returns(tab);
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.IsReadOnly.Should().BeTrue();
            var find = new FindReplaceViewModel(Substitute.For<IWindowManager>(), dock, localization)
                { SearchText = "world", ReplaceText = "changed" };
            find.FindNextCommand.Execute(null);
            tab.SelectionStart.Should().Be(6);
            editor.SelectionLength.Should().Be(5);
            find.ReplaceOneCommand.Execute(null);
            find.ReplaceAllCommand.Execute(null);
            tab.Document.Text.Should().Be("hello world hello");
            FluentActions.Invoking(() => tab.Document.Insert(0, "bad")).Should().ThrowExactly<InvalidOperationException>();
            tab.Document.Text.Should().Be("hello world hello");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task A_twin_entry_greys_its_inactive_branches_as_twinBASIC_compiles_them()
    {
        // At 32-bit, as #1691 measured beta 1024: Win64 is 0, and a call reads 0, so CBool(Win32) does not hold. The
        // container records neither the beta nor, with no Settings, the build type, so those branches are left alone.
        const string source = "Module Main\r\n#If Win64 Then\r\nA64\r\n#Else\r\nA32\r\n#End If\r\n" +
                              "#If CBool(Win32) Then\r\nCalled\r\n#Else\r\nReadAsZero\r\n#End If\r\n" +
                              "#If TWINBASIC_BUILD > 1000 Then\r\nBuildNew\r\n#Else\r\nBuildOld\r\n#End If\r\n" +
                              "#If TWINBASIC_BUILD_TYPE = \"standard exe\" Then\r\nStandardExe\r\n#Else\r\nOtherType\r\n#End If\r\n" +
                              "End Module\r\n";
        await archives.Open(Write("one.twinproj", source));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 600 };
        window.Show();
        try
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("Editor")!;
            int LineOf(string text) => editor.Document.GetLineByOffset(editor.Document.Text.IndexOf(text, StringComparison.Ordinal)).LineNumber;
            tab.InactiveLines.Should().Equal(new HexIDE.Language.LineRange(LineOf("A64"), LineOf("A64")),
                new HexIDE.Language.LineRange(LineOf("Called"), LineOf("Called")));

            var grey = ((Avalonia.Media.ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
            List<Avalonia.Media.Color?> ColoursOn(string text) => editor.TextArea.TextView
                .GetOrConstructVisualLine(editor.Document.GetLineByNumber(LineOf(text))).Elements
                .Where(e => e.DocumentLength > 0).Select(e => (e.TextRunProperties.ForegroundBrush as Avalonia.Media.ISolidColorBrush)?.Color)
                .ToList();
            ColoursOn("A64").Should().OnlyContain(c => c == grey);
            ColoursOn("Called").Should().OnlyContain(c => c == grey);
            foreach (var live in new[] { "A32", "ReadAsZero", "BuildNew", "BuildOld", "StandardExe", "OtherType" })
                ColoursOn(live).Should().Contain(c => c != grey, $"{live} is active or undetermined");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Only_twin_entries_are_read_for_conditional_compilation()
    {
        await archives.Open(Write("one.twinproj", "#If Win64 Then\r\nA64\r\n#End If\r\n", name: "notes.txt"));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which.InactiveLines.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Opaque_and_hidden_entries_are_inspectable_without_text_guessing()
    {
        await archives.Open(Write("one.twinproj", "not a text preview", name: "form.tbform", flags: 1));
        var root = archives.Projects[0].Root;
        new ArchiveTreeNode(root).Children.Should().BeEmpty();
        new ArchiveTreeNode(root, showHidden: true).Children.Should().ContainSingle();
        archives.OpenDocument(root.Children[0]);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        tab.Document.Text.Should().BeEmpty();
        tab.Notice.Should().Contain("UnsupportedType");
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
        focus.FocusedOrStartupProject.Should().BeNull();
        tree.SelectedItem = tree.RootItems.OfType<ArchiveTreeNode>().Should().ContainSingle().Which;
        tree.AddFormCommand.CanExecute(null).Should().BeFalse();
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        dock.ActiveDocument.Returns(tabs[0]);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        focus.FocusedOrStartupProject.Should().BeNull();
        dock.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        dock.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(dock, new PropertyChangedEventArgs(nameof(dock.ActiveDocument)));
        focus.FocusedOrStartupProject.Should().BeSameAs(project);
    }

    [AvaloniaFact]
    public async Task Source_folder_command_uses_selected_project_saved_bytes_and_keeps_previews_read_only()
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
        var tree = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        tree.SynchronizeArchiveFolderCommand.CanExecute(null).Should().BeFalse();
        var path = Write("one.twinproj", "preview");
        await archives.Open(path);
        await archives.Open(Write("two.twinproj", "other"));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        tree.SelectedItem = tree.RootItems.OfType<ArchiveTreeNode>().First().Children[0];
        tree.SynchronizeArchiveFolderCommand.CanExecute(null).Should().BeTrue();
        Directory.Exists(Path.Combine(directory, ".hexide")).Should().BeFalse();
        Write("one.twinproj", "saved after opening");
        var before = File.ReadAllBytes(path);
        await tree.SynchronizeArchiveFolder();
        var target = Path.Combine(directory, ".hexide", ".twinproj", "one.twinproj");
        File.ReadAllText(Path.Combine(target, "main.twin")).Should().Be("saved after opening");
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj", "two.twinproj")).Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(before);
        tab.Document.Text.Should().Be("preview");
        tab.IsReadOnlyDocument.Should().BeTrue();
        await windows.Received(1).MessageBox(Arg.Is<string>(s => s.StartsWith("Ready: ") && s.Contains("one.twinproj")),
            "Str.Archive.SyncFolder", MessageBoxButtons.Ok, MessageBoxIcon.Information);
        await archives.Reload(path);
        tab.Document.Text.Should().Be("saved after opening");
    }

    [AvaloniaFact]
    public async Task Source_folder_conflict_is_reported_and_closed_projects_cannot_be_exported()
    {
        var path = Write("one.twinproj", "saved");
        await archives.Open(path);
        await archives.SynchronizeFolder(path);
        var member = Path.Combine(directory, ".hexide", ".twinproj", "one.twinproj", "main.twin");
        File.WriteAllText(member, "user edit");
        windows.ClearReceivedCalls();
        await archives.SynchronizeFolder(path);
        File.ReadAllText(member).Should().Be("user edit");
        await windows.Received(1).MessageBox(Arg.Is<string>(s => s.StartsWith("Stopped: ") && s.Contains("main.twin")),
            "Str.Archive.SyncFolder", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        await archives.Close(path);
        windows.ClearReceivedCalls();
        await archives.SynchronizeFolder(path);
        windows.ReceivedCalls().Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Source_folder_command_disables_reentry_and_result_dialog_does_not_hold_lifecycle_gate()
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(Array.Empty<ProjectDefinition>());
        var tree = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        var path = Write("one.twinproj", "saved");
        await archives.Open(path);
        tree.SelectedItem = tree.RootItems.Should().ContainSingle().Which;
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dismissed = new TaskCompletionSource<MessageBoxResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        windows.MessageBox(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>())
            .Returns(_ => { shown.TrySetResult(); return dismissed.Task; });
        var operation = tree.SynchronizeArchiveFolder();
        try
        {
            tree.SynchronizeArchiveFolderCommand.CanExecute(null).Should().BeFalse();
            await tree.SynchronizeArchiveFolder(); // A repeated invocation must not queue another export/dialog.
            await shown.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await archives.Close(path).WaitAsync(TimeSpan.FromSeconds(20));
            archives.Projects.Should().BeEmpty();
        }
        finally { dismissed.TrySetResult(default); }
        await operation;
        windows.ReceivedCalls().Should().ContainSingle();
        tree.SynchronizeArchiveFolderCommand.CanExecute(null).Should().BeFalse();
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
            composition.ProjectManager.LoadedProjects.Should().BeEmpty();
            root.ProjectExplorer.RootItems.OfType<ArchiveTreeNode>().Should().ContainSingle();
            await composition.ProjectService.SaveAllProjects(false);
            await composition.ProjectService.SaveAllProjects(true);
            File.ReadAllBytes(path).Should().Equal(bytes);
            Directory.GetFiles(directory).Should().ContainSingle();
            var vb = new ProjectDefinition(VBProjectType.EXE, "VBProject");
            composition.ProjectManager.AddProject(vb);
            composition.ProjectRunnerService.CanStartDefaultProject.Should().BeFalse();
            composition.ProjectRunnerService.RunStartupProject();
            composition.ProjectRunnerService.IsRunning.Should().BeFalse();
            root.AddFormCommand.CanExecute(null).Should().BeFalse();
            root.AddFormCommand.Execute(null);
            vb.Forms.Should().BeEmpty();
            // Avoid a save prompt for the unrelated, newly created VB6 project.
            composition.ProjectManager.UnloadAllProjects();
            await composition.ProjectService.UnloadAllProjects();
            root.ProjectExplorer.RootItems.Should().BeEmpty();
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
