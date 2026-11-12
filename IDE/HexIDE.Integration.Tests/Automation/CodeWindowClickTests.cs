using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Integration.Tests.Automation;

public class CodeWindowClickTests
{
    private sealed class Scene : IDisposable
    {
        public Window Window { get; }
        public TextEditor Editor { get; }
        public CodeEditorViewModel Vm { get; }
        private readonly CodeLensSession lenses;
        private readonly CodeLensDisplay display;

        public Scene()
        {
            var events = Substitute.For<IEventBus>();
            events.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
            events.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
            events.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
            events.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
            var settings = Substitute.For<ISettingsService>();
            settings.TabWidth.Returns(4);
            var lsp = Substitute.For<ILspClient>();
            lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
            Vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
                Substitute.For<IProjectService>(), events, lsp, settings, Substitute.For<IStatusBarService>(),
                new BookmarkService(), new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(),
                Substitute.For<ILocalizationService>(), new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
            var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P3827"), "Main", ModuleKind.StandardModule);
            module.UpdateCode(string.Join("\n", Enumerable.Repeat("' abcdefghijklmnop", 120)));
            Vm.Initialize(module);
            var view = new CodeEditorView { DataContext = Vm };
            Window = new Window { Content = view, Width = 800, Height = 400 };
            Window.Show();
            Window.SetRenderScaling(1.5);
            Editor = view.FindControl<TextEditor>("TextEditor")!;
            Render();
            var before = Editor.TextArea.TextView.GetVisualPosition(new TextViewPosition(5, 1), VisualYPosition.TextMiddle).Y;
            var client = Substitute.For<ILspClient>();
            client.IsRunning.Returns(true);
            client.RequestCodeLensesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([
                new CodeLens(new HexIDE.Lsp.Messages.Range(new Position(2, 0), new Position(2, 0)), new Command("0 references", "test"))]);
            lenses = new CodeLensSession(client, Vm.GetDocumentUriPublic());
            display = new CodeLensDisplay(Editor, new Canvas(), lenses, _ => { });
            Render();
            Editor.TextArea.TextView.GetVisualPosition(new TextViewPosition(5, 1), VisualYPosition.TextMiddle).Y
                .Should().BeGreaterThan(before, "the lens has shifted the click's rendered line");
        }

        public void Render()
        {
            Dispatcher.UIThread.RunJobs();
            Window.UpdateLayout();
            Editor.TextArea.TextView.EnsureVisualLines();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }

        public Control Target(string kind) => kind switch
        {
            "text" => Editor.TextArea.TextView,
            "editor" => Editor,
            "view" => (Control)Window.Content!,
            "breakpoint" => Editor.TextArea.LeftMargins.OfType<BreakpointMargin>().Single(),
            "numbers" => Editor.TextArea.LeftMargins.OfType<LineNumberMargin>().Single(),
            _ => Editor.TextArea.LeftMargins.First(m => m.GetType().Name == "FoldingMargin")
        };

        public string Point(Control target, int line, int column = 1)
        {
            var view = Editor.TextArea.TextView;
            var point = view.GetVisualPosition(new TextViewPosition(line, column), VisualYPosition.TextMiddle) - view.ScrollOffset;
            var local = view.TranslatePoint(point, target)!.Value;
            if (target is AbstractMargin) local = new Point(target.Bounds.Width / 2, local.Y);
            return FormattableString.Invariant($"{local.X},{local.Y}");
        }

        public void Dispose() { display.Dispose(); lenses.Dispose(); Window.Close(); Vm.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData("text", "click")]
    [InlineData("view", "click")]
    [InlineData("breakpoint", "click")]
    [InlineData("numbers", "click")]
    [InlineData("fold", "click")]
    [InlineData("text", "double_click")]
    [InlineData("breakpoint", "double_click")]
    [InlineData("numbers", "double_click")]
    [InlineData("fold", "double_click")]
    public async Task Pixel_click_reports_the_document_line_and_existing_marks(string kind, string action)
    {
        using var scene = new Scene();
        scene.Vm.ToggleBreakpoint(5);
        scene.Vm.ToggleBookmark(5);
        var target = scene.Target(kind);
        var result = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(target, action, scene.Point(target, 5, 3)));
        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("line 5").And.Contain("of P3827/Main").And.Contain("breakpoint").And.Contain("bookmark");
        if (kind == "text") result.Detail.Should().Contain("column 3");
        if (kind == "breakpoint" && action == "click") scene.Vm.HasBreakpoint(5).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("text")]
    [InlineData("breakpoint")]
    [InlineData("numbers")]
    [InlineData("fold")]
    public async Task Drag_reports_both_endpoints(string kind)
    {
        using var scene = new Scene();
        var target = scene.Target(kind);
        var result = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(target, "click",
            scene.Point(target, 4) + " to " + scene.Point(target, 7)));
        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("line 4").And.Contain("line 7").And.Contain("of P3827/Main");
    }

    [AvaloniaTheory]
    [InlineData("text")]
    [InlineData("editor")]
    [InlineData("view")]
    [InlineData("breakpoint")]
    [InlineData("numbers")]
    [InlineData("fold")]
    public async Task Line_target_scrolls_to_the_requested_line(string kind)
    {
        using var scene = new Scene();
        var result = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(scene.Target(kind), "click", "line:100,col:3"));
        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("line 100").And.Contain("of P3827/Main");
        if (kind is "text" or "editor" or "view") scene.Editor.TextArea.Caret.Location.Should().Be(new AvaloniaEdit.Document.TextLocation(100, 3));
        if (kind == "breakpoint") scene.Vm.HasBreakpoint(100).Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData("text", "double_click")]
    [InlineData("breakpoint", "double_click")]
    [InlineData("numbers", "double_click")]
    [InlineData("fold", "double_click")]
    [InlineData("text", "click")]
    public async Task Line_only_targets_use_the_rendered_glyph(string kind, string action)
    {
        using var scene = new Scene();
        var result = await UiAutomationDriver.FinishAsync(await UiAutomationDriver.InteractAsync(scene.Target(kind), action, "line:5"));
        result.Success.Should().BeTrue(result.Error);
        result.Detail.Should().Contain("line 5").And.Contain("of P3827/Main");
    }

    [AvaloniaTheory]
    [InlineData("line:9999", "121 lines")]
    [InlineData("line:0", "121 lines")]
    [InlineData("line:5,col:999", "column")]
    public async Task Invalid_positions_are_refused_before_pressing(string value, string reason)
    {
        using var scene = new Scene();
        var result = await UiAutomationDriver.InteractAsync(scene.Editor, "click", value);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain(reason);
    }
}
