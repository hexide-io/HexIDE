using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Desktop.Server;
using HexIDE.Runtime.Serialization;
using HexIDE.Projects;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// Drives the production menu resolution, command binding and pointer/key delivery without an MCP transport.
/// A cancel reaches the real WindowManager picker bypass without writing files or recent-project history.
/// </summary>
public class UiSaveAnswerTimingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Document_save_fault_reports_failure_rather_than_a_running_save(bool timeout)
    {
        using var save = UiSaveFeedback.BeginDocumentSave();
        var failed = Task.FromException(timeout ? new TimeoutException("save failed") : new InvalidOperationException("save failed"));
        UiSaveFeedback.Started(failed);
        try
        {
            (await save.AfterAsync("Invoked Save.")).Should().Contain("Document save failed")
                .And.NotContain("still running");
        }
        finally { _ = failed.Exception; }
    }

    // press_key's and click's details end without a full stop ("the caret is at offset 485"); live, the save's
    // sentence ran straight on from them (#4047's review).
    [AvaloniaTheory]
    [InlineData("the caret is at offset 485", "the caret is at offset 485. Document save completed.")]
    [InlineData("Invoked 'Save Form1'.", "Invoked 'Save Form1'. Document save completed.")]
    [InlineData(null, "Document save completed.")]
    public async Task Document_save_sentence_follows_the_tools_detail_as_its_own_sentence(string? detail, string expected)
    {
        using var save = UiSaveFeedback.BeginDocumentSave();
        UiSaveFeedback.Started(Task.CompletedTask);
        (await save.AfterAsync(detail)).Should().StartWith(expected);
    }

    [AvaloniaTheory]
    [InlineData("menu", true)]
    [InlineData("click", true)]
    [InlineData("menu", false)]
    [InlineData("click", false)]
    [InlineData("invoke", true)]
    [InlineData("invoke", false)]
    public async Task Project_save_reports_an_answer_only_when_it_opened_no_picker(string route, bool opensPicker)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "Timing");
        if (opensPicker) composition.ProjectManager.AddProject(project);
        var window = ToolbarAutomationNameTests.ShowMainView();
        var main = window.Content.Should().BeOfType<MainView>().Which;
        main.DataContext = root;
        main.Focusable = true;
        Dispatcher.UIThread.RunJobs();
        main.Focus().Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Labs.Input.CommandManager.GetCommandBindings(main)
            .Single(b => ReferenceEquals(b.Command, ApplicationCommands.SaveProjectAsCommand))
            .ExecutingCommand.Should().NotBeNull();
        try
        {
            ScriptedFileDialogs.Clear();
            ScriptedFileDialogs.AnswerNextWith(null);
            string? reply;
            if (route == "menu")
            {
                // The same resolution and Execute as invoke_menu_item, without a transport or desktop lifetime.
                var menu = window.GetVisualDescendants().OfType<Menu>().First();
                var found = MenuPath.Resolve(menu.Items, opensPicker ? "File/Save Project As..." : "File/Save Project");
                found.Error.Should().BeNull();
                found.Command.Should().NotBeNull();
                found.Command!.CanExecute(found.CommandParameter).Should().BeTrue();
                reply = UiSaveFeedback.Execute(found.Command, found.CommandParameter, $"Invoked '{found.Header}'.");
            }
            else
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b =>
                    AutomationProperties.GetAutomationId(b) == "Standard.SaveProject");
                var result = await UiAutomationDriver.InteractAsync(button, route, null);
                result = await UiAutomationDriver.FinishAsync(result);
                result.Success.Should().BeTrue(result.Error);
                reply = result.Detail;
            }
            ScriptedFileDialogs.Pending.Should().Be(opensPicker ? 0 : 1,
                "read immediately after the reply, without pumping dispatcher jobs");
            if (opensPicker)
                reply.Should().NotContain("still pending");
            else
                reply.Should().EndWith("1 file-dialog answer armed before this call is still pending: this save opened no "
                    + "picker. The next file dialog to open will take it, whatever it is for; if it was armed for this "
                    + "save, clear_file_dialog_answers discards it, with any armed since.");
        }
        finally
        {
            ScriptedFileDialogs.Clear();
            composition.ProjectManager.UnloadAllProjects();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("key", false, true, "form")]
    [InlineData("key", false, false, "form")]
    [InlineData("menu", false, true, "form")]
    [InlineData("menu", false, false, "form")]
    [InlineData("invoke", false, true, "form")]
    [InlineData("click", false, true, "form")]
    [InlineData("key", true, true, "form")]
    [InlineData("menu", true, true, "form")]
    [InlineData("invoke", false, true, "module")]
    [InlineData("menu", false, true, "save-as")]
    public async Task Document_save_waits_for_formatting_and_reports_answer_state(string route, bool expires, bool opensPicker, string kind)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        _ = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "Timing");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-save-4047-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        if (!opensPicker) form.AbsolutePath = System.IO.Path.Combine(directory, "Form1.frm");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        if (kind == "module") project.AddModule(module);
        else project.AddForm(form);
        composition.ProjectManager.AddProject(project);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        var formatting = new TaskCompletionSource<TextEdit[]>();
        var formattingStarted = new TaskCompletionSource();
        lsp.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>()).Returns(_ => { formattingStarted.TrySetResult(); return formatting.Task; });
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.FormatOnSave.Returns(true);
        using var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), composition.ProjectService,
            composition.EventBus, lsp, settings, Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(), new RunScope(),
            Substitute.For<ILocalizationService>());
        if (kind == "module") vm.Initialize(module);
        else vm.Initialize(form);
        var view = new CodeEditorView { DataContext = vm };
        AutomationProperties.SetAutomationId(view, "DocumentEditor");
        var item = new MenuItem { Header = "Save", Command = kind == "save-as" ? ApplicationCommands.SaveAsCommand : ApplicationCommands.SaveCommand };
        var menu = new Menu { Items = { new MenuItem { Header = "File", Items = { item } } } };
        // A button bound to the editor's save, as a customised toolbar can be.
        var button = new Button { Content = "Save", Command = new HexIDE.Utils.DelegateCommand(kind == "module" ? vm.SaveModule : kind == "save-as" ? vm.SaveFormAs : vm.SaveForm), Focusable = false };
        AutomationProperties.SetAutomationId(button, "SaveDocument");
        var panel = new DockPanel();
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(button, Avalonia.Controls.Dock.Top);
        panel.Children.Add(menu);
        panel.Children.Add(button);
        panel.Children.Add(view);
        var window = new Window { Content = panel, Width = 900, Height = 600 };
        var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        lifetime.Windows.Returns(new[] { window });
        var lifetimeField = typeof(Avalonia.Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        var previousLifetime = lifetimeField.GetValue(Avalonia.Application.Current!);
        lifetimeField.SetValue(Avalonia.Application.Current!, lifetime);
        using var context = new IdeContext(null!, null!, lsp, null!, null!, null!, null!, null!, null!,
            Substitute.For<IDebugController>(), null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var tools = new HexIdeTools(context);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            ScriptedFileDialogs.Clear();
            ScriptedFileDialogs.AnswerNextWith(null);
            var gesture = ApplicationCommands.SaveCommand.Gestures.Single();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            editor.TextArea.Focus().Should().BeTrue();
            async Task<string?> Deliver()
            {
                if (route == "menu")
                {
                    var result = await tools.InvokeMenuItemAsync("File/Save", TestContext.Current.CancellationToken);
                    result.Success.Should().BeTrue(result.Error);
                    return result.Note;
                }
                var outcome = route == "key"
                    ? await tools.PressKeyAsync("Window/#DocumentEditor", gesture.Key.ToString(), gesture.KeyModifiers.ToString(), window: "ide")
                    : await tools.InteractAsync("Window/#SaveDocument", route, window: "ide");
                outcome.Success.Should().BeTrue(outcome.Error);
                return outcome.Detail;
            }
            var reply = Deliver();
            if (expires)
            {
                (await reply).Should().Contain("Document save is still running").And.Contain("still pending")
                    .And.NotContain("opened no picker");
                ScriptedFileDialogs.Pending.Should().Be(1);
                formatting.SetResult([]);
                Func<Task> finish = () => vm.LatestSave!;
                await finish.Should().ThrowAsync<OperationCanceledException>();
            }
            else
            {
                // Delivery may await the click's frame/release before starting formatting.
                await formattingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
                reply.IsCompleted.Should().BeFalse("the formatting response has not arrived");
                ScriptedFileDialogs.Pending.Should().Be(1);
                formatting.SetResult([]);
                var note = await reply;
                note.Should().Contain("Document save completed");
                if (opensPicker) note.Should().Contain("An armed file-dialog answer was taken").And.NotContain("still pending");
                else note.Should().Contain("still pending: this save opened no picker");
            }
            ScriptedFileDialogs.Pending.Should().Be(opensPicker ? 0 : 1);
        }
        finally
        {
            formatting.TrySetResult([]);
            if (vm.LatestSave is { } pending)
                try { await pending; }
                catch (OperationCanceledException) { } // The armed picker cancel is the expected end of this save.
            ScriptedFileDialogs.Clear();
            composition.ProjectManager.UnloadAllProjects();
            window.Close();
            System.IO.Directory.Delete(directory, true);
            lifetimeField.SetValue(Avalonia.Application.Current!, previousLifetime);
        }
    }
}
