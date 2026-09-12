using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Conversations;
using HexIDE.Redaction;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

[McpServerToolType]
internal sealed class HexIdeTools(IdeContext ctx)
{
    [McpServerTool(Name = "get_project_info")]
    [Description("Returns the currently loaded VB6 project name, path, and lists of forms, modules and carried files (RelatedDoc entries).")]
    public async Task<ProjectInfoResult> GetProjectInfoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new ProjectInfoResult(null, null, [], [], []);

            return new ProjectInfoResult(
                project.Name,
                project.AbsolutePath,
                project.Forms.Select(f => f.Name).ToArray(),
                project.Modules.Select(m => m.Name).ToArray(),
                project.RelatedDocuments.Select(d => d.Name).ToArray());
        });
    }

    [McpServerTool(Name = "get_open_editors")]
    [Description("Returns the list of currently open editor windows and which one is active.")]
    public async Task<OpenEditorsResult> GetOpenEditorsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var docs = ctx.DocumentDockService.OpenDocuments;
            var windows = docs.Select(d => d.Title).ToArray();
            var active = ctx.DocumentDockService.ActiveDocument?.Title;
            return new OpenEditorsResult(windows, active);
        });
    }

    [McpServerTool(Name = "get_diagnostics")]
    [Description("Returns current LSP errors and warnings from the VB6 language server.")]
    public DiagnosticsResult GetDiagnostics()
    {
        var items = ctx.Diagnostics.GetAll()
            .SelectMany(p => p.Diagnostics.Select(d => new DiagnosticItem(
                p.Uri,
                d.Message,
                d.Severity?.ToString() ?? "Unknown",
                d.Range.Start.Line + 1,
                d.Range.Start.Character + 1)))
            .ToArray();
        return new DiagnosticsResult(items);
    }

    [McpServerTool(Name = "get_file_content")]
    [Description("Returns the current VB6 source code of a named form or module. Reads from the live editor if the file is open, otherwise from the last saved state.")]
    public async Task<FileContentResult> GetFileContentAsync(string name, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new FileContentResult(null, false, "No project loaded");

            var editor = FindEditor(name);
            if (editor is not null)
                return new FileContentResult(editor.Document.Text, true, null);

            var form = project.Forms.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (form is not null)
                return new FileContentResult(form.Code, false, null);

            var module = project.Modules.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
                return new FileContentResult(module.Code, false, null);

            return new FileContentResult(null, false, $"No form or module named '{name}' found");
        });
    }

    [McpServerTool(Name = "set_file_content")]
    [Description("Replaces the VB6 source code of a named form or module and saves to disk. Use get_project_info to list available names. Pass the CODE SECTION, not a whole file: a .frm's VERSION/Begin designer block is refused (it describes controls, which this tool does not apply), and a .bas/.cls header is stripped. A form's leading 'Attribute VB_*' block is its identity and is invisible in the editor -- if your content omits it the existing one is kept and the result says so, so a body written from what is on screen can no longer destroy VB_Name.")]
    public async Task<MutateResult> SetFileContentAsync(string name, string content, CancellationToken ct)
    {
        var restoredHeader = false;
        var (form, module, error) = await Dispatcher.UIThread.InvokeAsync<(FormDefinition?, ModuleDefinition?, string?)>(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return (null, null, "No project loaded");

            var form = project.Forms.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (form is not null)
            {
                // REFUSED rather than stripped, and the asymmetry with the module branch below is the
                // point. A module's header carries nothing the model does not already own, so dropping it
                // loses nothing. A form's designer block describes its CONTROLS, and this tool does not
                // apply them -- so accepting the file would either bury the header in the code (where it is
                // compiled as VB) or silently discard the controls the caller supplied. (#338)
                if (HexIDE.Runtime.Serialization.FormCodeText.LooksLikeFormFile(content))
                    return (null, null,
                        "That is a whole .frm file, not a form's code section: it opens with a VERSION / "
                        + "Begin designer block. This tool replaces code only and would not apply the "
                        + "controls. Pass what get_file_content returns, or edit the .frm on disk.");

                // The attribute block is restored when the incoming text omits it. VB_Name is the form's
                // identity, it sits at the top of the code section, and NEITHER VB6 nor this IDE's editor
                // shows it -- so writing "the code" as seen on screen used to delete it, with no warning,
                // straight to disk. (gap 14)
                var kept = HexIDE.Runtime.Serialization.FormCodeText.PreserveAttributes(content, form.Code);
                restoredHeader = !ReferenceEquals(kept, content);

                var editor = FindEditor(name);
                if (editor is not null)
                    editor.Document.Text = kept;
                else
                    form.UpdateCode(kept);
                return (form, null, null);
            }

            var module = project.Modules.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
            {
                // .bas/.cls hold the BODY only; strip a VB6 header if a caller passed full file content
                // (idempotent for a body that has none).
                var body = HexIDE.Runtime.Serialization.ModuleFileFormat.StripHeader(content, module.Kind);
                var editor = FindEditor(name);
                if (editor is not null)
                    editor.Document.Text = body;
                else
                    module.UpdateCode(body);
                return (null, module, null);
            }

            return (null, null, $"No form or module named '{name}' found");
        });

        if (error is not null)
            return new MutateResult(false, error);

        if (form is not null && form.AbsolutePath is null)
            return new MutateResult(false, "Form has no saved path — save the project via File > Save first");
        if (module is not null && module.AbsolutePath is null)
            return new MutateResult(false, "Module has no saved path — save the project via File > Save first");

        try
        {
            // The result is honoured rather than assumed. A refusal used to come back as
            // MutateResult(true, null) — success for a file that was not written — and an agent, unlike a
            // developer, has no dialog to read and will build on that answer. (#147)
            // ON THE UI THREAD, and that is the whole of #334 rather than a tidiness point. A save first
            // publishes ApplyAllUnsavedChangesEvent to flush open editor buffers into the model; the
            // handler reads AvaloniaEdit's Document.Text, which throws "Call from invalid thread" off it.
            // EventBus logs that and carries on, so the flush silently does nothing and the PREVIOUS text
            // is serialized -- a file with a fresh timestamp, stale contents, and a success returned to a
            // caller who cannot see the log. Awaiting the save here left this method on a pool thread.
            var written = await Dispatcher.UIThread.InvokeAsync(async () => form is not null
                ? await ctx.ProjectService.SaveForm(form, false)
                : module is not null && await ctx.ProjectService.SaveModule(module, false));
            return written
                ? new MutateResult(true, null, restoredHeader
                    ? "Kept the form's Attribute header, which the content omitted. VB_Name is the form's "
                      + "identity and is invisible in the editor; without this the write would have "
                      + "destroyed it. Call get_file_content first to see the whole code section."
                    : null)
                : new MutateResult(false, "HexIDE cannot reproduce this file faithfully, so it was not "
                                        + "written and the copy on disk is unchanged.");
        }
        catch (Exception ex)
        {
            return new MutateResult(false, ex.Message);
        }
    }

    [McpServerTool(Name = "add_file")]
    [Description("Adds a new form or module to the project, saves it to disk, and returns the file path. type must be 'Form', 'Module', 'ClassModule', 'UserControl', or 'PropertyPage'.")]
    public async Task<AddFileResult> AddFileAsync(string name, string type, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new AddFileResult(false, null, "No project loaded");

            if (project.Forms.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                project.Modules.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                return new AddFileResult(false, null, $"A form or module named '{name}' already exists in the project");

            try
            {
                return type.ToLowerInvariant() switch
                {
                    "form" => new AddFileResult(true,
                        ctx.ProjectService.AddNewForm(project, name).GetAwaiter().GetResult().AbsolutePath, null),
                    "module" => new AddFileResult(true,
                        ctx.ProjectService.AddNewModule(project, name, ModuleKind.StandardModule).GetAwaiter().GetResult().AbsolutePath, null),
                    "classmodule" => new AddFileResult(true,
                        ctx.ProjectService.AddNewModule(project, name, ModuleKind.ClassModule).GetAwaiter().GetResult().AbsolutePath, null),
                    "usercontrol" => new AddFileResult(true,
                        ctx.ProjectService.AddNewUserControl(project, name).GetAwaiter().GetResult().AbsolutePath, null),
                    "propertypage" => new AddFileResult(true,
                        ctx.ProjectService.AddNewPropertyPage(project, name).GetAwaiter().GetResult().AbsolutePath, null),
                    _ => new AddFileResult(false, null, $"Unknown type '{type}' — must be 'Form', 'Module', 'ClassModule', 'UserControl', or 'PropertyPage'")
                };
            }
            catch (Exception ex)
            {
                return new AddFileResult(false, null, ex.Message);
            }
        });
    }

    [McpServerTool(Name = "get_form_controls")]
    [Description("Returns all controls on a form or UserControl with their key design-time properties (name, type, position, size, caption, text, visible, enabled).")]
    public async Task<FormControlsResult> GetFormControlsAsync(string formName, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new FormControlsResult(null, []);

            var form = FindFormDefinition(project, formName);
            if (form is null)
                return new FormControlsResult($"No form or UserControl named '{formName}' found", []);

            // When the form is open in the designer, read live VM state (FormDefinition.Components
            // is only synced on save via ApplyAllUnsavedChangesEvent and won't reflect unsaved additions).
            var designerVm = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d => d.FormDefinition == form);
            var components = designerVm is not null
                ? (IEnumerable<ComponentInstance>)designerVm.AllComponents.Select(v => v.Instance).ToList()
                : form.Components;

            var controls = components.Select(c =>
            {
                var controlName = c.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";
                var left    = c.GetPropertyOrDefault(VBProperties.LeftProperty);
                var top     = c.GetPropertyOrDefault(VBProperties.TopProperty);
                var width   = c.GetPropertyOrDefault(VBProperties.WidthProperty);
                var height  = c.GetPropertyOrDefault(VBProperties.HeightProperty);
                var visible = c.GetPropertyOrDefault(VBProperties.VisibleProperty);
                var enabled = c.GetPropertyOrDefault(VBProperties.EnabledProperty);

                string? caption = c.BaseClass.PropertiesByName.ContainsKey("Caption")
                    ? c.GetPropertyOrDefault(VBProperties.CaptionProperty) : null;
                string? text = c.BaseClass.PropertiesByName.ContainsKey("Text")
                    ? c.GetPropertyOrDefault(VBProperties.TextProperty) : null;

                var typeName = c.BaseClass is FormComponentClass
                    ? form.RootVBTypeName
                    : c.BaseClass.VBTypeName;

                var container = c.Container is { } parent && parent.BaseClass is not FormComponentClass
                    ? parent.GetPropertyOrDefault(VBProperties.NameProperty)
                    : null;

                return new ControlInfo(
                    controlName,
                    typeName,
                    left, top, width, height,
                    visible, enabled,
                    caption, text,
                    container);
            }).ToArray();

            return new FormControlsResult(null, controls);
        });
    }

    [McpServerTool(Name = "set_control_property")]
    [Description("Sets a named property on a form or UserControl control and saves the file. Supports string, number, and bool properties. Use get_form_controls to see available controls and properties.")]
    public async Task<MutateResult> SetControlPropertyAsync(
        string formName, string controlName, string property, string value, CancellationToken ct)
    {
        var (form, ownerModule, error) = await Dispatcher.UIThread.InvokeAsync<(FormDefinition?, ModuleDefinition?, string?)>(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return (null, null, "No project loaded");

            var form = FindFormDefinition(project, formName);
            if (form is null)
                return (null, null, $"No form or UserControl named '{formName}' found");

            var ownerModule = project.Modules.FirstOrDefault(m => m.FormPart == form);

            // When the form is open in the designer, use live VM state (form.Components is only
            // synced on save and won't reflect unsaved additions from add_control).
            var designerVm = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d => d.FormDefinition == form);
            var components = designerVm is not null
                ? (IEnumerable<ComponentInstance>)designerVm.AllComponents.Select(v => v.Instance).ToList()
                : form.Components;

            var control = components.FirstOrDefault(c =>
                string.Equals(c.GetPropertyOrDefault(VBProperties.NameProperty), controlName,
                    StringComparison.OrdinalIgnoreCase));
            if (control is null)
                return (null, null, $"No control named '{controlName}' on form '{formName}'");

            if (!control.BaseClass.PropertiesByName.TryGetValue(property, out var propClass))
                return (null, null, $"Property '{property}' not found on {control.BaseClass.VBTypeName}");

            object? parsed;
            try
            {
                parsed = propClass.PropertyType switch
                {
                    var t when t == typeof(string)  => (object?)value,
                    var t when t == typeof(double)  => double.Parse(value),
                    var t when t == typeof(float)   => float.Parse(value),
                    var t when t == typeof(int)     => int.Parse(value),
                    var t when t == typeof(bool)    => bool.Parse(value),
                    _ => null
                };
                if (parsed is null)
                    return (null, null, $"Property '{property}' has type '{propClass.PropertyType.Name}' which is not supported by set_control_property");
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                // OverflowException too: int/float/double.Parse of an out-of-range literal (e.g. "99999999999" as
                // int) overflows — return a clean parse error instead of crashing the tool handler.
                return (null, null, $"Cannot parse '{value}' as {propClass.PropertyType.Name}");
            }

            var before = control.GetBoxedPropertyOrDefault(propClass);
            control.SetUntypedProperty(propClass, parsed);

            designerVm?.PushSetPropertyCommand(control, propClass, before, parsed);

            return (form, ownerModule, null);
        });

        if (error is not null)
            return new MutateResult(false, error);

        try
        {
            bool written;
            if (ownerModule is not null)
            {
                if (ownerModule.AbsolutePath is null)
                    return new MutateResult(false, "UserControl has no saved path — save the project via File > Save first");
                written = await Dispatcher.UIThread.InvokeAsync(
                    async () => await ctx.ProjectService.SaveModule(ownerModule, false));
            }
            else
            {
                if (form!.AbsolutePath is null)
                    return new MutateResult(false, "Form has no saved path — save the project via File > Save first");
                // Same UI-thread requirement as the other write tool, and for the same reason. (#334)
                written = await Dispatcher.UIThread.InvokeAsync(
                    async () => await ctx.ProjectService.SaveForm(form, false));
            }
            // See the note on the other write tool: a refusal must not come back as success. (#147)
            return written
                ? new MutateResult(true, null)
                : new MutateResult(false, "HexIDE cannot reproduce this file faithfully, so it was not "
                                        + "written and the copy on disk is unchanged.");
        }
        catch (Exception ex)
        {
            return new MutateResult(false, ex.Message);
        }
    }

    [McpServerTool(Name = "open_file")]
    [Description("Opens a form, module or carried file by name in the IDE code editor. Use get_project_info to list available names.")]
    public async Task<MutateResult> OpenFileAsync(string name, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new MutateResult(false, "No project loaded");

            var form = project.Forms.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (form is not null)
            {
                ctx.EditorService.EditCode(form);
                return new MutateResult(true, null);
            }

            var module = project.Modules.FirstOrDefault(m =>
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
            {
                ctx.EditorService.EditCode(module);
                return new MutateResult(true, null);
            }

            // Carried files last, and by name OR filename. They are the only project members with no
            // other route in: the Project Explorer opens one on a DOUBLE-CLICK, which no interaction tool
            // can produce, the row exposes no selection provider to select first, OpenSelected is a plain
            // method rather than a command, and Add File goes through a native dialog. Without this branch
            // a whole editor type is undrivable, which is what blocked verifying #255 against the running
            // IDE. See docs/mcp-server-gaps.md.
            var document = project.RelatedDocuments.FirstOrDefault(d =>
                string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            if (document is not null)
            {
                ctx.EditorService.EditRelatedDocument(document);
                return new MutateResult(true, null);
            }

            return new MutateResult(
                false, $"No form, module or carried file named '{name}' found in the project");
        });
    }

    [McpServerTool(Name = "view_designer")]
    [Description("Opens a form or UserControl by name in the visual designer, bringing it to the front. Useful before take_snapshot to ensure the designer surface is visible. Use get_project_info to list available names.")]
    public async Task<MutateResult> ViewDesignerAsync(string name, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new MutateResult(false, "No project loaded");

            var formDef = FindFormDefinition(project, name);
            if (formDef is not null)
            {
                ctx.EditorService.EditForm(formDef);
                return new MutateResult(true, null);
            }

            return new MutateResult(false, $"No form or UserControl named '{name}' found in the project");
        });
    }

    [McpServerTool(Name = "run_project")]
    [Description("Starts running the current VB6 project in the IDE. Returns an error if no project is loaded or it is already running.")]
    public async Task<MutateResult> RunProjectAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanStartDefaultProject)
            {
                var reason = ctx.ProjectManager.StartupProject is null
                    ? "No project loaded"
                    : "Project is already running";
                return new MutateResult(false, reason);
            }
            // So get_last_runtime_error answers "did THIS run raise" rather than "has anything ever".
            // The sequence survives, so a caller holding an older one can still tell something happened.
            ctx.RootViewModel.RuntimeErrors.Clear();
            ctx.ProjectRunnerService.RunStartupProject();
            return new MutateResult(true, null);
        });
    }


    [McpServerTool(Name = "get_last_runtime_error")]
    [Description("Returns the last runtime error the running program raised, WITH ITS TEXT, and keeps it after the error dialog has been dismissed. Use it after any run that might raise: stop_project and shutdown_ide both close open dialogs, so a run whose form silently did nothing is otherwise indistinguishable from a run whose error dialog was dismissed a moment earlier. run_project clears the message first, so a result here belongs to the most recent run. 'sequence' only ever increases and is not cleared — compare it across runs to tell 'no error' from 'the same error again', which message text cannot do. Returns raised:false when the current run has raised nothing.")]
    public async Task<RuntimeErrorResult> GetLastRuntimeErrorAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var last = ctx.RootViewModel.RuntimeErrors.Last;
            return last is null
                ? new RuntimeErrorResult(false, null, null, 0)
                : new RuntimeErrorResult(true, last.Value.Message,
                                         last.Value.At.ToString("o"), last.Value.Sequence);
        });
    }

    [McpServerTool(Name = "stop_project")]
    [Description("Stops the currently running VB6 project in the IDE.")]
    public async Task<MutateResult> StopProjectAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanEndProject)
                return new MutateResult(false, "No project is currently running");
            ctx.ProjectRunnerService.EndProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "get_window_state")]
    [Description("Returns the current main window state (Maximized/Normal/Minimized) and position/size when in Normal mode.")]
    public async Task<WindowStateResult> GetWindowStateAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = (Avalonia.Application.Current!.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window is null)
                return new WindowStateResult("Unknown", 0, 0, 0, 0);

            var state = window.WindowState switch
            {
                Avalonia.Controls.WindowState.Maximized => "Maximized",
                Avalonia.Controls.WindowState.Minimized => "Minimized",
                _ => "Normal"
            };
            var pos  = window.Position;
            var size = window.ClientSize;
            return new WindowStateResult(state, pos.X, pos.Y, (int)size.Width, (int)size.Height);
        });
    }

    [McpServerTool(Name = "set_window_state")]
    [Description("Sets the main window to Maximized, Normal, or Minimized. When setting Normal, optional x/y/width/height are applied first.")]
    public async Task<MutateResult> SetWindowStateAsync(
        string state,
        int? x, int? y, int? width, int? height,
        CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var window = (Avalonia.Application.Current!.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (window is null)
                return new MutateResult(false, "No main window");

            var target = state.ToLowerInvariant() switch
            {
                "maximized" => Avalonia.Controls.WindowState.Maximized,
                "minimized" => Avalonia.Controls.WindowState.Minimized,
                "normal"    => Avalonia.Controls.WindowState.Normal,
                _ => (Avalonia.Controls.WindowState?)null
            };

            if (target is null)
                return new MutateResult(false, $"Unknown state '{state}' — use Maximized, Normal, or Minimized");

            if (target == Avalonia.Controls.WindowState.Normal)
            {
                if (x is not null && y is not null)
                    window.Position = new Avalonia.PixelPoint(x.Value, y.Value);
                if (width is not null)  window.Width  = width.Value;
                if (height is not null) window.Height = height.Value;
            }

            window.WindowState = target.Value;
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "get_tool_windows")]
    [Description("Returns the list of all registered tool panels with their current visibility.")]
    public async Task<ToolWindowsResult> GetToolWindowsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var tools = ctx.RootViewModel.GetToolWindows()
                .Select(t => new ToolWindowInfo(t.Name, t.Visible))
                .ToArray();
            return new ToolWindowsResult(tools);
        });
    }

    [McpServerTool(Name = "set_tool_window_visible")]
    [Description("Shows or hides a named tool panel. Valid names: Toolbox, Properties, ProjectGroup, FormLayout, Immediate, Locals, Watches, CallStack.")]
    public async Task<MutateResult> SetToolWindowVisibleAsync(string name, bool visible, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var error = ctx.RootViewModel.SetToolWindowVisible(name, visible);
            return error is null
                ? new MutateResult(true, null)
                : new MutateResult(false, error);
        });
    }

    [McpServerTool(Name = "get_undo_state")]
    [Description("Returns the current undo/redo state of the active editor: whether it is a form designer, whether undo/redo are available, and the descriptions that would appear in the Edit menu.")]
    public async Task<UndoStateResult> GetUndoStateAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var active = ctx.DocumentDockService.ActiveDocument;
            if (active is HexIDE.VisualDesigner.FormEditViewModel designer)
                return new UndoStateResult(
                    "FormDesigner",
                    designer.CanUndo,
                    designer.CanRedo,
                    designer.UndoStack.UndoDescription,
                    designer.UndoStack.RedoDescription);

            return new UndoStateResult(
                active?.GetType().Name ?? "None",
                false, false, null, null);
        });
    }

    [McpServerTool(Name = "invoke_designer_undo")]
    [Description("Invokes Undo on the active form designer. Returns an error if no form designer is active or nothing is on the undo stack.")]
    public async Task<MutateResult> InvokeDesignerUndoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");
            if (!designer.CanUndo)
                return new MutateResult(false, "Nothing to undo");
            designer.UndoStack.Undo();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "invoke_designer_redo")]
    [Description("Invokes Redo on the active form designer. Returns an error if no form designer is active or nothing is on the redo stack.")]
    public async Task<MutateResult> InvokeDesignerRedoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");
            if (!designer.CanRedo)
                return new MutateResult(false, "Nothing to redo");
            designer.UndoStack.Redo();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "move_control")]
    [Description("Moves or resizes a control on a form designer canvas by simulating a drag transaction. " +
        "Calls BeginDrag on the active designer, updates only the supplied position/size values on the ViewModel, " +
        "then calls EndDrag — so the operation lands as a single undo step on the designer undo stack. " +
        "The form must already be open in the visual designer (call view_designer first). " +
        "Use the form's own name as controlName to resize the form itself. " +
        "If left/top/width/height are all omitted, EndDrag is still called (tests the no-change path). " +
        "left/top are CONTAINER-RELATIVE, matching get_form_controls and the .frm: for a control inside a " +
        "Frame or PictureBox they are measured from that container, not from the form. Note that a VB6 control " +
        "array shares one name across its elements (Options Dialog.frm has four picOptions), so a name that is " +
        "not unique resolves to the first in document order.")]
    public async Task<MutateResult> MoveControlAsync(
        string formName,
        string controlName,
        double? left,
        double? top,
        double? width,
        double? height,
        CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var designer = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d =>
                    string.Equals(d.FormDefinition?.Name, formName, StringComparison.OrdinalIgnoreCase));

            if (designer is null)
                return new MutateResult(false,
                    $"Form '{formName}' is not open in the visual designer — call view_designer first");

            var target = designer.AllComponents.FirstOrDefault(c =>
                string.Equals(c.Name, controlName, StringComparison.OrdinalIgnoreCase));

            if (target is null)
                return new MutateResult(false,
                    $"No control named '{controlName}' on form '{formName}'");

            designer.BeginDrag([target]);

            // Written to the MODEL, not through the view-model. The view-model's Left/Top are canvas
            // coordinates — they add the accumulated origin of every container above — while
            // get_form_controls and set_control_property both read and write the model's container-relative
            // values. Going through the view-model here would make move_control the only tool on the other
            // side of that boundary, so a control inside a Frame would move to a different place than the
            // number implies. Width and Height mean the same thing in either space.
            if (left.HasValue)   target.Instance.SetProperty(VBProperties.LeftProperty, left.Value);
            if (top.HasValue)    target.Instance.SetProperty(VBProperties.TopProperty, top.Value);
            if (width.HasValue)  target.Instance.SetProperty(VBProperties.WidthProperty, width.Value);
            if (height.HasValue) target.Instance.SetProperty(VBProperties.HeightProperty, height.Value);

            designer.EndDrag();

            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "add_control")]
    [Description("Places a control of the given type on a named form's designer canvas at the given position and size, and SAVES the form. The form must be open in the visual designer (call view_designer first if needed). Returns the auto-generated control name (e.g. 'Command1'). A refusal to write (an unfaithful form) comes back as success:false naming the control that is in the designer but not on disk.")]
    public async Task<AddControlResult> AddControlAsync(
        string formName, string type,
        double x, double y, double width, double height,
        CancellationToken ct)
    {
        var spawned = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var designer = ctx.DocumentDockService.OpenDocuments
                .OfType<HexIDE.VisualDesigner.FormEditViewModel>()
                .FirstOrDefault(d =>
                    string.Equals(d.FormDefinition?.Name, formName, StringComparison.OrdinalIgnoreCase));

            if (designer is null)
                return new AddControlResult(false, null,
                    $"Form '{formName}' is not open in the visual designer — call view_designer first");

            var componentClass = designer.ToolsBoxToolViewModel.Components
                .Where(c => c.BaseClass is not null)
                .FirstOrDefault(c =>
                    string.Equals(c.BaseClass!.VBTypeName, type, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals("VB." + type, c.BaseClass!.VBTypeName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(c.BaseClass!.Name, type, StringComparison.OrdinalIgnoreCase))
                ?.BaseClass;

            if (componentClass is null)
            {
                var known = string.Join(", ", designer.ToolsBoxToolViewModel.Components
                    .Where(c => c.BaseClass is not null)
                    .Select(c => c.BaseClass!.VBTypeName.Replace("VB.", "")));
                return new AddControlResult(false, null,
                    $"Unknown control type '{type}'. Known types: {known}");
            }

            designer.SpawnControlAt(componentClass, new Avalonia.Rect(x, y, width, height));
            return new AddControlResult(true, designer.SelectedComponent?.Name, null, designer.FormDefinition);
        });

        if (!spawned.Success || spawned.Form is null)
            return spawned with { Form = null };

        // PERSISTED, because until this it was not: nine controls were added through this tool and all nine
        // were lost when the IDE crashed, with the .frm on disk still holding a bare form. Its sibling
        // set_control_property saved on every call, so the pair disagreed about whether a designer edit was
        // durable, and the one that did not save is the one that creates things.
        //
        // On the UI thread: a save first publishes ApplyAllUnsavedChangesEvent, whose handler reads
        // AvaloniaEdit's Document.Text and throws off it, leaving the previous content to be written and
        // reported as success. (#334)
        try
        {
            var written = await Dispatcher.UIThread.InvokeAsync(
                async () => await ctx.ProjectService.SaveForm(spawned.Form, false));

            // A refusal must not come back as success. (#147) The control is real and in the designer --
            // saying otherwise would be its own wrong answer -- but the file on disk does not have it, and
            // a caller who is told "saved" will not find out until something else reads that file.
            return written
                ? spawned with { Form = null }
                : new AddControlResult(false, spawned.ControlName,
                    $"'{spawned.ControlName}' was added to the designer but NOT written: HexIDE cannot reproduce "
                    + "this form faithfully, so the copy on disk is unchanged. Undo the add or the designer "
                    + "and the file will stay out of step.");
        }
        catch (Exception ex)
        {
            return new AddControlResult(false, spawned.ControlName,
                $"'{spawned.ControlName}' was added to the designer but the save failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "invoke_format_command")]
    [Description("Invokes a Format menu command on the active form designer, which lands as one undo step. " +
        "Commands: AlignLefts, AlignRights, AlignTops, AlignBottoms, AlignCentersH, AlignCentersV, " +
        "MakeSameWidth, MakeSameHeight, MakeSameSize, MakeHorizontalSpacingEqual, IncreaseHorizontalSpacing, " +
        "DecreaseHorizontalSpacing, RemoveHorizontalSpacing, MakeVerticalSpacingEqual, IncreaseVerticalSpacing, " +
        "DecreaseVerticalSpacing, RemoveVerticalSpacing, SizeToGrid, CenterHorizontally, CenterVertically.")]
    public async Task<MutateResult> InvokeFormatCommandAsync(string command, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DocumentDockService.ActiveDocument is not HexIDE.VisualDesigner.FormEditViewModel designer)
                return new MutateResult(false, "Active window is not a form designer");

            Action? action = command switch
            {
                "AlignLefts"               => designer.AlignLefts,
                "AlignRights"              => designer.AlignRights,
                "AlignTops"                => designer.AlignTops,
                "AlignBottoms"             => designer.AlignBottoms,
                "AlignCentersH"            => designer.AlignCentersH,
                "AlignCentersV"            => designer.AlignCentersV,
                "MakeSameWidth"            => designer.MakeSameWidth,
                "MakeSameHeight"           => designer.MakeSameHeight,
                "MakeSameSize"             => designer.MakeSameSize,
                "MakeHorizontalSpacingEqual"  => designer.MakeHorizontalSpacingEqual,
                "IncreaseHorizontalSpacing"   => designer.IncreaseHorizontalSpacing,
                "DecreaseHorizontalSpacing"   => designer.DecreaseHorizontalSpacing,
                "RemoveHorizontalSpacing"     => designer.RemoveHorizontalSpacing,
                "MakeVerticalSpacingEqual"    => designer.MakeVerticalSpacingEqual,
                "IncreaseVerticalSpacing"     => designer.IncreaseVerticalSpacing,
                "DecreaseVerticalSpacing"     => designer.DecreaseVerticalSpacing,
                "RemoveVerticalSpacing"       => designer.RemoveVerticalSpacing,
                "SizeToGrid"               => designer.SizeToGrid,
                "CenterHorizontally"       => designer.CenterHorizontally,
                "CenterVertically"         => designer.CenterVertically,
                _                          => (Action?)null
            };

            if (action is null)
                return new MutateResult(false,
                    $"Unknown command '{command}'. See tool description for valid names.");

            action();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "get_bookmarks")]
    [Description("Returns the bookmarked line numbers (0-based) for a named form or module. Returns an empty array if no bookmarks exist.")]
    public async Task<BookmarksResult> GetBookmarksAsync(string name, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new BookmarksResult(null, [], "No project loaded");

            var uri = ResolveDocumentUri(project, name);
            if (uri is null)
                return new BookmarksResult(null, [], $"No form or module named '{name}' found");

            var lines = ctx.BookmarkService.GetBookmarks(uri);
            return new BookmarksResult(uri, [.. lines], null);
        });
    }

    [McpServerTool(Name = "set_bookmarks")]
    [Description("Replaces all bookmarks for a named form or module with the supplied 0-based line numbers. Pass an empty array to clear all bookmarks for that document.")]
    public async Task<MutateResult> SetBookmarksAsync(string name, int[] lines, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new MutateResult(false, "No project loaded");

            var uri = ResolveDocumentUri(project, name);
            if (uri is null)
                return new MutateResult(false, $"No form or module named '{name}' found");

            ctx.BookmarkService.SetBookmarks(uri, lines);
            return new MutateResult(true, null);
        });
    }

    // ---- Debugger ----

    [McpServerTool(Name = "get_breakpoints")]
    [Description("Returns the breakpoint line numbers (1-based) for a named form or module. Empty array if none.")]
    public async Task<BreakpointsResult> GetBreakpointsAsync(string name, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new BreakpointsResult(null, [], "No project loaded");

            var uri = ResolveDocumentUri(project, name);
            if (uri is null)
                return new BreakpointsResult(null, [], $"No form or module named '{name}' found");

            return new BreakpointsResult(uri, [.. ctx.BreakpointService.GetBreakpoints(uri)], null);
        });
    }

    [McpServerTool(Name = "set_breakpoints")]
    [Description("Replaces all breakpoints for a named form or module with the supplied 1-based line numbers. Pass an empty array to clear that document's breakpoints. Takes effect immediately if the project is running.")]
    public async Task<MutateResult> SetBreakpointsAsync(string name, int[] lines, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new MutateResult(false, "No project loaded");

            var uri = ResolveDocumentUri(project, name);
            if (uri is null)
                return new MutateResult(false, $"No form or module named '{name}' found");

            ctx.BreakpointService.SetDocument(uri, lines);
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "clear_all_breakpoints")]
    [Description("Removes every breakpoint in the project.")]
    public async Task<MutateResult> ClearAllBreakpointsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ctx.BreakpointService.ClearAll();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "break_program")]
    [Description("Pauses the running project at the next executed statement (VB6 Break / Ctrl+Break). Error if not running or already paused.")]
    public async Task<MutateResult> BreakProgramAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanBreakProject)
                return new MutateResult(false, ctx.ProjectRunnerService.IsRunning ? "Already paused" : "No project is running");
            ctx.ProjectRunnerService.BreakCurrentProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "continue_program")]
    [Description("Resumes a paused project (VB6 Continue / F5 in break mode). Error if not currently paused.")]
    public async Task<MutateResult> ContinueProgramAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanContinueProject)
                return new MutateResult(false, "Project is not paused");
            ctx.ProjectRunnerService.ContinueProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "step_into")]
    [Description("Step Into (F8): from idle, starts the project and breaks at the first executed statement; while paused, executes the next statement and breaks (descending into any called Sub/Function); while running, breaks at the next statement. Call get_debug_state afterward to read the new paused module/line.")]
    public async Task<MutateResult> StepIntoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanStepIntoProject)
                return new MutateResult(false, "No project to step — load a project first");
            ctx.ProjectRunnerService.StepIntoProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "step_over")]
    [Description("Step Over (Shift+F8): while paused, executes the next statement and breaks in the SAME frame — a called Sub/Function runs to completion without descending (unlike step_into). On a non-call statement it behaves like step_into. From idle, starts the project and breaks at the first statement. Call get_debug_state afterward to read the new paused module/line.")]
    public async Task<MutateResult> StepOverAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanStepOverProject)
                return new MutateResult(false, "No project to step — load a project first");
            ctx.ProjectRunnerService.StepOverProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "step_out")]
    [Description("Step Out (Ctrl+Shift+F8): while paused, runs the rest of the current procedure and breaks at the statement in the CALLER after it returns. Stepping out of the outermost frame runs that event/procedure to completion. From idle it starts the project (like Step Into); while running it requests a pause. Call get_debug_state afterward to read the new paused module/line.")]
    public async Task<MutateResult> StepOutAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanStepOutProject)
                return new MutateResult(false, "No project to step — load a project first");
            ctx.ProjectRunnerService.StepOutProject();
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "run_to_cursor")]
    [Description("Run To Cursor (Ctrl+F8): run until (module, 1-based line) then break — a one-shot temporary breakpoint. While paused it continues to the target; while running it arms the target; from idle it starts the project and runs to the target (a real breakpoint hit first stays paused there; continue proceeds toward the target). Call get_debug_state afterward to read the paused module/line.")]
    public async Task<MutateResult> RunToCursorAsync(string module, int line, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!ctx.ProjectRunnerService.CanRunToCursor)
                return new MutateResult(false, "No project to run — load a project first");
            ctx.ProjectRunnerService.RunToCursorProject(module, line);
            return new MutateResult(true, null);
        });
    }

    [McpServerTool(Name = "set_next_statement")]
    [Description("Set Next Statement (Ctrl+F9): move the execution point to (module, 1-based line) WITHOUT running the statements in between — the next step_into/continue executes from there. Only while paused, and only to a TOP-LEVEL statement of the currently paused procedure (a target nested inside an If/For/Do/Select block, or a move while paused inside such a block, is refused — a tree-walker limit, not VB6's). Returns an error result if refused. Call get_debug_state afterward to read the moved current line.")]
    public async Task<MutateResult> SetNextStatementAsync(string module, int line, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
            ctx.DebugController.SetNextStatement(module, line)
                ? new MutateResult(true, null)
                : new MutateResult(false, "Refused — must be paused, and the target must be a top-level statement of the paused procedure (not nested in a block)"));
    }

    [McpServerTool(Name = "get_debug_state")]
    [Description("Returns the interpreter debug state: whether a project is running, the controller state (Running/Paused/Stopped), and — when paused — the break location (module, 1-based line) and reason.")]
    public async Task<DebugStateResult> GetDebugStateAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var stop = ctx.DebugController.CurrentStop;
            return new DebugStateResult(
                ctx.ProjectRunnerService.IsRunning,
                ctx.DebugController.State.ToString(),
                stop?.Reason.ToString(),
                stop?.Module,
                stop?.Line);
        });
    }

    [McpServerTool(Name = "get_locals")]
    [Description("Returns the paused frame's Locals as a tree (Expression/Value/Type), depth-capped. Valid only while paused (get_debug_state.state == Paused) — otherwise Success is false. 'context' is the Module.Procedure header; each row has has_children and, down to max_depth, nested children (arrays/UDTs/objects expand; a class instance's Me/fields appear under a Me/module root).")]
    public async Task<LocalsResult> GetLocalsAsync(int maxDepth = 3, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var scope = ctx.DebugController.GetLocals();
            if (scope is null)
                return new LocalsResult(false, "Project is not paused", null, null);
            int cap = Math.Clamp(maxDepth, 1, 8);
            int[] budget = { MaxLocalsNodes };   // total-node budget across the whole eager projection
            var rows = scope.Locals.Select(n => MapLocalsNode(n, cap, 1, budget)).ToArray();
            return new LocalsResult(true, null, scope.Context, rows);
        });
    }

    // Bound the eager depth-projection: even with the per-array element cap + depth cap, a pathologically wide
    // nested tree could realize a lot of nodes on the UI thread. Stop after this many.
    private const int MaxLocalsNodes = 5000;

    // Depth-bounded projection of the lazy DebugNode tree into serializable rows. Children below max_depth are
    // omitted (has_children still signals they exist); a truncated array tail becomes a "… N more" row.
    private static LocalsRow MapLocalsNode(DebugNode node, int maxDepth, int depth, int[] budget)
    {
        LocalsRow[]? children = null;
        if (node.HasChildren && depth < maxDepth && budget[0] > 0)
        {
            var kids = new List<LocalsRow>();
            foreach (var c in node.Expand())
            {
                if (budget[0]-- <= 0)
                    break;
                kids.Add(MapLocalsNode(c, maxDepth, depth + 1, budget));
            }
            children = kids.ToArray();
        }
        string expression = node.TruncatedRemaining > 0 ? $"… {node.TruncatedRemaining} more" : node.Name;
        return new LocalsRow(expression, node.Value, node.TypeName, node.HasChildren, children);
    }

    [McpServerTool(Name = "get_call_stack")]
    [Description("Returns the call stack at the current break — the chain of running procedure activations, current/deepest frame first, each with proc, module, and 1-based line. Valid only while paused (get_debug_state.state == Paused) — otherwise Success is false with an empty list.")]
    public async Task<CallStackResult> GetCallStackAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ctx.DebugController.State != HexIDE.Runtime.Debugging.DebugState.Paused)
                return new CallStackResult(false, "Project is not paused", System.Array.Empty<CallStackFrameRow>());
            var frames = ctx.DebugController.GetCallStack()
                .Select(f => new CallStackFrameRow(f.ProcName, f.Module, f.Line))
                .ToArray();
            return new CallStackResult(true, null, frames);
        });
    }

    [McpServerTool(Name = "add_watch")]
    [Description("Adds a watch expression to the Watches window. watchType is one of Expression (default; display the value), BreakWhenTrue, or BreakWhenChanged (P6a stores all three; only Expression displays a value today). Returns the full watch list after adding.")]
    public async Task<WatchesResult> AddWatchAsync(string expression, string? watchType = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var type = watchType?.Trim().ToLowerInvariant() switch
            {
                "breakwhentrue" or "break_when_true" or "true"    => HexIDE.Debugging.WatchType.BreakWhenTrue,
                "breakwhenchanged" or "break_when_changed" or "changed" => HexIDE.Debugging.WatchType.BreakWhenChanged,
                _ => HexIDE.Debugging.WatchType.Expression,
            };
            var context = ctx.DebugController.GetLocals()?.Context ?? "(All Procedures)";
            ctx.RootViewModel.Watches.Service.Add(new HexIDE.Debugging.WatchExpression(expression, type, context));
            return await BuildWatchesResult();
        });
    }

    [McpServerTool(Name = "get_watches")]
    [Description("Returns every watch (expression, watch type, context) and its current value/type. Values are live only while paused (get_debug_state.state == Paused); otherwise value is '<Out of context>'.")]
    public async Task<WatchesResult> GetWatchesAsync(CancellationToken ct = default)
        => await Dispatcher.UIThread.InvokeAsync(BuildWatchesResult);

    // Snapshot the watch list, evaluating each against the paused frame (live only while Paused).
    private async Task<WatchesResult> BuildWatchesResult()
    {
        var rows = new List<WatchRow>();
        foreach (var w in ctx.RootViewModel.Watches.Service.Watches)
        {
            var result = await ctx.DebugController.EvaluateWatchAsync(w.Expression);
            rows.Add(new WatchRow(
                w.Expression, w.Type.ToString(), w.Context,
                result?.Display ?? "<Out of context>", result?.TypeName ?? "", result?.Ok ?? false));
        }
        return new WatchesResult(true, null, rows.ToArray());
    }

    [McpServerTool(Name = "evaluate")]
    [Description("Runs an Immediate-window line against the paused frame. A leading ?/Print/Debug.Print (or a bare expression) EVALUATES and returns the value (variables, operators, intrinsics). A BARE assignment or Set (e.g. \"count = 7\", \"Set obj = Nothing\") is EXECUTED and mutates the paused frame (returns empty) — whereas \"?count = 7\" compares. User Sub/Function calls are still rejected (they would deadlock the paused gate). Valid only while paused (get_debug_state.state == Paused). Returns the formatted result / empty (for a statement) / a VB6-style error message.")]
    public async Task<EvaluateResult> EvaluateAsync(string expression, CancellationToken ct = default)
    {
        string? result = await Dispatcher.UIThread.InvokeAsync(() => ctx.DebugController.EvaluateAsync(expression));
        return result is null
            ? new EvaluateResult(false, "Project is not paused", null)
            : new EvaluateResult(true, null, result);
    }

    [McpServerTool(Name = "get_toolbox_items")]
    [Description("Returns the names of all controls currently in the Toolbox, in order. Includes both built-in controls and any add-in registered controls.")]
    public async Task<ToolboxItemsResult> GetToolboxItemsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var items = ctx.ToolBoxViewModel.Components
                .Select(c => new ToolboxItem(c.Name, c.BaseClass?.VBTypeName))
                .ToArray();
            return new ToolboxItemsResult(items);
        });
    }

    [McpServerTool(Name = "get_new_project_templates")]
    [Description("Returns the list of project templates that would appear in the New Project dialog, including personality templates and any add-in registered templates.")]
    public Task<NewProjectTemplatesResult> GetNewProjectTemplatesAsync(CancellationToken ct)
    {
        var personality = ctx.PersonalityService.AvailableProjectTypes
            .Select(t => new TemplateInfo(t.Name, t.Supported, "personality"))
            .ToArray();
        var addin = ctx.AddinProjectTemplateService.Templates
            .Select(t => new TemplateInfo(t.Name, t.Supported, "addin"))
            .ToArray();
        return Task.FromResult(new NewProjectTemplatesResult([.. personality, .. addin]));
    }

    [McpServerTool(Name = "shutdown_ide")]
    [Description("Shuts down the HexIDE application cleanly, triggering all shutdown handlers. " +
                 "Stops a running project and closes any dialogs still open first — otherwise the app " +
                 "keeps running (its shutdown mode is last-window-close) and the next build fails on a " +
                 "file lock. The reply says what was torn down; it CANNOT confirm the process exited, " +
                 "because the reply has to be sent before it does — poll /health until it stops " +
                 "answering for that. " +
                 "force (default true) skips the save-changes prompt and DISCARDS unsaved edits — " +
                 "without it that prompt would block the shutdown and wedge automation. " +
                 "Pass force=false to get exactly what a user closing the window sees, including the " +
                 "prompt; the IDE then stays up if the user cancels.")]
    public async Task<ShutdownResult> ShutdownIdeAsync(bool force = true, CancellationToken ct = default)
    {
        // Tear down synchronously, so the reply can say what actually happened, then post the main-window
        // close. Closing every window here instead would end the process mid-request and the caller would
        // see a dropped connection rather than a result.
        var (projectStopped, dialogsClosed) = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Static.ForceCloseWithoutPrompt = force;

            // Only when force was asked for. force=false promises exactly what a user closing the window
            // sees, and a user closing it does not have their running program stopped or their open
            // dialogs shut from under them — so that path is left alone, and the IDE staying up is then
            // a correct outcome rather than the bug below.
            //
            // A running program owns windows the main window does not: its VBFormRuntime, and any MsgBox
            // or InputBox layered over that. Under ShutdownMode.OnLastWindowClose those keep the app
            // alive after the main window goes, and the symptom lands on whoever builds next —
            // "file is locked by HexIDE.Desktop" — which looks nothing like a shutdown problem.
            var stopped = false;
            if (force && ctx.ProjectRunnerService.CanEndProject)
            {
                ctx.ProjectRunnerService.EndProject();
                stopped = true;
            }

            var lifetime = Avalonia.Application.Current!.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;

            // Whatever survived that — a dialog whose owner is gone, an IDE modal, a runtime error box.
            // Close children before parents: closing an owner does not close what it owns, and a dialog
            // outliving its owner is exactly the window that keeps the process up.
            var closed = 0;
            if (force && lifetime is not null)
            {
                foreach (var w in lifetime.Windows.Reverse().ToList())
                {
                    if (w == mainWindow || !w.IsVisible)
                        continue;
                    w.Close();
                    closed++;
                }
            }

            return (stopped, closed);
        });

        Dispatcher.UIThread.Post(() =>
        {
            var lifetime = Avalonia.Application.Current!.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            lifetime?.MainWindow?.Close();
        });

        return new ShutdownResult(true, projectStopped, dialogsClosed,
            "Shutdown requested. Poll /health until it stops answering to confirm the process exited.");
    }

    [McpServerTool(Name = "set_ide_language")]
    [Description("Switches the IDE chrome language by pack id ('en', 'pseudo', 'pseudo-rtl', or an installed pack id), driving the exact live-apply + countdown-revert confirmation gate the Options dropdown uses. Returns immediately; the gate stays open and auto-reverts after its countdown. Call take_snapshot right after to capture the gate, or wait for it to time out to see the reverted chrome.")]
    public Task<MutateResult> SetIdeLanguageAsync(string id, CancellationToken ct)
    {
        // Fire the switch+gate on the UI thread and return at once, so the modal gate is left open
        // for take_snapshot to capture (awaiting here would block until the gate resolved).
        Dispatcher.UIThread.Post(() => _ = ctx.LanguageSwitch.SwitchWithGateAsync(id));
        return Task.FromResult(new MutateResult(true, null));
    }

    [McpServerTool(Name = "take_snapshot")]
    [Description("Captures the current HexIDE window as a PNG and returns the file path so the caller can read the image. If a modal dialog is open it is captured in preference to the main window (its title is reported in 'active_dialog'); otherwise the main window is captured. 'window' selects which top-level window to address: \"auto\" (default) is the frontmost one, which while a VB6 program runs — INCLUDING while it is paused at a breakpoint — is the program's form, not the IDE; pass \"ide\" to address the IDE itself in that state.")]
    public async Task<SnapshotResult> TakeSnapshotAsync(
        string? window = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Through the shared resolver, so 'window' means the same thing here as in every other tool.
            // A modal dialog is a separate top-level window and is captured in preference to whatever is
            // underneath — which may be a running program's form rather than the main window.
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new SnapshotResult(null, error, null);

            var activeDialog = label == "MainWindow" ? null : label;

            // A dropped-down menu, a combo's list, a flyout: each is realised in its own top-level root,
            // so rendering the window alone gives a menu bar with no menu. The composer draws them in
            // their real positions — gap 12 in docs/mcp-server-gaps.md.
            using var bitmap = HexIDE.Automation.SnapshotComposer.Capture(active);
            if (bitmap is null)
                return new SnapshotResult(null, "Window has no size", activeDialog);

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide_snapshot.png");
            bitmap.Save(path);

            return new SnapshotResult(path, null, activeDialog);
        });
    }

    [McpServerTool(Name = "dump_visual_tree")]
    [Description("Walks the live control tree of the active window (a visible modal dialog is preferred over the main window) and returns a structured node tree for discovering and addressing controls. Uses the UIA 'control view': structural layout wrappers (Panels, Borders, ContentPresenters, dock plumbing) are collapsed away, so the tree is shallow and paths are short. Each node carries its addressing 'path' (feed it back as a target), automation ControlType, Name, AutomationId, ClassName, the DataContext ViewModel type, supported interaction providers (invoke/selection/selectionItem/value/toggle/expandCollapse/...), and enabled/offscreen flags. Use this to find what is on screen before inspect_element or interact. A node carries 'isHidden': true when it is in the tree but NOT on screen (an effectively-invisible control, e.g. a collapsed banner); the field is absent when the node is showing. Presence in the tree never meant visible — check this before asserting that a banner, warning or overlay is displayed, and note it answers a different question from 'isOffscreen', which is about clipping and scroll position. Params: root (optional path to scope to a subtree; null = whole window), maxDepth (default 20, counted in meaningful/control-view levels), interactiveOnly (default true — keeps only nodes that are interactive or have an interactive descendant). For deeply nested or large areas, pass a 'root' to scope the dump. 'window' selects which top-level window to address: \"auto\" (default) is the frontmost one, which while a VB6 program runs — INCLUDING while it is paused at a breakpoint — is the program's form, not the IDE; pass \"ide\" to address the IDE itself in that state.")]
    public async Task<VisualTreeResult> DumpVisualTreeAsync(
        string? root = null, int maxDepth = 20, bool interactiveOnly = true, string? window = null,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new VisualTreeResult(error, null, null);

            Control start = active;
            var basePath = "Window";
            if (root is not null)
            {
                var (resolved, resolveError) = UiAutomationDriver.Resolve(active, root);
                if (resolved is null)
                    return new VisualTreeResult(resolveError, label, null);
                start = resolved;
                basePath = root;
            }

            var node = UiAutomationDriver.Dump(start, basePath, maxDepth, interactiveOnly);
            return new VisualTreeResult(null, label, node);
        });
    }

    [McpServerTool(Name = "inspect_element")]
    [Description("Returns a deep inspection of a single control addressed by 'target' (a path from dump_visual_tree): identity, supported interaction providers, bounding rectangle, current selection/value/toggle state, and the DataContext ViewModel's public command and property members (the surface the reflection-based interact actions target). Use before interact to confirm an element supports the action you intend, or — for a control with no provider — to discover the VM members the reflection fallback can reach. 'window' picks the top-level window the path is resolved against — \"auto\" (default, the frontmost) or \"ide\"; pass \"ide\" to reach the IDE while a program is running or paused. Reports 'isHidden': true when the control is in the tree but not on screen (effectively invisible, e.g. collapsed by a binding); absent when it is showing.")]
    public async Task<InspectResult> InspectElementAsync(
        string target, string? window = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, label, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InspectResult(error, null, null);

            var (control, resolveError) = UiAutomationDriver.Resolve(active, target);
            if (control is null)
                return new InspectResult(resolveError, label, null);

            return new InspectResult(null, label, UiAutomationDriver.Inspect(control, target));
        });
    }

    [McpServerTool(Name = "interact")]
    [Description("Drives a live control addressed by 'target' (a path from dump_visual_tree) through its UI Automation provider — one polymorphic verb instead of a tool per interaction. Provider actions: invoke (click a Button / menu item), select (pick a ComboBox/ListBox item), set_value (set a TextBox's text), toggle (flip a CheckBox), expand / collapse (open/close a dropdown, tree node, expander). Reflection fallback (for controls with no provider — see inspect_element's dataContextMembers): invoke_command (value = a command name; executes that ICommand on the target's DataContext after a CanExecute check) and set_property (value = \"PropertyName=NewValue\"; sets that VM property, coercing to its type). 'value': required for set_value (the text) and the reflection actions; for select, the item text to match (omit if 'target' already points at the item). A missing provider fails with \"element does not support '<action>'\" — there is NO implicit fallback to reflection; choose invoke_command/set_property explicitly. Virtualized dropdown items aren't addressable until realized — 'expand' first, then dump_visual_tree(root=combo), then 'select'. Actions are real and unguarded (the server is DEBUG-only). Use dump_visual_tree/inspect_element first to find the target and confirm what it supports. 'window' picks the top-level window the path is resolved against — \"auto\" (default, the frontmost) or \"ide\"; pass \"ide\" to reach the IDE while a program is running or paused.")]
    public async Task<InteractOutcome> InteractAsync(
        string target, string action, string? value = null, string? window = null,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, "peer", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(active, target);
            if (control is null)
                return new InteractOutcome(false, "peer", null, resolveError);

            return UiAutomationDriver.Interact(control, action, value);
        });
    }

    [McpServerTool(Name = "type_text")]
    [Description("Types text into the control at 'target' (a path from dump_visual_tree) by inserting at the caret via the control's own API — works on the code editor (AvaloniaEdit), which has no value provider for 'interact set_value'. If 'target' isn't itself a text surface, the nearest descendant editor/textbox is used (the AvaloniaEdit editor is preferred over incidental textboxes). Multi-line text is inserted verbatim (include \\n for new lines); exact, reliable, and not altered by live auto-indent/IntelliSense. For a typing cadence, call this once per line. Use press_key for Enter/Tab/commands. 'window' picks the top-level window the path is resolved against — \"auto\" (default, the frontmost) or \"ide\"; pass \"ide\" to reach the IDE while a program is running or paused.")]
    public async Task<InteractOutcome> TypeTextAsync(
        string target, string text, string? window = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, "keyboard", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(active, target);
            if (control is null)
                return new InteractOutcome(false, "keyboard", null, resolveError);

            return UiAutomationDriver.TypeText(control, text);
        });
    }

    [McpServerTool(Name = "hover")]
    [Description("Moves the pointer onto the control at 'target' (a path from dump_visual_tree) by raising real PointerEntered/PointerMoved events, watches for a tip, and reports its text. WORKS for tips a control raises from its OWN pointer handler — LSP quick-info and the debugger's Auto Data Tips in the code editor. Does NOT work for a declarative ToolTip.Tip: Avalonia's ToolTipService ignores a synthetic pointer, so a toolbar button's tooltip stays shut. 'x'/'y' are optional and relative to the target's own top-left; omit them and the point is the CARET for a code editor (position it first with interact set_property CaretOffset, which is how you hover a particular identifier) and the centre of anything else. 'dwellMs' (default 1500) is how long to watch — it must exceed the 400ms quick-info dwell plus the language server's round trip. The tip is TRANSIENT (a synthetic pointer never sets IsPointerOver, so it closes again shortly after opening), which is why this polls rather than looking once, and it is placed AT THE REAL POINTER — wherever it last crossed this window — and not under the target: the editor opens quick-info with PlacementMode.Pointer, which anchors to a position Avalonia tracks per window and a synthetic event never updates. Measured: with the mouse parked over the Toolbox, hovering the caret opened the tip beside the Toolbox; on a fresh window at screen (300,250) that no pointer had crossed, it opened at screen (0,15). So assert the reported text, never a snapshot. 'window' picks the top-level window the path is resolved against — \"auto\" (default, the frontmost) or \"ide\"; pass \"ide\" to reach the IDE while a program is running or paused.")]
    public async Task<InteractOutcome> HoverAsync(
        string target, double? x = null, double? y = null, int dwellMs = 1500, string? window = null,
        CancellationToken ct = default)
    {
        Control? hovered = null;

        var raised = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null) return new InteractOutcome(false, "pointer", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(active, target);
            if (control is null) return new InteractOutcome(false, "pointer", null, resolveError);

            hovered = control;
            return UiAutomationDriver.Hover(control, x, y);
        });

        if (!raised.Success || hovered is null) return raised;

        // POLLED, and measured to need it. A synthetic pointer never sets IsPointerOver, so Avalonia closes
        // the tip again shortly after the editor opens it: the tip is real, visible, and transient. Looking
        // once when the dwell expires reports "no tip" for a tip a human plainly sees on screen.
        //
        // Waited off the UI thread throughout: every tip this exists to surface comes from an async
        // continuation -- a 400ms dwell, then an await -- so holding the dispatcher would stop the very
        // thing being waited for and then report, accurately, that nothing happened.
        string? tip = null;
        var deadline = Environment.TickCount64 + Math.Clamp(dwellMs, 0, 10_000);
        while (tip is null && Environment.TickCount64 < deadline)
        {
            await Task.Delay(50, ct);
            tip = await Dispatcher.UIThread.InvokeAsync(() => DescribeOpenToolTip(hovered));
        }

        if (tip is not null)
            return raised with { Detail = raised.Detail + "; tip: " + tip };

        // Nothing OPENED. Say so plainly -- and then, separately, say what the control declares, because
        // the caller's real question is usually "has this button the right tooltip?" and that is answerable
        // from the attached property even when Avalonia will not show it. The two are reported under
        // different words on purpose: "tip:" means a popup was observed open, "declared tip:" means only
        // that the text is set. Collapsing them would turn a limitation into a passing assertion.
        var declared = await Dispatcher.UIThread.InvokeAsync(() => DescribeDeclaredToolTip(hovered));
        var unopened = raised.Detail + "; no tip opened within " + dwellMs + "ms";
        return raised with
        {
            Detail = declared is null
                ? unopened
                : unopened
                  + " (Avalonia's ToolTipService opens a declarative tip from IsPointerOver, which a"
                  + " synthetic pointer never sets); declared tip: " + declared,
        };
    }

    /// <summary>
    /// The tooltip text a control (or one of its ancestors or descendants) <i>declares</i>, open or not.
    /// </summary>
    /// <remarks>
    /// Deliberately says nothing about whether anything is on screen. It exists so a caller can still assert
    /// a toolbar button's tooltip <i>text</i> in the one case this tool cannot make a tip appear, and its
    /// result is reported under different wording from an observed-open tip so the two can never be
    /// mistaken for each other.
    /// </remarks>
    private static string? DescribeDeclaredToolTip(Control? from)
    {
        if (from is null) return null;

        for (var c = from; c is not null; c = c.Parent as Control)
            if (DeclaredOn(c) is { } fromAncestor) return fromAncestor;
        foreach (var c in from.GetVisualDescendants().OfType<Control>())
            if (DeclaredOn(c) is { } fromDescendant) return fromDescendant;
        return null;

        static string? DeclaredOn(Control c) =>
            ToolTip.GetTip(c)?.ToString() is { Length: > 0 } text ? text.Trim() : null;
    }

    /// <summary>
    /// The text of a tooltip currently OPEN on a control, one of its ancestors, or one of its
    /// descendants; null when none is open.
    /// </summary>
    /// <remarks>
    /// <b>Reads the attached property rather than looking for a ToolTip in the window's visual tree, and the
    /// difference is the whole reason this works.</b> On the desktop a tooltip is realised in its own popup
    /// <c>TopLevel</c>, so it is NOT a visual descendant of the window that owns it — the same parenting that
    /// makes a context menu invisible to <c>take_snapshot</c> (see docs/mcp-server-gaps.md). A headless test
    /// does not show this, because headless popups are in-window overlays and a descendant walk finds them.
    ///
    /// <para>
    /// Ancestors are searched because the tip is set on whichever control owns it: the code editor attaches
    /// quick-info to its <c>TextEditor</c>, which may be above the element a caller addressed.
    /// </para>
    /// </remarks>
    private static string? DescribeOpenToolTip(Control? from)
    {
        if (from is null) return null;

        // Ancestors AND descendants. The tip is set on whichever control owns it, which is rarely the one
        // a caller addressed: the code editor attaches quick-info to its TextEditor, several levels BELOW
        // the CodeEditorView a path resolves to. Walking only upwards finds nothing and reports no tip for
        // one that is plainly open.
        for (var c = from; c is not null; c = c.Parent as Control)
            if (TipOn(c) is { } fromAncestor) return fromAncestor;

        foreach (var c in from.GetVisualDescendants().OfType<Control>())
            if (TipOn(c) is { } fromDescendant) return fromDescendant;

        return null;
    }

    private static string? TipOn(Control c) =>
        ToolTip.GetIsOpen(c) && ToolTip.GetTip(c) is { } tip && tip.ToString() is { Length: > 0 } text
            ? text.Trim()
            : null;
    [McpServerTool(Name = "press_key")]
    [Description("Presses a key on the control at 'target' (a path from dump_visual_tree) by raising real KeyDown/KeyUp events — for navigation and commands that type_text doesn't cover: Enter, Tab, Back(space), Delete, Escape, arrow keys, etc., optionally with modifiers. 'key' is an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, ...). 'modifiers' is an optional combo like 'Ctrl', 'Ctrl+Shift', 'Alt'. Resolves to the DEEPEST input surface under 'target' — for the code editor that is AvaloniaEdit's TextArea, where its key handling lives — because a routed event reaches only the element it is raised on and its ancestors, never anything below. Falls back to the first focusable descendant, and FAILS rather than reporting success when nothing under 'target' can take keyboard focus. The reply names the control that actually received the key when it is not the one addressed. 'window' picks the top-level window the path is resolved against — \"auto\" (default, the frontmost) or \"ide\"; pass \"ide\" to reach the IDE while a program is running or paused.")]
    public async Task<InteractOutcome> PressKeyAsync(
        string target, string key, string? modifiers = null, string? window = null,
        CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (active, _, error) = ResolveActiveWindow(window);
            if (active is null)
                return new InteractOutcome(false, "keyboard", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(active, target);
            if (control is null)
                return new InteractOutcome(false, "keyboard", null, resolveError);

            return UiAutomationDriver.PressKey(control, key, modifiers);
        });
    }

    // Active window for the automation tools: prefer a visible modal dialog over the main window
    // (mirrors take_snapshot) so dialogs are addressable with no extra parameter.
    private static (Window? window, string? label, string? error) ResolveActiveWindow(string? scope = null)
    {
        var lifetime = Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = lifetime?.MainWindow;
        if (mainWindow is null)
            return (null, null, "No main window");

        var (window, error) = HexIDE.IDE.ForegroundWindow.Pick(scope, mainWindow, lifetime!.Windows);
        if (window is null)
            return (null, null, error);

        var label = window != mainWindow ? DescribeWindow(window) : "MainWindow";
        return (window, label, null);
    }

    /// <summary>
    /// A name the caller can act on. Title first, but it cannot be relied on: a VB6 `MsgBox` reaches the
    /// runtime with an empty caption (issue #131), and a blank label reads as "no dialog is open" — the
    /// exact confusion #61 existed to remove. Fall back to what the window is showing.
    /// </summary>
    private static string DescribeWindow(Avalonia.Controls.Window window)
    {
        if (!string.IsNullOrWhiteSpace(window.Title))
            return window.Title;
        return window.Content?.GetType().Name is { Length: > 0 } content ? content : "Dialog";
    }

    [McpServerTool(Name = "get_document_tabs")]
    [Description("Returns EVERY tab in the document region with its title, type and whether it is the active one. 'type' is 'designer' for a form or UserControl designer, 'code' for a source editor, and 'tool' for a document that is not an editor at all — the Object Browser, the language-server connection list, the protocol inspector. Those three are real tabs in the same strip and used to be missing from this answer, which made an automation client believe a tab it could see on screen did not exist.")]
    public async Task<DocumentTabsResult> GetDocumentTabsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // The DOCK's tabs, not the editor service's list. The service tracks the editors it was asked
            // to open; the shell adds other documents to the same dock directly, and reporting only the
            // first set answers a different question from the one asked.
            var active = ctx.DocumentDockService.ActiveTab;
            var tabs = ctx.DocumentDockService.AllTabs
                .Select(d => new DocumentTabInfo(
                    d.Title ?? "",
                    d switch
                    {
                        HexIDE.VisualDesigner.FormEditViewModel => "designer",
                        BaseEditorWindowViewModel => "code",
                        _ => "tool",
                    },
                    ReferenceEquals(d, active)))
                .ToArray();
            return new DocumentTabsResult(tabs);
        });
    }

    [McpServerTool(Name = "activate_document_tab")]
    [Description("Brings the named document tab to the front, whatever kind it is. title must match a Title returned by get_document_tabs (case-insensitive). If nothing matches, the error names every tab that IS open, so a near-miss does not need a second call to diagnose.")]
    public async Task<MutateResult> ActivateDocumentTabAsync(string title, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var found = ctx.DocumentDockService.TryActivateAny(
                d => string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase));
            return found ? new MutateResult(true, null) : new MutateResult(false, NoSuchTab(title));
        });
    }

    /// <summary>Says which tabs there are, rather than only that this one is not among them.</summary>
    /// <remarks>
    /// A bare "no tab called X" leaves a caller unable to tell a typo from a tab that never opened, and
    /// costs a second call to find out. The tabs are already in hand.
    /// </remarks>
    private string NoSuchTab(string title)
    {
        var open = ctx.DocumentDockService.AllTabs
            .Select(d => d.Title)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToArray();

        return open.Length == 0
            ? $"No document tab with title '{title}'. Nothing is open in the document region."
            : $"No document tab with title '{title}'. Open tabs: {string.Join(", ", open)}.";
    }

    [McpServerTool(Name = "close_document_tab")]
    [Description("Closes the named document tab, whatever kind it is. title must match a Title returned by get_document_tabs (case-insensitive). If nothing matches, the error names every tab that IS open.")]
    public async Task<MutateResult> CloseDocumentTabAsync(string title, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var closed = ctx.DocumentDockService.TryCloseAny(
                d => string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase));
            return closed ? new MutateResult(true, null) : new MutateResult(false, NoSuchTab(title));
        });
    }

    [McpServerTool(Name = "invoke_menu_item")]
    [Description("Invokes a menu item by slash-separated path, e.g. 'Tools/Hello from TestAddin' or 'Add-Ins/TestAddin/Do Something'. Headers are matched case-insensitively with leading underscores (access-key prefixes) stripped. Works reliably for add-in contributed items (DelegateCommand). Built-in items that use routed commands may not execute correctly via this tool. Returns an error if the path cannot be resolved or the item has no executable command.")]
    public async Task<MutateResult> InvokeMenuItemAsync(string path, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lifetime = Avalonia.Application.Current!.ApplicationLifetime
                as IClassicDesktopStyleApplicationLifetime;
            var window = lifetime?.MainWindow;
            if (window is null)
                return new MutateResult(false, "No main window");

            var menu = window.FindDescendantOfType<Menu>();
            if (menu is null)
                return new MutateResult(false, "No menu bar found in main window");

            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return new MutateResult(false, "Path is empty");

            IEnumerable<object?> currentItems = menu.Items;
            MenuItem? found = null;

            foreach (var segment in segments)
            {
                found = currentItems
                    .OfType<MenuItem>()
                    .FirstOrDefault(mi => MenuHeaderMatches(mi.Header, segment));

                if (found is null)
                    return new MutateResult(false, $"Menu item '{segment}' not found");

                currentItems = found.Items;
            }

            if (found!.Command is null)
                return new MutateResult(false, $"'{path}' is a submenu or has no command");

            if (!found.Command.CanExecute(found.CommandParameter))
                return new MutateResult(false, $"'{path}' command cannot execute (canExecute returned false)");

            found.Command.Execute(found.CommandParameter);
            return new MutateResult(true, null);
        });
    }

    private static bool MenuHeaderMatches(object? header, string segment)
    {
        var text = header?.ToString() ?? string.Empty;
        if (text.StartsWith('_')) text = text[1..];
        return string.Equals(text, segment, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDocumentUri(
        HexIDE.Runtime.ProjectElements.ProjectDefinition project, string name)
    {
        if (project.Forms.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"vb6://form/{name}";
        if (project.Modules.Any(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
            return $"vb6://module/{name}";
        return null;
    }

    private CodeEditorViewModel? FindEditor(string name) =>
        ctx.DocumentDockService.OpenDocuments
            .OfType<CodeEditorViewModel>()
            .FirstOrDefault(e =>
                string.Equals(e.FormDefinition?.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.ModuleDefinition?.Name, name, StringComparison.OrdinalIgnoreCase));

    private static FormDefinition? FindFormDefinition(
        HexIDE.Runtime.ProjectElements.ProjectDefinition project, string name)
    {
        var form = project.Forms.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        if (form is not null) return form;

        return project.Modules
            .FirstOrDefault(m =>
                m.FormPart is not null &&
                string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase))
            ?.FormPart;
    }

    // ── The recorded language-server conversation ────────────────────────────
    //
    // Four thin wrappers. Everything they do lives in HexIDE.Core's CaptureQueries, for two reasons that
    // are both requirements rather than taste: the automation server is compiled out of distributed builds
    // and the capture is not, so logic the capture needs in order to be readable belongs on the other side
    // of that line; and nothing in the tree references this executable, so anything implemented in a tool
    // body cannot be reached by any test.

    [McpServerTool(Name = "list_lsp_messages")]
    [Description("Lists recorded language-server message envelopes — time, direction, method, id, size, outcome and latency — with no message content. Use it to answer 'was this request even sent', 'what came back', and 'how long did it take', which the editor cannot tell you and a diagnostics list cannot distinguish. Envelopes are recorded for every connection always, whether or not capture is armed, so this works without arming anything, and every argument is optional — call it with none to see the whole timeline.\n\nIF THE ANSWER IS EMPTY, READ 'note': it says which of the possible reasons applies. The commonest is that no server has started, because a language server starts on the first document of a language it claims — so open a file first.\n\n'direction' is Sent, Received, or Local for the entries that are not messages at all. 'kind' is Request, Response, ErrorResponse or Notification for wire traffic, plus Lifecycle (a process starting, stopping, its standard error and exit code), NeverSent (a request this client declined to make, and why) and Unconsumed (a capability the server advertised that this client does not use) — those three are the ones a server author most often wants and they exist nowhere else. 'detail' carries their text.\n\nSequence numbers have GAPS, and they are not dropped frames: a reply completes its request's existing envelope rather than adding one, so the response's own sequence is consumed. 'framesDropped' is the only thing that reports real loss.\n\nFilters: connection_id for one server, method for an exact method name, failures_only for error responses and requests that failed, were cancelled or never came back, after_sequence to poll for only what is new since a sequence you have already seen. The newest matches are returned when there are more than limit, and 'truncated' plus 'matched' say what was left out. For message content, list first and then get_lsp_message — a conversation runs to megabytes per minute of typing, so nothing returns bodies in bulk.")]
    public async Task<LspMessagesResult> ListLspMessagesAsync(
        string? connectionId = null,
        string? method = null,
        bool failuresOnly = false,
        long? afterSequence = null,
        int? limit = null,
        CancellationToken ct = default)
    {
        var page = await CaptureQueries.ListAsync(ctx.Capture, new EnvelopeFilter(
            ConnectionId: string.IsNullOrWhiteSpace(connectionId) ? null : connectionId,
            Method: string.IsNullOrWhiteSpace(method) ? null : method,
            FailuresOnly: failuresOnly,
            AfterSequence: afterSequence,
            Limit: limit ?? 200));

        var rows = page.Entries.Select(e => new LspMessageRow(
            e.Sequence,
            e.ConnectionId,
            e.Timestamp.ToString("o"),
            e.Direction.ToString(),
            e.Kind.ToString(),
            e.Method,
            e.CorrelationId,
            e.SizeBytes,
            e.Outcome == ConversationOutcome.None ? null : e.Outcome.ToString(),
            e.Elapsed?.TotalMilliseconds,
            e.Detail,
            HasBody: ctx.Capture.Body(e.ConnectionId, e.Sequence) is not null)).ToArray();

        return new LspMessagesResult(
            rows, page.Matched, page.Truncated, ctx.Capture.QueueDropped, page.Note);
    }

    [McpServerTool(Name = "get_lsp_message")]
    [Description("Returns one recorded message's body by its sequence number, as the bytes that crossed the wire. Get the sequence from list_lsp_messages; rows there carry 'hasBody', so you can tell before asking.\n\nSEQUENCE NUMBERS ARE UNIQUE ACROSS THE WHOLE RECORD, not per connection — if you name the wrong connection for a real sequence, the reply says which connection it actually belongs to.\n\nBodies are kept only for a connection that has been armed (see arm_lsp_capture, or launch the IDE with --capture-lsp), except for the opening of every connection, which is always kept because a handshake cannot be captured after the fact. When there is no body, 'unavailable' explains which of the possible reasons applies as far as the record knows — including when the record cannot tell them apart, which it says rather than guessing.\n\nA body longer than the per-frame limit comes back as a head and a tail with the true length stated. It is NOT valid JSON in that case, and pretending otherwise would be a lie about what was sent. Not redacted: this is the developer's own machine, and export is where redaction belongs.")]
    public async Task<LspMessageBodyResult> GetLspMessageAsync(
        string connectionId, long sequence, CancellationToken ct)
    {
        if (await CaptureQueries.FetchAsync(ctx.Capture, connectionId, sequence) is not { } view)
        {
            return new LspMessageBodyResult(
                sequence, connectionId, null, 0, null, null, false,
                await CaptureQueries.ExplainMissingBodyAsync(ctx.Capture, connectionId, sequence));
        }

        return new LspMessageBodyResult(
            view.Sequence, view.ConnectionId, view.Method, view.TrueLength,
            view.Head, view.Tail, view.Tail is not null, null);
    }

    [McpServerTool(Name = "arm_lsp_capture")]
    [Description("Arms or disarms retention of message BODIES for one language-server connection, or for every connection when connection_id is omitted. Envelopes are always recorded whatever this says; arming decides only whether CONTENT is kept, which is the entire privacy boundary — so if you only need to know what was sent and what came back, you do not need this at all.\n\nConnection ids come from list_lsp_messages or get_lsp_capture_state; the reply lists every connection it knows with its new state, so a misspelled id is visible rather than silent.\n\nArming is session-scoped and is lost when the IDE restarts — launch with --capture-lsp to arm before the first connection is made, which is what the rebuild-and-relaunch loop needs. It cannot reach a handshake that has already happened, so a server already running keeps only what follows.")]
    public async Task<LspCaptureStateResult> ArmLspCaptureAsync(
        string? connectionId = null, bool armed = true, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            foreach (var id in ctx.Capture.ConnectionIds) ctx.Capture.Arm(id, armed);
        }
        else
        {
            ctx.Capture.Arm(connectionId, armed);
        }

        return await CaptureStateAsync();
    }

    [McpServerTool(Name = "get_lsp_capture_state")]
    [Description("Reports what is being recorded: every known language-server connection, whether its message bodies are being kept, how many envelopes it holds, and what it has had to discard. Read-only — ask this rather than arming something to find out what is armed. 'armsEveryConnection' is true when the IDE was launched with --capture-lsp, which arms connections as they appear rather than waiting to be asked.")]
    public async Task<LspCaptureStateResult> GetLspCaptureStateAsync(CancellationToken ct = default) =>
        await CaptureStateAsync();

    [McpServerTool(Name = "clear_lsp_capture")]
    [Description("Discards the recorded conversation for one connection, or for every connection when connection_id is omitted, and reports how many envelopes went. Whatever was armed stays armed: this throws away what has been watched, not the decision to watch. Use it between iterations so the next thing you exercise is the only thing in the record. It does not hand a connection a fresh opening allowance — a server that has been running for an hour is not new because its record is empty.")]
    public async Task<LspCaptureClearedResult> ClearLspCaptureAsync(
        string? connectionId = null, CancellationToken ct = default)
    {
        var discarded = await ctx.Capture.ClearAsync(
            string.IsNullOrWhiteSpace(connectionId) ? null : connectionId);

        return new LspCaptureClearedResult(discarded, await CaptureStateAsync());
    }


    [McpServerTool(Name = "answer_next_file_dialog")]
    [Description("Pre-answers the next file dialog the IDE opens, so a Save As / Open / Export flow can be driven end to end. A file picker is a NATIVE operating-system dialog: it is outside the control tree, dump_visual_tree cannot see it, interact cannot address it, and a modal one stops this server answering at all — so without this every feature ending in a file dialog needed a person to click.\n\nPass the absolute path the dialog should return. Pass nothing (or an empty path) to answer as CANCELLED, which is a distinct path through most of these flows and the one least likely to have been exercised by hand. The answer is consumed by ONE dialog and is not sticky: arm it immediately before the action that opens the picker, or it will be spent by whichever dialog opens first.\n\nThis does not simulate the dialog. Everything downstream of the picker — the writing, the naming, the refusals — is the same code a real click reaches; only the part a person performs is skipped. Nothing is created: name a path in a directory that exists, or the flow under test will report the failure it would really report.")]
    public Task<FileDialogAnsweredResult> AnswerNextFileDialogAsync(
        string? path = null, CancellationToken ct = default)
    {
        HexIDE.IDE.ScriptedFileDialogs.AnswerNextWith(path);

        var cancels = string.IsNullOrWhiteSpace(path);
        return Task.FromResult(new FileDialogAnsweredResult(
            true,
            cancels ? null : path,
            HexIDE.IDE.ScriptedFileDialogs.Pending,
            cancels
                ? "The next file dialog will answer as cancelled."
                : $"The next file dialog will return '{path}'. Nothing has been created there."));
    }

    [McpServerTool(Name = "clear_file_dialog_answers")]
    [Description("Discards every armed file-dialog answer. Use it after a step that did not open the dialog it was expected to, so a leftover answer cannot be spent by an unrelated save later on. Reports how many were discarded, which is also how you find out that a step you thought opened a picker did not.")]
    public Task<FileDialogAnsweredResult> ClearFileDialogAnswersAsync(CancellationToken ct = default)
    {
        var discarded = HexIDE.IDE.ScriptedFileDialogs.Clear();

        return Task.FromResult(new FileDialogAnsweredResult(
            true, null, 0,
            discarded == 0
                ? "Nothing was armed."
                : $"Discarded {discarded} armed answer(s); the next file dialog will be shown for real."));
    }

    [McpServerTool(Name = "export_lsp_conversation")]
    [Description("Writes the recorded conversation to two files and returns their paths: one JSON-RPC message per line, plus a manifest carrying the envelope table with timings, the limits the record was taken under, and everything it had to discard. This is the form to attach to an issue or send to whoever wrote the server.\n\nALWAYS PSEUDONYMISED. Paths, workspace folders and server launch configuration are replaced with stable, session-scoped fake names — consistently, so two spellings of one path stay distinguishable and a normalisation bug survives the redaction. Use get_lsp_message instead if you need the real bytes for your own inspection on this machine; that one is raw and is not for sharing. The manifest states which of the two it is, because an export that does not say is worse than one that never redacted.\n\nEvery envelope gets a line, including those whose body was never kept, because a file that omitted them would read exactly like a shorter conversation. A truncated body is written as head, tail and true length rather than as something that parses — pretending otherwise would misdescribe what was sent. Omit connection_id for the whole interleaved timeline.")]
    public async Task<LspExportResult> ExportLspConversationAsync(
        string? connectionId = null, CancellationToken ct = default)
    {
        // ALWAYS redacting, and the opt-out is deliberately not a parameter here. The design records that
        // a non-pseudonymising mode should exist and that its surface is an open question needing "a big
        // red flag" wherever it lands; adding a boolean to this tool would settle that question quietly,
        // by accident, in the one place with no way to show a flag. The raw path already exists for
        // inspection — get_lsp_message — so nothing is unreachable, only unshareable.
        var redactor = new ConversationRedactor(ctx.Pseudonyms);

        // No `unobservable` map: what a transport cannot show is already IN the record, written once per
        // connection as a Note envelope when it connects, so it travels in the envelope table like
        // everything else rather than being reassembled here from a second source.
        var export = await ConversationExporter.ExportAsync(
            ctx.Capture, redactor, string.IsNullOrWhiteSpace(connectionId) ? null : connectionId);

        var stem = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide_lsp_conversation");
        var messages = stem + ".jsonl";
        var manifest = stem + ".manifest.json";

        await System.IO.File.WriteAllTextAsync(messages, export.Messages, ct);
        await System.IO.File.WriteAllTextAsync(manifest, export.Manifest, ct);

        return new LspExportResult(
            messages, manifest, export.Lines, export.BodiesPresent, export.BodiesAbsent,
            export.BodiesReserialized, ctx.Pseudonyms.Assigned,
            export.Lines == 0
                ? "Nothing was recorded, so both files describe an empty conversation. Language servers "
                + "start on the first document of a language they claim — open a file and export again."
                : null);
    }

    /// <summary>What is being recorded right now, returned by both mutating capture tools.</summary>
    /// <remarks>
    /// Returned rather than left to a follow-up call because arming is invisible: a tool that answered
    /// only "done" would leave an agent unable to tell an armed connection from one whose id it had
    /// misspelled.
    /// </remarks>
    private async Task<LspCaptureStateResult> CaptureStateAsync()
    {
        var page = await CaptureQueries.ListAsync(ctx.Capture, new EnvelopeFilter(Limit: int.MaxValue));

        // The LOG's connections, not the record's. Deriving them from the envelopes present gives the
        // same answer right up until somebody clears, and then reports no connections at all while they
        // are alive and armed — measured, on the first real use of these tools.
        var connections = ctx.Capture.ConnectionIds
            .Order(StringComparer.Ordinal)
            .Select(id =>
            {
                var (envelopes, bodies, refused) = ctx.Capture.Losses(id);
                return new LspCaptureConnectionState(
                    id, ctx.Capture.IsArmed(id), page.Entries.Count(e => e.ConnectionId == id),
                    envelopes, bodies, refused);
            })
            .ToArray();

        return new LspCaptureStateResult(ctx.Capture.ArmsEveryConnection, connections);
    }
}

internal record LspExportResult(
    string MessagesPath,
    string ManifestPath,
    int Lines,
    int BodiesPresent,
    // Lines standing in for an envelope whose body was never kept or has been evicted. Counted rather
    // than omitted: a file that dropped them would read exactly like a shorter conversation.
    int BodiesAbsent,
    // Whole bodies that had to be compacted onto one line, so a reader knows those are not byte-exact.
    int BodiesReserialized,
    // Distinct values given a pseudonym. The honest way to size the disclosure: how many real names the
    // reader is NOT getting.
    long PseudonymsAssigned,
    string? Note);

internal record LspMessagesResult(
    LspMessageRow[] Messages,
    // How many matched in total, so a limited answer is visibly limited rather than quietly short.
    int Matched,
    bool Truncated,
    // Frames the capture could not keep up with. Never silent: a record that dropped some reads exactly
    // like one that had fewer to drop.
    long FramesDropped,
    // Why an empty answer is empty. A bare zero cannot be told apart from "nothing happened", "nothing
    // was configured" and "the tool is broken" — which is the ambiguity this whole capability exists to
    // remove, and the tool that removes it is the worst place to reintroduce it.
    string? Note);

internal record LspMessageRow(
    long Sequence,
    string ConnectionId,
    string At,
    string Direction,
    string Kind,
    string? Method,
    string? Id,
    int SizeBytes,
    string? Outcome,
    double? ElapsedMs,
    // A short note for the entries that are not messages: an exit code, a line of standard error, the
    // capability a never-sent entry was refused for.
    string? Detail,
    bool HasBody);

internal record LspMessageBodyResult(
    long Sequence,
    string ConnectionId,
    string? Method,
    int TrueLength,
    string? Head,
    string? Tail,
    bool Truncated,
    // Why there is nothing, in the terms a reader needs. Null when there is something.
    string? Unavailable);

internal record LspCaptureStateResult(
    bool ArmsEveryConnection,
    LspCaptureConnectionState[] Connections);

internal record LspCaptureConnectionState(
    string ConnectionId,
    bool Armed,
    int Envelopes,
    long EnvelopesDropped,
    long BodiesEvicted,
    long BodiesRefused);

internal record LspCaptureClearedResult(
    int EnvelopesDiscarded,
    LspCaptureStateResult State);

internal record FileContentResult(string? Content, bool HasUnsavedChanges, string? Error);

internal record FormControlsResult(string? Error, ControlInfo[] Controls);

internal record ControlInfo(
    string Name,
    string Type,
    double Left,
    double Top,
    double Width,
    double Height,
    bool Visible,
    bool Enabled,
    string? Caption,
    string? Text,
    // The control this one sits inside, or null when it is on the form itself. Left/Top are measured from
    // this container's client origin, exactly as the .frm records them, so the space is self-describing.
    string? Container);

internal record SnapshotResult(string? Path, string? Error, string? ActiveDialog);

internal record ProjectInfoResult(
    string? ProjectName,
    string? ProjectPath,
    string[] Forms,
    string[] Modules,
    // Carried files — a `RelatedDoc=` in the .vbp. Listed because they are openable and, since they are
    // the file types a configured language server exists to serve, they are exactly what needs driving
    // when verifying one.
    string[] RelatedDocuments);

internal record OpenEditorsResult(
    string[] OpenWindows,
    string? ActiveWindow);

internal record DiagnosticsResult(DiagnosticItem[] Diagnostics);

internal record DiagnosticItem(
    string Uri,
    string Message,
    string Severity,
    int Line,
    int Column);

/// <param name="Note">
/// Something the caller should know about a write that DID happen — not an error. A silent adjustment is
/// how a form lost its identity and reached a commit; saying so costs one line.
/// </param>
internal record MutateResult(bool Success, string? Error, string? Note = null);

/// <summary>
/// What <c>shutdown_ide</c> tore down on the way out. <see cref="Requested"/> is deliberately not
/// "succeeded": the reply has to be sent before the process exits, so no in-process result can honestly
/// claim it did. Poll <c>/health</c> for that.
/// </summary>
internal record ShutdownResult(bool Requested, bool ProjectStopped, int DialogsClosed, string Note);

internal record AddFileResult(bool Success, string? Path, string? Error);

internal record WindowStateResult(string State, int X, int Y, int Width, int Height);

internal record ToolWindowInfo(string Name, bool Visible);

internal record ToolWindowsResult(ToolWindowInfo[] Tools);

internal record DocumentTabsResult(DocumentTabInfo[] Tabs);

internal record DocumentTabInfo(string Title, string Type, bool IsActive);

internal record UndoStateResult(
    string ActiveEditorKind,
    bool CanUndo,
    bool CanRedo,
    string? UndoDescription,
    string? RedoDescription);

/// <param name="Form">
/// Carried between the spawn and the save and blanked before returning -- internal plumbing, never
/// serialized to a caller. The two steps run on the UI thread and the save is awaited, so the form has to
/// survive the hop between them.
/// </param>
internal record AddControlResult(bool Success, string? ControlName, string? Error,
    [property: System.Text.Json.Serialization.JsonIgnore] FormDefinition? Form = null);

internal record BookmarksResult(string? Uri, int[] Lines, string? Error);

internal record BreakpointsResult(string? Uri, int[] Lines, string? Error);

internal record DebugStateResult(bool Running, string State, string? StopReason, string? Module, int? Line);

internal record LocalsResult(bool Success, string? Error, string? Context, LocalsRow[]? Locals);

internal record LocalsRow(string Expression, string Value, string Type, bool HasChildren, LocalsRow[]? Children);

internal record EvaluateResult(bool Success, string? Error, string? Result);

internal record CallStackResult(bool Success, string? Error, CallStackFrameRow[] Frames);

internal record CallStackFrameRow(string Proc, string Module, int Line);

internal record WatchesResult(bool Success, string? Error, WatchRow[] Watches);

internal record WatchRow(string Expression, string WatchType, string Context, string Value, string Type, bool Ok);

internal record ToolboxItem(string Name, string? VBTypeName);

internal record ToolboxItemsResult(ToolboxItem[] Items);

internal record TemplateInfo(string Name, bool Supported, string Source);

internal record NewProjectTemplatesResult(TemplateInfo[] Templates);

internal record RuntimeErrorResult(bool Raised, string? Message, string? At, int Sequence);

/// <param name="Path">What the next dialog will return, or null when it will answer as cancelled.</param>
/// <param name="Pending">How many answers are still armed, so a stale one is visible rather than latent.</param>
internal record FileDialogAnsweredResult(bool Success, string? Path, int Pending, string Note);

internal record VisualTreeResult(string? Error, string? Window, UiNode? Root);

internal record InspectResult(string? Error, string? Window, UiNodeDetail? Element);
