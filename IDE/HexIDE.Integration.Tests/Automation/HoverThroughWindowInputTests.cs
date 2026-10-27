using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// <c>hover</c> sends its move through the window's own input, as the platform delivers a real one, so it is hit-tested
/// against what was drawn and the tooltip service sees it (#1265). Events raised on the control never opened a declared
/// <c>ToolTip.Tip</c>; they remain the fallback for a window with nothing drawn at the point.
/// </summary>
public class HoverThroughWindowInputTests
{
    // Drawn, as the tool waits for: the move is hit-tested against the frame the window last drew.
    private static Window Show(Control content)
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Button TippedButton(string content, string tip, bool enabled = true)
    {
        var button = new Button { Content = content, Width = 120, Height = 40, IsEnabled = enabled };
        ToolTip.SetTip(button, tip);
        ToolTip.SetShowDelay(button, 0);
        return button;
    }

    private static async Task Settle(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public async Task A_declared_tooltip_opens_and_the_reply_names_the_route()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(button);
        try
        {
            var outcome = UiAutomationDriver.Hover(button, null, null, null, out var landing);
            await Settle(100);

            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Be("hovered (60, 20) through the window's input");
            landing!.Value.ThroughWindowInput.Should().BeTrue();
            ToolTip.GetIsOpen(button).Should().BeTrue("the tooltip service saw the move");
        }
        finally { window.Close(); }
    }

    // What a person's pointer would be over is what is drawn at the point, so a control drawn over the target takes the
    // move, its tip is the one that opens, and the reply says where it landed rather than claiming the target (#1265 review).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_move_onto_a_control_drawn_over_the_target_lands_on_that_control_and_says_so(bool enabled)
    {
        var button = TippedButton("Under", "Under tip", enabled);
        var cover = new Border { Width = 120, Height = 40, Background = Avalonia.Media.Brushes.Red };
        ToolTip.SetTip(cover, "Cover tip");
        ToolTip.SetShowDelay(cover, 0);
        var window = Show(new Grid { Children = { button, cover } });
        try
        {
            var outcome = UiAutomationDriver.Hover(button, null, null, null, out var landing);
            await Settle(100);

            outcome.Detail.Should().StartWith("hovered (60, 20) through the window's input; it landed on ")
                .And.EndWith(", not the target, as that is what is drawn at the point, so it is what a person's pointer "
                             + "there would be over");
            landing!.Value.Receiver.Should().BeSameAs(cover);
            ToolTip.GetIsOpen(cover).Should().BeTrue();
            UiAutomationDriver.DeclaredToolTipAt(landing.Value.Receiver, landing.Value.Point).Should().Be("Cover tip");
            ToolTip.GetIsOpen(button).Should().BeFalse();
        }
        finally { window.Close(); }
    }

