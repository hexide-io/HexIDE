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

    [AvaloniaTheory]
    [InlineData("context")]
    [InlineData("lookup")]
    [InlineData("expression")]
    [InlineData("lifetime")]
    [InlineData("other-document")]
    [InlineData("refresh")]
    [InlineData("stale")]
    public async Task ExternalInlineValuesUseTheSelectedStoppedFrame(string check)
    {
        var token = TestContext.Current.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "hexide-inline-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        // The fixture adapter names its frames' sources from its working directory, which macOS reports with /var's
        // link resolved (/private/var). The code window matches a frame to its document by exact path, as its
        // stopped-line marker does, so the module's path has to be the one the adapter will report.
        directory = ResolvePath(directory);
        await using var log = new HexIDE.Conversations.ConversationLog(armEveryConnection: true);
        var external = new HexIDE.Debugging.ExternalDebugService(log);
        var settings = Substitute.For<ISettingsService>();
        settings.ShowInlineValues.Returns(true);
        var client = Substitute.For<ILspClient>();
        using var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), client, settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), new LocalizationService(), external);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Caller", ModuleKind.StandardModule)
        { AbsolutePath = Path.Combine(directory, "Caller.bas") };
        module.UpdateCode(string.Join("\n", Enumerable.Repeat("callerValue", 20)));
        vm.Initialize(module);
        var contexts = new List<InlineValueContext>();
        var pending = new TaskCompletionSource<InlineValue[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var range = new HexIDE.Lsp.Messages.Range(new(11, 0), new(11, 11));
        client.RequestInlineValuesAsync(Arg.Any<string>(), Arg.Any<HexIDE.Lsp.Messages.Range>(),
            Arg.Any<InlineValueContext>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                contexts.Add(call.Arg<InlineValueContext>());
                if (check == "stale") return pending.Task;
                if (check == "refresh") return Task.FromResult<InlineValue[]>([
                    new InlineValueVariableLookup(range, false) { VariableName = "callerValue" }]);
                return Task.FromResult<InlineValue[]>([
                    new InlineValueText(range, "literal"),
                    new InlineValueVariableLookup(range, false) { VariableName = "CALLERVALUE" },
                    new InlineValueVariableLookup(range, true) { VariableName = "CallerValue" },
                    new InlineValueEvaluatableExpression(range) { Expression = "value + 1" },
                    new InlineValueEvaluatableExpression(range) { Expression = "reject" }]);
            });
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""),
            System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory());
        var config = Path.Combine(directory, "launch.json");
        await File.WriteAllTextAsync(config, System.Text.Json.JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(HexIDE.Dap.TestAdapter.Program).Assembly.Location, "evaluate-running" },
            adapterId = "fixture", launch = new { }
        }), token);
        InlayHint[] shown = [];
        using var pane = vm.CreateInlineValueSession(values => shown = values);
        pane.SetVisibleRange(new(new(0, 0), new(19, 11)));
        var run = external.StartAsync(config);
        try
        {
            await Until(() => external.CanEvaluate && external.Variables.Count > 0);
            if (check == "other-document")
            {
                (await vm.RequestInlineValuesAsync(range, token)).Should().BeEmpty();
                contexts.Should().BeEmpty();
                return;
            }
            await external.SelectFrameAsync(external.Stop!, 7);
            if (check == "context")
            {
                await Until(() => contexts.Count > 0);
                contexts.Last().Should().Be(new InlineValueContext(7, new(new(11, 0), new(11, 11))));
            }
            else if (check == "lookup")
            {
                await Until(() => shown.Any(h => h.Label.GetString()!.Contains("CALLERVALUE = 42")));
                shown.Single().Label.GetString().Should().Be("literal, CALLERVALUE = 42, value + 1 = frame:7:value + 1");
            }
            else if (check == "expression")
            {
                var hints = await vm.RequestInlineValuesAsync(range, token);
                hints.Should().ContainSingle();
                hints.Single().Label.GetString().Should().Contain("value + 1 = frame:7:value + 1").And.NotContain("reject");
                await log.DrainAsync();
                var evaluations = log.Snapshot().Where(e => e.Method == "evaluate").ToArray();
                evaluations.Should().NotBeEmpty();
                foreach (var entry in evaluations)
                {
                    using var body = System.Text.Json.JsonDocument.Parse(log.Body(entry.ConnectionId, entry.Sequence)!.Head);
                    body.RootElement.GetProperty("arguments").GetProperty("context").GetString().Should().Be("watch");
                }
            }
            else if (check == "refresh")
            {
                await Until(() => shown.Any(h => h.Label.GetString() == "callerValue = 42"));
                var count = contexts.Count;
                external.ToggleBreakpoint(vm.GetDocumentUriPublic()!, 1).Should().BeTrue();
                await Until(() => external.GetBreakpoints(vm.GetDocumentUriPublic()!).Any(row => !row.Pending));
                Dispatcher.UIThread.RunJobs();
                contexts.Should().HaveCount(count, "breakpoint changes do not change the inspection key");
                var stop = external.Stop;
                await external.EvaluateAsync("callerValue = 99");
                external.Stop.Should().Be(stop);
                external.SelectedFrame!.Value.GetProperty("id").GetInt32().Should().Be(7);
                await Until(() => shown.Any(h => h.Label.GetString() == "callerValue = 99"));
            }
            else if (check == "stale")
            {
                await Until(() => contexts.Count > 0);
                var late = vm.RequestInlineValuesAsync(range, token);
                await external.CommandAsync("continue");
                await Until(() => external.Stop is null);
                pending.SetResult([new InlineValueText(range, "obsolete")]);
                (await late).Should().BeEmpty();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                shown.Should().BeEmpty();
            }
            else
            {
                await Until(() => shown.Length > 0);
                await external.CommandAsync("continue");
                await Until(() => shown.Length == 0);
                (await vm.RequestInlineValuesAsync(range, token)).Should().BeEmpty();
            }
        }
        finally
        {
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), token);
            Directory.Delete(directory, true);
        }
    }

    // Resolves links in every directory of the path, parents first; the temp folder itself is no link on macOS, /var is.
    private static string ResolvePath(string path)
    {
        var full = Path.GetFullPath(path);
        var parent = Path.GetDirectoryName(full);
        if (parent is null) return full;
        var resolved = Path.Combine(ResolvePath(parent), Path.GetFileName(full));
        FileSystemInfo info = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
        var target = info.Exists ? info.ResolveLinkTarget(true) : null;
        return target is null ? resolved : ResolvePath(target.FullName);
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
