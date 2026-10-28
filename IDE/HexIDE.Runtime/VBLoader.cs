using System;
using HexIDE.Runtime.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime;

public class VBLoader
{
    /// <summary>
    /// The icon a form with no Icon line runs with, where the host has one: the made program's runner gives its own. Null
    /// leaves Avalonia's, the executable's icon on Windows (#1594).
    /// </summary>
    public static Avalonia.Controls.WindowIcon? DefaultFormIcon { get; set; }

    /// <param name="standardLibrary">
    /// The run's library, which shows a CommonDialog's dialogs (#1084). Without one a form holding a CommonDialog is
    /// refused, as a form holding any control HexIDE can't create is.
    /// </param>
    /// <param name="form">The form instance the controls are put into, which is given their Controls collection (#1060).</param>
    public static Control SpawnComponents(FormDefinition element,
        ModuleExecutionContext executionContext,
        ExecutionEnvironment environment,
        IBasicStandardLibrary? standardLibrary = null,
        VbObject? form = null,
        VBProgram? program = null)
    {
        var toolbars = element.Components.Where(c => c.BaseClass is FormComponentClass)
            .SelectMany(c => c.PreservedChildSubtrees)
            .Select(block => (block.Text, Component: Components.InBox.ToolbarComponentClass.ReadForRuntime(block.Text)))
            .Where(pair => pair.Component is not null).ToDictionary(pair => pair.Text, pair => pair.Component!);
        RefuseUnsupportedControls(element, toolbars.Keys);
        var canvas = new Canvas()
        {
            ClipToBounds = true
        };
        // The form's ScaleMode is the unit its controls' positions are read and written in (#933). The .frm holds them
        // in twips whatever it is.
        VBProps.SetScaleMode(canvas, (VBScaleMode)(element.Scale?.Mode ?? (int)VBScaleMode.Twip));
        // The form's own mouse events come from its client area, which is hit-tested only where it has a background: a
        // transparent one shows the form's BackColor through it (#974).
        canvas.Background = Avalonia.Media.Brushes.Transparent;
        AttachedEvents.AttachFormMouse(canvas);
        var menu = new Menu();
        DockPanel.SetDock(menu, Avalonia.Controls.Dock.Top);
        var shortcuts = new List<(KeyGesture Gesture, Action Invoke)>();
        var menus = new List<(ComponentInstance Component, Control Control, ItemsControl Parent)>();
        BuildMenuBar(element, menu, shortcuts, menus);

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

        // What each block of the form became, for its Controls collection (#1060).
        var placed = new Dictionary<ComponentInstance, Control>();
        var dialogs = new Dictionary<string, Control>(StringComparer.Ordinal);

        void OnPlaced(ComponentInstance component, Control control, Canvas host)
        {
            if (control is VBCommonDialog dialog)
                dialog.host = standardLibrary ?? throw new NotImplementedException("A CommonDialog needs a dialog host (#1084)");
            placed[component] = control;
            AvaloniaInterop.UserControlContainers.Register(control, component.Container is { } owner ? placed.GetValueOrDefault(owner) : null);
            if (AvaloniaInterop.UserControlMembers.IsUserControl(control))
            {
                // The extender's file properties are carried raw until the designer models them (group 2).
                if (RawNumber(component, "TabIndex") is { } tabIndex)
                    KeyboardNavigation.SetTabIndex(control, tabIndex);
                if (RawDouble(component, "TabStop") is { } tabStop)
                    KeyboardNavigation.SetIsTabStop(control, tabStop != 0);
                AvaloniaInterop.UserControlMembers.Configure(control);
            }
            if (control is VBUserControl userControl && program is not null)
                userControl.Attach(program, component.BaseClass.ClassName);
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
            VBProps.SetRunTimeId(control);
        }

        foreach (var component in TopLevelComponents(element))
            PlaceComponentTree(component, canvas, canvas, element, OnPlaced);

        foreach (var block in element.UnknownChildSubtreeTexts)
            if (RuntimeControlBlock(block, "MSComCtl2.UpDown") is { } upDownBlock)
            {
                var upDown = (VBUpDown)Components.InBox.UpDownComponentClass.Instance.Instantiate(
                    new ComponentInstance(Components.InBox.UpDownComponentClass.Instance, upDownBlock.Name));
                foreach (var (property, value) in upDownBlock.Properties)
                {
                    if (property is "Left" or "Top" or "Width" or "Height")
                    {
                        var pixels = Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture) / 15;
                        switch (property)
                        {
                            case "Left": Canvas.SetLeft(upDown, pixels); break;
                            case "Top": Canvas.SetTop(upDown, pixels); break;
                            case "Width": upDown.Width = pixels; break;
                            case "Height": upDown.Height = pixels; break;
                        }
                    }
                    else if (property == "TabIndex")
                        Avalonia.Input.KeyboardNavigation.SetTabIndex(upDown, Convert.ToInt32(value.Value));
                    else if (property == "Orientation" && Convert.ToInt32(value.Value) is 0 or 1)
                        upDown.Horizontal = Convert.ToInt32(value.Value) == 1;
                    else if (property == "Visible")
                        upDown.IsVisible = Convert.ToInt32(value.Value) != 0;
                }
                upDown.LoadProperties(upDownBlock.Properties);
                VBProps.SetName(upDown, upDownBlock.Name);
                VBProps.SetRunTimeId(upDown);
                canvas.Children.Add(upDown);
                executionContext.AllocVariable(environment, upDownBlock.Name, new Vb6Value(upDown));
                dialogs[block] = upDown;
            }

        // The Toolbar is runtime-only until its file slice; its designer block remains carried verbatim.
        foreach (var (block, component) in toolbars)
        {
            PlaceComponentTree(component, canvas, canvas, element, (part, control, host) =>
            {
                OnPlaced(part, control, host);
                dialogs[block] = control;
            });
        }

