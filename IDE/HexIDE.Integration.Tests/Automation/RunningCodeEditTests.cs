using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using System.Reflection;
using HexIDE.Automation;
using AvaloniaEdit;
using HexIDE.Debugging;
using HexIDE.Desktop.Server;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Projects;

namespace HexIDE.Integration.Tests.Automation;

public class RunningCodeEditTests : IAsyncDisposable
{
    private RealComposition? composition;
    private DISetup Composition => (composition ??= new RealComposition()).Composition;

    public async ValueTask DisposeAsync()
    {
        if (composition is not null) await composition.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    // The document action without desktop-window lookup (headless has no desktop lifetime). Before #3191
    // there was no wrapper: exercise the original action for the teeth run too.
    private static InteractOutcome TypeDocument(HexIdeTools tools, Control control, string text)
    {
        var action = typeof(HexIdeTools).GetMethod("TypeDocumentText", BindingFlags.Instance | BindingFlags.NonPublic);
        return action is null ? UiAutomationDriver.TypeText(control, text, "Window/Editor")
            : (InteractOutcome)action.Invoke(tools, [control, text, "Window/Editor"])!;
    }

    [AvaloniaTheory]
    [InlineData(false, "set_file_content")]
    [InlineData(true, "set_file_content")]
    [InlineData(false, "set_form_content")]
    [InlineData(true, "set_form_content")]
    [InlineData(false, "type_text")]
    [InlineData(true, "type_text")]
    [InlineData(false, "programmatic")]
    [InlineData(true, "programmatic")]
    public async Task Edits_report_the_snapshot_and_track_documents_until_stop(bool paused, string mode)
    {
        void Phase(string phase) => TestContext.Current.TestOutputHelper!.WriteLine("3191: " + phase);
        Phase("create project");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        var caption = "3191-" + Guid.NewGuid();
        form.Components[0].SetProperty(VBProperties.CaptionProperty, caption);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDebug.Print 0\r\nEnd Sub\r\n"
            + "Private Sub Form_Click()\r\nDebug.Print Version()\r\nEnd Sub\r\n");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Public Function Version() As Integer\r\nVersion = 1\r\nEnd Function\r\n");
        project.AddModule(module);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var save = Substitute.For<IProjectService>();
        var directory = Path.Combine(Path.GetTempPath(), "hexide-run-edit-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        project.AbsolutePath = Path.Combine(directory, "P.vbp");
        module.AbsolutePath = Path.Combine(directory, "Module1.bas");
        form.AbsolutePath = Path.Combine(directory, "Form1.frm");
        var controller = new DebugController();
        var marks = new BreakpointService();
        var scope = new RunScope();
        var bus = new EventBus();
        Phase("create runner");
        var runner = TestRunners.WithInterpreter(bus, Substitute.For<IWindowManager>(), projects,
            Composition.LocalizationService, controller, marks, new WatchService(), scope);
        var dock = Substitute.For<IDocumentDockService>();
        Phase("create context and root");
        using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
            null!, runner, save, null!, null!, marks, controller, Composition.Root, null!, null!, null!, null!,
            null!, null!, Composition.LocalizationService, null!, new Vb6ProjectFileFormat());
        var tools = new HexIdeTools(context);
        save.SaveForm(form, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>()).Returns(true);
        save.SaveModule(module, false, interactive: false, rewrites: Arg.Any<HexIDE.Projects.SaveRewriteReport>()).Returns(true);
        Phase("create editor");
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            save, bus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<HexIDE.Bookmarks.IBookmarkService>(), marks, controller, scope,
            Composition.LocalizationService).Initialize(module);
        var text = new TextEditor { Document = editor.Document, DataContext = editor };
        var window = new Window { Content = text, Width = 400, Height = 300 };
        var shown = new TaskCompletionSource<VBFormRuntime>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var opened = Window.WindowOpenedEvent.AddClassHandler<VBFormRuntime>((w, _) =>
        {
            if (w.Title == caption) shown.TrySetResult(w);
        });
        var output = new StringBuilder();
        void Printed(string value) => output.Append(value);
        VBDebugConsole.Written += Printed;
        try
        {
            Phase("show editor window");
            window.Show();
            // Design mode keeps the existing reply, with no running-snapshot warning.
            Phase("design write");
            var design = await tools.SetFileContentAsync("Module1", module.Code);
            design.Success.Should().BeTrue();
            design.Note.Should().NotContain("code it started with");
            text.CaretOffset = editor.CaretOffset;
            var designTyping = TypeDocument(tools, text, "");
            designTyping.Success.Should().BeTrue(designTyping.Error);
            designTyping.Detail.Should().Be("typed 0 char(s) into TextEditor");
            if (mode is "type_text" or "programmatic") dock.OpenDocuments.Returns([editor]);
            Phase("start run");
            runner.RunProject(project, stepInto: paused);
            Phase("run started");
            await Task.Yield();
            runner.IsRunning.Should().BeTrue();
            if (paused) controller.State.Should().Be(DebugState.Paused);
            JsonSerializer.Serialize(await tools.GetDebugStateAsync(TestContext.Current.CancellationToken))
                .Should().NotContain("changed since");
            Phase("edit while running");
            string? note;
            if (mode is "set_file_content" or "set_form_content")
            {
                var reply = mode == "set_form_content"
                    ? await tools.SetFileContentAsync("Form1", form.Code.Replace("Debug.Print 0", "Debug.Print 2"))
                    : await tools.SetFileContentAsync("Module1", module.Code.Replace("Version = 1", "Version = 2"));
                reply.Success.Should().BeTrue();
                note = reply.Note;
            }
            else if (mode == "type_text")
            {
                text.CaretOffset = editor.Document.TextLength;
                var reply = TypeDocument(tools, text, "' next run\r\n");
                reply.Success.Should().BeTrue(reply.Error);
                note = reply.Detail;
                // The detail has no full stop of its own, so the note must not run on from it (live check, #3196).
                note.Should().MatchRegex(@"into TextEditor\. The project is (running|paused)\.");
            }
            else
            {
                editor.Document.Insert(editor.Document.TextLength, "' other programmatic edit\r\n");
                note = null;
            }
            runner.IsRunning.Should().BeTrue();
            if (mode != "programmatic")
            {
                // Running as well as paused: a breakpoint set on the new text's line never fired while running (#3191).
                note.Should().Contain("code it started with").And.Contain("next run")
                    .And.Contain("running code's line numbers");
                if (paused) note.Should().Contain("break location");
                else note.Should().NotContain("break location");
            }
            Phase("edit replied");
            var stateResult = await tools.GetDebugStateAsync(TestContext.Current.CancellationToken);
            // Standing alone, the note starts at its first word, not with the space it carries when appended (#3196).
            stateResult.Note.Should().StartWith("The project is");
            var state = JsonSerializer.Serialize(stateResult);
            var changed = mode == "set_form_content" ? "P.Form1" : "P.Module1";
            state.Should().Contain(changed).And.Contain("changed since");
            var breakpoint = await tools.SetBreakpointsAsync("Module1", []);
            breakpoint.Note.Should().Contain(changed).And.Contain("changed since");
            var search = new TextBox { DataContext = editor };
            window.Content = search;
            var incidental = TypeDocument(tools, search, "search query");
            incidental.Success.Should().BeTrue(incidental.Error);
            incidental.Detail.Should().NotContain("code it started with");
            Phase("continue and await window");
            if (paused) controller.Continue();
            var runningWindow = await shown.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Phase("call old code");
            output.Clear();
            await runningWindow.Context.ExecuteControlEventAsync("Form_Click", [])
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            output.ToString().Trim().Should().Be("1", "the running module keeps its starting code (#3191)");
            Phase("stop");
            await tools.StopProjectAsync(TestContext.Current.CancellationToken);
            state = JsonSerializer.Serialize(await tools.GetDebugStateAsync(TestContext.Current.CancellationToken));
            state.Should().NotContain("changed since");
        }
        finally
        {
            Phase("cleanup");
            VBDebugConsole.Written -= Printed;
            runner.EndProject();
            await Task.Yield();
            window.Close();
            Directory.Delete(directory, true);
            Phase("cleanup complete");
        }
    }
}
