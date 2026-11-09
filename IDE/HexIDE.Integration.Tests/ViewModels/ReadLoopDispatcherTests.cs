using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Controls;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Redaction;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.Diagnostics;
using HexIDE.Tools.LanguageServers;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Integration.Tests.ViewModels;

// #2530: each of these callbacks arrives on a language-server client's read loop or transport thread, and posted to
// Dispatcher.UIThread from there. Avalonia.Headless clears the UI dispatcher as each test is set up, and a thread that
// asks for it in that moment becomes the UI thread, so the setup throws and the run hangs. #3319's Linux dump caught
// LspDocumentSession doing it (#3326). Each case builds its object on the UI thread, clears the UI dispatcher as a
// setup does, and raises the callback from another thread.
public class ReadLoopDispatcherTests
{
    // Avalonia's own field. If it is ever renamed, this test fails here rather than passing without testing anything.
    private static readonly FieldInfo? UiThreadField =
        typeof(Dispatcher).GetField("s_uiThread", BindingFlags.Static | BindingFlags.NonPublic);

    [AvaloniaTheory]
    [InlineData("LanguageServersToolViewModel: connections changed")]
    [InlineData("LanguageServersToolViewModel: output")]
    [InlineData("LanguageServersToolViewModel: arming")]
    [InlineData("DiagnosticsToolViewModel: diagnostics published")]
    [InlineData("ObjectBrowserToolViewModel: connections changed")]
    [InlineData("CodeEditorViewModel: language server state")]
    [InlineData("RelatedDocumentEditorViewModel: language server state")]
    [InlineData("CodeLensDisplay: lenses changed")]
    public async Task A_read_loop_callback_does_not_make_its_thread_the_UI_thread(string site)
    {
        UiThreadField.Should().NotBeNull("the test clears Avalonia's UI dispatcher through this field");
        var ui = Dispatcher.UIThread;
        var cleanup = new List<Func<ValueTask>>();
        try
        {
            var raise = Build(site, cleanup);
            UiThreadField!.GetValue(null).Should().BeSameAs(ui);
            var (claimed, finished, thrownThere) = RaiseWithNoUiDispatcher(raise, ui);
            ui.RunJobs();

            claimed.Should().BeNull("{0} must post to the dispatcher its object was built on, not make the " +
                "thread it arrives on the UI thread (#2530){1}", site, thrownThere is null ? "" : "; it also threw there: " + thrownThere.Message);
            finished.Should().BeTrue("{0} only posts, so it has nothing to wait for", site);
            thrownThere.Should().BeNull();
        }
        finally
        {
            foreach (var step in cleanup) await step();
        }
    }

    // Nothing may await between clearing the UI dispatcher and restoring it. The headless session asks for
    // Dispatcher.UIThread when a test first yields to it, so a test that yields in this window hands the defect to the
    // session itself, and the run hangs instead of failing. That is why the raise is waited for here, for a limited time.
    private static (object? Claimed, bool Finished, Exception? Thrown) RaiseWithNoUiDispatcher(Action raise, Dispatcher ui)
    {
        Exception? thrown = null;
        // A new thread rather than the pool's, so it certainly has no dispatcher of its own to begin with.
        var worker = new Thread(() =>
        {
            try { raise(); }
            catch (Exception error) { thrown = error; } // view work run off the UI thread can throw there
        }) { IsBackground = true };
        UiThreadField!.SetValue(null, null);
        try
        {
            worker.Start();
            var finished = worker.Join(TimeSpan.FromSeconds(15));
            return (UiThreadField.GetValue(null), finished, thrown);
        }
        finally
        {
            UiThreadField.SetValue(null, ui);
        }
    }