        // Saved references may name an ImageList later in the file (#2334).
        foreach (var toolbar in placed.Values.OfType<VBToolbar>())
        {
            if (toolbar.SavedImageListName is not { } name) continue;
            var matches = placed.Values.Where(c => VBProps.GetName(c) == name).ToArray();
            if (matches.Length != 1 || matches[0] is not VBImageList images)
                throw ToolbarItems.Unmeasured();
            toolbar.TrySetProperty("ImageList", new Vb6Value((Control)images));
        }

        // A menu has a name in code as a control has (#1126): a variable of its own, or an element of its menu control
        // array, whose Click then passes the element's Index.
        foreach (var (component, control, parent) in menus)
        {
            placed[component] = control;
            if (component.GetPropertyOrDefault(VBProperties.NameProperty) is not { } name)
                continue;
            VBProps.SetName(control, name);
            if (arrayGroups.TryGetValue(name, out var group))
            {
                var index = TryParseControlArrayIndex(component, out var parsed) ? parsed : group.Count;
                VBProps.SetIndex(control, index);
                group.AddDesignTimeMenuElement(index, control, parent, live => NewMenuLike(component, live, menu));
            }
            else
                executionContext.AllocVariable(environment, name, new Vb6Value(control));
            VBProps.SetRunTimeId(control);
        }

        // Bind each control-array group to its shared Name once every element is in place.
        foreach (var (name, group) in arrayGroups)
            executionContext.AllocVariable(environment, name, new Vb6Value(group));

        var controls = new FormControls(InFileOrder(element, placed, dialogs).OrderBy(AvaloniaInterop.UserControlMembers.Windowless), arrayGroups);
        foreach (var (block, control) in dialogs)
            if (control is VBUpDown upDown && RuntimeControlBlock(block, "MSComCtl2.UpDown") is { } saved)
                upDown.LoadBuddy(saved.Properties, controls);
        controls.Maker = (componentClass, name, container) => MakeAtRunTime(componentClass, name, container, canvas, element);
        foreach (var group in arrayGroups.Values)
            group.FormControls = controls;
        if (form is not null)
            form.Controls = controls;

        var root = new DockPanel()
        {
            Children =
            {
                menu,
                canvas
            }
        };

