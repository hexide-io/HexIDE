using BrowserFolder = HexIDE.Projects.ContainerFolder;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Controls;
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
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public ArchiveProjectBrowsingTests()
    {
        Directory.CreateDirectory(directory);
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        dock.OpenDocuments.Returns(tabs);
        dock.When(d => d.OpenDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call => tabs.Add(call.Arg<BaseEditorWindowViewModel>()));
        dock.When(d => d.CloseDocument(Arg.Any<BaseEditorWindowViewModel>())).Do(call => { tabs.Remove(call.Arg<BaseEditorWindowViewModel>()); call.Arg<BaseEditorWindowViewModel>().Dispose(); });
        dock.TryActivate<ArchiveDocumentViewModel>(Arg.Any<Func<ArchiveDocumentViewModel, bool>>())
            .Returns(call => tabs.OfType<ArchiveDocumentViewModel>().Any(call.Arg<Func<ArchiveDocumentViewModel, bool>>()));
        localization.GetString("Str.Archive.FolderReadyFormat").Returns("Ready: {0}");
        localization.GetString("Str.Archive.FolderErrorFormat").Returns("Stopped: {0}");
        archives = new(dock, recent, localization, windows, InBoxRegistry.LanguageEditing(), () => lsp);
    }

    private static string OfficeFile(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root.Parent != null && !Directory.Exists(Path.Combine(root.FullName, "corpus"))) root = root.Parent;
        return Path.Combine(root.FullName, "corpus", "office", "m365-x64", name);
    }

    [AvaloniaFact]
    public async Task Ambiguous_folder_sync_prompts_with_both_names_and_cancellation_writes_nothing()
    {
        var path = Write("choice.twinproj", "Module Main");
        var stock = Path.Combine(directory, ".hexide");
        var brand = Path.Combine(directory, ".rdcore");
        Directory.CreateDirectory(stock);
        Directory.CreateDirectory(brand);
        File.WriteAllText(Path.Combine(brand, "project-state.json"), """{"kind":"hexide.project-state","version":1}""");
        localization.GetString("Str.ProjectLocalState.ChooseFormat").Returns(
            "Multiple saved states; choose one for this session or cancel. Nothing is written before choosing.\n{0}");
        var shown = false;
        windows.InputBox(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>(), false).Returns(call =>
        {
            var prompt = call.ArgAt<string>(0);
            var input = new HexIDE.Dialogs.InputBox { Prompt = prompt };
            var window = new Window { Content = input, Width = 700, Height = 400 };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                input.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(t =>
                    t.Text != null && t.Text.Contains(stock, StringComparison.Ordinal) && t.Text.Contains(brand, StringComparison.Ordinal));
                shown = true;
            }
            finally { window.Close(); }
            return Task.FromResult<string?>(null);
        });
        await archives.Open(path);
        await archives.SynchronizeFolder(path);
        shown.Should().BeTrue();
        Directory.GetFileSystemEntries(stock).Should().BeEmpty();
        Directory.GetFileSystemEntries(brand).Should().HaveCount(1);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Linked_state_folder_refusal_explains_unavailability_instead_of_invalid_brand_name(bool checkDescriptorText)
    {
        var path = Write("linked.twinproj", "Module Main");
        var target = Path.Combine(directory, "target");
        var link = Path.Combine(directory, ".hexide");
        Directory.CreateDirectory(target);
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe")
            {
                ArgumentList = { "/d", "/c", "mklink", "/J", link, target },
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            })!;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw Xunit.Sdk.SkipException.ForSkip("Cannot create directory junction: " + await process.StandardError.ReadToEndAsync());
        }
        else
        {
            try { Directory.CreateSymbolicLink(link, target); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                throw Xunit.Sdk.SkipException.ForSkip("Cannot create directory symbolic link: " + ex.Message);
            }
        }
        try
        {
            localization.GetString("Str.ProjectLocalState.UnavailableFormat").Returns(
                "{0} is a link or junction, or is no longer where it was found, so nothing is read from it or written to it.");
            localization.GetString("Str.Startup.Descriptor.ProjectLocalName").Returns(
                "{0}: Invalid project-local state name in '{1}': '{2}'.");
            await archives.Open(path);
            await archives.SynchronizeFolder(path);
            var call = windows.ReceivedCalls().Single(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox));
            var message = (string)call.GetArguments()[0]!;
            message.Should().Contain(link);
            if (checkDescriptorText)
                message.Should().NotContain("Invalid project-local state name");
            else
            {
                // The engine names the project's directory with its links resolved, so on macOS the path starts
                // /private/var where the temporary directory starts /var. Check the wording around the path.
                message.Should().StartWith("Stopped: ");
                message.Should().EndWith(Path.DirectorySeparatorChar + ".hexide is a link or junction, or is no longer where it was found, so nothing is read from it or written to it.");
            }
            Directory.GetFileSystemEntries(target).Should().BeEmpty();
        }
        finally { Directory.Delete(link); }
    }

    [AvaloniaTheory]
    [InlineData("basic.xlsm", "Str.Office.ExcelObjects")]
    [InlineData("basic.docm", "Str.Office.WordObjects")]
    [InlineData("userform.xlsm", "Str.Office.ExcelObjects")]
    public async Task Office_tree_and_readonly_code_show_the_source_notice_with_a_language_session(string file, string hostKey)
    {
        localization.GetString("Str.Office.ProjectCaption").Returns("{0} ({1})");
        await archives.Open(OfficeFile(file));
        var manager = Substitute.For<IProjectManager>();
        var model = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        var root = model.RootItems.OfType<ArchiveTreeNode>().Single();
        root.Name.Should().Contain(file);
        root.IsExpanded = true;
        root.Children.First().Name.Should().Be(hostKey);
        model.SelectedItem = root;
        model.SynchronizeArchiveFolderCommand.CanExecute(null).Should().BeFalse();
        var treeView = new ProjectToolView { DataContext = model };
        treeView.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml"),
        });
        var treeWindow = new Window { Content = treeView, Width = 700, Height = 500 };
        treeWindow.Show();
        var module = root.Children.Single(n => n.Name == (file == "userform.xlsm" ? "Str.Office.Forms" : "Str.Office.Modules")).Children.First();
        model.SelectedItem = module;
        model.OpenSelected();
        var tab = tabs.OfType<ArchiveDocumentViewModel>().Single();
        tab.Notice.Should().Be(file == "userform.xlsm" ? "Str.Office.FormNotice" : "Str.Office.SourceNotice");
        tab.InactiveLines.Should().BeEmpty();
        tab.Metadata.Should().BeEmpty();
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 700, Height = 500 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tree = treeView.GetLogicalDescendants().OfType<TreeView>().Single();
            tree.Items.Should().Contain(root);
            treeView.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(b => b.Text == hostKey);
            var caption = treeView.GetLogicalDescendants().OfType<TextBlock>().First(b => b.Text == root.Name);
            ToolTip.GetTip(caption).Should().Be("Str.Office.SourceNotice");
            var menu = (MenuFlyout)tree.ContextFlyout!;
            menu.ShowAt(tree);
            Dispatcher.UIThread.RunJobs();
            menu.Items.OfType<MenuItem>().First().IsVisible.Should().BeFalse();
            menu.Items.OfType<MenuItem>().Single(m => ReferenceEquals(m.Command, model.SynchronizeArchiveFolderCommand)).IsVisible.Should().BeFalse();
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.IsReadOnly.Should().BeTrue();
            editor.Text.Should().Contain(file == "userform.xlsm" ? "CommandButton1_Click" : "Option Explicit");
            view.GetLogicalDescendants().OfType<TextBlock>().Should().Contain(b => b.Text == tab.Notice);
            FluentActions.Invoking(() => tab.Document.Insert(0, "bad")).Should().Throw<InvalidOperationException>();
            await lsp.Received(1).OpenDocumentAsync(tab.DocumentUri, tab.Document.Text, true, languageId: null);
            lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
                new PublishDiagnosticsParams(tab.DocumentUri, [new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 6)), "problem", DiagnosticSeverity.Error)]));
            Dispatcher.UIThread.RunJobs();
            tab.Markers.Should().ContainSingle();
            editor.TextArea.TextView.BackgroundRenderers.OfType<LspTextMarkerService>().Single().Markers.Should().ContainSingle();
            await archives.Reload(OfficeFile(file));
            await lsp.Received(1).CloseDocumentAsync(tab.DocumentUri);
            await lsp.Received(2).OpenDocumentAsync(tab.DocumentUri, tab.Document.Text, true, languageId: null);
            tab.Markers.Should().BeEmpty();
            await archives.Close(OfficeFile(file));
            await lsp.Received(2).CloseDocumentAsync(tab.DocumentUri);
            lsp.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(ILspClient.ChangeDocumentAsync));
        }
        finally
        {
            ((MenuFlyout)treeView.GetLogicalDescendants().OfType<TreeView>().Single().ContextFlyout!).Hide();
            window.Close();
            treeWindow.Close();
        }
    }

    [AvaloniaFact]
    public async Task Office_locked_root_is_labelled_and_has_no_module_nodes()
    {
        localization.GetString("Str.Office.LockedCaption").Returns("{0} ({1}) locked");
        await archives.Open(OfficeFile("locked.xlsm"));
        var model = new ProjectToolViewModel(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        var root = model.RootItems.OfType<ArchiveTreeNode>().Single();
        root.Name.Should().Be("LockedProject (locked.xlsm) locked");
        root.Children.Should().OnlyContain(n => n.Name == "Str.Office.References");
        model.SelectedItem = root;
        model.InspectArchive();
        tabs.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task Office_selection_and_active_tab_refuse_run_and_make_in_the_composition()
    {
        var recentPath = UserDataPath.For("recent-projects.json");
        var savedRecent = File.Exists(recentPath) ? File.ReadAllBytes(recentPath) : null;
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var vb = new ProjectDefinition(VBProjectType.EXE, "VBProject");
        composition.ProjectManager.AddProject(vb);
        try
        {
            await composition.ArchiveProjects.Open(OfficeFile("basic.xlsm"));
            root.ProjectExplorer.SelectedItem = root.ProjectExplorer.RootItems.OfType<ArchiveTreeNode>().Single();
            composition.ProjectRunnerService.CanStartDefaultProject.Should().BeFalse();
            root.MakeProjectCommand.CanExecute(null).Should().BeFalse();
            composition.ProjectRunnerService.RunStartupProject();
            composition.ProjectRunnerService.IsRunning.Should().BeFalse();
            root.ProjectExplorer.SelectedItem = null;
            composition.ArchiveProjects.OpenDocument(composition.ArchiveProjects.Projects.Single().Root.Children.Cast<BrowserFolder>().Single(n => n.Name == "Standard").Children.First());
            composition.ArchiveProjects.IsActive.Should().BeTrue();
            composition.ProjectRunnerService.CanStartDefaultProject.Should().BeFalse();
            composition.ProjectRunnerService.CanRunStartupProject.Should().BeFalse();
            root.MakeProjectCommand.CanExecute(null).Should().BeFalse();
            LspDocumentSession.VersionsFor(composition.LspClient,
                composition.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single().DocumentUri).Should().NotBeNull();
            composition.ProjectRunnerService.RunStartupProject();
            composition.ProjectRunnerService.IsRunning.Should().BeFalse();
        }
        finally
        {
            composition.ProjectManager.UnloadAllProjects();
            await composition.ArchiveProjects.Close();
            if (savedRecent != null) File.WriteAllBytes(recentPath, savedRecent);
            else File.Delete(recentPath);
        }
    }

    [AvaloniaFact]
    public async Task Archive_text_sessions_open_reload_close_and_render_diagnostics()
    {
        var path = Write("one.twinproj", "Module Main", name: "Module # 1.twin");
        await archives.Open(path);
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = tabs.OfType<ArchiveDocumentViewModel>().Single();
        var uri = "archive-entry:/Module%20%23%201.twin?container=" + Uri.EscapeDataString(LspDocumentUri.ForFile(path));
        await lsp.Received(1).OpenDocumentAsync(uri, "Module Main", false, languageId: DocumentLanguage.TwinBasic);
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            // A drive-letter case change must still reach the window on Windows; Linux runs the same delivery path.
            var echo = OperatingSystem.IsWindows()
                ? uri.Replace(Uri.EscapeDataString(Path.GetPathRoot(path)![..1] + ":"),
                    Uri.EscapeDataString((char.IsUpper(path[0]) ? char.ToLowerInvariant(path[0]) : char.ToUpperInvariant(path[0])) + ":"), StringComparison.Ordinal)
                : uri;
            lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
                new PublishDiagnosticsParams(echo, [new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 6)), "problem", DiagnosticSeverity.Error)]));
            Dispatcher.UIThread.RunJobs();
            tab.Markers.Should().ContainSingle();
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.TextArea.TextView.BackgroundRenderers.OfType<LspTextMarkerService>().Single().Markers.Should().ContainSingle();
            tab.IsReadOnlyDocument.Should().BeFalse();
            Write("one.twinproj", "replacement", name: "Module # 1.twin");
            await archives.Reload(path);
            await lsp.Received(1).CloseDocumentAsync(uri);
            await lsp.Received(1).OpenDocumentAsync(uri, "replacement", false, languageId: DocumentLanguage.TwinBasic);
            tab.Markers.Should().BeEmpty();
            lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
                new PublishDiagnosticsParams(uri, [new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 3)), "new", DiagnosticSeverity.Error)]));
            Dispatcher.UIThread.RunJobs();
            editor.TextArea.TextView.BackgroundRenderers.OfType<LspTextMarkerService>().Single().Markers.Should().ContainSingle();
            await archives.Close(path);
            await lsp.Received(2).CloseDocumentAsync(uri);
            lsp.ReceivedCalls().Should().NotContain(c => c.GetMethodInfo().Name == nameof(ILspClient.ChangeDocumentAsync));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Ordinary_project_menu_hides_archive_toggle_and_focuses_first_visible_entry()
    {
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns(new[] { new ProjectDefinition(VBProjectType.EXE, "Ordinary") });
        var model = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization, archives);
        model.SelectedItem = model.LoadedProjects.Single();
        var view = new ProjectToolView { DataContext = model };
        view.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml"),
        });
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
        var menu = (MenuFlyout)tree.ContextFlyout!;
        try
        {
            Dispatcher.UIThread.RunJobs();
            tree.ContainerFromIndex(0)!.Focus().Should().BeTrue();
            menu.ShowAt(tree);
            Dispatcher.UIThread.RunJobs();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            items[0].IsVisible.Should().BeFalse("the hidden-entry toggle applies only to archive selections (#3131)");
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(items.First(item => item.IsVisible));
        }
        finally
        {
            menu.Hide();
            window.Close();
        }
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
    public async Task A_caret_prepared_before_the_archive_view_exists_is_shown_at_that_place()
    {
        await archives.Open(Write("one.twinproj", "first\nsecond"));
        archives.OpenDocument(archives.Projects[0].Root.Children[0]);
        var tab = tabs.Should().ContainSingle().Which.Should().BeOfType<ArchiveDocumentViewModel>().Which;
        HexIDE.Automation.CaretPlacement.Place(tab, 2, 3).Should().BeTrue();
        var view = new ArchiveDocumentView { DataContext = tab };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.CaretOffset.Should().Be(8);
            editor.TextArea.Caret.Line.Should().Be(2);
            editor.TextArea.Caret.Column.Should().Be(3);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Text_is_searchable_but_editor_replace_and_direct_mutation_are_read_only()
    {
        await archives.Open(Write("one.twinproj", "hello world hello", name: "preview.json"));
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reopened_twin_entry_keeps_the_latest_build_type_inactive_lines(bool updated)
    {
        const string source = "#If TWINBASIC_BUILD_TYPE = \"Standard EXE\" Then\r\nExeBranch\r\n#Else\r\nOtherBranch\r\n#End If\r\n";
        await archives.Open(Write("one.twinproj", source));
        var entry = archives.Projects[0].Root.Children[0];
        // As the archive service opens and updates it, with the entry's language: its #If reader is twinbasic's (#5403).
        var language = archives.Projects[0].LanguageOf(entry);
        var tab = new ArchiveDocumentViewModel(entry, localization,
            InBoxRegistry.LanguageEditing(),
            updated ? "ActiveX DLL" : "Standard EXE", languageId: language);
        if (updated) tab.Update(entry, "Standard EXE", language);
        tab.InactiveLines.Should().NotBeEmpty();

        var reopened = tab.Reopen();

        reopened.InactiveLines.Should().Equal(tab.InactiveLines);
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
    public async Task Source_folder_command_uses_selected_project_saved_bytes_and_keeps_open_buffers_unchanged()
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
        var target = Path.Combine(directory, ".hexide", "one.twinproj");
        File.ReadAllText(Path.Combine(target, "main.twin")).Should().Be("saved after opening");
        Directory.Exists(Path.Combine(directory, ".hexide", "two.twinproj")).Should().BeFalse();
        File.ReadAllBytes(path).Should().Equal(before);
        tab.Document.Text.Should().Be("preview");
        tab.IsReadOnlyDocument.Should().BeFalse();
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
        var member = Path.Combine(directory, ".hexide", "one.twinproj", "main.twin");
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
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var path = Write("one.twinproj", "source");
        var bytes = File.ReadAllBytes(path);
        try
        {
            await composition.ProjectService.OpenProject(path);
            composition.ProjectManager.LoadedProjects.Should().BeEmpty();
            root.ProjectExplorer.RootItems.OfType<ArchiveTreeNode>().Should().ContainSingle();
            composition.ArchiveProjects.OpenDocument(composition.ArchiveProjects.Projects.Single().Root.Children[0]);
            var archiveTab = composition.DocumentDockService.OpenDocuments.OfType<ArchiveDocumentViewModel>().Single();
            LspDocumentSession.VersionsFor(composition.LspClient, archiveTab.DocumentUri).Should().NotBeNull(
                "the production archive service must supply the same router as the rest of the IDE");
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
            // Production wiring uses the real history service; retain the temporary profile's entire history,
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

    public void Dispose()
    {
        foreach (var tab in tabs) tab.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
