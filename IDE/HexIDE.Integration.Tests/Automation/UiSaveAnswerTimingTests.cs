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
    [InlineData("menu", true)]
    [InlineData("click", true)]
    [InlineData("menu", false)]
    [InlineData("click", false)]
    [InlineData("invoke", true)]
    [InlineData("invoke", false)]
    public async Task Project_save_reports_an_answer_only_when_it_opened_no_picker(string route, bool opensPicker)
    {
        var composition = new DISetup();
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
                var found = MenuPath.Resolve(menu.Items, "File/Save Project As...");
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_key_picker_timing_depends_on_formatting(bool formatOnSave)
    {
        var composition = new DISetup();
        _ = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "Timing");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        composition.ProjectManager.AddProject(project);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        var formatting = new TaskCompletionSource<TextEdit[]>();
        lsp.RequestFormattingAsync(Arg.Any<string>(), Arg.Any<FormattingOptions>(), Arg.Any<CancellationToken>()).Returns(formatting.Task);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.FormatOnSave.Returns(formatOnSave);
        using var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), composition.ProjectService,
            composition.EventBus, lsp, settings, Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(), Substitute.For<IDebugController>(), new RunScope(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new CodeEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            ScriptedFileDialogs.Clear();
            ScriptedFileDialogs.AnswerNextWith(null);
            var gesture = ApplicationCommands.SaveCommand.Gestures.Single();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var result = UiAutomationDriver.PressKey(editor, gesture.Key.ToString(), gesture.KeyModifiers.ToString());
            result.Success.Should().BeTrue(result.Error);
            result.Detail.Should().NotContain("still pending", "a document save may still be waiting for formatting");
            ScriptedFileDialogs.Pending.Should().Be(formatOnSave ? 1 : 0,
                "the count is read before any dispatcher drain or formatting completion");
            if (formatOnSave)
            {
                lsp.ReceivedCalls().Should().Contain(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestFormattingAsync));
                formatting.SetResult([]);
                // Let the real continuation reach the picker; no timing sleeps.
                for (var i = 0; i < 10 && ScriptedFileDialogs.Pending != 0; i++)
                    await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                ScriptedFileDialogs.Pending.Should().Be(0, "the pending answer belonged to a save still in progress");
            }
        }
        finally
        {
            formatting.TrySetResult([]);
            ScriptedFileDialogs.Clear();
            composition.ProjectManager.UnloadAllProjects();
            window.Close();
        }
    }
}