    private static ILocalizationService Localization()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return localization;
    }

    private static Action Build(string site, List<Func<ValueTask>> cleanup)
    {
        switch (site)
        {
            case "LanguageServersToolViewModel: connections changed":
            case "LanguageServersToolViewModel: output":
            case "LanguageServersToolViewModel: arming":
            {
                var registry = Substitute.For<ILanguageConnectionRegistry>();
                registry.Connections.Returns([]);
                registry.ConfigurationProblems.Returns([]);
                var capture = new ConversationLog();
                cleanup.Add(() => capture.DisposeAsync());
                _ = new LanguageServersToolViewModel(registry, Localization(), capture, Substitute.For<IEventBus>(),
                    new Pseudonymiser());
                return site switch
                {
                    "LanguageServersToolViewModel: connections changed" =>
                        () => registry.ConnectionsChanged += Raise.Event(),
                    "LanguageServersToolViewModel: output" => () => capture.Output.Append("bundled", "stderr", "line"),
                    _ => () => capture.ArmsEveryConnection = !capture.ArmsEveryConnection,
                };
            }
            case "DiagnosticsToolViewModel: diagnostics published":
            {
                var registry = Substitute.For<ILanguageConnectionRegistry>();
                registry.Connections.Returns([]);
                registry.Diagnostics.Returns([]);
                var client = Substitute.For<ILspClient>();
                _ = new DiagnosticsToolViewModel(registry, client, Substitute.For<IDocumentDockService>(),
                    Substitute.For<IEditorService>(), Localization());
                return () => client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
                    client, new PublishDiagnosticsParams("file:///x.bas", []));
            }
            case "ObjectBrowserToolViewModel: connections changed":
            {
                var projects = Substitute.For<IProjectManager>();
                projects.LoadedProjects.Returns(new List<ProjectDefinition>());
                var registry = Substitute.For<ILanguageConnectionRegistry>();
                registry.Connections.Returns([]);
                _ = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(), Substitute.For<IEditorService>(),
                    Substitute.For<IComponentRegistry>(), Substitute.For<ITypeLibraryService>(),
                    Substitute.For<IFocusedProjectUtil>(), Localization(), registry);
                return () => registry.ConnectionsChanged += Raise.Event();
            }
            case "CodeEditorViewModel: language server state":
            {
                var project = new ProjectDefinition(VBProjectType.EXE, "P");
                var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
                module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
                project.AddModule(module);
                var client = Substitute.For<ILspClient>();
                var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
                    Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), client,
                    Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
                    Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
                    Substitute.For<IDebugController>(), Substitute.For<IRunScope>(), Localization()).Initialize(module);
                cleanup.Add(() => { editor.Dispose(); return ValueTask.CompletedTask; });
                return () => client.StateChanged += Raise.Event();
            }
            case "RelatedDocumentEditorViewModel: language server state":
            {
                var directory = Path.Combine(Path.GetTempPath(), "hexide-readloop-" + Guid.NewGuid());
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "notes.md");
                File.WriteAllText(path, "# notes\n");
                var client = Substitute.For<ILspClient>();
                var editor = new RelatedDocumentEditorViewModel(client, Substitute.For<IWindowManager>(), Localization())
                    .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "notes.md", path));
                cleanup.Add(() =>
                {
                    (editor as IDisposable)?.Dispose();
                    Directory.Delete(directory, true);
                    return ValueTask.CompletedTask;
                });
                return () => client.StateChanged += Raise.Event();
            }
            case "CodeLensDisplay: lenses changed":
            {
                var session = new CodeLensSession(Substitute.For<ILspClient>(), "file:///x.bas");
                var display = new CodeLensDisplay(new TextEditor { Document = new TextDocument("Sub Main()\r\nEnd Sub\r\n") },
                    new Canvas(), session, _ => { });
                cleanup.Add(() =>
                {
                    display.Dispose();
                    session.Dispose();
                    return ValueTask.CompletedTask;
                });
                // The session raises Changed when a server's reply lands; the handler is what that event calls.
                var onChanged = typeof(CodeLensDisplay).GetMethod("OnChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
                return () => onChanged.Invoke(display, [session, EventArgs.Empty]);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(site), site, null);
        }
    }
}
