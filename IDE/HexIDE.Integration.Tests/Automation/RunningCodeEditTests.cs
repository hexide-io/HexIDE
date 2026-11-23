using HexIDE.Runtime.Serialization;
using System.Text;
using System.Windows.Input;
using System.Text.Json;
using Avalonia.Labs.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    [AvaloniaTheory]
    [InlineData("immediate", false, false)]
    [InlineData("locals", false, false)]
    [InlineData("watch", false, false)]
    [InlineData("project", false, false)]
    [InlineData("browser", false, false)]
    [InlineData("palette", false, false)]
    [InlineData("stack", false, false)]
    [InlineData("immediate", true, false)]
    [InlineData("locals", true, false)]
    [InlineData("watch", true, false)]
    [InlineData("project", true, false)]
    [InlineData("browser", true, false)]
    [InlineData("palette", true, false)]
    [InlineData("stack", true, false)]
    [InlineData("project", false, true)]
    [InlineData("project", true, true)]
    [InlineData("project-document", true, false)]
    public async Task Paused_run_keeps_other_View_commands_routed_from_keyboard_focus(string destination, bool codeOpen, bool focusProject)
    {
        var setup = Composition;
        var root = setup.Root;
        var window = ToolbarAutomationNameTests.ShowMainView();
        var view = (MainView)window.Content!;
        view.DataTemplates.Add(new ViewLocator());
        view.DataContext = root;
        var previousMain = Static.MainView;
        Static.MainView = view;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        // A statement stop already reveals and focuses code; an idle Break exposes the lost designer focus.
        if (destination == "stack") form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDebug.Print 0\r\nEnd Sub\r\n");
        setup.ProjectManager.AddProject(project);
        var command = destination switch
        {
            "immediate" or "project-document" => ApplicationCommands.OpenImmediateCommand,
            "locals" => ApplicationCommands.OpenLocalsCommand,
            "watch" => ApplicationCommands.OpenWatchesCommand,
            "project" => ApplicationCommands.OpenProjectExplorerCommand,
            "browser" => ApplicationCommands.OpenObjectBrowserCommand,
            "palette" => ApplicationCommands.OpenColorPaletteCommand,
            _ => ApplicationCommands.OpenCallStackCommand,
        };
        var designerCommands = new[] { ApplicationCommands.ViewObjectCommand, ApplicationCommands.OpenPropertiesCommand,
            ApplicationCommands.OpenFormLayoutCommand, ApplicationCommands.OpenToolBoxCommand };
        try
        {
            await SettleFocus();
            foreach (var tab in setup.DocumentDockService.AllTabs.ToArray())
                setup.DocumentDockService.TryCloseAny(t => ReferenceEquals(t, tab));
            root.SetToolWindowVisible("Immediate", false).Error.Should().BeNull();
            if (destination == "project-document") setup.EditorService.EditProject(project);
            else if (codeOpen) setup.EditorService.EditCode(form);
            setup.EditorService.EditForm(form);
            await SettleFocus();
            var designer = view.GetVisualDescendants().OfType<HexIDE.VisualDesigner.FormEditView>()
                .Single(v => v.IsEffectivelyVisible);
            var source = designer.FindControl<Control>("FormContainer")!;
            source.Focus().Should().BeTrue();
            if (focusProject)
            {
                root.OpenProjectExplorerTool();
                await SettleFocus();
                source = (Control)window.FocusManager!.GetFocusedElement()!;
            }
            foreach (var designCommand in designerCommands)
                designCommand.CanExecute(null, source).Should().BeTrue();
            command.CanExecute(null, source).Should().BeTrue();

            setup.ProjectRunnerService.RunProject(project, stepInto: destination == "stack");
            await SettleFocus();
            if (destination != "stack")
            {
                setup.ProjectRunnerService.BreakCurrentProject();
                await SettleFocus();
            }
            setup.DebugController.State.Should().Be(DebugState.Paused);
            var focused = window.FocusManager!.GetFocusedElement() as Control;
            TestContext.Current.TestOutputHelper!.WriteLine($"Window focus: {focused}; command target: {CommandManager.FocusedElement}");
            ((ICommand)command).CanExecute(null).Should().BeTrue(destination);
            focused.Should().NotBeNull();
            command.CanExecute(null, focused!).Should().BeTrue(destination);
            if (focusProject) focused.Should().BeSameAs(source);
            else if (destination == "project-document")
                focused!.GetVisualAncestors().Prepend(focused).Should().Contain(v => v is HexIDE.Forms.Views.ProjectDocumentView);
            else if (codeOpen || destination == "stack")
                focused!.GetVisualAncestors().Should().Contain(v => v is HexIDE.Forms.Views.CodeEditorView);
            else focused.Should().BeSameAs(view);
            foreach (var designCommand in designerCommands)
                designCommand.CanExecute(null, focused!).Should().BeFalse();
            if (command.Gestures.FirstOrDefault() is { } gesture)
            {
                var modifiers = Enum.Parse<RawInputModifiers>(gesture.KeyModifiers.ToString());
                var key = Enum.Parse<PhysicalKey>(gesture.Key.ToString());
                window.KeyPressQwerty(key, modifiers);
                window.KeyReleaseQwerty(key, modifiers);
                await SettleFocus();
                if (destination == "browser")
                    setup.DocumentDockService.ActiveTab.Should().BeOfType<HexIDE.Tools.ObjectBrowser.ObjectBrowserToolViewModel>();
                else
                {
                    var tool = destination switch
                    {
                        "project" => "ProjectGroup",
                        "stack" => "CallStack",
                        _ => "Immediate",
                    };
                    root.GetToolWindows().Single(t => t.Name == tool).Visible.Should().BeTrue();
                }
            }
        }
        finally
        {
            setup.ProjectRunnerService.EndProject();
            await SettleFocus();
            setup.ProjectManager.UnloadAllProjects();
            window.Close();
            Static.MainView = previousMain;
        }
    }

    private static async Task SettleFocus()
    {
        for (var i = 0; i < 3; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    // VB6 SP6 measurements: docs/vb6-fidelity-oracle/the-vb6-ides-own-windows-designer-tab-dialog-keys-and-run-mode.md (#870).
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task In_box_run_suspends_designers_and_restores_their_state(bool paused, bool open)
    {
        var setup = Composition;
        var root = setup.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDim n As Long\r\nn = 0\r\nEnd Sub\r\n");
        setup.ProjectManager.AddProject(project);
        var dock = setup.DocumentDockService;
        setup.EditorService.EditForm(form);
        var designer = dock.OpenDocuments.OfType<HexIDE.VisualDesigner.FormEditViewModel>().Single();
        var originalWidth = designer.Form.Width;
        designer.BeginDrag([designer.Form]);
        designer.Form.Width += 10;
        designer.EndDrag();
        designer.SelectedComponent = null;
        var undo = designer.UndoStack.UndoDescription;
        undo.Should().NotBeNull();
        var factory = (MainViewViewModel.DockFactory)root.Layout.Factory!;
        BaseEditorWindowViewModel? earlierCode = null;
        if (open)
        {
            foreach (var kind in new[] { ModuleKind.UserControl, ModuleKind.PropertyPage })
            {
                var module = ModuleDefinition.NewDesigner(project, kind.ToString(), kind,
                    FormComponentClass.ForRoot(kind == ModuleKind.UserControl ? "VB.UserControl" : "VB.PropertyPage"));
                project.AddModule(module);
                setup.EditorService.EditForm(module.FormPart);
            }
            setup.EditorService.EditCode(form);
            var moduleBefore = new ModuleDefinition(project, "Earlier", ModuleKind.StandardModule);
            moduleBefore.UpdateCode("Option Explicit\r\n");
            project.AddModule(moduleBefore);
            setup.EditorService.EditCode(moduleBefore);
            earlierCode = dock.ActiveDocument!;
            var original = factory.DocumentDock!;
            original.VisibleDockables!.Remove(earlierCode);
            original.VisibleDockables.Insert(0, earlierCode);
            var split = factory.CreateDocumentDock();
            split.VisibleDockables = factory.CreateList<Dock.Model.Core.IDockable>();
            factory.MoveDockable(original, split, designer, null);
            factory.SplitToDock(original, split, Dock.Model.Core.DockOperation.Bottom);
            factory.SetFocusedDockable(split, designer);
        }
        setup.EditorService.EditCode(form);
        var before = dock.AllTabs.ToArray();
        var designers = dock.AllTabs.OfType<HexIDE.VisualDesigner.FormEditViewModel>().ToArray();
        var areas = designers.Select(dock.AreaOf).ToArray();
        if (!open) dock.CloseDocument(designer);
        root.SetToolWindowVisible("Toolbox", open).Error.Should().BeNull();
        root.SetToolWindowVisible("Properties", true).Error.Should().BeNull();
        root.SetToolWindowVisible("FormLayout", open && !paused).Error.Should().BeNull();
        var savedLayout = LayoutManifestJson.Serialize(factory.CaptureManifest(root.Layout));
        var recentlyClosed = dock.RecentlyClosed.Entries.ToArray();
        try
        {
            setup.ProjectRunnerService.RunProject(project, stepInto: paused);
            await Task.Yield();
            setup.ProjectRunnerService.RunningProject.Should().BeSameAs(project,
                "the run must start: {0}", root.RuntimeErrors.Last?.Message);
            if (paused) setup.DebugController.State.Should().Be(DebugState.Paused);
            dock.AllTabs.Should().NotContain(t => t is HexIDE.VisualDesigner.FormEditViewModel);
            root.GetToolWindows().Where(t => t.Name is "Toolbox" or "Properties" or "FormLayout")
                .Should().OnlyContain(t => !t.Visible);
            setup.EditorService.EditForm(form);
            setup.EditorService.EditForm(form, form.Components[0]);
            root.ProjectExplorer.SelectedForm = new HexIDE.Tools.FormViewModel(root.ProjectExplorer.LoadedProjects.Single(), form);
            root.ProjectExplorer.OpenSelected();
            dock.AllTabs.Should().NotContain(t => t is HexIDE.VisualDesigner.FormEditViewModel);
            dock.RecentlyClosed.Entries.Should().Equal(recentlyClosed);
            if (earlierCode is not null)
            {
                dock.CloseDocument(earlierCode);
                before = before.Where(tab => !ReferenceEquals(tab, earlierCode)).ToArray();
            }
            LayoutManifestJson.Serialize(factory.CaptureManifest(root.Layout)).Should().Be(savedLayout);
            var active = dock.ActiveTab;
            setup.ProjectRunnerService.EndProject();
            await Task.Yield();
            dock.ActiveTab.Should().BeSameAs(active);
            if (open)
            {
                dock.AllTabs.Should().Equal(before);
                designers.Select(dock.AreaOf).Should().Equal(areas);
                designer.UndoStack.UndoDescription.Should().Be(undo);
                designer.SelectedComponent.Should().BeNull();
                designer.CanUndo.Should().BeTrue();
                designer.UndoStack.Undo();
                designer.Form.Width.Should().Be(originalWidth);
            }
            else
            {
                dock.AllTabs.Should().NotContain(t => t is HexIDE.VisualDesigner.FormEditViewModel);
                setup.EditorService.EditForm(form);
                dock.AllTabs.Should().Contain(t => t is HexIDE.VisualDesigner.FormEditViewModel);
            }
            root.GetToolWindows().Single(t => t.Name == "Properties").Visible.Should().BeTrue();
            root.GetToolWindows().Single(t => t.Name == "Toolbox").Visible.Should().Be(open);
            root.GetToolWindows().Single(t => t.Name == "FormLayout").Visible.Should().Be(open && !paused);
        }
        finally
        {
            setup.ProjectRunnerService.EndProject();
            await Task.Yield();
            setup.ProjectManager.UnloadAllProjects();
        }
    }

    [AvaloniaTheory]
    [InlineData("property")]
    [InlineData("move")]
    [InlineData("add")]
    [InlineData("undo")]
    [InlineData("redo")]
    [InlineData("format")]
    [InlineData("view")]
    public async Task Paused_designer_automation_refuses_without_moving_breakpoints(string action)
    {
        var setup = Composition;
        var root = setup.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        form.UpdateCode(form.Code + "\r\nPrivate Sub Form_Load()\r\nDebug.Print 0\r\nEnd Sub\r\n");
        var directory = Path.Combine(Path.GetTempPath(), "hexide-870-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        project.AbsolutePath = Path.Combine(directory, "P.vbp");
        project.WorkingDirectory = directory;
        form.AbsolutePath = Path.Combine(directory, "Form1.frm");
        setup.ProjectManager.AddProject(project);
        setup.EditorService.EditForm(form);
        var identity = DocumentIdentity.For(form);
        var line = form.Code.ReplaceLineEndings("\n").Split('\n').ToList().FindIndex(s => s == "Debug.Print 0") + 1;
        setup.BreakpointService.Toggle(identity, line);
        using var context = new IdeContext(setup.ProjectManager, setup.DocumentDockService, setup.LspClient,
            setup.EditorService, setup.ProjectRunnerService, setup.ProjectService, setup.EventBus,
            setup.BookmarkService, setup.BreakpointService, setup.DebugController, root, setup.ToolBoxViewModel,
            setup.ProfileService, setup.AddinProjectTemplateService, setup.LanguageSwitchService, setup.Capture,
            setup.Pseudonyms, setup.LocalizationService, setup.SettingsService);
        var tools = new HexIdeTools(context);
        try
        {
            setup.ProjectRunnerService.RunProject(project);
            await Task.Yield();
            setup.DebugController.State.Should().Be(DebugState.Paused);
            await setup.LspClient.OpenDocumentAsync(new Uri(form.AbsolutePath).AbsoluteUri, form.Code,
                TestContext.Current.CancellationToken);
            LanguageServerLeakGuard.Launched.Values.Should().Contain(l => l.Test == TestContext.Current.Test!.TestDisplayName,
                "the paused code window starts the bundled server in the temporary project directory");
            var code = form.Code;
            string? error;
            bool success;
            if (action == "add")
            {
                var reply = await tools.AddControlAsync(form.Name, "CommandButton", 10, 10, 81, 33);
                (success, error) = (reply.Success, reply.Error);
            }
            else
            {
                var reply = action switch
                {
                    "property" => await tools.SetControlPropertyAsync(form.Name, form.Name, "Tag", "Changed"),
                    "move" => await tools.MoveControlAsync(form.Name, form.Name, width: 400),
                    "undo" => await tools.InvokeDesignerUndoAsync(TestContext.Current.CancellationToken),
                    "redo" => await tools.InvokeDesignerRedoAsync(TestContext.Current.CancellationToken),
                    "format" => await tools.InvokeFormatCommandAsync("SizeToGrid", TestContext.Current.CancellationToken),
                    _ => await tools.ViewDesignerAsync(form.Name)
                };
                (success, error) = (reply.Success, reply.Error);
            }
            success.Should().BeFalse();
            error.Should().Contain("project is running").And.Contain("designer").And.Contain("stop_project");
            form.Code.Should().Be(code);
            setup.BreakpointService.GetBreakpoints(identity).Should().Equal(line);
        }
        finally
        {
            setup.ProjectRunnerService.EndProject();
            await Task.Yield();
            setup.ProjectManager.UnloadAllProjects();
            await DisposeAsync();
            LanguageServerLeakGuard.StillRunning().Should().NotContain(l => l.Test == TestContext.Current.Test!.TestDisplayName,
                "the language server must exit before deleting its working directory on Windows");
            Directory.Delete(directory, true);
        }
    }

    private RealComposition? composition;
    private DISetup Composition => (composition ??= new RealComposition()).Composition;

    public async ValueTask DisposeAsync()
    {
        if (composition is { } owned)
        {
            composition = null;
            await owned.DisposeAsync();
        }
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
            null!, null!, Composition.LocalizationService, null!);
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
