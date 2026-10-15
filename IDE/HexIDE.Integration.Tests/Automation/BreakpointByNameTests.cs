using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
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
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The hexide-mcp-server delta's scenario <i>Setting a breakpoint by name in the wrong case</i>
/// (hexide-io/HexIDE#273): a client sets a breakpoint in <c>form1</c> of a project containing <c>Form1</c>, and it
/// is shown in <c>Form1</c>'s gutter, pushed to the next run, and saved with the project.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is reduced to, and why that is the tool.</b> <c>set_breakpoints</c> is compiled into an executable
/// nothing references, so this cannot call it. Its body is three steps, and this makes the same three: the name
/// resolves through <see cref="NamedDocuments.Resolve"/>, the lines are checked by <see cref="MarkLineRules.Refusal"/>
/// in the open window's text, and the store is given the identity the name resolved to. Every outcome the scenario
/// names is then read from the real thing that produces it: the gutter is the code window's own margin, rendered;
/// the run is <see cref="ProjectRunnerService"/> with a real <see cref="DebugController"/>, stopping on the line;
/// and the save is the sidecar's own debounced write, read back from disk.
/// </para>
/// <para>
/// <b>The defect it stands against</b> is #467. The tool used to mint the store's key from the caller's spelling, so
/// <c>form1</c> was a second entry beside <c>Form1</c>'s, which the gutter, the runner and the sidecar all read
/// past, while the reply said success. Each assertion below fails if the key is anything but Form1's identity.
/// </para>
/// </remarks>
public sealed class BreakpointByNameTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "HexIDE.Integration.Tests.BreakpointByName",
        Guid.NewGuid().ToString("N"));
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Lines 1-7 are the header; line 9 is Form_Load's first statement. Counted from the top of the file, as the
    // gutter and set_breakpoints count.
    private const string Frm =
        "VERSION 5.00\r\n"                              // 1
      + "Begin VB.Form Form1 \r\n"                      // 2
      + "   Caption         =   \"Form1\"\r\n"          // 3
      + "   ClientHeight    =   3000\r\n"               // 4
      + "   ClientWidth     =   4000\r\n"               // 5
      + "End\r\n"                                       // 6
      + "Attribute VB_Name = \"Form1\"\r\n"             // 7
      + "Private Sub Form_Load()\r\n"                   // 8
      + "    Debug.Print 1\r\n"                         // 9
      + "End Sub\r\n";                                  // 10

    private const int Line = 9;

    private CodeEditorViewModel OpenCodeWindow(FormDefinition form, BreakpointService breakpoints, BookmarkService bookmarks)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), bookmarks, breakpoints,
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm.Initialize(form);
    }

    private TextEditor Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.TextView.EnsureVisualLines();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    /// <summary>The document lines the breakpoint gutter draws a dot beside, read off what it renders.</summary>
    private static List<int> LinesWithADot(TextEditor editor)
    {
        var margin = editor.TextArea.LeftMargins.OfType<BreakpointMargin>().Single();
        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();

        var drawing = new DrawingGroup();
        using (var context = drawing.Open())
            margin.Render(context);

        // The margin draws nothing but its dots. What a drawing context records for an ellipse is the platform's
        // own geometry rather than an EllipseGeometry, so a dot is read by where its bounds are centred.
        return drawing.Children.OfType<GeometryDrawing>()
            .Select(d => d.Geometry!.Bounds.Center.Y)
            .Select(y => textView.GetVisualLineFromVisualTop(y + textView.VerticalOffset)!.FirstDocumentLine.LineNumber)
            .ToList();
    }

    [AvaloniaFact]
    public async Task SettingABreakpointByNameInTheWrongCase()
    {
        Directory.CreateDirectory(dir);
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = Path.Combine(dir, "P.vbp") };
        var form = new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!;
        project.AddForm(form);
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(new List<ProjectDefinition> { project });

        var breakpoints = new BreakpointService();
        var bookmarks = new BookmarkService();
        // Written as the IDE writes it, on the change, with the delay taken out so the test need not wait it.
        var sidecar = new UserSidecarService(bookmarks, breakpoints, projectManager) { Debounce = _ => Task.CompletedTask };

        var window = OpenCodeWindow(form, breakpoints, bookmarks);
        var editor = Show(window);
        LinesWithADot(editor).Should().BeEmpty("the control: nothing is drawn before the breakpoint is set");

        // ---- set_breakpoints {"name":"form1","lines":[9]}, step by step as the tool takes it ----
        var named = NamedDocuments.Resolve(projectManager.LoadedProjects, "form1", project: null);
        named.Error.Should().BeNull();
        named.Name.Should().Be("Form1", "the reply's 'document' is the IDE's spelling, not the caller's");
        named.Project.Should().Be("P");
        var document = named.Document!;
        MarkLineRules.Refusal(window.CodeWindowText, document.Display, [Line], breakpoints.GetBreakpoints(document),
            first: 1, "breakpoint").Should().BeNull("line 9 is a line of code");
        breakpoints.SetDocument(document, [Line]);
        Dispatcher.UIThread.RunJobs();

        // ---- shown in Form1's gutter ----
        LinesWithADot(editor).Should().Equal([Line],
            "Form1's gutter draws what the store holds under Form1's identity, and nothing under any other key");

        // ---- pushed to the next run ----
        var debugger = new DebugController();
        var runner = new ProjectRunnerService(
            Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), projectManager,
            Substitute.For<ILocalizationService>(), debugger, breakpoints, new WatchService(), new RunScope());
        var stopped = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        debugger.Stopped += info => stopped.TrySetResult(info);
        try
        {
            runner.RunProject(project);
            var winner = await Task.WhenAny(stopped.Task, Task.Delay(5000, TestContext.Current.CancellationToken));
            winner.Should().BeSameAs(stopped.Task, "the run is given Form1's breakpoints, so it stops on line 9");
            var at = await stopped.Task;
            at.Module.Should().Be("Form1");
            at.Line.Should().Be(Line);
            at.Reason.Should().Be(StopReason.Breakpoint);
        }
        finally
        {
            runner.EndProject();
            Dispatcher.UIThread.RunJobs();
        }

        // ---- saved with the project ----
        await sidecar.SettledAsync();
        var path = Path.Combine(dir, "P.user.hexproj");
        File.Exists(path).Should().BeTrue("setting the breakpoint is a change the project's sidecar records");

        // Every document name the file records, under whichever key holds a kind's marks: the format moves to new
        // keys when the sidecar is migrated (#273 task 3.17), and what is asserted here is the name, not the key.
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        var names = json.RootElement.EnumerateObject()
            .Where(kind => kind.Value.ValueKind == JsonValueKind.Object)
            .SelectMany(kind => kind.Value.EnumerateObject().Select(entry => entry.Name))
            .ToList();
        names.Should().Equal(["Form1"], "the sidecar names the document as the IDE does, and never as the caller spelled it");

        var reloaded = new BreakpointService();
        await new UserSidecarService(new BookmarkService(), reloaded, projectManager).LoadAsync(project);
        reloaded.GetBreakpoints(DocumentIdentity.For(form)).Should().Equal([Line], "the next session finds it on Form1");
    }
}
