using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Editing;

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
                members.Add(new VmMember(p.Name, "property", p.PropertyType.Name, p.CanWrite));
        }
        return [.. members];
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

        if (control is MenuItem menuItem)
        {
            if (!list.Contains("invoke")) list.Add("invoke");
            // Only where there is a submenu to open: advertising expandCollapse on a leaf would promise
            // an action that correctly refuses.
            if (menuItem.HasSubMenu && !list.Contains("expandCollapse")) list.Add("expandCollapse");
        }

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
            if (peer.GetProvider<ISelectionItemProvider>() is not { } selfSip) return Unsupported("select");
            selfSip.Select();
            return Ok($"selected '{LabelOf(control, peer)}'");
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

        if (ControlAutomationPeer.CreatePeerForElement(matches[0]).GetProvider<ISelectionItemProvider>() is not { } sip)
            return Unsupported("select");
        sip.Select();
        return Ok($"selected '{value}'");
    }

    // Realized selectable items reachable from a container: the container itself if it exposes
    // ISelectionItemProvider, plus every descendant that does.
    private static List<MeaningfulChild> SelectableCandidates(Control container)
    {
        var result = new List<MeaningfulChild>();
        foreach (var c in new[] { container }.Concat(Descendants(container)))
        {
            var info = Classify(c);
            if (info.Peer is not null && info.Peer.GetProvider<ISelectionItemProvider>() is not null)
                result.Add(new MeaningfulChild(c, info));
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

            var target = FindTextSurface(control) ?? control;
            target.Focus();
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = parsedKey, KeyModifiers = mods });
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = parsedKey, KeyModifiers = mods });
            return new InteractOutcome(true, "keyboard",
                $"pressed {(mods == KeyModifiers.None ? string.Empty : mods + "+")}{parsedKey}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, "keyboard", null, $"press_key threw: {ex.Message}"); }
    }

    // The nearest editable text surface: the control itself, else its first AvaloniaEdit editor/area, else
    // a plain TextBox descendant (editor surfaces are preferred over incidental TextBoxes like combo edits).
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
            children);
    }

    // Nearest control-view children: descends transparently through non-Control visuals AND structural
    // control wrappers, collecting the closest meaningful Control descendants.
    private static void CollectMeaningfulChildren(Visual parent, List<MeaningfulChild> acc)
    {
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
    double[] BoundingRect,
    string[] SelectionItems,
    string? Value,
    bool? ToggleState,
    VmMember[] DataContextMembers);

/// <summary>A reflectable public member of a control's DataContext.</summary>
public record VmMember(string Name, string Kind, string? TypeName, bool CanWrite);

/// <summary>Outcome of <see cref="UiAutomationDriver.Interact"/>. <c>Mechanism</c> is "peer" for
/// provider-backed actions (Phase 6) and "reflection" for the DataContext fallback (Phase 7).</summary>
public record InteractOutcome(bool Success, string Mechanism, string? Detail, string? Error);
