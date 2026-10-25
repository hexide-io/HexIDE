using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private sealed class Shell : IDisposable
    {
        public readonly DISetup Composition = new();
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
            Tool.Focus().Should().BeTrue();
        }

        public void Dispose()
        {
            Composition.DocumentDockService.CloseAll();
            Window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tool_focus_saves_the_active_designer(bool saveAs)
    {
        using var shell = new Shell();
        var projects = Substitute.For<IProjectService>();
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), projects,
            null!, null!, null!, Substitute.For<ILocalizationService>());
        designer.Initialize(form);
        shell.Composition.DocumentDockService.OpenDocument(designer);
        Dispatcher.UIThread.RunJobs();
        shell.Tool.Focus();
        var command = saveAs ? ApplicationCommands.SaveAsCommand : ApplicationCommands.SaveCommand;

        command.CanExecute(null, shell.Main).Should().BeTrue();
        command.Execute(null, shell.Main);

        projects.Received(1).SaveForm(form, saveAs);
        shell.Composition.DocumentDockService.CloseDocument(designer);
        command.CanExecute(null, shell.Main).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Switching_active_form_requeries_commands_without_moving_keyboard_focus()
    {
        using var shell = new Shell();
        Dispatcher.UIThread.RunJobs();
        var queries = 0;
        EventHandler handler = (_, _) => queries++;
        CommandManager.RequerySuggested += handler;
        try
        {
            // Change only the model: no document view is focused or attached.
            var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
            var focus = (FocusedProjectUtil)shell.Composition.Root.FocusedProjectUtil;
            focus.FocusedForm = form;
            Dispatcher.UIThread.RunJobs();
            queries.Should().BeGreaterThan(0);
            shell.Tool.IsFocused.Should().BeTrue();
            queries = 0;
            focus.FocusedForm = null;
            Dispatcher.UIThread.RunJobs();
            queries.Should().BeGreaterThan(0);
        }
        finally { CommandManager.RequerySuggested -= handler; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Form_code_save_waits_for_formatting_before_saving(bool saveAs)
    {
        using var shell = new Shell();
        var projects = Substitute.For<IProjectService>();
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        var formatting = new TaskCompletionSource<TextEdit[]>();
        lsp.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(formatting.Task);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.FormatOnSave.Returns(true);
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), projects,
            shell.Composition.EventBus, lsp, settings, Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(), new RunScope(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        shell.Composition.DocumentDockService.OpenDocument(vm);
        var view = new CodeEditorView { DataContext = vm };
        shell.Content.Children.Add(view);
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus().Should().BeTrue();
        var command = saveAs ? ApplicationCommands.SaveAsCommand : ApplicationCommands.SaveCommand;
        command.CanExecute(null, shell.Main).Should().BeTrue();

        if (saveAs)
        {
            shell.Tool.Focus();
            command.Execute(null, shell.Main);
        }
        else
            PressSave(editor.TextArea);

        lsp.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestFormattingAsync));
        await projects.DidNotReceiveWithAnyArgs().SaveForm(default!, default);
        formatting.SetResult([]);
        for (var i = 0; i < 10 && !projects.ReceivedCalls().Any(); i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        await projects.Received(1).SaveForm(form, saveAs);
    }

    [AvaloniaFact]
    public void Save_key_in_a_carried_file_reaches_its_own_binding()
    {
        using var shell = new Shell();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        try
        {
            File.WriteAllText(path, "before");
            var vm = new RelatedDocumentEditorViewModel(Substitute.For<ILspClient>()).Initialize(
                new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "notes.txt", path));
            shell.Composition.DocumentDockService.OpenDocument(vm);
            var view = new RelatedDocumentEditorView { DataContext = vm };
            shell.Content.Children.Add(view);
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            vm.Document.Text = "after";
            editor.TextArea.Focus().Should().BeTrue();
            ApplicationCommands.SaveCommand.CanExecute(null, shell.Main).Should().BeFalse();

            PressSave(editor.TextArea);

            File.ReadAllText(path).Should().Be("after");
        }
        finally { File.Delete(path); }
    }

    private static void PressSave(InputElement target)
    {
        var gesture = ApplicationCommands.SaveCommand.Gestures.Single();
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers
        });
        Dispatcher.UIThread.RunJobs();
    }
}