        // Menu shortcuts are matched on the form's content, and in the TUNNEL phase, so they beat whatever
        // has focus. That is VB6's behaviour — Ctrl+S saves from inside a text box — and it is also the only
        // thing that works: KeyBindings on an ancestor are not evaluated for a key event raised on a
        // descendant, so binding them here and letting them bubble fires nothing at all.
        if (shortcuts.Count > 0)
        {
            root.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Handled)
                    return;
                foreach (var (gesture, invoke) in shortcuts)
                {
                    if (!gesture.Matches(e))
                        continue;
                    e.Handled = true;
                    invoke();
                    return;
                }
            }, RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
        }

        AvaloniaInterop.UserControlKeys.Attach(root, controls);
        AvaloniaInterop.UserControlFocus.Attach(root);
        root.SizeChanged += (_, _) =>
        {
            foreach (var control in controls.All)
                AvaloniaInterop.UserControlMembers.Align(control);
        };
        return root;
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
    /// Fills the form's menu bar from its menus, and collects a key binding for each shortcut.
    ///
    /// The menu tree is the one the loader records on each menu's SubItems. Menus are NOT placed on the
    /// canvas: a VB6 menu has no Left/Top/Width/Height, so every one of them used to land as a zero-sized
    /// child of the form's canvas while the bar itself stayed empty.
    /// </summary>
    private static void BuildMenuBar(FormDefinition element, Menu menu, List<(KeyGesture Gesture, Action Invoke)> shortcuts,
        List<(ComponentInstance Component, Control Control, ItemsControl Parent)> built)
    {
        foreach (var component in TopLevelMenus(element))
            if (BuildMenuItem(component, menu, shortcuts, menu, built) is { } item)
                menu.Items.Add(item);

        // An empty bar still occupies a strip at the top of the form and pushes the canvas down, so a form
        // with no menus would be laid out differently from the same form in VB6. So does a bar whose menus are all
        // hidden, as a form's are when it keeps them only for PopupMenu, and VB6 shows no bar then (measured, #1127).
        menu.IsVisible = HasMenuBar(element);
        // VB6's bar, so that the client area below it is the .frm's ClientHeight in a window HasMenuBar made taller by
        // this much, before anything has been laid out (#1127).
        menu.Height = MenuBarHeight;
    }

    /// <summary>
    /// The height of a running form's menu bar, in pixels: VB6's, 285 twips at 96 dpi (measured, #1127). It is fixed
    /// rather than measured from the theme's Menu because Form_Load reads ScaleHeight before the form is shown, so
    /// before any layout, and because a form's client area must not change with the IDE's theme.
    /// </summary>
    public const double MenuBarHeight = 19;

    /// <summary>
    /// Whether the form runs with a menu bar: it has a top-level menu that is visible. In VB6 the bar is outside the
    /// client area, so the window is the form's ClientHeight plus <see cref="MenuBarHeight"/> tall, and a form whose
    /// top-level menus are all hidden has no bar and is no taller (measured, #1127).
    /// </summary>
    public static bool HasMenuBar(FormDefinition element) =>
        TopLevelMenus(element).Any(menu => menu.GetPropertyOrDefault(VBProperties.VisibleProperty));

    // The menus on the bar itself. A menu some other menu claims as a sub-item belongs in that menu, not on the bar:
    // the same filter the writer applies, and for the same reason.
    private static IEnumerable<ComponentInstance> TopLevelMenus(FormDefinition element)
    {
        var claimed = new HashSet<ComponentInstance>();
        foreach (var component in element.Components)
            foreach (var child in SubItemsOf(component))
                claimed.Add(child);

        foreach (var component in element.Components)
            if (component.BaseClass is MenuComponentClass && !claimed.Contains(component))
                yield return component;
    }

    /// <summary>
    /// The line VB6 draws where a menu item's caption is a single hyphen.
    ///
    /// Templated and brushed here rather than left to whichever resource dictionaries happen to be merged,
    /// because both answers the environment gives are invisible. Under the base theme alone a Separator is
    /// laid out one pixel high and painted WhiteSmoke — a line that cannot be seen against a menu. With
    /// HexIDE's own dictionaries merged it collapses to zero height and is not drawn at all. Neither looks
    /// any different from a separator that was never added, which is exactly how this shipped: the object
    /// was in the menu, the space was reserved, and there was no rule to see.
    ///
    /// Doing it in code rather than as a keyed theme also keeps it out of the IDE's own chrome, which uses
    /// separators of its own that this has no business restyling.
    /// </summary>
    private static Separator NewMenuSeparator()
    {
        return new VBMenuSeparator
        {
            Height = 1,
            // Width is set to auto EXPLICITLY, and that is the whole reason this renders. The IDE's own
            // Classic theme carries an implicit ControlTheme for every Separator that makes it a 1px
            // VERTICAL toolbar divider. A local value is the only thing that beats it — without this, a menu
            // separator inherits Width=1, meets Height=1 here, and draws as a two-pixel dot in the middle of
            // the menu. It is invisible in a test that does not merge that theme, which is how it shipped.
            Width = double.NaN,
            Margin = new Thickness(2, 3),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            // The 3-D shadow colour, which is what Win32 etches a menu separator in and what the IDE's own
            // toolbar separators already use. Taken from the theme rather than written as a literal so it
            // follows a dark theme pack, where a mid-grey rule on a dark menu would be as wrong as the
            // near-black one this replaces.
            //
            // A key that fails to resolve would leave the brush null and the rule invisible, which is exactly
            // the defect this started as. What makes that safe to rely on is the regression test: it asserts
            // the rule has a brush AND is wider than it is tall, against the same dictionaries the
            // application merges. If the key ever stops resolving, that fails rather than quietly shipping.
            [!TemplatedControl.BackgroundProperty] =
                new DynamicResourceExtension(Classic.CommonControls.SystemColors.ControlDarkBrushKey),
            Template = new FuncControlTemplate<Separator>((separator, _) => new Border
            {
                [!Border.BackgroundProperty] = separator[!TemplatedControl.BackgroundProperty],
                // The shadow colour at full strength is a hard rule — near-black against a light menu, and
                // heavier than either VB6 or a modern menu draws. Win32 softens it by etching: the shadow
                // line with a highlight line beneath it, which reads as a groove rather than a bar. That
                // trick needs a background to lift against and does nothing on a white menu, so the same
                // effect is had by letting the menu show through the rule instead.
                //
                // Opacity rather than a lighter colour because it stays right in both directions: on a dark
                // theme pack the same rule softens against a dark menu, where a fixed light grey would glare.
                Opacity = 0.45,
            }),
        };
    }

    private static IReadOnlyList<ComponentInstance> SubItemsOf(ComponentInstance component) =>
        component.BaseClass is MenuComponentClass &&
        component.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems
            ? subItems
            : [];

    /// <summary>
    /// One menu item and everything under it, or a separator.
    ///
    /// Click is dispatched through the MENU rather than through the item, because a sub-item lives in a
    /// popup with its own visual root: the usual walk up the tree to find the execution root leaves the
    /// window entirely and finds nothing, so every submenu item would silently do nothing while the
    /// top-level ones worked.
    /// </summary>
    private static Control? BuildMenuItem(ComponentInstance component, Menu menu, List<(KeyGesture Gesture, Action Invoke)> shortcuts,
        ItemsControl parent, List<(ComponentInstance Component, Control Control, ItemsControl Parent)> built)
    {
        var caption = component.GetPropertyOrDefault(VBProperties.CaptionProperty);

        // VB6 spells a separator as a menu whose caption is a single hyphen.
        if (caption == "-")
        {
            var separator = NewMenuSeparator();
            separator.IsVisible = component.GetPropertyOrDefault(VBProperties.VisibleProperty);
            built.Add((component, separator, parent));
            return separator;
        }

        var item = (VBMenu)((ComponentBaseClass)component.BaseClass).Instantiate(component);
        built.Add((component, item, parent));

        var name = component.GetPropertyOrDefault(VBProperties.NameProperty);
        if (name is not null)
        {
            WireMenuClick(item, name, menu);

            if (VBMenuShortcut.TryParse(component, out var gesture))
            {
                // Displays right-aligned in the item, as VB6 draws it.
                item.InputGesture = gesture;
                shortcuts.Add((gesture, () => RaiseShortcut(item, name, menu)));
            }
        }

        foreach (var child in SubItemsOf(component))
            if (BuildMenuItem(child, menu, shortcuts, item, built) is { } childItem)
                item.Items.Add(childItem);

        return item;
    }

    private static void WireMenuClick(MenuItem item, string name, Menu menu)
    {
        // Click bubbles, so a submenu click reaches every ancestor item too, where VB6 raises the chosen item's alone.
        item.Click += (_, e) =>
        {
            if (ReferenceEquals(e.Source, item))
                RaiseMenuClick(item, name, menu);
        };
        // A menu that holds items raises its Click as they open, a submenu as much as a top-level one, so a program can
        // enable and disable them first (measured, #1344). Avalonia raises Click only for an item with none. Opening
        // bubbles too, so a submenu opening reaches its parent, which raised its own when it opened.
        item.SubmenuOpened += (_, e) =>
        {
            if (ReferenceEquals(e.Source, item))
                RaiseMenuClick(item, name, menu);
        };
    }

    // The item's Click, with its Index first when it is an element of a menu control array: `mnuLevel_Click(Index As
    // Integer)` (#1126).
    private static void RaiseMenuClick(Control item, string name, Menu menu) =>
        menu.FindAncestorOfType<IModuleExecutionRoot>()?.ExecuteSub($"{name}_Click", MenuArgs(item));

    private static Vb6Value[]? MenuArgs(Control item) => VBProps.GetIndex(item) is { } index ? [new Vb6Value(index)] : null;

    /// <summary>
    /// A menu shortcut, as vb6.exe raises it (measured, #1344): the Click of each menu from the top-level one down to the
    /// item, stopping at the first that is disabled or hidden, whose Click is not raised. So nothing is raised under a
    /// disabled or hidden top-level menu, only Top's under a disabled Sub, and Top's and Sub's for a disabled item.
    /// </summary>
    /// <remarks>
    /// Each is asked at its turn, after the Clicks above it have run: vb6.exe raised an item a parent's Click had just
    /// enabled. That a menu between the two is asked late as well is assumed rather than measured.
    /// </remarks>
    private static void RaiseShortcut(VBMenu item, string name, Menu menu)
    {
        if (menu.FindAncestorOfType<IModuleExecutionRoot>() is not { } root)
            return;
        var chain = new List<VBMenu> { item };
        for (var parent = item.Parent as VBMenu; parent is not null; parent = parent.Parent as VBMenu)
            chain.Insert(0, parent);
        for (var i = 0; i < chain.Count; i++)
        {
            var reached = chain.GetRange(0, i + 1);
            var linkName = i == chain.Count - 1 ? name : VBProps.GetName(chain[i]);
            if (linkName is not null)
                root.ExecuteSubIf($"{linkName}_Click", MenuArgs(chain[i]), () => reached.TrueForAll(m => m.IsEnabled && m.IsVisible));
        }
    }

    /// <summary>
    /// A new element of a menu control array, for <c>Load mnuRecent(n)</c>: a copy of the array's first element as it
    /// stands, its caption, tick, Enabled and Visible included (measured, #1126). Unlike a loaded control, which starts
    /// hidden, a loaded menu is visible when the element it copies is. Its Shortcut is not copied, which is unmeasured.
    /// </summary>
    private static Control NewMenuLike(ComponentInstance component, Control live, Menu menu)
    {
        if (live is not VBMenu first)
            return new VBMenuSeparator { IsVisible = live.IsVisible };
        var item = (VBMenu)((ComponentBaseClass)component.BaseClass).Instantiate(component);
        item.Header = first.Header;
        item.Checked = first.Checked;
        item.IsEnabled = first.IsEnabled;
        item.IsVisible = first.IsVisible;
        if (component.GetPropertyOrDefault(VBProperties.NameProperty) is { } name)
            WireMenuClick(item, name, menu);
        return item;
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
    /// <summary>
    /// The form's controls in the order its file holds their blocks, which is the order of its Controls collection
    /// (measured, #1060): a container before what it holds, a menu before its submenus, and a block HexIDE keeps as text
    /// at the place it was read, among its container's children by its ordinal, as the writer puts it back.
    /// </summary>
    private static IEnumerable<Control> InFileOrder(FormDefinition element, Dictionary<ComponentInstance, Control> placed,
        Dictionary<string, Control> dialogs)
    {
        var root = element.Components.FirstOrDefault(c => c.BaseClass is FormComponentClass);
        if (root is null)
            yield break;
        var claimed = new HashSet<ComponentInstance>();
        foreach (var component in element.Components)
            if (component != root)
                foreach (var child in ChildrenOf(component))
                    claimed.Add(child);

        var order = new List<Control>();
        void Walk(ComponentInstance container, IReadOnlyList<ComponentInstance> children)
        {
            var blocks = container.PreservedChildSubtrees;
            var child = 0;
            var block = 0;
            for (var position = 0; position < children.Count + blocks.Count; position++)
            {
                if (block < blocks.Count && blocks[block].Ordinal <= position)
                {
                    if (dialogs.TryGetValue(blocks[block++].Text, out var dialog))
                        order.Add(dialog);
                }
                else if (child < children.Count)
                {
                    var component = children[child++];
                    if (placed.TryGetValue(component, out var control))
                        order.Add(control);
                    Walk(component, ChildrenOf(component));
                }
            }
            for (; block < blocks.Count; block++)
                if (dialogs.TryGetValue(blocks[block].Text, out var dialog))
                    order.Add(dialog);
            for (; child < children.Count; child++)
            {
                if (placed.TryGetValue(children[child], out var control))
                    order.Add(control);
                Walk(children[child], ChildrenOf(children[child]));
            }
        }
        Walk(root, element.Components.Where(c => c != root && !claimed.Contains(c)).ToList());
        foreach (var control in order)
            yield return control;
    }

    // A menu's children are its submenus, and anything else's the controls it contains.
    private static IReadOnlyList<ComponentInstance> ChildrenOf(ComponentInstance component) =>
        component.BaseClass is MenuComponentClass ? SubItemsOf(component) : component.ContainedControls;

    private static IEnumerable<ComponentInstance> TopLevelComponents(FormDefinition element)
    {
        var form = element.Components.FirstOrDefault(c => c.BaseClass is FormComponentClass);
        foreach (var component in element.Components)
        {
            if (component.BaseClass is FormComponentClass)
                continue;
            // A menu belongs in the menu bar, not on the canvas. It has no Left/Top/Width/Height, so placing
            // one here made it a zero-sized child of the form at the origin — invisible, but present in the
            // visual tree and in the way.
            if (component.BaseClass is MenuComponentClass)
                continue;
            // Placed by its own container's recursion instead.
            if (component.Container is not null && !ReferenceEquals(component.Container, form))
                continue;
            yield return component;
        }
    }

    /// <summary>
    /// A control <c>Controls.Add</c> makes (#1556), as vb6.exe makes one (measured, the oracle's "Controls.Add and
    /// Controls.Remove"): at 0, 0 in its container, its class's size, hidden, with an empty Caption and Text, the form's
    /// font as it stands at that moment, and under every control already there.
    /// </summary>
    private static Control MakeAtRunTime(ComponentBaseClass componentClass, string name, Control? container, Canvas formCanvas,
        FormDefinition form)
    {
        var host = formCanvas;
        if (container is not null)
        {
            ComponentBaseClass containerClass = container is VBFrame ? FrameComponentClass.Instance : PictureBoxComponentClass.Instance;
            if (!containerClass.TryGetChildHost(container, out var childHost))
                throw new NotImplementedException($"Controls.Add into this {containerClass.ClassName} is not implemented (#1556)");
            host = childHost;
        }
        var font = formCanvas.FindLogicalAncestorOfType<Window>() is { } window ? AttachedProperties.GetFont(window) : null;
        font ??= form.Components.FirstOrDefault(c => c.BaseClass is FormComponentClass)?.GetPropertyOrDefault(VBProperties.FontProperty);

        var component = new ComponentInstance(componentClass, name)
            .SetProperty(VBProperties.CaptionProperty, "")
            .SetProperty(VBProperties.TextProperty, "")
            .SetProperty(VBProperties.VisibleProperty, false)
            .SetProperty(VBProperties.LeftProperty, 0)
            .SetProperty(VBProperties.TopProperty, 0);
        if (font is { } f)
            component.SetProperty(VBProperties.FontProperty, f);
        // A Timer's Interval is 0 (measured), which the property's own default isn't yet (#2068).
        if (componentClass is TimerComponentClass)
            component.SetProperty(VBProperties.IntervalProperty, 0);
        if (AddedSize(componentClass, font) is var (width, height))
            component.SetProperty(VBProperties.WidthProperty, width / VBScaleModeExtensions.PixelToTwips)
                .SetProperty(VBProperties.HeightProperty, height / VBScaleModeExtensions.PixelToTwips);

        Control? made = null;
        PlaceComponentTree(component, host, formCanvas, form, (_, control, _) => made = control);
        VBProps.SetRunTimeId(made!);
        return made!;
    }

    // Each class's size when Controls.Add makes it, in twips (measured): 1200 by 480, but for these. None for a Timer, which
    // has no position, nor a Line, whose ends are all 0.
    private static (double Width, double Height)? AddedSize(ComponentBaseClass componentClass, VBFont? font) => componentClass switch
    {
        TimerComponentClass or LineComponentClass => null,
        ListBoxComponentClass => (1200, AddedListBoxHeight(font)),
        ComboBoxComponentClass => (1200, 315),
        HScrollBarComponentClass => (1200, 240),
        VScrollBarComponentClass => (240, 1200),
        _ => (1200, 480),
    };

    // An added ListBox's row, in pixels, by the font it takes (measured, fourteen fonts): its height is 60 twips and as many
    // rows as fit in 480, at least one.
    private static readonly (string Font, double Size, int Row)[] AddedListBoxRows =
    [
        ("MS Sans Serif", 8.25, 13), ("MS Sans Serif", 9.75, 16), ("MS Sans Serif", 12, 20), ("MS Sans Serif", 13.5, 24),
        ("Microsoft Sans Serif", 8.25, 13), ("Tahoma", 8.25, 13), ("Tahoma", 9.75, 16), ("Verdana", 8.25, 13),
        ("Arial", 8.25, 14), ("Arial", 9.75, 16), ("Arial", 12, 18), ("Times New Roman", 12, 19), ("Courier New", 9.75, 16),
        ("Segoe UI", 9, 15),
    ];

    private static double AddedListBoxHeight(VBFont? font)
    {
        foreach (var (name, size, row) in AddedListBoxRows)
            if (font is { } f && string.Equals(f.FontFamilyName, name, StringComparison.OrdinalIgnoreCase) && Math.Abs(f.Size - size) < 0.01)
            {
                double rowTwips = row * VBScaleModeExtensions.PixelToTwips;
                return 60 + Math.Max(1, Math.Floor(420 / rowTwips)) * rowTwips;
            }
        throw new NotImplementedException(
            $"Controls.Add of a ListBox in {font?.FontFamilyName} {font?.Size} is not implemented: its height follows its font, " +
            "and was measured for fourteen fonts, not this one (#1556)");
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
    private static void PlaceComponentTree(ComponentInstance component, Canvas host, Canvas formCanvas, FormDefinition form,
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

        // A Line lays itself out from its endpoints, which are in its container's scale (#163).
        if (control is VBLine line)
            line.Resolve(LineComponentClass.TwipsPerUnit(component, form));

        // Under what its plane already holds there, so the first block in a container is drawn on top, and a windowless
        // control under every windowed one, as VB6 runs a form (measured, #1497).
        ControlStacking.PlaceUnder(control, componentClass.IsWindowless, target);
        target.Children.Add(control);
        onPlaced?.Invoke(component, control, target);

        // A PictureBox's ScaleMode is the unit of the controls inside it (measured: in a pixel PictureBox, a TextBox at 150
        // twips reads 10), and of its own mouse events' X and Y. HexIDE keeps it as the line the .frm wrote. A Frame has
        // none, so its children stay in twips.
        if (componentClass.TryGetChildHost(control, out var ownHost) && DeclaredScaleMode(component) is { } scaleMode)
            VBProps.SetScaleMode(ownHost, scaleMode);
        // Where a 3-D bordered TextBox's or ListBox's client area starts, which is where its mouse X and Y count from: 2
        // pixels in, and at the edge when it is flat or has no border (measured with GetClientRect, #974).
        if (componentClass is TextBoxComponentClass or ListBoxComponentClass)
        {
            var flat = componentClass is ListBoxComponentClass
                ? component.GetPropertyOrDefault(VBProperties.AppearanceProperty) == VBAppearance.Flat
                : RawNumber(component, "Appearance") == 0;
            var borderless = componentClass is TextBoxComponentClass && RawNumber(component, "BorderStyle") == 0;
            if (!flat && !borderless)
                VBProps.SetClientInset(control, 2);
        }

        if (!componentClass.TryGetChildHost(control, out var childHost))
        {
            // A class that is not a container cannot have been recorded as one by the deserializer, so this
            // is unreachable from a loaded file. Placing the children on this control's own host keeps them
            // on screen rather than dropping them silently if some other path ever gets it wrong.
            childHost = host;
        }

        foreach (var child in component.ContainedControls)
            PlaceComponentTree(child, childHost, formCanvas, form, onPlaced);
        if (control is VBSSTab tab)
            component.GetPropertyOrDefault(Components.InBox.SSTabSavedState.Property)?.PlacePages(tab);
    }

    // The ScaleMode a container's .frm block declares, from the line HexIDE preserves for it: `ScaleMode = 3  'Pixel`.
    internal static VBScaleMode? DeclaredScaleMode(ComponentInstance component) =>
        RawNumber(component, "ScaleMode") is { } mode && Enum.IsDefined(typeof(VBScaleMode), mode) ? (VBScaleMode)mode : null;

    // The number on a property line HexIDE preserves without modelling it, as a Double, sign and fraction included:
    // `ScaleTop = -12.5`. Null when the component has no such line, or its value is not a number.
    internal static double? RawDouble(ComponentInstance component, string name)
    {
        foreach (var property in component.UnknownRawProperties)
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || property.Lines.Count == 0)
                continue;
            var line = property.Lines[0];
            var value = line[(line.IndexOf('=') + 1)..].Trim();
            var end = value.IndexOfAny([' ', '\'']);
            if (double.TryParse(end < 0 ? value : value[..end], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var number))
                return number;
        }
        return null;
    }

    // The number on a property line HexIDE preserves without modelling it, such as `BorderStyle = 0  'None`.
    internal static int? RawNumber(ComponentInstance component, string name)
    {
        foreach (var property in component.UnknownRawProperties)
        {
            if (!property.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || property.Lines.Count == 0)
                continue;
            var line = property.Lines[0];
            var value = line[(line.IndexOf('=') + 1)..].TrimStart();
            var digits = value.Length - value.TrimStart("0123456789".ToCharArray()).Length;
            if (digits > 0 && int.TryParse(value[..digits], out var number))
                return number;
        }
        return null;
    }

    /// <summary>
    /// A form holding a control HexIDE cannot create, or a data-bound control, is refused before placement (#857, #859).
    /// Left out, the control made the form a different program: code naming it read Empty, was told the name was not defined, or skipped a call
    /// with Err.Number 0, and a form whose code never names it ran as if it had never held it. This is an engine limit,
    /// so no On Error in the program can trap it. A form the designer opens is not refused: it shows what it holds.
    /// </summary>
    private static void RefuseUnsupportedControls(FormDefinition element, ICollection<string> runtimeToolbars)
    {
        var lines = new List<string>();
        foreach (var block in element.UnknownChildSubtreeTexts)
            if (!runtimeToolbars.Contains(block) && RuntimeControlBlock(block, "MSComCtl2.UpDown") is null)
                DescribeUncreatable(block, lines);
        foreach (var component in element.Components)
            foreach (var property in component.UnknownRawProperties)
                if (property.Name.Equals("DataSource", StringComparison.OrdinalIgnoreCase))
                {
                    var line = property.Lines.FirstOrDefault() ?? "";
                    var source = DesignTimeValue(line[(line.IndexOf('=') + 1)..].Trim());
                    var name = component.GetPropertyOrDefault(VBProperties.NameProperty);
                    lines.Add($"HexIDE does not implement data binding: {name} has DataSource {source.Value}.");
                }
        if (lines.Count > 0)
            throw new NotImplementedException(string.Join("\n", lines.Distinct()));
    }

    /// <summary>
    /// A block for a control HexIDE creates at run time from the block itself, the UpDown's (#1093): its name, and the
    /// properties it sets, as code would set them. Null for any other block, and for one this can't create: one holding
    /// a control, or an element of a control array.
    /// </summary>
    /// <remarks>The OCX's own properties, <c>_ExtentX</c>, <c>_ExtentY</c> and <c>_Version</c>, mean nothing to a running program.</remarks>
    private static (string Name, List<(string Property, Vb6Value Value)> Properties)? RuntimeControlBlock(string block, string typeName)
    {
        var lines = block.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count < 2)
            return null;
        var begin = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (begin.Length < 3 || !begin[0].Equals("Begin", StringComparison.OrdinalIgnoreCase)
            || !begin[1].Equals(typeName, StringComparison.OrdinalIgnoreCase))
            return null;
        var properties = new List<(string, Vb6Value)>();
        foreach (var line in lines.Skip(1).SkipLast(1))
        {
            if (line.StartsWith("Begin", StringComparison.OrdinalIgnoreCase))
                return null;
            var equals = line.IndexOf('=');
            if (equals < 0)
                continue;
            var name = line[..equals].Trim();
            var text = line[(equals + 1)..].Trim();
            if (name.Equals("Index", StringComparison.OrdinalIgnoreCase))
                return null;
            if (name.StartsWith('_'))
                continue;
            properties.Add((name, DesignTimeValue(text)));
        }
        return (begin[2], properties);
    }

    // A value as the .frm writes it: a quoted string, -1 or 0 for a Boolean, or a number, with any trailing comment
    // dropped. The control's own setter converts it to the property's type, as an assignment would.
    internal static Vb6Value DesignTimeValue(string text)
    {
        if (text.StartsWith('"'))
        {
            var end = text.LastIndexOf('"');
            return new Vb6Value(text[1..Math.Max(1, end)].Replace("\"\"", "\""));
        }
        var token = text.Split(' ', 2)[0];
        if (token.StartsWith("&H", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(token[2..].TrimEnd('&'), System.Globalization.NumberStyles.HexNumber, null, out var hex))
            return new Vb6Value(hex);
        if (double.TryParse(token, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            return new Vb6Value(number);
        return new Vb6Value(token);
    }

    // Every control in a preserved block, from its Begin lines: the block's own, then each nested one. A control nested in
    // one can't be created either, even one HexIDE models, because the block is carried as text and nothing in it is read.
    // A control array's elements share a name, so they are named once.
    private static void DescribeUncreatable(string block, List<string> lines)
    {
        (string Type, string Name)? outer = null;
        foreach (var raw in block.Split('\n'))
        {
            var line = raw.Trim();
            // BeginProperty opens a property block, not a control.
            if (!line.StartsWith("Begin ", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                continue;
            // An in-box control HexIDE will model names the issue that tracks it (#1082).
            var tracked = Components.InBox.InBoxComponentClass.IssueFor(parts[1]) is { } issue
                ? $", which isn't implemented yet (#{issue})" : "";
            if (outer is not { } container)
            {
                outer = (parts[1], parts[2]);
                lines.Add($"HexIDE cannot create {parts[2]} ({parts[1]}){tracked}.");
            }
            else
                lines.Add($"HexIDE cannot create {parts[2]}, inside {container.Name} ({container.Type}){tracked}.");
        }
    }

    public static Canvas SpawnComponentsForDesigner(
        FormDefinition formDef,
        IReadOnlyDictionary<string, ComponentBaseClass>? extraComponents = null)
    {
        var canvas = new Canvas { ClipToBounds = true };
        foreach (var component in TopLevelComponents(formDef))
            PlaceComponentTree(component, canvas, canvas, formDef);
        return canvas;
    }

    /// <summary>
    /// The project's code modules, in the shape <see cref="BasicInterpreter"/> takes them.
    ///
    /// <para>
    /// One place, because there are three run paths — a form, a form in the browser host, and <c>Sub
    /// Main</c> — and they answered this question differently. <c>Sub Main</c> loaded the modules and the
    /// form paths loaded none, so the same project behaved differently depending on its startup object
    /// (hexide-io/HexIDE#220). A shared helper is what stops that recurring the next time a fourth path
    /// appears.
    /// </para>
    ///
    /// <para>
    /// <b>UserControls and PropertyPages are deliberately excluded.</b> They are component-backed: each is
    /// instantiated through the component model with its own execution context, and its code runs there.
    /// Loading one here as a free-standing module would run its declarations a second time, in the wrong
    /// scope.
    /// </para>
    ///
    /// <para>
    /// <b>Each module is its whole file, header included</b> (hexide-io/HexIDE#273 task 3.6), so the line
    /// a statement is on in the interpreter is the line the code window shows it on. The grammar parses the
    /// header and the walk executes none of it.
    /// </para>
    /// </summary>
    public static (IReadOnlyList<(string Name, string Code)> Standard,
                   IReadOnlyList<(string Name, string Code)> Classes)
        InterpreterModules(ProjectDefinition? project)
    {
        if (project is null)
            return ([], []);

        static IReadOnlyList<(string, string)> Of(ProjectDefinition p, ModuleKind kind) =>
            p.Modules.Where(m => m.Kind == kind).Select(m => (m.Name, FormCodeText.WholeFile(m))).ToList();

        return (Of(project, ModuleKind.StandardModule), Of(project, ModuleKind.ClassModule));
    }

    public static Task RunForm(FormDefinition element, CancellationToken token, out VBFormRuntime window,
        Debugging.IDebugController? debugController = null)
    {
        var form = FormComponentOf(element);
        var shown = window = ((FormComponentClass)form.BaseClass).InstantiateWindow(form, HasMenuBar(element));
        // Another form of the program gets a window of its own the same way (#822).
        PrepareForm(element, shown.Context, content => shown.Content = content, debugController, OpenWindow);

        // The run lasts until the program ends, not until this window closes: the startup form can unload and leave
        // another form running (measured, #822).
        return StartAndShow(shown.Context, token, close: shown.Close, closeUnshown: shown.Close, showUntilClosed: () =>
        {
            shown.ShowForm();
            return new TaskCompletionSource().Task;
        });
    }

    /// <summary>
    /// Run a loaded project as a made program runs it (#1062): from its startup form, or from <c>Sub Main</c> where the
    /// project starts there, with every form and module the project holds. The task completes when the program has ended.
    /// A project with no startup object, a missing or ambiguous Main, and a syntax error in any module throw before
    /// anything runs.
    /// </summary>
    /// <param name="startupWindow">The startup form's window, or null for a program that starts at Sub Main.</param>
    public static Task RunMadeProgram(ProjectDefinition project, CancellationToken token, out VBFormRuntime? startupWindow)
    {
        if (!project.StartsAtSubMain)
        {
            startupWindow = null;
            var form = project.StartupForm ?? throw new VBCompileErrorException(BasicInterpreter.NoStartupObject);
            var run = RunForm(form, token, out var window);
            startupWindow = window;
            return run;
        }
        // Main has no window of its own, so its MsgBox and InputBox are centred on the screen: an owner that is never shown,
        // closed when the run ends so a box still open goes with it, as one over a form does.
        startupWindow = null;
        var owner = new Window();
        var program = new VBProgram(project, new VBWindowContext(new StandaloneStandardLib(owner)), debugController: null, OpenWindow);
        program.Interpreter.FindStartupSubMain();
        return RunThenClose(RunSubMain(program, token), owner);

        static async Task RunThenClose(Task run, Window owner)
        {
            try
            {
                await run;
            }
            finally
            {
                owner.Close();
            }
        }
    }

    /// <summary>A desktop window for one of a program's forms (#822).</summary>
    public static IFormView OpenWindow(FormDefinition definition)
    {
        var component = FormComponentOf(definition);
        return ((FormComponentClass)component.BaseClass).InstantiateWindow(component, HasMenuBar(definition));
    }

    /// <summary>
    /// Run a program that starts at <c>Sub Main</c> until it ends (#822): Main has returned and no form is loaded, or the
    /// run was cancelled, by End, Stop or a fault, which closes every window. An error in Main is thrown, once every window
    /// it left has gone.
    /// </summary>
    public static async Task RunSubMain(VBProgram program, CancellationToken token)
    {
        await using var registration = token.Register(program.Abandon);
        try
        {
            await program.RunMainAsync();
            if (!token.IsCancellationRequested)
                await program.Ended;
        }
        finally
        {
            if (!program.Ended.IsCompleted)
                program.Abandon();
            program.Events.Close();
            program.Interpreter.BuiltIns.Files.CloseAll();
        }
    }

    /// <summary>The form's own component in its definition.</summary>
    public static ComponentInstance FormComponentOf(FormDefinition element) =>
        element.Components.FirstOrDefault(x => x.BaseClass is FormComponentClass)
        ?? throw new Exception("No form found");

    /// <summary>
    /// Build a form's program into its window's context (#822). Every module the run loads is parsed here, so a syntax
    /// error throws before anything is shown. The form's whole file is the primary module, so its line numbers are the
    /// code window's, and it is named after the form, so the debug gate reports, and matches breakpoints by, the form's
    /// real name: the bare name the IDE pushes each document's breakpoints under. Then the form's instance is made and
    /// its controls put into it, held until it loads.
    /// </summary>
    /// <param name="openForm">
    /// How the host makes a window for the program's other forms (#822), or null in a host that runs the startup form only:
    /// a use of another form is then refused, naming #822.
    /// </param>
    public static VBProgram PrepareForm(FormDefinition element, VBWindowContext context, Action<Control> setContent,
        Debugging.IDebugController? debugController, Func<FormDefinition, IFormView>? openForm = null)
    {
        var program = new VBProgram(element, context, debugController, openForm);
        setContent(SpawnComponents(element, context.ExecutionContext, context.InstanceEnv, context.StandardLibrary,
            context.FormInstance, program));
        context.HoldControlsUntilLoad();
        return program;
    }

    // OLE Automation, the one in-box library a .vbp lists as a Reference: VBA, VBRUN and VB are implicit.
    private const string StdoleGuid = "00020430-0000-0000-C000-000000000046";

    /// <summary>
    /// Tell an interpreter what of its project a declaration may name (#822 review): the project's forms, which are
    /// types though only the running one is a module, and whether the project references a type library beyond the
    /// in-box four, whose types the interpreter can't read.
    /// </summary>
    public static void DescribeProject(BasicInterpreter interpreter, ProjectDefinition? project)
    {
        if (project is null)
            return;
        interpreter.ProjectFormNames = new HashSet<string>(project.Forms.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
        interpreter.ReferencesOtherLibraries = project.References.Any(r =>
            !string.Equals(r.Guid.Trim('{', '}'), StdoleGuid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Start a prepared form, then show its window, as VB6 shows its startup form: Initialize and Load run before the
    /// window appears (measured, #822), so a Resize comes after Load. The task completes when the window closes, or when
    /// the start does not end in a form to show: a fault in Initialize or Load, End, the debugger's Stop, or the run
    /// cancelled first. Shown or not, a cancellation closes the form.
    /// </summary>
    /// <param name="closeUnshown">
    /// How to close the form before it was ever shown, or null when there is nothing to close. A desktop window must be
    /// closed even then: a MsgBox or InputBox opened from Initialize or Load waits on its owner's Closed, so a window
    /// left unclosed left the box standing, and answering it ran the ended run's Load on beside the next one (#820,
    /// #1010 review).
    /// </param>
    public static Task StartAndShow(VBWindowContext context, CancellationToken token, Action close, Func<Task> showUntilClosed,
        Action? closeUnshown = null)
    {
        var run = new TaskCompletionSource();
        var program = context.Program;
        // How the window goes while it was never shown, and then how it goes once it has been: also how an unloaded form's
        // window goes, once its handler has returned.
        context.CloseWindow = closeUnshown;
        // Where the run ends, its event pump closes, so nothing it left waiting can run beside the next one (#766). That is
        // the program's end (#822).
        void Ended()
        {
            context.Events.Close();
            // The program's files close with it, so what it printed reaches the disk (#1145).
            context.CloseFiles();
            run.TrySetResult();
        }
        token.Register(() =>
        {
            // Ended from outside the program, by End, Stop or a fault: every form's window goes, shown or not.
            if (program is not null)
                program.CloseAll();
            else
                context.CloseOnce();
            Ended();
        });

        async Task StartThenShow()
        {
            try
            {
                var started = await context.StartAsync();
                if (token.IsCancellationRequested || run.Task.IsCompleted)
                    return;
                Task? closed = null;
                if (started)
                {
                    context.CloseWindow = close;
                    closed = showUntilClosed();
                }
                // A form that unloaded itself in Load is never shown, and its unload completes here (#1046 review). A form
                // whose start was a fault, or that the program ended, is never shown either, and nothing more runs (#1010).
                else if (context.UnloadedDuringStart)
                    await context.FinishUnloadAsync(closeUnshown);
                else
                    return;
                // The run lasts until the program ends: until no form is loaded (measured, #822). A host whose window can
                // close without an unload ends it there too.
                if (program is null)
                    await (closed ?? Task.CompletedTask);
                else if (closed is null)
                    await program.Ended;
                else
                    await Task.WhenAny(program.Ended, closed);
            }
            finally
            {
                Ended();
            }
        }
        StartThenShow().ListenErrors();
        return run.Task;
    }
}