    // Avalonia's hit test skips a disabled control, so it is taken as reached, the tooltip service decides, and the reply
    // says why a tip may not open: the command-bound toolbar buttons are disabled much of the time (#1265 review).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_disabled_target_is_hovered_through_the_window_and_the_reply_says_it_is_disabled(bool showOnDisabled)
    {
        var button = TippedButton("End", "End", enabled: false);
        ToolTip.SetShowOnDisabled(button, showOnDisabled);
        var window = Show(new StackPanel { Children = { button } });
        try
        {
            var outcome = UiAutomationDriver.Hover(button, null, null, null, out var landing);
            await Settle(100);

            outcome.Detail.Should().Be("hovered (60, 20) through the window's input; the target is disabled, so it takes no "
                                       + "pointer input and shows a tip only when it sets ToolTip.ShowOnDisabled");
            landing!.Value.Receiver.Should().BeSameAs(button);
            ToolTip.GetIsOpen(button).Should().Be(showOnDisabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_point_with_nothing_drawn_at_it_falls_back_to_raising_on_the_control_and_says_so()
    {
        var button = new Button { Content = "Off", Width = 120, Height = 40 };
        var window = Show(new StackPanel { Children = { button } });
        try
        {
            var moved = false;
            button.AddHandler(InputElement.PointerMovedEvent, (object? _, PointerEventArgs _) => moved = true, RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.Hover(button, -600, -600, null, out var landing);

            outcome.Detail.Should().Be("hovered (-600, -600) (raised on the control, as the move through the window's input "
                                       + "found nothing drawn at the point, which is outside the window or not drawn yet, so a "
                                       + "declared tooltip will not open)");
            landing!.Value.ThroughWindowInput.Should().BeFalse();
            moved.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    // The tool ends every hover by moving the pointer out of the window, so a tip left open does not cover what the next
    // click is aimed at, and nothing stays pointer-over (#1265 review).
    [AvaloniaFact]
    public async Task Ending_a_hover_closes_the_tip_and_leaves_nothing_pointer_over_so_a_click_reaches_its_target()
    {
        var hovered = TippedButton("A", "A tip");
        var clicked = 0;
        var other = new Button { Content = "B", Width = 120, Height = 40 };
        other.Click += (_, _) => clicked++;
        var window = Show(new StackPanel { Children = { hovered, other } });
        try
        {
            UiAutomationDriver.Hover(hovered, null, null, null, out _).Success.Should().BeTrue();
            await Settle(100);
            ToolTip.GetIsOpen(hovered).Should().BeTrue();

            UiAutomationDriver.EndHover(hovered);
            Dispatcher.UIThread.RunJobs();

            ToolTip.GetIsOpen(hovered).Should().BeFalse();
            hovered.IsPointerOver.Should().BeFalse();
            var click = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(other, "click", null));
            click.Success.Should().BeTrue(click.Error);
            clicked.Should().Be(1);
        }
        finally { window.Close(); }
    }

    // A hover through the window moves the window's pointer on, so the control the last hover left it over is told it has
    // gone, as it would be by a real pointer.
    // #1303 asks that the same control hovered twice reports its tip both times. It does, here and live; the live misses
    // were the real mouse, resting over the window, taking the tip (#2376).
    [AvaloniaFact]
    public async Task Hovering_the_same_control_twice_opens_its_tip_twice()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(new StackPanel { Children = { button } });
        try
        {
            for (var round = 1; round <= 2; round++)
            {
                UiAutomationDriver.Hover(button, null, null, null, out _).Success.Should().BeTrue();
                await Settle(100);
                ToolTip.GetIsOpen(button).Should().BeTrue($"hover {round} should open the tip");
                UiAutomationDriver.EndHover(button);
                await Settle(50);
                ToolTip.GetIsOpen(button).Should().BeFalse($"the end of hover {round} should close it");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_hover_moves_the_pointer_off_the_control_the_last_one_left_it_over()
    {
        var first = new Button { Content = "First", Width = 120, Height = 40 };
        var second = new Button { Content = "Second", Width = 120, Height = 40 };
        var window = Show(new StackPanel { Children = { first, second } });
        try
        {
            UiAutomationDriver.Hover(first, null, null, null, out _).Success.Should().BeTrue();
            first.IsPointerOver.Should().BeTrue();
            UiAutomationDriver.Hover(second, null, null, null, out _).Success.Should().BeTrue();

            first.IsPointerOver.Should().BeFalse();
            second.IsPointerOver.Should().BeTrue();
        }
        finally { window.Close(); }
    }

    // What a handler throws is the program's: reported as the hover's failure, once, and not taken for an unreachable
    // Avalonia and raised a second time on the control (#1265 review).
    [AvaloniaFact]
    public async Task A_handler_that_throws_fails_the_hover_once()
    {
        var button = TippedButton("Add", "Add Form");
        var window = Show(new StackPanel { Children = { button } });
        try
        {
            var moves = 0;
            button.AddHandler(InputElement.PointerMovedEvent, (object? _, PointerEventArgs _) =>
            {
                moves++;
                throw new InvalidOperationException("app bug");
            }, RoutingStrategies.Bubble);

            var outcome = UiAutomationDriver.Hover(button, null, null, null, out _);

            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Be("hover threw: app bug");
            moves.Should().Be(1);
            await Settle(100);
            button.IsPointerOver.Should().BeFalse();
            window.IsPointerOver.Should().BeFalse();
            ToolTip.GetIsOpen(button).Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_hidden_disabled_target_is_refused_as_not_showing()
    {
        var hidden = TippedButton("Hidden", "Hidden tip", enabled: false);
        hidden.IsVisible = false;
        var window = Show(new StackPanel { Children = { hidden, TippedButton("Shown", "Shown tip") } });
        try
        {
            var outcome = UiAutomationDriver.Hover(hidden, null, null, null, out var landing);
            outcome.Success.Should().BeFalse();
            outcome.Error.Should().Contain("is not showing");
            landing.Should().BeNull();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_container_prefers_its_showing_editor_to_its_first_hidden_editor()
    {
        var hidden = new TextEditor { Name = "HiddenEditor", IsVisible = false, Text = "Hidden", Height = 80 };
        var shown = new TextEditor { Name = "ShownEditor", Text = "Shown", Height = 80 };
        var panel = new StackPanel { Children = { hidden, shown } };
        var window = Show(panel);
        try
        {
            var outcome = UiAutomationDriver.Hover(panel, null, null, "0", out var landing);
            outcome.Success.Should().BeTrue(outcome.Error);
            outcome.Detail.Should().Contain("on TextEditor[ShownEditor], at 0");
            landing!.Value.Receiver.Should().BeSameAs(shown);
        }
        finally { window.Close(); }
    }

    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly List<Position> hoverAsks = [];

    private (Window Window, TextEditor Editor, CodeEditorViewModel Vm) ShowEditor(ExternalDebugService? external = null)
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
        lsp.RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                hoverAsks.Add(ci.ArgAt<Position>(1));
                return Task.FromResult<HoverResult?>(new HoverResult(new MarkupContent("plaintext", "Sub Foo()")));
            });
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.AutoQuickInfo.Returns(true);
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>(), externalDebugger: external);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    Mid\r\nEnd Sub\r\n");
        vm.Initialize(module);
        var window = new Window { Width = 1200, Height = 800 };
        window.Show();
        window.Content = new CodeEditorView { DataContext = vm };
        Dispatcher.UIThread.RunJobs();
        var editor = ((CodeEditorView)window.Content).FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Caret.Offset = editor.Document.Text.IndexOf("    Mid", StringComparison.Ordinal) + 5;
        for (var i = 0; i < 5; i++) { Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        return (window, editor, vm);
    }

    [AvaloniaFact]
    public async Task Scrolled_hover_requests_the_visible_identifier()
    {
        var (window, editor, vm) = ShowEditor();
        try
        {
            editor.Text = string.Join("\n", Enumerable.Range(1, 150).Select(i => $"identifier{i}"));
            editor.ScrollToLine(80);
            for (var i = 0; i < 5; i++) { Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            var view = editor.TextArea.TextView;
            view.ScrollOffset.Y.Should().BeGreaterThan(0);
            var point = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(80, 4), AvaloniaEdit.Rendering.VisualYPosition.TextMiddle) - view.ScrollOffset;
            var target = view.TranslatePoint(point, editor)!.Value;
            UiAutomationDriver.Hover(editor, target.X, target.Y, null, out _).Success.Should().BeTrue();
            await Settle(700);
            hoverAsks.Should().ContainSingle();
            hoverAsks[0].Line.Should().Be(79);
            var asked = hoverAsks[0];
            var line = editor.Document.GetLineByNumber(asked.Line + 1);
            editor.Document.GetText(line).Should().Be("identifier80");
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaFact]
    public async Task External_hover_uses_a_stopped_frame_and_quick_info_after_continue()
    {
        // Read once, before any await: after continuations on Avalonia's dispatcher, TestContext.Current can resolve a
        // context already disposed, and a throw from the finally hides whatever the test body threw.
        var cancellationToken = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-hover-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        await using var log = new HexIDE.Conversations.ConversationLog();
        var external = new ExternalDebugService(log);
        var config = Path.Combine(directory, "launch.json");
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        await File.WriteAllTextAsync(config, System.Text.Json.JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(HexIDE.Dap.TestAdapter.Program).Assembly.Location, "evaluate-running" },
            adapterId = "fixture", launch = new { },
        }), cancellationToken);
        var run = external.StartAsync(config);
        var (window, editor, vm) = ShowEditor(external);
        try
        {
            for (var i = 0; i < 200 && !external.CanEvaluateHover; i++) await Settle(20);
            external.CanEvaluateHover.Should().BeTrue();
            editor.Text = "answer";
            for (var i = 0; i < 5; i++) { Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
            var view = editor.TextArea.TextView;
            var point = view.GetVisualPosition(new AvaloniaEdit.TextViewPosition(1, 3), AvaloniaEdit.Rendering.VisualYPosition.TextMiddle) - view.ScrollOffset;
            var target = view.TranslatePoint(point, editor)!.Value;
            UiAutomationDriver.Hover(editor, target.X, target.Y, null, out _).Success.Should().BeTrue();
            await Settle(700);
            ToolTip.GetTip(editor)?.ToString().Should().Be("answer = frame:0:answer");
            hoverAsks.Should().BeEmpty();
            UiAutomationDriver.EndHover(editor);
            await external.CommandAsync("continue");
            for (var i = 0; i < 200 && external.Stop is not null; i++) await Settle(20);
            external.IsActive.Should().BeTrue();
            external.Stop.Should().BeNull();
            UiAutomationDriver.Hover(editor, target.X, target.Y, null, out _).Success.Should().BeTrue();
            await Settle(700);
            hoverAsks.Should().ContainSingle();
            ToolTip.GetTip(editor)?.ToString().Should().Contain("Sub Foo()");
        }
        finally
        {
            window.Close(); vm.Dispose();
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            Directory.Delete(directory, true);
        }
    }

    // The editor's quick info through the window's input, twice at the same point: once with the tool's end of hover
    // between, as a person moves away and back, and once without. A new pointer per hover told the editor the last one
    // had left, which closed its tip, and the repeat was then taken for jitter and opened none (#1265 review).
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_editor_shows_quick_info_on_a_second_hover_at_the_same_point(bool endedBetween)
    {
        var (window, editor, vm) = ShowEditor();
        try
        {
            UiAutomationDriver.Hover(editor, null, null, null, out var first).Success.Should().BeTrue();
            first!.Value.ThroughWindowInput.Should().BeTrue();
            await Settle(700);
            ToolTip.GetIsOpen(editor).Should().BeTrue();
            if (endedBetween)
            {
                UiAutomationDriver.EndHover(editor);
                Dispatcher.UIThread.RunJobs();
                ToolTip.GetIsOpen(editor).Should().BeFalse();
            }

            UiAutomationDriver.Hover(editor, null, null, null, out _).Success.Should().BeTrue();
            await Settle(700);

            ToolTip.GetIsOpen(editor).Should().BeTrue();
            ToolTip.GetTip(editor)?.ToString().Should().Contain("Sub Foo()");
            hoverAsks.Should().HaveCount(endedBetween ? 2 : 1);
        }
        finally { window.Close(); vm.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task Diagnostic_hover_combines_overlaps_sources_and_optional_quick_info(bool autoQuickInfo, bool hasQuickInfo)
    {
        var (window, editor, vm) = ShowEditor();
        vm.Settings.AutoQuickInfo.Returns(autoQuickInfo);
        lsp.RequestHoverAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<HoverResult?>(hasQuickInfo
                ? new HoverResult(new MarkupContent("plaintext", "Quick info")) : null));
        var line = editor.Document.GetLocation(editor.CaretOffset).Line - 1;
        try
        {
            lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(lsp,
                new PublishDiagnosticsParams(vm.GetDocumentUriPublic(), [
                    new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(line, 4), new Position(line, 7)),
                        "First message", DiagnosticSeverity.Error, Source: "Server"),
                    new Diagnostic(new HexIDE.Lsp.Messages.Range(new Position(line, 5), new Position(line, 5)),
                        "Second message", DiagnosticSeverity.Warning)]));
            Dispatcher.UIThread.RunJobs();
            UiAutomationDriver.Hover(editor, null, null, null, out _).Success.Should().BeTrue();
            await Settle(700);
            ToolTip.GetIsOpen(editor).Should().BeTrue();
            var tip = ToolTip.GetTip(editor)?.ToString();
            tip.Should().Contain("Server: First message").And.Contain("Second message");
            if (autoQuickInfo && hasQuickInfo) tip.Should().Contain("Quick info");
            else tip.Should().NotContain("Quick info");

            editor.CaretOffset = editor.Document.GetLineByNumber(line + 1).Offset + 7;
            UiAutomationDriver.Hover(editor, null, null, null, out _).Success.Should().BeTrue();
            await Settle(700);
            var outsideTip = ToolTip.GetTip(editor)?.ToString() ?? "";
            outsideTip.Should().NotContain("First message");
            outsideTip.Should().NotContain("Second message");

            UiAutomationDriver.EndHover(editor);
            Dispatcher.UIThread.RunJobs();
            ToolTip.GetTip(editor).Should().BeNull();
        }
        finally { window.Close(); vm.Dispose(); }
    }
}
