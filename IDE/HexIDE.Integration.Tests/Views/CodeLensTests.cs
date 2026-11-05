using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Location = HexIDE.Lsp.Messages.Location;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class CodeLensTests
{
    [AvaloniaTheory]
    [InlineData("drawing")]
    [InlineData("text")]
    [InlineData("viewport")]
    [InlineData("click")]
    [InlineData("click-editor")]
    [InlineData("click-server")]
    [InlineData("keyboard")]
    [InlineData("keyboard-multiple")]
    [InlineData("toggle")]
    [InlineData("off")]
    [InlineData("background-selection")]
    [InlineData("background-current-line")]
    public void LensInCodeWindow(string check)
    {
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        var localization = new LocalizationService();
        localization.Apply("en");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var editors = Substitute.For<IEditorService>();
        var results = new FindReferencesToolViewModel(client, editors, projects, documents, localization);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        typeof(ISettingsService).GetProperty("ShowCodeLenses")?.SetValue(settings, check != "off");
        var events = new EventBus();
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), events, client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(check == "viewport"
            ? string.Join("\r\n", Enumerable.Range(0, 50).Select(i => $"Sub Member{i}()\r\nEnd Sub")) + "\r\n"
            : "Sub Greet()\r\nEnd Sub\r\n");
        vm.Initialize(module);
        var line = FormCodeText.LineBreaks(FormCodeText.Prefix(module));
        var location = new Location(vm.GetDocumentUriPublic(), new HexIDE.Lsp.Messages.Range(new Position(line, 4), new Position(line, 9)));
        var args = System.Text.Json.JsonSerializer.SerializeToElement(new object[] { location.Uri, location.Range.Start, new[] { location } });
        var command = new Command("1 reference", check == "click-server" ? "server.run"
            : check == "click-editor" ? "editor.action.showReferences" : "rdcore.showReferences", args.EnumerateArray().ToArray());
        client.RequestCodeLensesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(check == "viewport"
            ? Enumerable.Range(0, 50).Select(i => new CodeLens(new HexIDE.Lsp.Messages.Range(
                new Position(line + i * 2, 4), new Position(line + i * 2, 9)))).ToArray()
            : [new CodeLens(location.Range, command)]);
        client.ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<CodeLens>() with { Command = command });
        if (check == "keyboard-multiple")
            client.RequestCodeLensesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns([new CodeLens(location.Range, command), new CodeLens(location.Range, command with { Title = "other" })]);
        var originalText = vm.Document.Text;
        var originalLineCount = vm.Document.LineCount;
        var view = new CodeEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = check == "viewport" ? 240 : 600 };
        // The production subscription uses this same event to reuse the singleton pane.
        using var subscription = events.Subscribe<ShowReferenceLocationsEvent>(e => results.ShowLocations(e.Locations));
        window.SetRenderScaling(1.5);
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var editor = view.FindControl<AvaloniaEdit.TextEditor>("TextEditor")!;
            if (check == "off")
            {
                client.DidNotReceive().RequestCodeLensesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
                view.GetVisualDescendants().OfType<Button>().Should().NotContain(b => Equals(b.Content, "1 reference"));
                return;
            }
            if (check == "viewport")
            {
                int[] ResolvedLines() => client.ReceivedCalls().Where(c => c.GetMethodInfo().Name == nameof(ILspClient.ResolveCodeLensAsync))
                    .Select(c => ((CodeLens)c.GetArguments()[0]!).Range.Start.Line).ToArray();
                var initial = ResolvedLines();
                initial.Length.Should().BeInRange(1, 15, "only the short viewport, not all fifty members, is resolved");
                editor.ScrollToLine(editor.Document.LineCount);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                ResolvedLines().Should().Contain(l => l >= line + 80, "scrolling resolves members near the end");
                return;
            }
            var label = view.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => Equals(b.Content, "1 reference"));
            label.Should().NotBeNull();
            label!.Bounds.Width.Should().BeGreaterThan(0);
            label.Bounds.Height.Should().BeGreaterThan(0);
            if (check == "toggle")
            {
                var property = typeof(ISettingsService).GetProperty("ShowCodeLenses");
                property.Should().NotBeNull();
                property!.SetValue(settings, false);
                settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(settings,
                    new PropertyChangedEventArgs("ShowCodeLenses"));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                view.GetVisualDescendants().OfType<Button>().Should().NotContain(b => Equals(b.Content, "1 reference"));
                var requests = client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestCodeLensesAsync));
                client.CodeLensRefreshRequested += Raise.Event<EventHandler>(client, EventArgs.Empty);
                vm.Document.Insert(vm.Document.TextLength, "' edited\r\n");
                vm.FlushDocumentAsync().GetAwaiter().GetResult();
                Dispatcher.UIThread.RunJobs();
                client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestCodeLensesAsync)).Should().Be(requests);
                property.SetValue(settings, true);
                settings.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(settings,
                    new PropertyChangedEventArgs("ShowCodeLenses"));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                view.GetVisualDescendants().OfType<Button>().Should().Contain(b => Equals(b.Content, "1 reference"));
                client.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILspClient.RequestCodeLensesAsync)).Should().Be(requests + 1);
            }
            else if (check.StartsWith("keyboard", StringComparison.Ordinal))
            {
                var field = typeof(ApplicationCommands).GetField("RunCodeLensCommand");
                field.Should().NotBeNull();
                var routed = (Avalonia.Labs.Input.RoutedCommand)field!.GetValue(null)!;
                routed.Gestures.Should().BeEmpty();
                editor.TextArea.Caret.Line = line + 1;
                routed.CanExecute(null, editor.TextArea).Should().BeTrue();
                routed.Execute(null, editor.TextArea);
                Dispatcher.UIThread.RunJobs();
                if (check == "keyboard-multiple")
                {
                    var menu = editor.ContextMenu!;
                    menu.IsOpen.Should().BeTrue();
                    var item = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "1 reference"));
                    menu.Items.Should().HaveCount(2);
                    var popup = TopLevel.GetTopLevel(item)!;
                    popup.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    popup.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    popup.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                }
                results.Rows.Should().Contain(r => r.Location == location);
                editor.TextArea.Caret.Line = line + 2;
                routed.CanExecute(null, editor.TextArea).Should().BeFalse();
                client.DidNotReceive().FindReferencesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
            }
            else if (check.StartsWith("background-", StringComparison.Ordinal))
            {
                editor.Background = Avalonia.Media.Brushes.White;
                editor.TextArea.SelectionBrush = Avalonia.Media.Brushes.Red;
                editor.TextArea.SelectionCornerRadius = 0;
                editor.TextArea.TextView.CurrentLineBackground = Avalonia.Media.Brushes.Blue;
                editor.TextArea.TextView.CurrentLineBorder = null;
                editor.Options.HighlightCurrentLine = true;
                editor.TextArea.Caret.Line = line + 1;
                if (check == "background-selection") editor.SelectAll();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                // Sample well clear of the lens text, in the row that highlights used to cover.
                var point = label.TranslatePoint(new Point(2, label.Bounds.Height - 2), window)!.Value;
                using var buffer = frame!.Lock();
                var offset = (int)(point.Y * window.RenderScaling) * buffer.RowBytes + (int)(point.X * window.RenderScaling) * 4;
                for (var channel = 0; channel < 3; channel++)
                    System.Runtime.InteropServices.Marshal.ReadByte(buffer.Address, offset + channel).Should().Be(255);
            }
            else if (check == "drawing")
            {
                var textView = editor.TextArea.TextView;
                var visualLine = textView.GetVisualLine(line + 1)!;
                var bottom = label!.TranslatePoint(new Point(0, label.Bounds.Height), textView)!.Value.Y;
                var memberTop = visualLine.GetVisualPosition(1, AvaloniaEdit.Rendering.VisualYPosition.TextTop).Y - textView.VerticalOffset;
                bottom.Should().BeLessThanOrEqualTo(memberTop + 1 / window.RenderScaling,
                    "the entire lens row must fit above the member, allowing one pixel for layout rounding");
                label.FontSize.Should().BeLessThan(editor.FontSize);
            }
            else if (check == "text")
            {
                editor.SelectAll();
                editor.SelectedText.Should().Be(originalText);
                editor.Document.Text.Should().Be(originalText);
                editor.Document.LineCount.Should().Be(originalLineCount);
                editor.Document.Text.Should().NotContain("1 reference");
                editor.CaretOffset = editor.Document.GetLineByNumber(line + 1).Offset;
                editor.TextArea.Caret.Line.Should().Be(line + 1);
            }
            else
            {
                var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                if (check == "click-server")
                    client.Received(1).ExecuteCommandAsync("server.run", Arg.Any<System.Text.Json.JsonElement[]>(), Arg.Any<CancellationToken>());
                else
                    results.Rows.Should().Contain(r => r.Location == location);
                client.DidNotReceive().FindReferencesAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>());
            }
        }
        finally { window.Close(); }
    }
}
