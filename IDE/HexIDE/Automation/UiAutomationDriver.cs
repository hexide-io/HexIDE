using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace HexIDE.Automation;

/// <summary>
/// Reads (and, from Phase 6, drives) the live Avalonia control tree through UI Automation peers, with a
/// reflection-over-DataContext fallback. Phase 5 implements the read surface
/// (<see cref="Dump"/> / <see cref="Inspect"/> / <see cref="Resolve"/>); later phases add interaction on
/// top of <see cref="Resolve"/>. Operates on any root <see cref="Control"/> (not the live window) so the
/// addressing and discovery logic is unit-testable headlessly.
///
/// The tree is the UIA-style **control view**: structural layout wrappers (controls whose automation type
/// is <c>None</c> with no interaction provider and no keyboard focus — Panels, Borders, ContentPresenters,
/// dock plumbing, …) are transparent. They never appear as nodes or in paths; the walk descends through
/// them and re-parents their meaningful descendants. This keeps the tree shallow and paths short/stable.
///
/// Addressing: a slash path whose first segment is the literal <c>Window</c>; each subsequent segment is
/// <c>ControlType</c>, <c>ControlType[discriminator]</c>, <c>ControlType[#index]</c>, or <c>#AutomationId</c>
/// (descendant search). Match precedence within a segment: AutomationId → x:Name → ControlType+index →
/// automation label. Paths emitted by <see cref="Dump"/>/<see cref="Inspect"/> round-trip through
/// <see cref="Resolve"/>.
/// </summary>
public static class UiAutomationDriver
{
    // ── discovery ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Builds the control-view node tree rooted at <paramref name="root"/>. <paramref name="basePath"/>
    /// is the path of the root node (e.g. "Window", or a resolved subtree path) so emitted child paths
    /// round-trip.</summary>
    public static UiNode Dump(Control root, string basePath, int maxDepth, bool interactiveOnly)
    {
        var rootNode = new MeaningfulChild(root, Classify(root));
        try { return BuildNode(rootNode, basePath, 0, maxDepth, interactiveOnly) ?? BareNode(rootNode, basePath); }
        catch { return BareNode(rootNode, basePath); }
    }

    /// <summary>Deep single-node inspection: identity, provider state, and the DataContext's reflectable
    /// command/property members (the surface the Phase-7 reflection actions target).</summary>
    public static UiNodeDetail Inspect(Control control, string path)
    {
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        var providers = DescribeProviders(peer, control);

        string[] selection = [];
        if (peer.GetProvider<ISelectionProvider>() is { } sel)
            selection = Safe(() => sel.GetSelection().Select(p => p.GetName()).ToArray(), []);

        string? value = peer.GetProvider<IValueProvider>() is { } vp ? Safe<string?>(() => vp.Value, null) : null;

        bool? toggle = null;
        if (peer.GetProvider<IToggleProvider>() is { } tp)
            toggle = Safe<bool?>(() => tp.ToggleState switch
            {
                ToggleState.On => true,
                ToggleState.Off => false,
                _ => null,
            }, null);

        var rect = Safe(() => peer.GetBoundingRectangle(), default(Rect));

        return new UiNodeDetail(
            path,
            ControlTypeOf(peer),
            NameOf(control, peer),
            NullIfEmpty(AutomationProperties.GetAutomationId(control)),
            ClassNameOf(control, peer),
            control.DataContext?.GetType().Name,
            providers,
            Safe(() => peer.IsEnabled(), true),
            Safe(() => peer.IsKeyboardFocusable(), false),
            Safe(() => peer.IsOffscreen(), false),
            Hidden(control),
            [rect.X, rect.Y, rect.Width, rect.Height],
            selection,
            value,
            toggle,
            ReflectDataContextMembers(control.DataContext));
    }

