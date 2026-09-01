using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
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
    [Description("Returns the currently loaded VB6 project name, path, and list of forms and modules.")]
    public async Task<ProjectInfoResult> GetProjectInfoAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return new ProjectInfoResult(null, null, [], []);

            return new ProjectInfoResult(
                project.Name,
                project.AbsolutePath,
                project.Forms.Select(f => f.Name).ToArray(),
                project.Modules.Select(m => m.Name).ToArray());
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
    [Description("Replaces the VB6 source code of a named form or module and saves to disk. Use get_project_info to list available names.")]
    public async Task<MutateResult> SetFileContentAsync(string name, string content, CancellationToken ct)
    {
        var (form, module, error) = await Dispatcher.UIThread.InvokeAsync<(FormDefinition?, ModuleDefinition?, string?)>(() =>
        {
            var project = ctx.ProjectManager.StartupProject;
            if (project is null)
                return (null, null, "No project loaded");

            var form = project.Forms.FirstOrDefault(f =>
                string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            if (form is not null)
            {
                var editor = FindEditor(name);
                if (editor is not null)
                    editor.Document.Text = content;
                else
                    form.UpdateCode(content);
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
            if (form is not null)
                await ctx.ProjectService.SaveForm(form, false);
            else if (module is not null)
                await ctx.ProjectService.SaveModule(module, false);
            return new MutateResult(true, null);
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
            if (ownerModule is not null)
            {
                if (ownerModule.AbsolutePath is null)
                    return new MutateResult(false, "UserControl has no saved path — save the project via File > Save first");
                await ctx.ProjectService.SaveModule(ownerModule, false);
            }
            else
            {
                if (form!.AbsolutePath is null)
                    return new MutateResult(false, "Form has no saved path — save the project via File > Save first");
                await ctx.ProjectService.SaveForm(form, false);
            }
            return new MutateResult(true, null);
        }
        catch (Exception ex)
        {
            return new MutateResult(false, ex.Message);
        }
    }

    [McpServerTool(Name = "open_file")]
    [Description("Opens a form or module by name in the IDE code editor. Use get_project_info to list available names.")]
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

            return new MutateResult(false, $"No form or module named '{name}' found in the project");
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
            ctx.ProjectRunnerService.RunStartupProject();
            return new MutateResult(true, null);
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
    [Description("Places a control of the given type on a named form's designer canvas at the given position and size. The form must be open in the visual designer (call view_designer first if needed). Returns the auto-generated control name (e.g. 'Command1').")]
    public async Task<AddControlResult> AddControlAsync(
        string formName, string type,
        double x, double y, double width, double height,
        CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
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
            return new AddControlResult(true, designer.SelectedComponent?.Name, null);
        });
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
    [Description("Captures the current HexIDE window as a PNG and returns the file path so the caller can read the image. If a modal dialog is open it is captured in preference to the main window (its title is reported in 'active_dialog'); otherwise the main window is captured.")]
    public async Task<SnapshotResult> TakeSnapshotAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var lifetime = Avalonia.Application.Current!.ApplicationLifetime as
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
            var mainWindow = lifetime?.MainWindow;
            if (mainWindow is null)
                return new SnapshotResult(null, "No main window", null);

            // A modal dialog is a separate top-level window; capture it (so the dialog UI is
            // visible) in preference to whatever is underneath — which may be a running program's
            // form rather than the main window.
            var window = HexIDE.IDE.ForegroundWindow.Pick(mainWindow, lifetime!.Windows);
            var activeDialog = window != mainWindow ? DescribeWindow(window) : null;

            // A dropped-down menu, a combo's list, a flyout: each is realised in its own top-level root,
            // so rendering the window alone gives a menu bar with no menu. The composer draws them in
            // their real positions — gap 12 in docs/mcp-server-gaps.md.
            using var bitmap = HexIDE.Automation.SnapshotComposer.Capture(window);
            if (bitmap is null)
                return new SnapshotResult(null, "Window has no size", activeDialog);

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide_snapshot.png");
            bitmap.Save(path);

            return new SnapshotResult(path, null, activeDialog);
        });
    }

    [McpServerTool(Name = "dump_visual_tree")]
    [Description("Walks the live control tree of the active window (a visible modal dialog is preferred over the main window) and returns a structured node tree for discovering and addressing controls. Uses the UIA 'control view': structural layout wrappers (Panels, Borders, ContentPresenters, dock plumbing) are collapsed away, so the tree is shallow and paths are short. Each node carries its addressing 'path' (feed it back as a target), automation ControlType, Name, AutomationId, ClassName, the DataContext ViewModel type, supported interaction providers (invoke/selection/selectionItem/value/toggle/expandCollapse/...), and enabled/offscreen flags. Use this to find what is on screen before inspect_element or interact. Params: root (optional path to scope to a subtree; null = whole window), maxDepth (default 20, counted in meaningful/control-view levels), interactiveOnly (default true — keeps only nodes that are interactive or have an interactive descendant). For deeply nested or large areas, pass a 'root' to scope the dump.")]
    public async Task<VisualTreeResult> DumpVisualTreeAsync(
        string? root = null, int maxDepth = 20, bool interactiveOnly = true, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (window, label, error) = ResolveActiveWindow();
            if (window is null)
                return new VisualTreeResult(error, null, null);

            Control start = window;
            var basePath = "Window";
            if (root is not null)
            {
                var (resolved, resolveError) = UiAutomationDriver.Resolve(window, root);
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
    [Description("Returns a deep inspection of a single control addressed by 'target' (a path from dump_visual_tree): identity, supported interaction providers, bounding rectangle, current selection/value/toggle state, and the DataContext ViewModel's public command and property members (the surface the reflection-based interact actions target). Use before interact to confirm an element supports the action you intend, or — for a control with no provider — to discover the VM members the reflection fallback can reach.")]
    public async Task<InspectResult> InspectElementAsync(string target, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (window, label, error) = ResolveActiveWindow();
            if (window is null)
                return new InspectResult(error, null, null);

            var (control, resolveError) = UiAutomationDriver.Resolve(window, target);
            if (control is null)
                return new InspectResult(resolveError, label, null);

            return new InspectResult(null, label, UiAutomationDriver.Inspect(control, target));
        });
    }

    [McpServerTool(Name = "interact")]
    [Description("Drives a live control addressed by 'target' (a path from dump_visual_tree) through its UI Automation provider — one polymorphic verb instead of a tool per interaction. Provider actions: invoke (click a Button / menu item), select (pick a ComboBox/ListBox item), set_value (set a TextBox's text), toggle (flip a CheckBox), expand / collapse (open/close a dropdown, tree node, expander). Reflection fallback (for controls with no provider — see inspect_element's dataContextMembers): invoke_command (value = a command name; executes that ICommand on the target's DataContext after a CanExecute check) and set_property (value = \"PropertyName=NewValue\"; sets that VM property, coercing to its type). 'value': required for set_value (the text) and the reflection actions; for select, the item text to match (omit if 'target' already points at the item). A missing provider fails with \"element does not support '<action>'\" — there is NO implicit fallback to reflection; choose invoke_command/set_property explicitly. Virtualized dropdown items aren't addressable until realized — 'expand' first, then dump_visual_tree(root=combo), then 'select'. Actions are real and unguarded (the server is DEBUG-only). Use dump_visual_tree/inspect_element first to find the target and confirm what it supports.")]
    public async Task<InteractOutcome> InteractAsync(string target, string action, string? value = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (window, _, error) = ResolveActiveWindow();
            if (window is null)
                return new InteractOutcome(false, "peer", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(window, target);
            if (control is null)
                return new InteractOutcome(false, "peer", null, resolveError);

            return UiAutomationDriver.Interact(control, action, value);
        });
    }

    [McpServerTool(Name = "type_text")]
    [Description("Types text into the control at 'target' (a path from dump_visual_tree) by inserting at the caret via the control's own API — works on the code editor (AvaloniaEdit), which has no value provider for 'interact set_value'. If 'target' isn't itself a text surface, the nearest descendant editor/textbox is used (the AvaloniaEdit editor is preferred over incidental textboxes). Multi-line text is inserted verbatim (include \\n for new lines); exact, reliable, and not altered by live auto-indent/IntelliSense. For a typing cadence, call this once per line. Use press_key for Enter/Tab/commands.")]
    public async Task<InteractOutcome> TypeTextAsync(string target, string text, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (window, _, error) = ResolveActiveWindow();
            if (window is null)
                return new InteractOutcome(false, "keyboard", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(window, target);
            if (control is null)
                return new InteractOutcome(false, "keyboard", null, resolveError);

            return UiAutomationDriver.TypeText(control, text);
        });
    }

    [McpServerTool(Name = "press_key")]
    [Description("Presses a key on the control at 'target' (a path from dump_visual_tree) by raising real KeyDown/KeyUp events — for navigation and commands that type_text doesn't cover: Enter, Tab, Back(space), Delete, Escape, arrow keys, etc., optionally with modifiers. 'key' is an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, ...). 'modifiers' is an optional combo like 'Ctrl', 'Ctrl+Shift', 'Alt'. Targets the nearest text surface if the resolved control wraps one.")]
    public async Task<InteractOutcome> PressKeyAsync(string target, string key, string? modifiers = null, CancellationToken ct = default)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var (window, _, error) = ResolveActiveWindow();
            if (window is null)
                return new InteractOutcome(false, "keyboard", null, error);

            var (control, resolveError) = UiAutomationDriver.Resolve(window, target);
            if (control is null)
                return new InteractOutcome(false, "keyboard", null, resolveError);

            return UiAutomationDriver.PressKey(control, key, modifiers);
        });
    }

    // Active window for the automation tools: prefer a visible modal dialog over the main window
    // (mirrors take_snapshot) so dialogs are addressable with no extra parameter.
    private static (Window? window, string? label, string? error) ResolveActiveWindow()
    {
        var lifetime = Avalonia.Application.Current!.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var mainWindow = lifetime?.MainWindow;
        if (mainWindow is null)
            return (null, null, "No main window");

        var window = HexIDE.IDE.ForegroundWindow.Pick(mainWindow, lifetime!.Windows);
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
    [Description("Returns all open document tabs in the editor area with their title, type ('code' or 'designer'), and whether each is the active tab.")]
    public async Task<DocumentTabsResult> GetDocumentTabsAsync(CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var activeTitle = ctx.DocumentDockService.ActiveDocument?.Title;
            var tabs = ctx.DocumentDockService.OpenDocuments
                .Select(d => new DocumentTabInfo(
                    d.Title,
                    d is HexIDE.VisualDesigner.FormEditViewModel ? "designer" : "code",
                    d.Title == activeTitle))
                .ToArray();
            return new DocumentTabsResult(tabs);
        });
    }

    [McpServerTool(Name = "activate_document_tab")]
    [Description("Brings the named document tab to the front. title must match a Title returned by get_document_tabs (case-insensitive).")]
    public async Task<MutateResult> ActivateDocumentTabAsync(string title, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var found = ctx.DocumentDockService.TryActivate<BaseEditorWindowViewModel>(
                d => string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase));
            return found
                ? new MutateResult(true, null)
                : new MutateResult(false, $"No document tab with title '{title}'");
        });
    }

    [McpServerTool(Name = "close_document_tab")]
    [Description("Closes the named document tab. title must match a Title returned by get_document_tabs (case-insensitive).")]
    public async Task<MutateResult> CloseDocumentTabAsync(string title, CancellationToken ct)
    {
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var doc = ctx.DocumentDockService.OpenDocuments.FirstOrDefault(d =>
                string.Equals(d.Title, title, StringComparison.OrdinalIgnoreCase));
            if (doc is null)
                return new MutateResult(false, $"No document tab with title '{title}'");
            ctx.DocumentDockService.CloseDocument(doc);
            return new MutateResult(true, null);
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
}

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
    string[] Modules);

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

internal record MutateResult(bool Success, string? Error);

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

internal record AddControlResult(bool Success, string? ControlName, string? Error);

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

internal record VisualTreeResult(string? Error, string? Window, UiNode? Root);

internal record InspectResult(string? Error, string? Window, UiNodeDetail? Element);
