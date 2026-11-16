using HexIDE.Runtime.Serialization;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Labs.Input;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class ActiveFormSaveTests
{
    private sealed class Shell : IAsyncDisposable
    {
        private readonly RealComposition owned = new();
        public DISetup Composition => owned.Composition;
        public readonly MainView Main;
        public readonly TextBox Tool = new();
        public readonly StackPanel Content = new();
        public readonly Window Window;

        public Shell()
        {
            Main = new MainView { DataContext = Composition.Root, Content = Content };
            Content.Children.Add(Tool);
            Window = new Window { Content = Main, Width = 900, Height = 600 };
            // MainWindow shares these bindings so its tunnel handler sees the fallback first.
            CommandManager.SetCommandBindings(Window, CommandManager.GetCommandBindings(Main));
            Window.Show();
            Dispatcher.UIThread.RunJobs();
            Tool.Focus();
        }

        public async ValueTask DisposeAsync()
        {
            Composition.DocumentDockService.CloseAll();
            Window.Close();
            await owned.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tool_focus_saves_the_active_designer(bool saveAs)
    {
        await using var shell = new Shell();
        await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
        var projects = Substitute.For<IProjectService>();
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), projects,
            null!, null!, null!, Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat());
        designer.Initialize(form);
        shell.Composition.DocumentDockService.OpenDocument(designer);
        Dispatcher.UIThread.RunJobs();
        shell.Tool.Focus();
        await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
        var command = saveAs ? ApplicationCommands.SaveAsCommand : ApplicationCommands.SaveCommand;

        await WaitFor(() => command.CanExecute(null, shell.Main), value => value, "Save command enabled");
        command.Execute(null, shell.Main);

        await WaitFor(() => projects.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProjectService.SaveForm)),
            value => value == 1, "SaveForm call count");
        await projects.Received(1).SaveForm(form, saveAs);
        shell.Composition.DocumentDockService.CloseDocument(designer);
        await WaitFor(() => command.CanExecute(null, shell.Main), value => !value, "Save command disabled");
    }

    [AvaloniaFact]
    public async Task Switching_active_form_requeries_commands_without_moving_keyboard_focus()
    {
        await using var shell = new Shell();
        await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
        Dispatcher.UIThread.RunJobs();
        var queries = 0;
        EventHandler handler = (_, _) => queries++;
        // Observe through the command, which retains its weak-event subscriber.
        var command = (ICommand)ApplicationCommands.SaveCommand;
        command.CanExecuteChanged += handler;
        try
        {
            // Change only the model: no document view is focused or attached.
            var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
            var focus = (FocusedProjectUtil)shell.Composition.Root.FocusedProjectUtil;
            focus.FocusedForm = form;
            await WaitFor(() => queries, value => value > 0, "command requery count");
            await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
            queries = 0;
            focus.FocusedForm = null;
            await WaitFor(() => queries, value => value > 0, "command requery count");
        }
        finally { command.CanExecuteChanged -= handler; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Form_code_save_waits_for_formatting_before_saving(bool saveAs)
    {
        await using var shell = new Shell();
        await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
        var projects = Substitute.For<IProjectService>();
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        var formatting = new TaskCompletionSource<TextEdit[]>();
        lsp.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>()).Returns(formatting.Task);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.FormatOnSave.Returns(true);
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), projects,
            shell.Composition.EventBus, lsp, settings, Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(), new RunScope(),
            Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader()).Initialize(form);
        shell.Composition.DocumentDockService.OpenDocument(vm);
        var view = new CodeEditorView { DataContext = vm };
        shell.Content.Children.Add(view);
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        await WaitFor(() => editor.TextArea.IsFocused, value => value, "editor focus");
        var command = saveAs ? ApplicationCommands.SaveAsCommand : ApplicationCommands.SaveCommand;
        await WaitFor(() => command.CanExecute(null, shell.Main), value => value, "Save command enabled");

        if (saveAs)
        {
            shell.Tool.Focus();
            await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
            command.Execute(null, shell.Main);
        }
        else
            PressSave(editor.TextArea);

        await WaitFor(() => lsp.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestFormattingAsync)),
            value => value > 0, "formatting request count");
        await projects.DidNotReceiveWithAnyArgs().SaveForm(default!, default);
        formatting.SetResult([]);
        await WaitFor(() => projects.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProjectService.SaveForm)),
            value => value == 1, "SaveForm call count");
        await projects.Received(1).SaveForm(form, saveAs);
    }

    [AvaloniaFact]
    public async Task Save_key_in_a_carried_file_reaches_its_own_binding()
    {
        await using var shell = new Shell();
        await WaitFor(() => shell.Tool.IsFocused, value => value, "tool focus");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "before");
            var vm = new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>(), Substitute.For<IWindowManager>(),
                Substitute.For<ILocalizationService>(), Substitute.For<HexIDE.IDE.ISettingsService>()).Initialize(
                new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "notes.txt", path));
            shell.Composition.DocumentDockService.OpenDocument(vm);
            var view = new RelatedDocumentEditorView { DataContext = vm };
            shell.Content.Children.Add(view);
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            vm.Document.Text = "after";
            editor.TextArea.Focus();
            await WaitFor(() => editor.TextArea.IsFocused, value => value, "editor focus");
            await WaitFor(() => ApplicationCommands.SaveCommand.CanExecute(null, shell.Main),
                value => !value, "shell Save command disabled");

            PressSave(editor.TextArea);

            await WaitFor(() => ReadSavedText(path), value => value == "after", "saved file content");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Project_file_save_reaches_the_tab_and_keeps_its_dock_position(bool toolFocus)
    {
        await using var shell = new Shell();
        var directory = Path.Join(Path.GetTempPath(), "hexide-project-tab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, "Example.vbp");
        try
        {
            File.WriteAllText(path, "Type=Exe\r\nName=\"Example\"\r\n");
            await shell.Composition.ProjectService.OpenProject(path);
            var project = shell.Composition.ProjectManager.LoadedProjects.Single();
            shell.Composition.EditorService.EditProjectFile(new ProjectFileSource(project));
            var tab = shell.Composition.DocumentDockService.OpenDocuments.OfType<ProjectFileDocumentViewModel>().Single();
            var owner = tab.Owner;
            var dock = (Dock.Model.Controls.IDocumentDock)owner!;
            var position = dock.VisibleDockables!.IndexOf(tab);
            var view = new ProjectFileDocumentView { DataContext = tab };
            shell.Content.Children.Add(view);
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("Editor")!;
            editor.IsReadOnly.Should().BeFalse();
            tab.Document.Insert(tab.Document.TextLength, "Title=\"X\"\r\n");
            var text = tab.Document.Text;
            if (toolFocus) shell.Tool.Focus();
            else editor.TextArea.Focus();
            await WaitFor(() => toolFocus ? shell.Tool.IsFocused : editor.TextArea.IsFocused, value => value, "save target focus");
            if (toolFocus)
            {
                await WaitFor(() => ApplicationCommands.SaveCommand.CanExecute(null, shell.Main), value => value, "project-file Save enabled");
                ApplicationCommands.SaveCommand.Execute(null, shell.Main);
            }
            else PressSave(editor.TextArea);
            await WaitFor(() => tab.IsModified, value => !value, "project-file save");
            File.ReadAllBytes(path).Should().Equal(Vb6TextFile.EncodeDocument(path, text, project.TextEncoding));
            tab.Owner.Should().BeSameAs(owner);
            dock.VisibleDockables!.IndexOf(tab).Should().Be(position);
            shell.Composition.DocumentDockService.OpenDocuments.Should().Contain(tab);
            tab.Source.Owner.Should().BeSameAs(shell.Composition.ProjectManager.LoadedProjects.Single());
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Closing_a_modified_project_file_uses_its_close_question(bool discard)
    {
        await using var shell = new Shell();
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid() + ".vbp");
        try
        {
            File.WriteAllText(path, "Type=Exe\r\nName=\"P\"\r\n");
            var projects = Substitute.For<IProjectService>();
            var vm = new ProjectFileDocumentViewModel(new ProjectFileSource(new ProjectDefinition(VBProjectType.EXE, "P")
                { AbsolutePath = path }), shell.Composition.EventBus, projects);
            projects.ConfirmProjectFileClose(vm).Returns(_ =>
            {
                if (discard) vm.Refresh();
                return Task.FromResult(discard);
            });
            shell.Composition.DocumentDockService.OpenDocument(vm);
            vm.Document.Insert(vm.Document.TextLength, "Title=\"X\"\r\n");
            shell.Composition.DocumentDockService.CloseDocument(vm);
            await WaitFor(() => projects.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(IProjectService.ConfirmProjectFileClose)),
                value => value == 1, "project-file close question");
            shell.Composition.DocumentDockService.OpenDocuments.Contains(vm).Should().Be(!discard);
            if (!discard)
            {
                vm.IsModified.Should().BeTrue();
                vm.Refresh();
            }
            File.ReadAllText(path).Should().NotContain("Title=");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public async Task A_clean_project_file_tab_stays_open_while_applying()
    {
        await using var shell = new Shell();
        var path = Path.Join(Path.GetTempPath(), Guid.NewGuid() + ".vbp");
        try
        {
            File.WriteAllText(path, "Type=Exe\r\nName=\"P\"\r\n");
            var projects = Substitute.For<IProjectService>();
            var vm = new ProjectFileDocumentViewModel(new ProjectFileSource(new ProjectDefinition(VBProjectType.EXE, "P")
                { AbsolutePath = path }), shell.Composition.EventBus, projects);
            shell.Composition.DocumentDockService.OpenDocument(vm);
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var applying = vm.DuringApply(() => completion.Task);
            try
            {
                shell.Composition.DocumentDockService.CloseDocument(vm);
                shell.Composition.DocumentDockService.OpenDocuments.Should().Contain(vm);
                await projects.DidNotReceive().ConfirmProjectFileClose(vm);
            }
            finally
            {
                completion.TrySetResult(true);
                await applying;
            }
        }
        finally { File.Delete(path); }
    }

    private static async Task WaitFor<T>(Func<T> read, Func<T, bool> completed, string description)
    {
        var timeout = Stopwatch.StartNew();
        T last;
        do
        {
            Dispatcher.UIThread.RunJobs();
            last = read();
            if (completed(last)) return;
            await Task.Delay(10, TestContext.Current.CancellationToken);
        } while (timeout.Elapsed < TimeSpan.FromSeconds(5));

        completed(last).Should().BeTrue("{0} should settle within five seconds; last observed: {1}", description, last);
    }

    private static string ReadSavedText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException ex) { return ex.Message; }
    }

    private static void PressSave(InputElement target)
    {
        var gesture = ApplicationCommands.SaveCommand.Gestures.Single();
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers
        });
    }
}