    /// <summary>Reflects the public instance command/property members of a control's DataContext.</summary>
    public static VmMember[] ReflectDataContextMembers(object? dataContext)
    {
        if (dataContext is null) return [];
        var members = new List<VmMember>();
        foreach (var p in dataContext.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType))
                members.Add(new VmMember(p.Name, "command", null, false));
            else
                members.Add(new VmMember(p.Name, "property", p.PropertyType.Name, p.CanWrite,
                                         ReadValue(dataContext, p)));
        }
        return [.. members];
    }


    /// <summary>Cap on a reported value, in characters. The Immediate buffer is unbounded.</summary>
    private const int MaxValueLength = 4000;

    /// <summary>
    /// A property's current value as text, or null when it cannot be read as text.
    /// </summary>
    /// <remarks>
    /// <para><b>Only recognised types are called at all.</b> This invokes a getter, and a getter is
    /// arbitrary code: restricting it to scalars and a couple of known text holders keeps the blast radius
    /// to properties whose reads are ordinary. Anything else is listed without a value, exactly as before.</para>
    ///
    /// <para><b><c>TextDocument</c> earns its special case.</b> The Immediate window's contents are an
    /// AvaloniaEdit <c>TextDocument</c>, not a string, so a scalars-only reader would leave the single most
    /// useful piece of text in the IDE unreadable — which is the gap this closes. Its <c>Text</c> must be
    /// read on the UI thread; every caller here is already on it.</para>
    ///
    /// <para><b>Truncated rather than unbounded,</b> and it says so in the value itself. A long-running
    /// program's Debug.Print output has no natural limit, and a tool result that grows without one is a
    /// different failure from the one being fixed.</para>
    ///
    /// <para><b>A throwing getter yields null, not an exception.</b> Inspecting a control must not fail
    /// because one view-model property is unhappy; a computed getter can easily depend on state that is not
    /// there yet.</para>
    /// </remarks>
    private static string? ReadValue(object dataContext, PropertyInfo p)
    {
        if (!p.CanRead) return null;

        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
        var readable = t == typeof(string) || t.IsEnum || t.IsPrimitive
                    || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
                    || t == typeof(TimeSpan) || t == typeof(Guid)
                    || typeof(AvaloniaEdit.Document.TextDocument).IsAssignableFrom(t);
        if (!readable) return null;

        return Safe(() =>
        {
            var raw = p.GetValue(dataContext);
            var text = raw switch
            {
                null => null,
                AvaloniaEdit.Document.TextDocument doc => doc.Text,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => raw.ToString(),
            };
            return text is { Length: > MaxValueLength }
                ? text[..MaxValueLength] + $"… [truncated at {MaxValueLength} chars]"
                : text;
        }, null);
    }

    /// <summary>
    /// The action tokens a control actually accepts. Mostly its automation providers, but not only:
    /// <see cref="Interact"/> has fallbacks for controls whose peer offers nothing, and a token missing
    /// here is a token nobody tries. <c>MenuItemAutomationPeer</c> exposes <b>no providers at all</b>, so
    /// reporting the peer verbatim would advertise a menu as undrivable while every verb on it works.
    /// </summary>
    public static string[] DescribeProviders(AutomationPeer peer, Control? control = null)
    {
        var list = new List<string>();
        if (peer.GetProvider<IInvokeProvider>() is not null) list.Add("invoke");
        if (peer.GetProvider<ISelectionProvider>() is not null) list.Add("selection");
        if (peer.GetProvider<ISelectionItemProvider>() is not null) list.Add("selectionItem");
        if (peer.GetProvider<IValueProvider>() is not null) list.Add("value");
        if (peer.GetProvider<IToggleProvider>() is not null) list.Add("toggle");
        if (peer.GetProvider<IExpandCollapseProvider>() is not null) list.Add("expandCollapse");
        if (peer.GetProvider<IRangeValueProvider>() is not null) list.Add("rangeValue");
        if (peer.GetProvider<IScrollProvider>() is not null) list.Add("scroll");

        // Advertised so the verb is discoverable: a control owning a context menu or a flyout accepts
        // expand/collapse even though its peer offers no ExpandCollapse provider. Without this the action
        // exists and nothing says so, which is how the eight toolbar Add commands came to be recorded as
        // unreachable.
        if (control is not null
            && (control.ContextMenu is not null || FlyoutsOf(control).Any())
            && !list.Contains("expandCollapse"))
            list.Add("expandCollapse");

        if (control is MenuItem menuItem)
        {
            if (!list.Contains("invoke")) list.Add("invoke");
            // Only where there is a submenu to open: advertising expandCollapse on a leaf would promise
            // an action that correctly refuses.
            if (menuItem.HasSubMenu && !list.Contains("expandCollapse")) list.Add("expandCollapse");
        }

        // A DataGridRow's peer offers NOTHING — not selectionItem, not invoke — so a grid was the one
        // common control an automation client could read and not drive. Selecting a row is how a
        // master-detail window is used at all, and the client's only recourse was a VM property whose type
        // is a row object no string can express. Interact selects it through the owning grid.
        if (control is DataGridRow && !list.Contains("selectionItem")) list.Add("selectionItem");

        return [.. list];
    }

    // ── interaction (Phase 6) ───────────────────────────────────────────────────────────────────────

    /// <summary>Drives a resolved control through its automation provider. Provider-backed actions only
    /// (Phase 6): <c>invoke | select | set_value | toggle | expand | collapse</c>. A missing provider
    /// yields a clean "element does not support '&lt;action&gt;'" error — the caller's signal to inspect
    /// and switch to a Phase-7 reflection action.</summary>
    public static InteractOutcome Interact(Control control, string action, string? value)
    {
        var norm = new string((action ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (norm.Length == 0) return Err("action is required");

        AutomationPeer peer;
        try { peer = ControlAutomationPeer.CreatePeerForElement(control); }
        catch (Exception ex) { return Err($"could not create automation peer: {ex.Message}"); }

        try
        {
            switch (norm)
            {
                case "invoke":
                    if (peer.GetProvider<IInvokeProvider>() is { } inv)
                    {
                        inv.Invoke();
                        return Ok($"invoked {ControlTypeOf(peer)} '{LabelOf(control, peer)}'");
                    }
                    // MenuItemAutomationPeer exposes NO providers at all — not invoke, not
                    // expandCollapse — so every verb failed on a menu and none of it was reachable.
                    // Raising Click is what a real click does: MenuItem's class handler for the event
                    // executes Command, so both plain Click handlers and bound commands fire once.
                    if (control is MenuItem clickItem)
                    {
                        clickItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                        return Ok($"invoked MenuItem '{LabelOf(control, peer)}'");
                    }
                    return Unsupported("invoke");

                case "toggle":
                    if (peer.GetProvider<IToggleProvider>() is not { } tog) return Unsupported("toggle");
                    tog.Toggle();
                    return Ok($"toggled '{LabelOf(control, peer)}'");

                case "expand":
                    if (peer.GetProvider<IExpandCollapseProvider>() is { } exp)
                    {
                        exp.Expand();
                        return Ok($"expanded '{LabelOf(control, peer)}'");
                    }
                    // A MenuItem exposes no ExpandCollapse provider, so without this a menu could not be
                    // opened through automation at all — and a menu that cannot be opened cannot be read
                    // either, since its items are only realised once the popup is up.
                    if (control is MenuItem { HasSubMenu: true } openMenu)
                    {
                        openMenu.Open();
                        return Ok($"opened menu '{LabelOf(control, peer)}'");
                    }
                    // A deterministic route to a context menu. press_key Apps also opens one -- watched
                    // on screen -- but it goes through ContextRequested and lands on whichever control the
                    // input layer decides, which is not necessarily the one addressed; this opens the menu
                    // belonging to the control named, and says so.
                    // Focused first, and that is not cosmetic. A popup opened over an UNFOCUSED owner
                    // light-dismisses almost immediately: press_key Apps opens the Project Explorer's menu
                    // and it is gone before the next MCP call can walk it -- watched on screen, "it did pop
                    // up a couple of times ... but it closed too soon". A dump that runs a round-trip later
                    // then reports nothing, which reads exactly like a menu that never opened. The same
                    // trap as the transient tooltip, one layer up.
                    if (control.ContextMenu is { } contextMenu)
                    {
                        Safe(() => control.Focus(), false);
                        contextMenu.Open(control);
                        return Ok($"opened context menu on '{LabelOf(control, peer)}'");
                    }
                    if (FlyoutsOf(control).FirstOrDefault() is { } flyout)
                    {
                        Safe(() => control.Focus(), false);
                        flyout.ShowAt(control);
                        return Ok($"opened flyout on '{LabelOf(control, peer)}'");
                    }
                    return Unsupported("expand");

                case "collapse":
                    if (peer.GetProvider<IExpandCollapseProvider>() is { } col)
                    {
                        col.Collapse();
                        return Ok($"collapsed '{LabelOf(control, peer)}'");
                    }
                    if (control is MenuItem { HasSubMenu: true } closeMenu)
                    {
                        closeMenu.Close();
                        return Ok($"closed menu '{LabelOf(control, peer)}'");
                    }
                    if (control.ContextMenu is { IsOpen: true } openContextMenu)
                    {
                        openContextMenu.Close();
                        return Ok($"closed context menu on '{LabelOf(control, peer)}'");
                    }
                    if (FlyoutsOf(control).FirstOrDefault(f => f.IsOpen) is { } openFlyout)
                    {
                        openFlyout.Hide();
                        return Ok($"closed flyout on '{LabelOf(control, peer)}'");
                    }
                    return Unsupported("collapse");

                case "setvalue":
                    if (peer.GetProvider<IValueProvider>() is not { } val) return Unsupported("set_value");
                    if (value is null) return Err("set_value requires 'value'");
                    val.SetValue(value);
                    return Ok($"set value of '{LabelOf(control, peer)}' to '{value}'");

                case "select":
                    return DoSelect(control, peer, value);

                // Phase 7 — reflection over the DataContext (opt-in: there is NO implicit fallback from the
                // provider actions above). Reaches VM commands/properties on controls with no useful provider.
                case "invokecommand":
                    return ReflectInvokeCommand(control, value);

                case "setproperty":
                    return ReflectSetProperty(control, value);

                default:
                    return Err($"unknown action '{action}' (expected invoke|select|set_value|toggle|expand|collapse|invoke_command|set_property)");
            }
        }
        catch (Exception ex)
        {
            return Err($"action '{norm}' threw: {ex.Message}");
        }
    }

    private static InteractOutcome DoSelect(Control control, AutomationPeer peer, string? value)
    {
        // value absent (treating "" as absent, like "omit if the target is the item") → select the target.
        value = NullIfEmpty(value);
        if (value is null)
        {
            if (peer.GetProvider<ISelectionItemProvider>() is { } selfSip)
            {
                selfSip.Select();
                return Ok($"selected '{LabelOf(control, peer)}'");
            }

            if (TrySelectThroughOwningGrid(control))
                return Ok($"selected '{LabelOf(control, peer)}'");

            return Unsupported("select");
        }

        // value present → match against the realized selectable items (the target itself if it is one, plus
        // its selectable descendants) using the SAME precedence + uniqueness as path resolution, so the two
        // addressing paths agree and an ambiguous text never silently selects the wrong row.
        var candidates = SelectableCandidates(control);
        if (candidates.Count == 0)
            return Err($"'{LabelOf(control, peer)}' has no selectable items realized — if it's a dropdown, 'expand' it first, then dump_visual_tree to read the item text");

        var (matches, facet) = MatchByDiscriminator(candidates, value);
        if (matches.Count == 0)
            return Err($"no selectable item matching '{value}' among {candidates.Count} realized item(s) — check the exact text via dump_visual_tree");
        if (matches.Count > 1)
            return Err($"ambiguous select '{value}' ({matches.Count} {facet} matches); target the item directly by path instead");

        if (ControlAutomationPeer.CreatePeerForElement(matches[0]).GetProvider<ISelectionItemProvider>() is { } sip)
        {
            sip.Select();
            return Ok($"selected '{value}'");
        }

        if (TrySelectThroughOwningGrid(matches[0])) return Ok($"selected '{value}'");

        return Unsupported("select");
    }

    /// <summary>
    /// Selects a <see cref="DataGridRow"/> the only way there is: through the grid that owns it.
    /// </summary>
    /// <remarks>
    /// <b>A DataGridRow's automation peer exposes no providers at all</b>, so <c>select</c> refused on
    /// every grid row in the IDE and there was no second route: the reflection actions set a VM property
    /// by name and coerce from a string, and a selected row is an object no string names. That left the
    /// protocol inspector's central gesture — click a row, read its body — undrivable, and a UI surface an
    /// automation client can read but not operate is not a surface.
    ///
    /// <para>
    /// The grid is found by walking up rather than through <c>DataGridRow.OwningGrid</c>, which is
    /// internal. Setting <c>SelectedItem</c> is what a click does; the return value is read back rather
    /// than assumed, because a grid in single-selection mode can decline.
    /// </para>
    /// </remarks>
    private static bool TrySelectThroughOwningGrid(Control control)
    {
        if (control is not DataGridRow row) return false;
        if (control.FindAncestorOfType<DataGrid>() is not { } grid) return false;
        if (row.DataContext is not { } item) return false;

        grid.SelectedItem = item;
        return ReferenceEquals(grid.SelectedItem, item);
    }

    // Realized selectable items reachable from a container: the container itself if it exposes
    // ISelectionItemProvider, plus every descendant that does.
    private static List<MeaningfulChild> SelectableCandidates(Control container)
    {
        var result = new List<MeaningfulChild>();
        foreach (var c in new[] { container }.Concat(Descendants(container)))
        {
            var info = Classify(c);
            if (info.Peer is not null
                && (info.Peer.GetProvider<ISelectionItemProvider>() is not null || c is DataGridRow))
            {
                result.Add(new MeaningfulChild(c, info));
            }
        }
        return result;
    }

    // value = the command name (a public ICommand property on the target's DataContext).
    private static InteractOutcome ReflectInvokeCommand(Control control, string? value)
    {
        try
        {
            if (NullIfEmpty(value) is not { } name) return ReflectErr("invoke_command requires 'value' = the command name");
            if (control.DataContext is not { } dc) return ReflectErr("target has no DataContext");
            if (dc.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { } prop)
                return ReflectErr($"no public property '{name}' on {dc.GetType().Name}");
            if (prop.GetValue(dc) is not System.Windows.Input.ICommand cmd)
                return ReflectErr($"'{name}' on {dc.GetType().Name} is not an ICommand");
            if (!cmd.CanExecute(null))
                return ReflectErr($"'{name}'.CanExecute(null) returned false");
            cmd.Execute(null);
            return new InteractOutcome(true, "reflection", $"executed command '{name}' on {dc.GetType().Name}", null);
        }
        catch (Exception ex) { return ReflectErr($"invoke_command '{value}' threw: {ex.Message}"); }
    }

    // value = "PropertyName=NewValue" (split on the first '='); the value is coerced to the property type.
    private static InteractOutcome ReflectSetProperty(Control control, string? value)
    {
        try
        {
            if (value is null) return ReflectErr("set_property requires 'value' = \"PropertyName=NewValue\"");
            var eq = value.IndexOf('=');
            if (eq <= 0) return ReflectErr("set_property 'value' must be \"PropertyName=NewValue\"");
            var name = value[..eq].Trim();
            var raw = value[(eq + 1)..];
            if (control.DataContext is not { } dc) return ReflectErr("target has no DataContext");
            if (dc.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { CanWrite: true } prop)
                return ReflectErr($"no writable public property '{name}' on {dc.GetType().Name}");
            object? coerced;
            try { coerced = Coerce(raw, prop.PropertyType); }
            catch (Exception ex) { return ReflectErr($"cannot convert '{raw}' to {prop.PropertyType.Name} for '{name}': {ex.Message}"); }
            prop.SetValue(dc, coerced);
            return new InteractOutcome(true, "reflection", $"set {dc.GetType().Name}.{name} = {raw}", null);
        }
        catch (Exception ex) { return ReflectErr($"set_property '{value}' threw: {ex.Message}"); }
    }

    private static object? Coerce(string raw, Type target)
    {
        var t = Nullable.GetUnderlyingType(target) ?? target;
        if (t == typeof(string)) return raw;
        if (t.IsEnum) return Enum.Parse(t, raw, ignoreCase: true);
        if (t == typeof(bool)) return bool.Parse(raw);
        return Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);
    }

    // ── keyboard input (Phase 10) ────────────────────────────────────────────────────────────────────

    /// <summary>Types text into the resolved control (or its nearest text surface) by inserting at the
    /// caret via the control's own API — reliable and exact (no synthetic keystrokes, so live
    /// auto-indent/IntelliSense don't garble it). Multi-line text is inserted verbatim.</summary>
    public static InteractOutcome TypeText(Control control, string text)
    {
        try
        {
            if (FindTextSurface(control) is not { } surface)
                return new InteractOutcome(false, "keyboard", null,
                    $"'{control.GetType().Name}' has no text surface (TextEditor/TextArea/TextBox) to type into");

            surface.Focus();
            switch (surface)
            {
                case TextEditor editor:
                    var eo = editor.CaretOffset;
                    editor.Document.Insert(eo, text);
                    editor.CaretOffset = eo + text.Length;
                    break;
                case TextArea area:
                    var ao = area.Caret.Offset;
                    area.Document.Insert(ao, text);
                    area.Caret.Offset = ao + text.Length;
                    break;
                case TextBox box:
                    var at = Math.Clamp(box.CaretIndex, 0, box.Text?.Length ?? 0);
                    box.Text = (box.Text ?? string.Empty).Insert(at, text);
                    box.CaretIndex = at + text.Length;
                    break;
            }
            return new InteractOutcome(true, "keyboard", $"typed {text.Length} char(s) into {surface.GetType().Name}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, "keyboard", null, $"type_text threw: {ex.Message}"); }
    }

    /// <summary>Presses a key (optionally with modifiers) on the resolved control by raising real
    /// KeyDown/KeyUp events — for navigation/commands (Enter, Tab, Backspace, Escape, Ctrl+S, …) that
    /// `type_text` doesn't cover.</summary>
    public static InteractOutcome PressKey(Control control, string key, string? modifiers)
    {
        try
        {
            if (!Enum.TryParse<Key>((key ?? string.Empty).Trim(), ignoreCase: true, out var parsedKey))
                return new InteractOutcome(false, "keyboard", null,
                    $"unknown key '{key}' — use an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, …)");
            if (!TryParseModifiers(modifiers, out var mods, out var modError))
                return new InteractOutcome(false, "keyboard", null, modError);

            if (FindKeyTarget(control) is not { } target)
                return new InteractOutcome(false, "keyboard", null,
                    $"'{Describe(control)}' has nothing under it that can take keyboard focus, so a key "
                  + "press there would go nowhere. Address a focusable control, or a text surface.");

            target.Focus();
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = parsedKey, KeyModifiers = mods });
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = parsedKey, KeyModifiers = mods });

            // Naming the receiver is half the point. Both gaps this closes presented as "reports success and
            // nothing happens", and in each the answer was that the event went to a control other than the
            // one the caller meant — which the old reply had no way to say.
            var chord = mods == KeyModifiers.None ? string.Empty : mods + "+";
            var where = ReferenceEquals(target, control) ? string.Empty : $" on {Describe(target)}";
            return new InteractOutcome(true, "keyboard", $"pressed {chord}{parsedKey}{where}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, "keyboard", null, $"press_key threw: {ex.Message}"); }
    }

    /// <summary>
    /// Moves the pointer onto a control so hover-only affordances appear.
    /// </summary>
    /// <remarks>
    /// <b>A hover is a position, not just an event.</b> The code editor's handler reads
    /// <c>e.GetPosition(TextView)</c> and converts it to a text location, so an event carrying no usable
    /// point produces no tip however correctly it is routed. The point is therefore computed and passed
    /// in top-level coordinates, which is what <c>PointerEventArgs</c> translates from.
    ///
    /// <para>
    /// <b>The default is the CARET, not the centre, for an AvaloniaEdit surface.</b> Hovering the middle
    /// of a code editor is meaningless — what a caller wants is a particular identifier — and the caret is
    /// already positionable through <c>interact set_property CaretOffset</c>. So "hover this identifier"
    /// becomes two steps a caller already knows, rather than a new addressing scheme that would have to
    /// convert a text offset to a pixel on their behalf. Anything else hovers at its centre.
    /// </para>
    ///
    /// <para>
    /// Raises <c>PointerEntered</c> then <c>PointerMoved</c>: the first is what a tooltip service waits
    /// for, the second is what the editor's own handler listens to. Neither the dwell nor whatever the
    /// tip needs afterwards happens here — this returns as soon as the events are delivered, and the
    /// caller waits.
    /// </para>
    /// </remarks>
    public static InteractOutcome Hover(Control control, double? x, double? y)
    {
        try
        {
            if (TopLevel.GetTopLevel(control) is not { } topLevel)
                return new InteractOutcome(false, "pointer", null,
                    "the control is not attached to a window, so it has no coordinates to hover at");

            var target = FindHoverTarget(control);
            var local = HoverPoint(target, x, y);
            if (target.TranslatePoint(local, topLevel) is not { } atTopLevel)
                return new InteractOutcome(false, "pointer", null,
                    $"could not translate ({local.X:0.#}, {local.Y:0.#}) on '{Describe(target)}' into window coordinates");

            var pointer = new Avalonia.Input.Pointer(
                Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);

            foreach (var routed in new[] { InputElement.PointerEnteredEvent, InputElement.PointerMovedEvent })
            {
                target.RaiseEvent(new PointerEventArgs(
                    routed, target, pointer, topLevel, atTopLevel,
                    (ulong)Environment.TickCount64, props, KeyModifiers.None));
            }

            var where = ReferenceEquals(target, control) ? string.Empty : $" on {Describe(target)}";
            return new InteractOutcome(true, "pointer",
                $"hovered ({local.X:0.#}, {local.Y:0.#}){where}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, "pointer", null, $"hover threw: {ex.Message}"); }
    }

    /// <summary>The element a hover should land on — the editor's text view, else the control itself.</summary>
    private static Control FindHoverTarget(Control control)
    {
        if (control is TextEditor or TextArea) return control;
        return control.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextEditor) ?? control;
    }

    /// <summary>Where on the control to hover: an explicit point, else the caret, else the centre.</summary>
    private static Point HoverPoint(Control target, double? x, double? y)
    {
        if (x is { } px && y is { } py) return new Point(px, py);

        var area = target as TextArea ?? (target as TextEditor)?.TextArea;
        if (area?.TextView is { } view && view.VisualLinesValid)
        {
            var caret = area.Caret.Position;
            var visual = view.GetVisualPosition(
                new TextViewPosition(caret.Line, caret.Column), VisualYPosition.TextMiddle);

            // GetVisualPosition is in document coordinates; the scroll offset is what makes it a point on
            // screen. A caret scrolled out of view yields a point outside the control, which is honest --
            // you cannot hover what is not shown.
            var point = visual - view.ScrollOffset;
            if (view.TranslatePoint(point, target) is { } onTarget) return onTarget;
        }

        return new Point(target.Bounds.Width / 2, target.Bounds.Height / 2);
    }
    /// <summary>
    /// Where a synthetic key press should be raised, or null when nothing under here could receive one.
    /// </summary>
    /// <remarks>
    /// <b>Deliberately not <see cref="FindTextSurface"/>, and the difference is the whole of two recorded
    /// gaps.</b> A routed event raised on an element travels down to it and back up from it — so anything
    /// BELOW the element chosen is on neither leg, and a handler attached there cannot fire however correct
    /// it is. <c>press_key</c> therefore wants the DEEPEST input surface; <c>type_text</c> wants the
    /// outermost, because it drives the editor's own insert API rather than the event system. The two tools
    /// want opposite ends of the same chain, which is why they no longer share a resolver.
    ///
    /// <para>
    /// Concretely: AvaloniaEdit nests <c>TextEditor</c> → <c>TextArea</c> → <c>TextView</c>, and the code
    /// editor attaches its key handling to <c>TextArea</c>. Resolving to <c>TextEditor</c> made every
    /// keyboard command in the editor undrivable while reporting success — go to definition, Enter
    /// auto-indent, format document, the edit-while-running prompt.
    /// </para>
    ///
    /// <para>
    /// The focusable fallback closes the other half: addressing a dock container resolved to the container
    /// itself, which cannot take focus, so the key went nowhere and the reply still said success.
    /// </para>
    /// </remarks>
    private static Control? FindKeyTarget(Control control)
    {
        if (control is TextArea or TextBox) return control;
        if (control is TextEditor outer) return outer.TextArea;

        var descendants = control.GetVisualDescendants().OfType<Control>().ToList();

        // An AvaloniaEdit surface, innermost first: its TextArea is where key handling lives.
        if (descendants.FirstOrDefault(c => c is TextArea) is { } area) return area;
        if (descendants.FirstOrDefault(c => c is TextEditor) is TextEditor editor) return editor.TextArea;

        if (descendants.FirstOrDefault(c => c is TextBox) is { } box) return box;

        // Nothing text-shaped: any focusable descendant will at least receive the event, and the caller is
        // told which one. Falling back to the addressed control is last, and only when it can take focus —
        // returning a container that cannot is what made a dead key press look like a successful one.
        return descendants.FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible)
            ?? (control.Focusable ? control : null);
    }

    /// <summary>A control named the way a caller would recognise it: type plus whatever identity it carries.</summary>
    private static string Describe(Control control)
    {
        var id = control.Name ?? (control as StyledElement)?.Name;
        return string.IsNullOrEmpty(id) ? control.GetType().Name : $"{control.GetType().Name}[{id}]";
    }

    // The nearest editable text surface: the control itself, else its first AvaloniaEdit editor/area, else
    // a plain TextBox descendant (editor surfaces are preferred over incidental TextBoxes like combo edits).
    // Used by type_text, which drives each surface's own insert API — see FindKeyTarget for why press_key
    // must not share this.
    private static Control? FindTextSurface(Control control)
    {
        if (control is TextEditor or TextArea or TextBox) return control;
        var descendants = control.GetVisualDescendants().OfType<Control>().ToList();
        return descendants.FirstOrDefault(c => c is TextEditor)
            ?? descendants.FirstOrDefault(c => c is TextArea)
            ?? descendants.FirstOrDefault(c => c is TextBox);
    }

    private static bool TryParseModifiers(string? modifiers, out KeyModifiers result, out string? error)
    {
        result = KeyModifiers.None;
        error = null;
        if (string.IsNullOrWhiteSpace(modifiers)) return true;
        foreach (var part in modifiers.Split(['+', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl" or "control": result |= KeyModifiers.Control; break;
                case "shift": result |= KeyModifiers.Shift; break;
                case "alt": result |= KeyModifiers.Alt; break;
                case "meta" or "win" or "cmd": result |= KeyModifiers.Meta; break;
                default: error = $"unknown modifier '{part}' (use Ctrl/Shift/Alt/Meta)"; result = KeyModifiers.None; return false;
            }
        }
        return true;
    }

    private static string LabelOf(Control control, AutomationPeer peer) => NameOf(control, peer) ?? ControlTypeOf(peer);

    private static InteractOutcome Ok(string detail) => new(true, "peer", detail, null);
    private static InteractOutcome Err(string error) => new(false, "peer", null, error);
    private static InteractOutcome Unsupported(string action) => new(false, "peer", null, $"element does not support '{action}'");
    private static InteractOutcome ReflectErr(string error) => new(false, "reflection", null, error);

    // ── addressing ────────────────────────────────────────────────────────────────────────────────

    /// <summary>Resolves a control-view slash path (rooted at <paramref name="root"/>, first segment
    /// "Window") to a single control, or an error explaining the miss / ambiguity.</summary>
    public static (Control? control, string? error) Resolve(Control root, string targetPath)
    {
        var segs = (targetPath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0)
            return (null, "empty target path");
        if (!string.Equals(StripUnderscore(segs[0]), "Window", StringComparison.OrdinalIgnoreCase))
            return (null, $"path must start with 'Window' (got '{segs[0]}')");

        Control current = root;
        var traversed = "Window";
        for (var i = 1; i < segs.Length; i++)
        {
            var (next, err) = ResolveSegment(current, segs[i], traversed);
            if (next is null) return (null, err);
            current = next;
            traversed += "/" + segs[i];
        }
        return (current, null);
    }

    private static (Control? control, string? error) ResolveSegment(Control parent, string raw, string parentPath)
    {
        var seg = ParseSegment(raw);

        // #AutomationId — search descendants at any depth (structural or not).
        if (seg.IdShortcut is not null)
        {
            var hits = Descendants(parent)
                .Where(c => string.Equals(NullIfEmpty(AutomationProperties.GetAutomationId(c)), seg.IdShortcut,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Pick(hits, raw, parentPath, "automationId");
        }

        // Candidate set is the control-view children (structural wrappers collapsed), matching Dump.
        var children = new List<MeaningfulChild>();
        CollectMeaningfulChildren(parent, children);
        var typed = seg.Type is null
            ? children
            : children.Where(m => string.Equals(m.Info.Type, seg.Type, StringComparison.OrdinalIgnoreCase)).ToList();

        if (seg.Index is { } ix)
        {
            if (ix < 0 || ix >= typed.Count)
                return (null, $"index [#{ix}] out of range for '{seg.Type}' under '{parentPath}' ({typed.Count} found)");
            return (typed[ix].Control, null);
        }

        if (seg.Discriminator is { } disc)
        {
            var (matches, facet) = MatchByDiscriminator(typed, disc);
            return Pick(matches, raw, parentPath, facet);
        }

        return Pick([.. typed.Select(m => m.Control)], raw, parentPath, seg.Type ?? "child");
    }

    // The single source of truth for `Type[discriminator]` matching, shared by ResolveSegment and (for
    // round-trip verification) BestSegment so the emitter can never pick a discriminator the resolver
    // would route elsewhere. Precedence: AutomationId → x:Name → automation label; the first facet with
    // any match wins (and must be unique to be decisive).
    private static (List<Control> matches, string facet) MatchByDiscriminator(List<MeaningfulChild> typed, string disc)
    {
        var byId = typed.Where(m => string.Equals(m.Info.AutoId, disc, StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Control).ToList();
        if (byId.Count > 0) return (byId, "automationId");

        var byName = typed.Where(m => NameEquals(m.Control.Name, disc)).Select(m => m.Control).ToList();
        if (byName.Count > 0) return (byName, "name");

        var byLabel = typed.Where(m => NameEquals(Safe<string?>(() => m.Info.Peer?.GetName(), null), disc))
            .Select(m => m.Control).ToList();
        if (byLabel.Count > 0) return (byLabel, "label");

        return ([], "match");
    }

    private static (Control? control, string? error) Pick(List<Control> matches, string raw, string parentPath, string by)
    {
        if (matches.Count == 0) return (null, $"no child matching '{raw}' under '{parentPath}'");
        if (matches.Count > 1)
            return (null, $"ambiguous segment '{raw}' under '{parentPath}' ({matches.Count} {by} matches); add [#index] or a Name/AutomationId");
        return (matches[0], null);
    }

    private readonly record struct Seg(string? Type, string? Discriminator, int? Index, string? IdShortcut);

    private static Seg ParseSegment(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('#'))
            return new Seg(null, null, null, raw[1..].Trim());
        var lb = raw.IndexOf('[');
        if (lb < 0)
            return new Seg(raw, null, null, null);
        var type = raw[..lb].Trim();
        var rb = raw.IndexOf(']', lb + 1);
        var inside = (rb > lb ? raw[(lb + 1)..rb] : raw[(lb + 1)..]).Trim();
        if (inside.StartsWith('#') && int.TryParse(inside[1..], out var ix))
            return new Seg(type, null, ix, null);
        return new Seg(type, inside.Length == 0 ? null : inside, null, null);
    }

    // ── control-view tree walk ──────────────────────────────────────────────────────────────────────

    private static UiNode? BuildNode(MeaningfulChild node, string path, int depth, int maxDepth, bool interactiveOnly)
    {
        var info = node.Info;
        var selfInteractive = info.Providers.Length > 0 || info.Focusable;

        var children = new List<UiNode>();
        if (depth < maxDepth)
        {
            var metas = new List<MeaningfulChild>();
            CollectMeaningfulChildren(node.Control, metas);
            foreach (var m in metas)
            {
                UiNode? child;
                try { child = BuildNode(m, path + "/" + BestSegment(m, metas), depth + 1, maxDepth, interactiveOnly); }
                catch { child = null; }   // one pathological control must not abort the whole dump
                if (child is not null) children.Add(child);
            }
        }

        if (interactiveOnly && !selfInteractive && children.Count == 0)
            return null;

        return MakeNode(node, path, [.. children]);
    }

    private static UiNode BareNode(MeaningfulChild node, string path) => MakeNode(node, path, []);

    private static UiNode MakeNode(MeaningfulChild node, string path, UiNode[] children)
    {
        var info = node.Info;
        return new UiNode(
            path,
            info.Type,
            info.Name,
            info.AutoId,
            info.Peer is null ? node.Control.GetType().Name : ClassNameOf(node.Control, info.Peer),
            node.Control.DataContext?.GetType().Name,
            info.Providers,
            info.Peer is null || Safe(() => info.Peer.IsEnabled(), true),
            info.Peer is not null && Safe(() => info.Peer.IsOffscreen(), false),
            Hidden(node.Control),
            children);
    }


    /// <summary>
    /// <c>true</c> when a control is in the tree but not on screen; null when it is showing.
    /// </summary>
    /// <remarks>
    /// <para><b>Effective visibility, not the local flag.</b> A control can be <c>IsVisible</c> itself and
    /// still be invisible because an ancestor is collapsed, so reporting the local property would move the
    /// same trap up one level rather than close it.</para>
    ///
    /// <para><b>Not the same question as <c>IsOffscreen</c>,</b> which is a UIA concept about clipping and
    /// scroll position: a control scrolled out of view is offscreen and visible, and a collapsed one is
    /// visible-to-UIA and not showing. Neither answers "is this on screen", which is the whole meaning of a
    /// banner, a validation message, an overlay or an empty-state placeholder.</para>
    ///
    /// <para><b>Null rather than false</b> so it is absent from the overwhelmingly common case and a dump
    /// stays readable — a hidden node is the exception worth spelling out, and the tree is already long.</para>
    /// </remarks>
    private static bool? Hidden(Control control) =>
        Safe(() => control.IsEffectivelyVisible, true) ? null : true;

    // Nearest control-view children: descends transparently through non-Control visuals AND structural
    // control wrappers, collecting the closest meaningful Control descendants.
    private static void CollectMeaningfulChildren(Visual parent, List<MeaningfulChild> acc)
    {
        // Asked of the PARENT, not of each child, and the difference is where the items end up. A flyout's
        // popup is not a visual child of its button, so grafting it while iterating the button's siblings
        // put eight Add commands next to the toolbar buttons instead of inside the one that owns them --
        // and left a dump rooted AT the button reporting no children at all, because the button's own
        // popup was never considered. Here they are children of their owner, which is both where they
        // appear on screen and what makes the path round-trip.
        if (parent is Control owner && AttachedPopupOf(owner) is { } attached)
            CollectPopupChildren(attached, acc);

        foreach (var v in parent.GetVisualChildren())
        {
            if (v is Popup popup)
            {
                CollectPopupChildren(popup, acc);
            }
            else if (v is Control c)
            {
                var info = Classify(c);
                if (info.Structural) CollectMeaningfulChildren(c, acc);
                else acc.Add(new MeaningfulChild(c, info));
            }
            else
            {
                CollectMeaningfulChildren(v, acc);
            }
        }
    }

    // A popup's content is NOT under the popup in this visual tree — it is realised in the popup's own
    // root (a separate top-level window on desktop, an overlay host elsewhere). So a dropped-down menu,
    // a combo's list, and a flyout are all invisible to a plain visual walk, which is why an open
    // MenuItem used to report "children": [] and its items could not be addressed at all.
    //
    // Grafting the popup's content in at the popup's own position keeps paths reading the way the UI
    // looks — Window/Menu/MenuItem[File]/MenuItem[Open] — and, because Resolve walks through this same
    // collector, those paths round-trip straight back to the live control for interact/press_key.
    private static void CollectPopupChildren(Popup popup, List<MeaningfulChild> acc)
    {
        // A closed popup has no realised content: its items genuinely do not exist yet, so there is
        // nothing to report and nothing to address. Open it first (interact expand).
        if (!popup.IsOpen) return;
        if (popup.Child is Visual content) CollectMeaningfulChildren(content, acc);
    }


    /// <summary>
    /// The open popup a control owns <i>logically</i> rather than visually — a context menu or a flyout —
    /// or null when it owns none or it is closed.
    /// </summary>
    /// <remarks>
    /// <para><b>Why a second route exists at all.</b> A menu-bar dropdown's <c>Popup</c> is a visual child
    /// of its <c>MenuItem</c>, so the ordinary walk finds it. A <c>ContextMenu</c> and a <c>Button.Flyout</c>
    /// are attached with <c>ISetLogicalParent.SetParent</c> instead: the popup is never a visual child of
    /// anything, so a visual walk cannot reach it however deep it goes. That asymmetry is what made menus
    /// look like they worked while context menus and toolbar flyouts reported <c>children: []</c> — one
    /// cause wearing two faces, recorded as two separate entries in docs/mcp-server-gaps.md.</para>
    ///
    /// <para><b>Found from the owner, not by enumerating popup roots.</b> Avalonia exposes no public list of
    /// live <c>PopupRoot</c>s, but every one of these popups is reachable from the control that owns it —
    /// which the visual walk is already standing on.</para>
    ///
    /// <para><b>The two routes differ, and neither generalises to the other.</b> A flyout hands over its
    /// <c>Popup</c> directly. An open <c>ContextMenu</c> does not: it is realised as the popup's
    /// <i>child</i>, so the popup is its LOGICAL PARENT. Both were measured before being relied on.</para>
    ///
    /// <para>Closed popups are skipped by <see cref="CollectPopupChildren"/>: nothing is realised, so there
    /// is nothing to report and nothing to address. Open it first.</para>
    /// </remarks>

    /// <summary>
    /// <see cref="AttachedPopupOf"/>, for the snapshot composer: the same question, asked by the code that
    /// renders rather than the code that walks. One definition, so a popup a caller can address is always a
    /// popup they can also see.
    /// </summary>
    internal static Popup? AttachedPopupOfPublic(Control c) => AttachedPopupOf(c);

    private static Popup? AttachedPopupOf(Control c)
    {
        if (c.ContextMenu is { IsOpen: true } menu)
            return menu.GetLogicalParent() as Popup;

        foreach (var f in FlyoutsOf(c))
            if (f is PopupFlyoutBase { IsOpen: true } open)
                return open.Popup;
        return null;
    }

    /// <summary>
    /// Every flyout a control owns, in the order <c>expand</c> should prefer them.
    /// </summary>
    /// <remarks>
    /// <b>Three routes, and missing one is indistinguishable from the popup not existing.</b>
    /// <c>Button.Flyout</c> is the toolbar case; <c>FlyoutBase.GetAttachedFlyout</c> is the attached-property
    /// form; and <c>Control.ContextFlyout</c> is how a context menu is usually written in this codebase —
    /// the Project Explorer's is a <c>MenuFlyout</c> on <c>TreeView.ContextFlyout</c>
    /// (Tools/Projects/ProjectToolView.axaml:125), not a <c>ContextMenu</c> at all. Omitting that one made
    /// an open, plainly visible menu report nothing, which reads exactly like a menu that never opened —
    /// and was twice diagnosed that way before the AXAML was checked.
    /// </remarks>
    private static IEnumerable<FlyoutBase> FlyoutsOf(Control c)
    {
        var primary = c switch
        {
            Button b => b.Flyout,
            SplitButton sb => sb.Flyout,
            _ => FlyoutBase.GetAttachedFlyout(c),
        };
        if (primary is not null) yield return primary;
        if (c.ContextFlyout is { } context && !ReferenceEquals(context, primary)) yield return context;
    }

    // All Control descendants at any depth (for the #AutomationId shortcut). Crosses into open popups
    // for the same reason the control-view walk does — otherwise #SomeId finds an item on the menu bar
    // but never one inside an open menu.
    private static IEnumerable<Control> Descendants(Visual parent)
    {
        foreach (var v in parent.GetVisualChildren())
        {
            if (v is Popup { IsOpen: true } popup)
            {
                if (popup.Child is Visual content)
                    foreach (var d in Descendants(content)) yield return d;
            }
            else if (v is Control c)
            {
                yield return c;
                // Same crossing as the control-view walk: without it #SomeId finds an item on the menu bar
                // but never one inside an open context menu or toolbar flyout.
                if (AttachedPopupOf(c) is { Child: Visual attached })
                    foreach (var d in Descendants(attached)) yield return d;
                foreach (var d in Descendants(c)) yield return d;
            }
            else
            {
                foreach (var d in Descendants(v)) yield return d;
            }
        }
    }

    // The cheapest discriminator that uniquely identifies a child among its (control-view) siblings:
    // AutomationId, else a unique Name, else a positional index among same-type siblings.
    private static string BestSegment(MeaningfulChild m, List<MeaningfulChild> siblings)
    {
        var type = m.Info.Type;
        var sameType = siblings.Where(s => s.Info.Type == type).ToList();

        // Emit the cheapest discriminator that PROVABLY resolves back to this control under the resolver's
        // own precedence — verified by running the shared matcher — so emitted paths always round-trip.
        // (Guards against e.g. a sibling whose AutomationId collides with this control's Name.)
        foreach (var disc in CandidateDiscriminators(m))
        {
            if (!IsPathSafe(disc)) continue;
            var (matches, _) = MatchByDiscriminator(sameType, disc);
            if (matches.Count == 1 && ReferenceEquals(matches[0], m.Control))
                return $"{type}[{disc}]";
        }

        // No discriminator round-trips: if it's the sole child of its type, a bare segment is cleaner and
        // stable-while-unique (the resolver's bare-type rule requires uniqueness and errors safely otherwise).
        if (sameType.Count == 1)
            return type;
        return $"{type}[#{sameType.IndexOf(m)}]";
    }

    // Discriminator candidates in resolver-precedence order: AutomationId, then x:Name, then automation label.
    private static IEnumerable<string> CandidateDiscriminators(MeaningfulChild m)
    {
        if (m.Info.AutoId is { } autoId) yield return autoId;
        if (NullIfEmpty(m.Control.Name) is { } xname) yield return xname;
        if (NullIfEmpty(Safe<string?>(() => m.Info.Peer?.GetName(), null)) is { } label) yield return label;
    }

    private readonly record struct NodeClass(
        AutomationPeer? Peer, string[] Providers, string Type, string? Name, string? AutoId, bool Focusable, bool Structural);

    private static NodeClass Classify(Control control)
    {
        AutomationPeer? peer = null;
        try { peer = ControlAutomationPeer.CreatePeerForElement(control); }
        catch { /* still emit it (non-structural) so a meaningful control is never silently lost */ }

        var providers = peer is null ? [] : DescribeProviders(peer, control);
        var type = peer is null ? "Control" : ControlTypeOf(peer);
        var name = peer is null ? NullIfEmpty(control.Name) : NameOf(control, peer);
        var autoId = NullIfEmpty(AutomationProperties.GetAutomationId(control));
        var focusable = peer is not null && Safe(() => peer.IsKeyboardFocusable(), false);

        // A control is a transparent structural wrapper iff it has no semantic automation type, exposes no
        // interaction provider, cannot take focus, and was not explicitly tagged with an AutomationId
        // (Panel/Border/ContentPresenter/StackPanel/dock plumbing). An AutomationId-tagged control is
        // intentional — keep it visible so it isn't addressable-via-#id yet invisible in the dump.
        var structural = type == "None" && providers.Length == 0 && !focusable && autoId is null;

        // A Separator matches every one of those conditions and is still not plumbing: it is a visible
        // element of a menu whose POSITION is the thing under test. Collapsing it away left a dump that
        // could not answer "is the separator above Exit or below it" — a real VB6-fidelity question. UIA
        // agrees; it has a Separator control type rather than treating one as a wrapper.
        if (control is Separator)
            return new NodeClass(peer, providers, "Separator", name, autoId, focusable, false);

        return new NodeClass(peer, providers, type, name, autoId, focusable, structural);
    }

    private sealed class MeaningfulChild(Control control, NodeClass info)
    {
        public Control Control => control;
        public NodeClass Info => info;
    }

    // ── peer reads (defensive) ──────────────────────────────────────────────────────────────────────

    private static string ControlTypeOf(AutomationPeer peer)
        => Safe(() => peer.GetAutomationControlType().ToString(), "Control");

    private static string? NameOf(Control control, AutomationPeer peer)
        => NullIfEmpty(control.Name) ?? NullIfEmpty(Safe(() => peer.GetName(), string.Empty));

    private static string? ClassNameOf(Control control, AutomationPeer peer)
        => NullIfEmpty(Safe(() => peer.GetClassName(), string.Empty)) ?? control.GetType().Name;

    // ── small helpers ─────────────────────────────────────────────────────────────────────────────

    private static T Safe<T>(Func<T> f, T fallback)
    {
        try { return f(); }
        catch { return fallback; }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string StripUnderscore(string s) => s.StartsWith('_') ? s[1..] : s;

    private static bool NameEquals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(StripUnderscore(a), StripUnderscore(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathSafe(string s) => s.IndexOfAny(['/', '[', ']', '#']) < 0;
}

/// <summary>A node in a <see cref="UiAutomationDriver.Dump"/> control-view tree.</summary>
public record UiNode(
    string Path,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? DataContextType,
    string[] Providers,
    bool IsEnabled,
    bool IsOffscreen,
    bool? IsHidden,
    UiNode[] Children);

/// <summary>Deep single-node inspection from <see cref="UiAutomationDriver.Inspect"/>.</summary>
public record UiNodeDetail(
    string Path,
    string ControlType,
    string? Name,
    string? AutomationId,
    string? ClassName,
    string? DataContextType,
    string[] Providers,
    bool IsEnabled,
    bool IsKeyboardFocusable,
    bool IsOffscreen,
    bool? IsHidden,
    double[] BoundingRect,
    string[] SelectionItems,
    string? Value,
    bool? ToggleState,
    VmMember[] DataContextMembers);

/// <summary>A reflectable public member of a control's DataContext.</summary>
/// <param name="Value">
/// The member's current value where it is readable as text, else null. Null means "not read" — a command,
/// an unsupported type, a getter that threw — never "the value is empty", which reads back as <c>""</c>.
/// </param>
public record VmMember(string Name, string Kind, string? TypeName, bool CanWrite, string? Value = null);

/// <summary>Outcome of <see cref="UiAutomationDriver.Interact"/>. <c>Mechanism</c> is "peer" for
/// provider-backed actions (Phase 6) and "reflection" for the DataContext fallback (Phase 7).</summary>
public record InteractOutcome(bool Success, string Mechanism, string? Detail, string? Error);
