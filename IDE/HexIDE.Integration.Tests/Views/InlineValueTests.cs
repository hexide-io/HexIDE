using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using HexIDE.Bookmarks;
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
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class InlineValueTests
{
    [AvaloniaFact]
    public async Task InlineValuesDrawAfterTextAndScrollThroughTheRealEditorWithoutChangingItsDocument()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        settings.ShowInlineValues.Returns(true);
        var client = Substitute.For<ILspClient>();
        var debug = Substitute.For<IDebugController>();
        var localization = new LocalizationService();
        localization.Apply("en");
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), debug,
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode(string.Join("\n", Enumerable.Repeat("    x = 1", 100)));
        vm.Initialize(module);
        var original = vm.Document.Text;
        var requested = new List<HexIDE.Lsp.Messages.Range>();
        client.RequestInlineValuesAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(),
            Arg.Any<InlineValueContext>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                var visible = call.Arg<HexIDE.Lsp.Messages.Range>();
                requested.Add(visible);
                return Task.FromResult<InlineValue[]>([
                    new InlineValueText(new(new(visible.Start.Line, 4), new(visible.Start.Line, 5)), "x = 7"),
                    new InlineValueText(new(new(visible.Start.Line, 4), new(visible.Start.Line, 5)), "other")]);
            });
        var view = new CodeEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 350 };
        window.SetRenderScaling(1.5);
        window.Show();
        try
        {
            var editor = view.GetVisualDescendants().OfType<TextEditor>().First();
            editor.TextArea.TextView.EnsureVisualLines();
            var stop = new StoppedInfo(StopReason.Step, "Module1", 1);
            debug.State.Returns(DebugState.Paused);
            debug.CurrentStop.Returns(stop);
            debug.Stopped += Raise.Event<Action<StoppedInfo>>(stop);
            await Until(() => Elements(editor).Length > 0);
            var element = Elements(editor).Should().ContainSingle().Subject;
            var line = editor.TextArea.TextView.VisualLines.First(l => l.Elements.Contains(element));
            line.GetRelativeOffset(element.VisualColumn).Should().Be(line.LastDocumentLine.Length);
            ((StackPanel)element.Element).Children.Cast<TextBlock>().Single().Text.Should().Be("x = 7, other");
            editor.TextArea.Focus().Should().BeTrue();
            editor.CaretOffset = line.LastDocumentLine.EndOffset - 1;
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(line.LastDocumentLine.EndOffset);
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
            editor.CaretOffset.Should().Be(line.LastDocumentLine.NextLine!.Offset);
            var beforeScroll = requested.Count;
            editor.ScrollToLine(65);
            await Until(() => requested.Count > beforeScroll && requested.Last().Start.Line > 0);
            editor.SelectAll();
            editor.SelectedText.Should().Be(original);
            editor.Copy();
            ((await window.Clipboard!.TryGetTextAsync())?.ReplaceLineEndings()).Should().Be(original.ReplaceLineEndings());
            vm.Document.Text.Should().Be(original);
            debug.State.Returns(DebugState.Stopped);
            debug.CurrentStop.Returns((StoppedInfo?)null);
            debug.Continued += Raise.Event<Action>();
            await Until(() => Elements(editor).Length == 0);
            var stoppedRequests = requested.Count;
            window.Close();
            debug.State.Returns(DebugState.Paused);
            debug.CurrentStop.Returns(stop);
            debug.Stopped += Raise.Event<Action<StoppedInfo>>(stop);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            requested.Should().HaveCount(stoppedRequests, "detached views unsubscribe");
        }
        finally { window.Close(); }
    }

    private static InlineObjectElement[] Elements(TextEditor editor)
    {
        editor.TextArea.TextView.EnsureVisualLines();
        return editor.TextArea.TextView.VisualLines.SelectMany(l => l.Elements).OfType<InlineObjectElement>().ToArray();
    }

    private static async Task Until(Func<bool> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        done().Should().BeTrue();
    }
}
