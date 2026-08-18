using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime;

public class VBLoader
{
    public static Control SpawnComponents(FormDefinition element,
        ModuleExecutionContext executionContext,
        ExecutionEnvironment environment)
    {
        var canvas = new Canvas()
        {
            ClipToBounds = true
        };
        var menu = new Menu();
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        // foreach (var topLevelMenu in TopLevelMenu)
        // {
        //     menu.Items.Add(topLevelMenu.Instance.BaseClass.Instantiate(topLevelMenu));
        // }

        // A VB6 control array is 2+ controls sharing a Name, or a single control carrying an explicit `Index` (a
        // 1-element array). Detect the array Names first so each element joins a group rather than overwriting the
        // shared scope slot (the old behaviour — the last same-named control won, silently dropping the rest).
        var componentsByName = new Dictionary<string, List<ComponentInstance>>(StringComparer.OrdinalIgnoreCase);
        foreach (var component in element.Components)
        {
            if (component.BaseClass is FormComponentClass)
                continue;
            if (component.GetPropertyOrDefault(VBProperties.NameProperty) is { } nm)
            {
                if (!componentsByName.TryGetValue(nm, out var list))
                    componentsByName[nm] = list = new List<ComponentInstance>();
                list.Add(component);
            }
        }
        var arrayGroups = new Dictionary<string, ControlArrayGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nm, list) in componentsByName)
            if (list.Count > 1 || list.Any(c => TryParseControlArrayIndex(c, out _)))
                arrayGroups[nm] = new ControlArrayGroup(nm);

        void OnPlaced(ComponentInstance component, Control control, Canvas host)
        {
            if (component.GetPropertyOrDefault(VBProperties.NameProperty) is not { } name)
                return;

            if (arrayGroups.TryGetValue(name, out var group))
            {
                // Stamp each element with its Index (parsed from the .frm; positional fallback for a malformed
                // array with no Index lines) so event dispatch can pass it to the shared handler. The component
                // + the canvas it actually landed on let a later runtime Load clone this element as a template
                // INTO THE SAME CONTAINER — a control array genuinely spans containers in VB6, so the host is
                // per element rather than one canvas for the whole group.
                var index = TryParseControlArrayIndex(component, out var parsed) ? parsed : group.Count;
                VBProps.SetIndex(control, index);
                group.AddDesignTimeElement(index, control, component, host);
            }
            else
            {
                // Form-module scope is flat in VB6: Text1 inside Frame1 is still Text1. So every control gets a
                // form-level variable regardless of how deeply it is nested.
                executionContext.AllocVariable(environment, name, new Vb6Value(control));
            }
        }

        foreach (var component in TopLevelComponents(element))
            PlaceComponentTree(component, canvas, canvas, OnPlaced);

        // Bind each control-array group to its shared Name once every element is in place.
        foreach (var (name, group) in arrayGroups)
            executionContext.AllocVariable(environment, name, new Vb6Value(group));

        return new DockPanel()
        {
            Children =
            {
                menu,
                canvas
            }
        };
    }

    // Parse a control's VB6 `Index` off its preserved raw property lines (Index isn't a modelled PropertyClass in
    // Phase 1 — see the control-arrays ROADMAP entry). Anchored on the exact name "Index" so "TabIndex"/"ListIndex"
    // never match.
    private static bool TryParseControlArrayIndex(ComponentInstance component, out int index)
    {
        foreach (var raw in component.UnknownRawPropertyLines)
        {
            var line = raw.Trim();
            if (line.Length <= 5 || !line.StartsWith("Index", StringComparison.OrdinalIgnoreCase)
                || char.IsLetterOrDigit(line[5]))   // reject "IndexFoo"; the next char must be '=' or whitespace
                continue;
            var eq = line.IndexOf('=');
            if (eq >= 0 && int.TryParse(line[(eq + 1)..].Trim(), out index))
                return true;
        }
        index = 0;
        return false;
    }

    /// <summary>
    /// The components a form places directly on its own canvas: everything the form itself contains, plus
    /// anything nothing has claimed.
    ///
    /// Two populations end up unclaimed, and both belong here. A form the designer built has no containment
    /// links recorded on it at all, so every one of its controls arrives with a null Container; and a menu
    /// is never contained by anything, yet it shares the component list with the controls. Filtering on
    /// "has a container" alone would drop the first population entirely and leave a blank form.
    /// </summary>
    private static IEnumerable<ComponentInstance> TopLevelComponents(FormDefinition element)
    {
        var form = element.Components.FirstOrDefault(c => c.BaseClass is FormComponentClass);
        foreach (var component in element.Components)
        {
            if (component.BaseClass is FormComponentClass)
                continue;
            // Placed by its own container's recursion instead.
            if (component.Container is not null && !ReferenceEquals(component.Container, form))
                continue;
            yield return component;
        }
    }

    /// <summary>
    /// Places one component on <paramref name="host"/> and recurses into whatever it contains.
    ///
    /// Shared by the running form and the hosted-UserControl spawner, which stay separate methods on
    /// purpose: only the first allocates interpreter variables and builds control-array groups, and folding
    /// them together would give a UserControl hosted on a form a second set of both.
    /// </summary>
    /// <param name="formCanvas">
    /// Where non-visual components go regardless of what contains them. A Timer is not drawn, so it has no
    /// place inside a Frame's clipped host; the model still records the container so the file round-trips.
    /// </param>
    private static void PlaceComponentTree(ComponentInstance component, Canvas host, Canvas formCanvas,
        Action<ComponentInstance, Control, Canvas>? onPlaced = null)
    {
        var componentClass = (ComponentBaseClass)component.BaseClass;
        var control = componentClass.Instantiate(component);
        var target = componentClass.IsVisual ? host : formCanvas;

        Canvas.SetLeft(control, component.GetPropertyOrDefault(VBProperties.LeftProperty));
        Canvas.SetTop(control, component.GetPropertyOrDefault(VBProperties.TopProperty));

        if (componentClass.IsVisual)
        {
            control.Width = component.GetPropertyOrDefault(VBProperties.WidthProperty);
            control.Height = component.GetPropertyOrDefault(VBProperties.HeightProperty);
            VBVisibility.Set(control, component.GetPropertyOrDefault(VBProperties.VisibleProperty));
        }
        else
        {
            // A non-visual control has no Width/Height/Visible in the .frm, so those read back as zero — and
            // zero is not what VBTimer renders at, because its template pins Min/MaxWidth to 28. That is why
            // a running form has always shown a clock face in its top-left corner.
            control.IsVisible = false;
        }

        target.Children.Add(control);
        onPlaced?.Invoke(component, control, target);

        if (component.ContainedControls.Count == 0)
            return;

        if (!componentClass.TryGetChildHost(control, out var childHost))
        {
            // A class that is not a container cannot have been recorded as one by the deserializer, so this
            // is unreachable from a loaded file. Placing the children on this control's own host keeps them
            // on screen rather than dropping them silently if some other path ever gets it wrong.
            childHost = host;
        }

        foreach (var child in component.ContainedControls)
            PlaceComponentTree(child, childHost, formCanvas, onPlaced);
    }

    public static Canvas SpawnComponentsForDesigner(
        FormDefinition formDef,
        IReadOnlyDictionary<string, ComponentBaseClass>? extraComponents = null)
    {
        var canvas = new Canvas { ClipToBounds = true };
        foreach (var component in TopLevelComponents(formDef))
            PlaceComponentTree(component, canvas, canvas);
        return canvas;
    }

    public static Task RunForm(FormDefinition element, CancellationToken token, out VBFormRuntime window,
        Debugging.IDebugController? debugController = null)
    {
        var form = element.Components.FirstOrDefault(x => x.BaseClass == FormComponentClass.Instance);
        if (form == null)
            throw new Exception("No form found");

        window = ((FormComponentClass)form.BaseClass).InstantiateWindow(form);
        var formName = form.GetPropertyOrDefault(VBProperties.NameProperty)?.ToString();
        if (formName is not null)
        {
            window.Context.ExecutionContext.AllocVariable(window.Context.RootEnv, formName, new Vb6Value(window));
            window.Context.ExecutionContext.AllocVariable(window.Context.RootEnv, "Me", new Vb6Value(window));
        }

        window.Content = SpawnComponents(element, window.Context.ExecutionContext, window.Context.RootEnv);
        // The form's own code runs as the primary module named after the form, so the debug gate reports — and
        // breakpoints are keyed by — the form's real name (matching the editor's vb6://form/{name} document).
        window.Context.SetCode(code: element.Code, moduleName: formName ?? "Module1", debugController: debugController);
        window.Show();
#if DEBUG
        window.AttachDevTools();
#endif

        var tcs = new TaskCompletionSource();

        token.Register((state, _) =>
        {
            (state as Window)!.Close();
        }, window);

        // Complete the run-task when the form closes. The TaskCompletionSource is captured directly in the handler
        // rather than parked in window.Tag: Control.Tag backs the VB6 `Tag` property, so stashing runtime state there
        // leaked "System.Threading.Tasks.TaskCompletionSource" into `Me.Tag` (and the Locals property surface, D7).
        // window is an out parameter and can't be captured, so the handler recovers the window from sender.
        void OnClosed(object? sender, EventArgs e)
        {
            (sender as Window)!.Closed -= OnClosed;
            tcs.TrySetResult();
        }
        window.Closed += OnClosed;

        return tcs.Task;
    }
}