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
    [AvaloniaFact]
    public async Task A_move_onto_a_control_drawn_over_the_target_lands_on_that_control_and_says_so()
    {
        var button = TippedButton("Under", "Under tip");
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
    public void A_handler_that_throws_fails_the_hover_once()
    {
        var button = new Button { Content = "Add", Width = 120, Height = 40 };
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
        }
        finally { window.Close(); }
    }

    private readonly ILspClient lsp = Substitute.For<ILspClient>();
    private readonly List<Position> hoverAsks = [];

    private (Window Window, TextEditor Editor, CodeEditorViewModel Vm) ShowEditor()
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
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
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
}
