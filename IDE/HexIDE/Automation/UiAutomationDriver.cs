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
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;
using HexIDE.Utils;
using HexIDE.VisualDesigner;

namespace HexIDE.Automation;

/// <summary>
/// Reads (and, from Phase 6, drives) the live Avalonia control tree through UI Automation peers, with a
/// reflection-over-DataContext fallback. Phase 5 implements the read surface
/// (<see cref="Dump"/> / <see cref="Inspect"/> / <see cref="Resolve"/>); later phases add interaction on
/// top of <see cref="Resolve"/>. Operates on any root <see cref="Control"/> (not the live window) so the
/// addressing and discovery logic is unit-testable headlessly.
///
/// The tree is the UIA-style **control view**: structural layout wrappers (controls whose automation type
/// is <c>None</c> with no non-scroll capability, no keyboard focus and no AutomationId — Panels, Borders, ContentPresenters,
/// dock plumbing, …) are transparent. They never appear as nodes or in paths; the walk descends through
/// them and re-parents their meaningful descendants. x:Name alone does not keep a wrapper. Scrollability
/// and usable range bounds do not change transparency, so growing content does not insert path segments.
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

    /// <summary>
    /// The nodes of <paramref name="tree"/> that answer to <paramref name="match"/>, each without its subtree, the first
    /// <paramref name="limit"/> in tree order; with how many answer in all, and how many nodes the walk cut off at its
    /// depth, whose subtrees were not searched. A node answers when its Name, AutomationId, ClassName, ControlType or
    /// type-name-as-name contains the text, ignoring case, or its DataContext type does where that context starts, not
    /// where the node inherits it: the question a first call asks, such as where the code editor is, before it has a path
    /// to scope a dump with (#699).
    /// </summary>
    public static (List<UiNode> Matches, int Total, int Unsearched) Find(UiNode tree, string match, int limit)
    {
        var matches = new List<UiNode>();
        int total = 0, unsearched = 0;
        var pending = new Stack<UiNode>([tree]);
        while (pending.TryPop(out var node))
        {
            // Without its subtree, and so without the mark that one was cut off below it.
            if (Answers(node, match) && total++ < limit)
                matches.Add(node with { Children = [], Truncated = null });
            if (node.Truncated == true)
                unsearched++;
            for (var i = node.Children.Length - 1; i >= 0; i--)
                pending.Push(node.Children[i]);
        }
        return (matches, total, unsearched);
    }

    // By its DataContext type only where that context starts: every node below a view inherits it, and the Properties
    // window's scroll bar parts all answered to 'Properties' with it.
    private static bool Answers(UiNode node, string match) =>
        new[]
            {
                node.Name, node.Label, node.AutomationId, node.ClassName, node.ControlType, node.TypeNameAsName,
                node.InheritsDataContext ? null : node.DataContextType,
            }
            .Any(f => f?.Contains(match, StringComparison.OrdinalIgnoreCase) == true);

    /// <summary>Builds the control-view node tree rooted at <paramref name="root"/>. <paramref name="basePath"/>
    /// is the path of the root node (e.g. "Window", or a resolved subtree path) so emitted child paths
    /// round-trip.</summary>
    public static UiNode Dump(Control root, string basePath, int maxDepth, bool interactiveOnly)
    {
        var rootNode = new MeaningfulChild(root, Classify(root));
        // The context the root was handed, compared by identity, so a subtree dumped from a node that inherits its
        // DataContext does not answer to it as if it started there.
        var handed = (Avalonia.VisualTree.VisualExtensions.GetVisualParent(root) as Control)?.DataContext;
        try { return BuildNode(rootNode, basePath, 0, maxDepth, interactiveOnly, handed) ?? BareNode(rootNode, basePath, handed); }
        catch { return BareNode(rootNode, basePath, handed); }
    }

    /// <summary>Window-wide focus metadata, independent of dump filters or subtree scope.</summary>
    public static KeyboardFocusReport FocusIn(Control window)
    {
        var focused = TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() as Control;
        var path = focused is null ? null : PathWithin(window, "Window", focused);
        return new KeyboardFocusReport(path, path is null ? "No control in this window has keyboard focus." : null);
    }

    /// <summary>Deep single-node inspection: identity, provider state, and the DataContext's reflectable
    /// command, Command-bound method and property members (the surface the Phase-7 reflection actions target).</summary>
    /// <param name="root">The window <paramref name="path"/> was resolved against. With it, a control whose
    /// DataContext is inherited names the control that owns it instead of repeating its members.</param>
    public static UiNodeDetail Inspect(Control control, string path, Control? root = null)
    {
        var peer = ControlAutomationPeer.CreatePeerForElement(control);
        var providers = DescribeProviders(peer, control);

        string[] selection = [];
        // A list first: from Avalonia 12.0.5 its peer offers a selection provider, but that names only the rows that
        // exist, so a selected item scrolled out of view, or in a list too short to show it, went missing (#2026).
        if (control is ListBox listBox)
            selection = Safe(() => SelectedNames(listBox).ToArray(), []);
        else if (peer.GetProvider<ISelectionProvider>() is { } sel)
            selection = Safe(() => sel.GetSelection().Select(p => p.GetName()).ToArray(), []);
        else if (control is SelectingItemsControl selecting and not (MenuItem or MenuBase))
            selection = Safe(() => SelectedNames(selecting).ToArray(), []);
        // Neither of those, so an empty list read as "nothing is selected" whatever the tree held (#2288).
        else if (control is TreeView tree)
            selection = Safe(() => TreeSelectedNames(tree).ToArray(), []);

        string? value = peer.GetProvider<IValueProvider>() is { } vp ? Safe<string?>(() => vp.Value, null) : null;
        // A dropdown's peer names its selection, and gives its value, by the item's ToString, which for a view model with
        // none of its own is its type name, or a record's members: read as the closed box shows it instead (#1288).
        if (control is ComboBox dropdown && ShownSelectionOf(dropdown) is { } shown)
        {
            var item = dropdown.SelectedItem;
            selection = [.. selection.Select(s => IsDefaultText(s, item) ? shown : s)];
            if (value is not null && IsDefaultText(value, item))
                value = shown;
        }

        // An editor has no value provider, so the Immediate window's text could be read only from its view's members, on
        // another node, and nothing said so (#2513). A code window's document is a project file, which get_file_content
        // returns with its open edits, so that one is still left out (#562). Its writability goes with it, as a value
        // provider's does.
        bool? editorReadOnly = null;
        if (value is null && control is TextEditor { Document: { } document } shownEditor
            && control.DataContext is not HexIDE.Forms.ViewModels.ISearchableDocument)
        {
            value = Safe<string?>(() => Capped(document.Text), null);
            editorReadOnly = shownEditor.IsReadOnly;
        }

        bool? toggle = null;
        if (peer.GetProvider<IToggleProvider>() is { } tp)
            toggle = Safe<bool?>(() => tp.ToggleState switch
            {
                ToggleState.On => true,
                ToggleState.Off => false,
                _ => null,
            }, null);
        // A checkable menu item's tick, which its peer offers no toggle for: the only read of it, since a running form's
        // program can move ticks a reply does not name, as one answering a dialog does (#1255 review).
        else if (control is MenuItem { ToggleType: not MenuItemToggleType.None } checkable)
            toggle = Safe<bool?>(() => checkable.IsChecked, null);

        // The range's own state, so a caller of set_range_value can read back what it set and learn the bounds
        // without provoking a refusal to find them. Reported the way the value provider's text is. (#550)
        RangeState? range = peer.GetProvider<IRangeValueProvider>() is { } rp
            ? Safe<RangeState?>(() => new RangeState(rp.Value, rp.Minimum, rp.Maximum, rp.IsReadOnly), null)
            : null;

        var rect = Safe(() => peer.GetBoundingRectangle(), default(Rect));

        // Members are listed where the DataContext is set, not on every control that inherits it. A scroll bar
        // inside a code window used to report the whole CodeEditorViewModel, source file included, ahead of
        // the range the caller asked about (#562).
        var owner = root is null ? control : DataContextOwner(control, root);
        var ownerPath = root is not null && !ReferenceEquals(owner, control)
            ? PathWithin(root, "Window", owner)
            : null;
        var (members, omitted) = ownerPath is null ? DescribeDataContext(control.DataContext) : ([], 0);
        string? note = null;
        if (ownerPath is not null)
            note = $"This control inherits its DataContext ({control.DataContext!.GetType().Name}) from {ownerPath}; "
                 + "inspect that path for its members. invoke_command and set_property on this control act on "
                 + "the same object.";
        else if (omitted > 0)
            note = $"{omitted} member(s) declared by the Dock framework's base types (layout plumbing such as "
                 + "Proportion and CanFloat) are not listed; set_property still reaches them by name.";

        if (ResolvedCommand(control) is { } bound)
        {
            members = [new VmMember("Command", "command", bound.Command.GetType().Name, false),
                .. members.Where(m => m.Name != "Command")];
            note = string.Join(" ", new[] { note, BoundCommandNote(bound) }.Where(n => n is not null));
        }

        var (itemTexts, itemsNote) = control is SelectingItemsControl listed and not (MenuItem or MenuBase)
            ? Safe(() => ItemTexts(listed), ((string[]?)null, (string?)null)) : (null, null);

        return new UiNodeDetail(
            path,
            ControlTypeOf(peer),
            NameOf(control, peer),
            NullIfEmpty(AutomationProperties.GetAutomationId(control)),
            ClassNameOf(control, peer),
            control.DataContext?.GetType().Name,
            providers,
            Safe(() => peer.IsEnabled(), true),
            // The peer answers for Focusable alone, so a disabled combo read as focusable, as if a key could reach it (#2366).
            Safe(() => peer.IsKeyboardFocusable(), false) && control.IsEffectivelyEnabled,
            control.IsFocused,
            Safe(() => peer.IsOffscreen(), false),
            Hidden(control),
            [rect.X, rect.Y, rect.Width, rect.Height],
            selection,
            value,
            toggle,
            members,
            range,
            ownerPath,
            note,
            control is SelectingItemsControl items and not (MenuItem or MenuBase) ? Safe<int?>(() => items.ItemCount, null) : null,
            DistinctLabelOf(NameOf(control, peer), peer),
            peer.GetProvider<IValueProvider>() is { } valueProvider ? Safe<bool?>(() => valueProvider.IsReadOnly, null) : editorReadOnly,
            control is Window window ? ReadFrame(window) : null,
            peer.GetProvider<IExpandCollapseProvider>() is { } expansion
                ? Safe<string?>(() => expansion.ExpandCollapseState.ToString(), null) : null,
            itemTexts,
            itemsNote,
            // The pointer it shows, and where that is set (#2453).
            Safe(() => PointerReader.Read(control, c => root is null ? Describe(c) : PathWithin(root, "Window", c)), null));
    }

    /// <summary>How many items <see cref="ItemTexts"/> reads at most.</summary>
    internal const int ItemTextLimit = 200;

    /// <summary>
    /// The text of each of <paramref name="list"/>'s items, as select matches it, whether its row exists or not, and a note
    /// on any it left out (#2450). A list makes rows only for what is on screen, so a dump and its match saw the first ten
    /// of Options' 31 languages, and nothing said what the rest were called, or how to spell one for select.
    /// </summary>
    /// <remarks>
    /// Each row missing is brought into view to be read, as <see cref="MatchAmongUnrealized"/> does, and the list is put
    /// back where it was before anything is drawn, so a person sees nothing move. A dropdown that is closed has no rows to
    /// bring into view: opening it is a person's action, not a read, so it is left closed and the note says so.
    /// </remarks>
    internal static (string[]? Items, string? Note) ItemTexts(SelectingItemsControl list)
    {
        if (list.ItemCount == 0)
            return ([], null);
        if (list is ComboBox { IsDropDownOpen: false })
            return (null, "Its items are made only while it is open, so their text can't be read now: 'expand' it, then "
                          + "inspect it again. Opening it is left to the caller, as it is a person's action, not a read.");
        var scroller = list.GetRealizedContainers().FirstOrDefault()?.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault()
                       ?? (list as ListBox)?.Scroll as ScrollViewer;
        var original = scroller?.Offset;
        var texts = new List<string>();
        var (hidden, unread) = (0, 0);
        var count = Math.Min(list.ItemCount, ItemTextLimit);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var row = list.ContainerFromIndex(i) as Control;
                if (row is null || HeldOutOfView(row))
                {
                    list.ScrollIntoView(i);
                    list.UpdateLayout();
                    row = list.ContainerFromIndex(i) as Control;
                }
                if (row is null) unread++;
                else if (Shows(row) is not true) hidden++;
                else texts.Add(LabelOf(row));
            }
        }
        finally
        {
            if (scroller is not null && original is { } offset)
            {
                scroller.Offset = offset;
                list.UpdateLayout();
            }
        }
        var notes = new List<string>();
        if (list.ItemCount > count)
            notes.Add($"{list.ItemCount - count} item(s) after the first {count} are not listed");
        if (hidden > 0)
            notes.Add($"{hidden} item(s) that do not show are left out, as select passes over them");
        if (unread > 0)
            notes.Add($"{unread} item(s) could not be brought into view to be read");
        return ([.. texts], notes.Count == 0 ? null : string.Join("; ", notes) + ".");
    }

    private static WindowFrameState ReadFrame(Window window)
    {
        var (style, exStyle, note) = NativeWindowFrame.Read(window);
        return new WindowFrameState(window.CanResize, window.WindowDecorations.ToString(), window.CanMinimize,
            window.CanMaximize, window.ShowInTaskbar, window.WindowState.ToString(), note, style, exStyle);
    }

    /// <summary>
    /// The outermost control, counting only those the control view shows, that shares
    /// <paramref name="control"/>'s DataContext: where that DataContext is set, as far as a caller can address.
    /// </summary>
    /// <remarks>
    /// Compared against meaningful ancestors only. A view built from a DataTemplate inherits its DataContext
    /// from a ContentPresenter, which the control view folds away, so against the raw visual parent nearly
    /// every view would read as inheriting. The walk stops at <paramref name="root"/>, because a path cannot
    /// name anything above it; Avalonia 12 puts a TopLevelHost above the window, sharing its DataContext.
    /// </remarks>
    private static Control DataContextOwner(Control control, Control root)
    {
        var dataContext = control.DataContext;
        var owner = control;
        if (dataContext is null || ReferenceEquals(control, root)) return owner;
        for (var a = control.GetVisualParent(); a is not null; a = a.GetVisualParent())
        {
            if (a is not Control c || Classify(c).Structural) continue;
            if (!ReferenceEquals(c.DataContext, dataContext)) break;
            owner = c;
            if (ReferenceEquals(c, root)) break;
        }
        return owner;
    }

    /// <summary>Reflects the command, Command-bound method and property members of a control's DataContext.</summary>
    public static VmMember[] ReflectDataContextMembers(object? dataContext) => DescribeDataContext(dataContext).Members;

    /// <summary>
    /// The members <see cref="ReflectDataContextMembers"/> lists, and how many it left out because the Dock
    /// framework's base types declare them.
    /// </summary>
    /// <remarks>
    /// A document or tool view model derives from Dock's, which adds about fifty layout members (MdiBounds,
    /// Proportion, AllowedDropOperations, ...). They say nothing about the view model's own surface, and they
    /// buried it: 73 members for a code window, of which the caller wanted none (#562).
    /// </remarks>
    public static (VmMember[] Members, int OmittedDockMembers) DescribeDataContext(object? dataContext)
    {
        if (dataContext is null) return ([], 0);
        var members = new List<VmMember>();
        var omitted = 0;
        foreach (var p in dataContext.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            if (p.DeclaringType?.Namespace is { } ns && (ns == "Dock" || ns.StartsWith("Dock.", StringComparison.Ordinal)))
            {
                omitted++;
                continue;
            }
            if (typeof(System.Windows.Input.ICommand).IsAssignableFrom(p.PropertyType))
                members.Add(new VmMember(p.Name, "command", null, false));
            else
                members.Add(new VmMember(p.Name, "property", p.PropertyType.Name, p.CanWrite,
                                         ReadValue(dataContext, p)));
        }
        // A menu item can bind its Command to a method, which invoke_command calls as Avalonia does (#836).
        foreach (var m in CommandMethods(dataContext.GetType()))
            members.Add(new VmMember(m.Name, "method", null, false));
        return ([.. members], omitted);
    }


    /// <summary>Cap on a reported value, in characters. The Immediate buffer is unbounded.</summary>
    private const int MaxValueLength = 4000;

    private static string Capped(string text) => text.Length > MaxValueLength
        ? text[..MaxValueLength] + $"… [truncated at {MaxValueLength} of {text.Length} chars]"
        : text;

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

        // An editor's document is a project file, which get_file_content already returns, open edits included.
        // Rendering it here put a whole source file into every inspection of the editor (#562). The Immediate
        // window's document is not a file and has no other reader, so it is still rendered below.
        if (dataContext is HexIDE.Forms.ViewModels.ISearchableDocument searchable
            && p.Name == nameof(HexIDE.Forms.ViewModels.ISearchableDocument.Document))
            return Safe(() =>
            {
                var lines = searchable.Document.LineCount;
                return $"<{lines} line{(lines == 1 ? "" : "s")} as the editor holds them, unsaved edits included; "
                       + "read the text with get_file_content>";
            }, null);

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
            return text is null ? null : Capped(text);
        }, null);
    }

    /// <summary>
    /// For a text box whose text is bound, pushes the text to its source, as Enter or leaving the box would, and
    /// says what came of it; null when there is nothing to commit.
    /// </summary>
    /// <remarks>
    /// A box bound with <c>UpdateSourceTrigger=LostFocus</c> -- every Properties window row -- took the text and
    /// changed nothing, and the reply said it had been set (#625). Whether the source KEPT the value is a separate
    /// question, answered by <see cref="RefusedCommit"/> once the source's own reaction has run.
    /// </remarks>
    private static string? CommitTyped(Control control, string typed)
    {
        if (control is not TextBox textBox
            || Avalonia.Data.BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty) is not { } binding)
            return null;

        binding.UpdateSource();
        return CommittedSuffix;
    }

    private const string CommittedSuffix = ", and committed it";

    /// <summary>
    /// <paramref name="outcome"/> of a <c>set_value</c> that committed, with <paramref name="refusal"/> (from
    /// <see cref="RefusedCommit"/>) in place of its claim that the value was committed.
    /// </summary>
    /// <remarks>
    /// The sentence being replaced is written in this file, so it is replaced here too. The interact tool used to
    /// match its text to decide whether to check at all, and a rewording would have stopped the check without a
    /// sound (#634); it now asks <see cref="InteractOutcome.Committed"/> instead.
    /// </remarks>
    public static InteractOutcome WithRefusal(InteractOutcome outcome, string refusal) =>
        outcome with
        {
            Detail = outcome.Detail is { } detail && detail.EndsWith(CommittedSuffix, StringComparison.Ordinal)
                ? detail[..^CommittedSuffix.Length] + refusal
                : outcome.Detail + refusal,
        };

    /// <summary>
    /// After <c>set_value</c> committed <paramref name="typed"/> into a bound text box, what to say instead of
    /// "committed it" when the box no longer shows it; null when it does.
    /// </summary>
    /// <remarks>
    /// Asked after the dispatcher has run what the commit queued. A source that refuses a value puts its own
    /// back from a posted callback, because a change raised while the binding is writing is not carried to the
    /// box; asked straight after the write, a refused value still reads as accepted. Measured: the Properties
    /// window's Name row, given "1st", opened its refusal and kept showing "1st".
    /// </remarks>
    public static string? RefusedCommit(Control control, string typed)
    {
        if (control is not TextBox textBox
            || Avalonia.Data.BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty) is null)
            return null;

        var shown = textBox.Text ?? "";
        return shown == typed
            ? null
            : $", but after committing it shows '{shown}': the change was refused or rewritten. A refusal opens a "
              + "message box saying why, which dump_visual_tree shows.";
    }

    /// <summary>
    /// Every action <see cref="Interact"/> accepts, in the spelling a caller passes. The unknown-action error
    /// is built from this, so the list it prints cannot drift from the switch.
    /// </summary>
    public static readonly IReadOnlyList<string> Verbs =
    [
        "invoke", "select", "add_to_selection", "remove_from_selection", "double_click", "click", "release", "set_value",
        "set_range_value", "toggle", "expand", "collapse", "scroll", "invoke_command", "set_property", "close",
    ];

    /// <summary>
    /// What each token <see cref="DescribeProviders"/> reports lets a caller do. A token names a capability,
    /// not a verb (<c>value</c> is driven with <c>set_value</c>), so this is the translation, and every token
    /// must have one: a token that reaches no verb is a lead that goes nowhere (hexide-io/HexIDE#361).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> VerbsByProvider = new Dictionary<string, string[]>
    {
        ["invoke"] = ["invoke"],
        ["selection"] = ["select"],
        ["selectionItem"] = ["select"],
        ["multiSelectItem"] = ["add_to_selection", "remove_from_selection"],
        ["value"] = ["set_value"],
        ["toggle"] = ["toggle"],
        ["expandCollapse"] = ["expand", "collapse"],
        ["rangeValue"] = ["set_range_value"],
        ["scroll"] = ["scroll"],
    };

    /// <summary>
    /// The action tokens a control actually accepts. Mostly its automation providers, but not only:
    /// <see cref="Interact"/> has fallbacks for controls whose peer offers nothing, and a token missing
    /// here is a token nobody tries. <c>MenuItemAutomationPeer</c> exposes no invoke or expandCollapse provider,
    /// and from 12.0.5 only a toggle that skips the click, so reporting the peer verbatim would advertise a menu as
    /// undrivable while every verb on it works.
    /// </summary>
    public static string[] DescribeProviders(AutomationPeer peer, Control? control = null)
    {
        var list = new List<string>();
        if (peer.GetProvider<IInvokeProvider>() is not null) list.Add("invoke");
        if (peer.GetProvider<ISelectionProvider>() is not null) list.Add("selection");
        if (peer.GetProvider<ISelectionItemProvider>() is not null) list.Add("selectionItem");
        // Only in a list that holds several at once: in any other, adding to the selection would replace it,
        // which is select under another name. (#661)
        if (control is not null && Safe(() => MultiSelectOwner(control) is not null, false)) list.Add("multiSelectItem");
        if (peer.GetProvider<IValueProvider>() is not null) list.Add("value");
        if (peer.GetProvider<IToggleProvider>() is not null) list.Add("toggle");
        if (peer.GetProvider<IExpandCollapseProvider>() is not null) list.Add("expandCollapse");
        // Both only when the verb would do something. ItemsControlAutomationPeer implements IScrollProvider
        // on EVERY ListBox, TreeView and TreeViewItem, whether or not there is a scroller behind it, and a
        // ProgressBar's range is read-only; advertising those kept 40 inert nodes in the default tree
        // (hexide-io/HexIDE#361) and promised a verb that could only refuse or silently do nothing.
        if (peer.GetProvider<IRangeValueProvider>() is { } range && Safe(() => IsSettable(range), false))
            list.Add("rangeValue");
        if (peer.GetProvider<IScrollProvider>() is { } scroller && Safe(() => CanScroll(scroller), false))
            list.Add("scroll");
        // And where the control's own template scrolls, which is where scroll will find it: an editor and a
        // DataGrid offer no scroll provider of their own, and are what a caller naturally aims at. (#545)
        else if (control is not null
                 && Safe(() => OwnScrollerFor(control, vertical: true) is not null || OwnScrollerFor(control, vertical: false) is not null, false))
            list.Add("scroll");

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
            // Avalonia 12.0.5's peer offers a toggle on a checkable item that only sets IsChecked, and Interact refuses
            // toggle on every menu item for that reason, so advertising it would promise a verb that only refuses.
            list.Remove("toggle");
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

        // Same hole, different control, and a worse one: a TreeViewItem's peer offers only scroll, so the
        // Project Explorer — the IDE's primary navigation surface — could be read in full and not driven at
        // all. Selecting a node is the precondition for its context menu, its toolbar and every
        // SelectedForm/SelectedModule/SelectedProject command, and none of that was reachable.
        if (control is TreeViewItem && !list.Contains("selectionItem")) list.Add("selectionItem");
        // Nor expand or collapse, so a collapsed node's children, once realized and then hidden, were out of reach
        // except by keyboard. Only on a node that has children, as for a menu item with a submenu.
        if (control is TreeViewItem { ItemCount: > 0 } && !list.Contains("expandCollapse")) list.Add("expandCollapse");

        return [.. list];
    }

    // ── interaction (Phase 6) ───────────────────────────────────────────────────────────────────────

    // An action as Interact matches it: its letters alone, lower-cased, so "double_click" and "DoubleClick" are one verb.
    private static string Normalise(string? action) =>
        new string((action ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();

    /// <summary>Drives a resolved control through its automation provider. The accepted actions are
    /// <see cref="Verbs"/>, and <see cref="VerbsByProvider"/> says which reported token each serves. A missing provider
    /// yields a clean "element does not support '&lt;action&gt;'" error — the caller's signal to inspect
    /// and switch to a Phase-7 reflection action.</summary>
    public static InteractOutcome Interact(Control control, string action, string? value)
    {
        var norm = Normalise(action);
        if (norm.Length == 0) return Err("action is required");

        // One mouse: with its button held for a later release, a second press is input it cannot make (#2231).
        if (norm is "click" or "doubleclick" && HeldButton.Current is { } held)
            return new InteractOutcome(false, "pointer", null, held.Refusal(norm == "click" ? "click" : "double_click"));

        // A provider or pointer action stands for something a person does to the control, and nobody can press
        // what is not showing. The peer acts regardless, so without this a hidden Approve button reported that it
        // approved (#1069). The reflection actions act on the view model, not the control, so they are exempt, and
        // so is release, which lets go of the held button: its target only places the point it may move to first.
        if (norm is not ("invokecommand" or "setproperty" or "release") && Verbs.FirstOrDefault(v => Normalise(v) == norm) is { } verb
            && Shows(control) is not true and var shows)
            return Err(NotShowingRefusal(control, verb, shows)
                       + (shows is false && Safe(() => ShowingCopyHint(control), null) is { } copyHint
                           ? copyHint
                           : verb == "scroll"
                           // scroll reaches past its target to the nearest container that scrolls, so the container is
                           // the useful thing to aim at, and the view model has nothing to offer.
                           ? " Aim scroll at the container that shows; it scrolls itself or the nearest container that can"
                           : shows is false && CollapsedPropertyCategoryHolding(control) is { } category
                               ? $" Make it show first, as a person would: 'toggle' the Button in '{category}', which holds it"
                               : " Make it show first; invoke_command and set_property act on "
                             + "its view model and still work"));

        if (norm is not ("invokecommand" or "setproperty")
            && Verbs.FirstOrDefault(v => Normalise(v) == norm) is { } disabledVerb
            && DisabledInteractionRefusal(control, disabledVerb) is { } disabled)
            return Err(disabled);

        AutomationPeer peer;
        try { peer = ControlAutomationPeer.CreatePeerForElement(control); }
        catch (Exception ex) { return Err($"could not create automation peer: {ex.Message}"); }

        try
        {
            switch (norm)
            {
                case "close":
                    return DoClose(control);

                case "invoke":
                    // Named BEFORE the invoke. A button that closes its dialog is detached by the time the
                    // invoke returns, its styles go with it, and its name falls back to the raw caption:
                    // the New Project dialog's Open reported itself as '_Open' (#578).
                    var invoked = LabelOf(control, peer);
                    if (peer.GetProvider<IInvokeProvider>() is { } inv)
                    {
#if DEBUG
                        // Read only after visibility/peer guards, inside the same exception boundary as the invoke.
                        var saveWatch = control is Button saveButton ? UiSaveFeedback.Watch(saveButton.Command) : null;
#endif
                        inv.Invoke();
                        var outcome = Ok($"invoked {ControlTypeOf(peer)} '{invoked}'");
#if DEBUG
                        return UiSaveFeedback.After(saveWatch, outcome);
#else
                        return outcome;
#endif
                    }
                    // MenuItemAutomationPeer exposes no invoke and no expandCollapse provider, so every verb failed
                    // on a menu and none of it was reachable.
                    // ClickMenuItem clicks it as Avalonia's menu does: it moves a checkable item's tick, raises
                    // Click, whose class handler executes Command, so plain Click handlers and bound commands
                    // fire once, then closes the menu. Raising Click alone was the #947 defect.
                    if (control is MenuItem clickItem)
                    {
                        // A routed command is disabled wherever focus has nothing for it to act on, so say where to
                        // put focus, as invoke_menu_item does (#678).
                        if (!clickItem.IsEffectivelyEnabled)
                            return Err($"'{invoked}' is disabled, so a click on it does nothing; nothing was done"
                                       + (clickItem.IsEnabled && clickItem.Command is Avalonia.Labs.Input.RoutedCommand
                                           ? ". Its command acts where keyboard focus is: if it belongs to a document, "
                                             + "open_file, view_designer or activate_document_tab put focus there"
                                           : ""));
                        // A running form's ticks are its program's to set, in a Click that runs later, through the program's
                        // queue, so they are read once the wait for the program finds it idle (#1255).
                        if (clickItem is HexIDE.Runtime.BuiltinControls.VBMenu programItem)
                        {
                            var moved = TicksMoved(programItem);
                            return Ok($"invoked MenuItem '{invoked}'{ClickMenuItem(clickItem)}") with { Settled = moved };
                        }
                        return Ok($"invoked MenuItem '{invoked}'{ClickMenuItem(clickItem)}");
                    }
                    return Unsupported("invoke");

                case "toggle":
                    // A menu item has no toggle of its own; only a click moves its tick. A running form's menu leaves the
                    // tick to its program, in its Click, so the refusal does not steer a caller to run an item such as Exit
                    // for its tick, and says where the tick is read (#1255 review). From Avalonia 12.0.5 the peer of a
                    // checkable item offers a toggle, but it only sets IsChecked: no Click is raised, so neither the
                    // program's handler nor the command behind an IDE tick runs. So a menu item is refused whatever its
                    // peer offers.
                    if (control is HexIDE.Runtime.BuiltinControls.VBMenu)
                        return Err("a running form's menu item has no toggle of its own: its program sets the tick, in the "
                                   + "item's Click, which 'invoke' runs as a click does. inspect_element reads the tick as "
                                   + "toggleState; nothing was done");
                    if (control is MenuItem toggleItem)
                        return Err(toggleItem.ToggleType == MenuItemToggleType.None
                            ? $"'{LabelOf(control, peer)}' has no tick to toggle, and 'invoke' would run it as a click "
                              + "does; nothing was done"
                            : "a menu item has no toggle of its own: 'invoke' clicks it, and its reply says whether the "
                              + "tick moved; nothing was done");
                    if (peer.GetProvider<IToggleProvider>() is not { } tog) return Unsupported("toggle");
                    return ToggleVerified(tog, LabelOf(control, peer), control is RadioButton, InProgram(control));

                case "expand":
                    if (peer.GetProvider<IExpandCollapseProvider>() is { } exp)
                    {
                        if (exp.ExpandCollapseState == ExpandCollapseState.Expanded)
                            return Ok($"'{LabelOf(control, peer)}' was already expanded");
                        exp.Expand();
                        return ExpandedVerified(exp, LabelOf(control, peer), true);
                    }
                    if (control is TreeViewItem { ItemCount: > 0 } openNode)
                        return SetTreeNodeExpanded(openNode, peer, true);
                    // A MenuItem exposes no ExpandCollapse provider, so without this a menu could not be
                    // opened through automation at all — and a menu that cannot be opened cannot be read
                    // either, since its items are only realised once the popup is up.
                    if (control is MenuItem { HasSubMenu: true } openMenu)
                    {
                        if (openMenu.IsSubMenuOpen)
                            return Ok($"menu '{LabelOf(control, peer)}' was already open");
                        openMenu.Open();
                        var opened = PopupVerified(() => openMenu.IsSubMenuOpen, true, "menu", LabelOf(control, peer));
                        return opened.Success ? Ok($"opened menu '{LabelOf(control, peer)}'") : opened;
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
                        if (IsOpenOn(contextMenu, control))
                            return Ok($"the context menu on '{LabelOf(control, peer)}' was already open");
                        Safe(() => control.Focus(), false);
                        // Avalonia ignores Open on an already-open shared menu, even for a different owner.
                        if (contextMenu.IsOpen) contextMenu.Close();
                        contextMenu.Open(control);
                        return PopupVerified(() => IsOpenOn(contextMenu, control), true, "context menu", LabelOf(control, peer));
                    }
                    if (FlyoutsOf(control).FirstOrDefault() is { } flyout)
                    {
                        if (IsOpenOn(flyout, control))
                            return Ok($"the flyout on '{LabelOf(control, peer)}' was already open");
                        Safe(() => control.Focus(), false);
                        flyout.ShowAt(control);
                        return PopupVerified(() => IsOpenOn(flyout, control), true, "flyout", LabelOf(control, peer));
                    }
                    return Unsupported("expand");

                case "collapse":
                    if (peer.GetProvider<IExpandCollapseProvider>() is { } col)
                    {
                        if (col.ExpandCollapseState == ExpandCollapseState.Collapsed)
                            return Ok($"'{LabelOf(control, peer)}' was already collapsed");
                        col.Collapse();
                        return ExpandedVerified(col, LabelOf(control, peer), false);
                    }
                    if (control is TreeViewItem { ItemCount: > 0 } closeNode)
                        return SetTreeNodeExpanded(closeNode, peer, false);
                    if (control is MenuItem { HasSubMenu: true } closeMenu)
                    {
                        // A menu bar's header closes the bar with it, as a second click on the header does, so the
                        // header is not left lit with the bar still taking keys (#947). A submenu closes alone.
                        // Each reply says what was open, so an already-closed menu is not reported as closed (#949).
                        var closing = LabelOf(control, peer);
                        var wasOpen = closeMenu.IsSubMenuOpen;
                        if (closeMenu.Parent is Menu bar)
                        {
                            if (!MenuIsOpen(bar))
                                return Ok($"menu '{closing}' was already closed, and the menu bar was not open; there "
                                          + "is nothing to close");
                            CloseMenuHolding(closeMenu);
                            var closed = PopupVerified(() => MenuIsOpen(bar), false, "menu", closing);
                            if (!closed.Success) return closed;
                            return Ok(wasOpen
                                ? $"closed menu '{closing}' and left the menu bar"
                                : $"menu '{closing}' was not open, but the menu bar was; left the menu bar");
                        }
                        if (!wasOpen)
                            return Ok($"menu '{closing}' was already closed; there is nothing to close");
                        closeMenu.Close();
                        var submenuClosed = PopupVerified(() => closeMenu.IsSubMenuOpen, false, "menu", closing);
                        return submenuClosed.Success ? Ok($"closed menu '{closing}'") : submenuClosed;
                    }
                    if (control.ContextMenu is { } openContextMenu && IsOpenOn(openContextMenu, control))
                    {
                        openContextMenu.Close();
                        return PopupVerified(() => IsOpenOn(openContextMenu, control), false, "context menu", LabelOf(control, peer));
                    }
                    if (FlyoutsOf(control).FirstOrDefault(f => IsOpenOn(f, control)) is { } openFlyout)
                    {
                        openFlyout.Hide();
                        return PopupVerified(() => IsOpenOn(openFlyout, control), false, "flyout", LabelOf(control, peer));
                    }
                    // The popup is why expandCollapse is reported here, so a closed one is nothing to close, as with a
                    // menu, rather than an action the control does not support (#949).
                    // But the code window's completion list and parameter info are listed under its editor and are not
                    // the editor's flyout, so with one open, "nothing to close" would deny what the dump shows.
                    if (AttachedPopupsOf(control).Count > 0)
                        return Err($"'{LabelOf(control, peer)}' has a popup open that collapse does not close, such as "
                                   + "the completion list; press_key Escape closes it");
                    if (control.ContextMenu is not null || FlyoutsOf(control).Any())
                        return Ok($"the {(control.ContextMenu is not null ? "context menu" : "flyout")} on "
                                  + $"'{LabelOf(control, peer)}' was not open; there is nothing to close");
                    return Unsupported("collapse");

                case "setvalue":
                    if (peer.GetProvider<IValueProvider>() is not { } val) return Unsupported("set_value");
                    if (value is null) return Err("set_value requires 'value'");
                    if (control is ComboBox { IsEditable: false } combo)
                    {
                        // Open the same rows select matches, whose peers read the text the dump shows, including
                        // item templates. Always close what we opened, including a refused or ambiguous selection.
                        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, true);
                        try
                        {
                            combo.UpdateLayout();
                            return DoSelect(control, peer, value, matchEmptyValue: true);
                        }
                        finally { combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false); }
                    }
                    if (val.IsReadOnly)
                        return Err($"value is read-only on '{LabelOf(control, peer)}'; set_value cannot change it. "
                                   + "inspect_element shows its providers and view-model members for other supported actions");
                    val.SetValue(value);
                    // A bound box's source may convert what it is given, and pushes the result back at once: " 90" reads
                    // back "90", taken. Whether the source kept it is RefusedCommit's to say, once it has reacted.
                    if (CommitTyped(control, value) is { } committed)
                        return Ok($"set value of '{LabelOf(control, peer)}' to '{value}'{committed}") with { Committed = true };
                    return ValueVerified(val, value, LabelOf(control, peer), InProgram(control));

                case "select":
                    return DoSelect(control, peer, value);

                case "addtoselection":
                    return DoChangeSelection(control, peer, value, add: true);

                case "removefromselection":
                    return DoChangeSelection(control, peer, value, add: false);

                case "doubleclick":
                    return DoDoubleClick(control, peer);

                case "click":
                    // The press only: the release is still to come, as the outcome's Release (FinishAsync).
                    return DoClick(control, value);

                case "release":
                    return DoRelease(control, value);

                case "scroll":
                    return DoScroll(control, value);

                case "setrangevalue":
                    return DoSetRangeValue(control, peer, value);

                // Phase 7 — reflection over the DataContext (opt-in: there is NO implicit fallback from the
                // provider actions above). Reaches VM commands/properties on controls with no useful provider.
                case "invokecommand":
                    return ReflectInvokeCommand(control, value);

                case "setproperty":
                    return ReflectSetProperty(control, value);

                default:
                    return Err($"unknown action '{action}' (expected {string.Join('|', Verbs)})");
            }
        }
        catch (Exception ex)
        {
            return Err($"action '{norm}' threw: {ex.Message}");
        }
    }

    // Modal windows disable platform input without necessarily changing a child's IsEffectivelyEnabled, so check the
    // owning window's dialogs too. A peer can otherwise invoke through them, while click blames the hit test (#2184).
    // press_key refuses with it too, since its routed fallback pressed a key on a disabled control and reported
    // success (#2366).
    /// <summary>
    /// Why a person couldn't act on <paramref name="window"/> itself, its caption buttons or frame, through
    /// <paramref name="verb"/>: a modal dialog over it, or the window disabled. Null when they could. It names the window
    /// as <paramref name="name"/>, as the caller reached it: a running form's window is otherwise only its class.
    /// </summary>
    public static string? WindowRefusal(Window window, string verb, string? name = null) =>
        DisabledInteractionRefusal(window, verb, name);

    private static string? DisabledInteractionRefusal(Control control, string verb, string? name = null)
    {
        var subject = name ?? Describe(control);
        var window = TopLevel.GetTopLevel(control) as Window ?? control.FindLogicalAncestorOfType<Window>();
        var open = OpenDialogs.Among(OpenWindows());
        var blocking = open.Where(d => OpenDialogs.Blocks(d, window)).ToList();
        if (blocking.FirstOrDefault(d => !open.Any(o => OpenDialogs.Blocks(o, d))) is { } dialog)
        {
            foreach (var blocked in blocking)
                OpenDialogs.NamedInReply(blocked);
            return $"'{subject}' is in a window a modal dialog has disabled: {OpenDialogs.DescriptionOf(dialog)}. "
                   + $"No person could use it, so '{verb}' was refused and nothing was done. Answer the dialog first: "
                   + "dump_visual_tree with 'window' set to its title shows it, and interact answers it";
        }
        if (control.IsEffectivelyEnabled && window is not { IsEffectivelyEnabled: false })
            return null;

        var container = control.GetVisualAncestors().OfType<Control>()
            .Concat(control.GetLogicalAncestors().OfType<Control>())
            .FirstOrDefault(c => !c.IsEnabled);
        var cause = control.IsEnabled && container is not null
            ? $" Its container '{Describe(container)}'{(window is null ? string.Empty : AddressOf(window, "Window", container))} disables it."
            : string.Empty;
        return $"'{subject}' is disabled (\"isEnabled\": false).{cause} "
               + $"No person could use it, so '{verb}' was refused and nothing was done. "
               + "Wait until it is enabled before trying again; inspect_element reports its current state";
    }

    /// <summary>
    /// The refusal for acting on <paramref name="control"/> through <paramref name="verb"/> while it is not showing,
    /// shared by every tool that stands for something a person does to a control (#1069).
    /// </summary>
    /// <remarks>
    /// The verb is quoted rather than used as one: "no person could set_value it" is not a sentence, and the
    /// caller's own spelling of an action is not necessarily the canonical one.
    /// </remarks>
    // shows: as the guard read it, false or null; read once, as a second read could answer differently.
    private static string NotShowingRefusal(Control control, string verb, bool? shows = false) =>
        $"'{Safe(() => LabelOf(control, ControlAutomationPeer.CreatePeerForElement(control)), control.GetType().Name)}' "
        + (shows is null
            // Refused all the same: a read that threw once counted as showing, and a hidden control was acted on (#1280).
            ? "is not known to be showing: whether it or a container is hidden could not be read. "
              + $"No person could be sure of reaching it, so '{verb}' was refused and nothing was done."
            : "is not showing: it or a container is hidden, which dump_visual_tree marks \"isHidden\": true. "
              + $"No person could reach it, so '{verb}' was refused and nothing was done.");

    private static string? ShowingCopyHint(Control control)
    {
        if (TopLevel.GetTopLevel(control) is not Window window) return null;
        var label = LabelOf(control);
        var id = NullIfEmpty(AutomationProperties.GetAutomationId(control));
        var paths = new List<string>();
        Walk(window, "Window");
        return paths.Count switch
        {
            0 => null,
            1 => $" a showing copy is at '{paths[0]}'; target that",
            _ => $" {paths.Count} showing copies are in this window; first paths: "
                 + string.Join(", ", paths.Take(3).Select(p => $"'{p}'")) + "; target the one you want",
        };

        void Walk(Control parent, string path)
        {
            var children = new List<MeaningfulChild>();
            CollectMeaningfulChildren(parent, children);
            foreach (var child in children)
            {
                var candidate = child.Control;
                var childPath = path + "/" + BestSegment(child, children);
                if (candidate.GetType() == control.GetType() && Shows(candidate) is true
                    && (LabelOf(candidate) == label
                        || id is not null && AutomationProperties.GetAutomationId(candidate) == id))
                    paths.Add(childPath);
                Walk(candidate, childPath);
            }
        }
    }

    /// <summary>
    /// The list <paramref name="item"/> belongs to, when that list holds several selected items at once.
    /// </summary>
    private static ListBox? MultiSelectOwner(Control item) =>
        ItemsControl.ItemsControlFromItemContainer(item) is ListBox owner
        && owner.SelectionMode.HasFlag(SelectionMode.Multiple)
        && owner.IndexFromContainer(item) >= 0
            ? owner
            : null;

    /// <summary>
    /// Adds a list item to the selection, or removes a selected list or tree item. <c>select</c>
    /// replaces a selection, so without this the designer canvas could hold one control or, through Ctrl+A,
    /// all of them, and the Format commands, which act on a chosen group, could not be aimed (#661).
    /// </summary>
    private static InteractOutcome DoChangeSelection(Control control, AutomationPeer peer, string? value, bool add)
    {
        var verb = add ? "add_to_selection" : "remove_from_selection";
        if (NullIfEmpty(value) is not null)
            return Err($"{verb} takes no value: target the item itself by its path from dump_visual_tree");
        if (!add && control is TreeViewItem item && item.FindAncestorOfType<TreeView>() is { } tree
            && tree.TreeItemFromContainer(item) is { } data)
        {
            var selected = tree.SelectedItems?.Contains(data) == true;
            if (selected) tree.SelectedItems?.Remove(data);
            if (tree.SelectedItems?.Contains(data) == true)
                return Err($"remove_from_selection did not clear '{LabelOf(control, peer)}'; try click with value 'ctrl'");
            var names = TreeSelectedNames(tree);
            var summary = names.Count == 0 ? "nothing is selected now"
                : $"{names.Count} item{(names.Count == 1 ? " is" : "s are")} selected now: {string.Join(", ", names)}";
            var treeChange = selected ? $"removed '{LabelOf(control, peer)}' from the selection"
                : $"'{LabelOf(control, peer)}' was not selected";
            return Ok($"{treeChange}; {summary}");
        }
        var owner = ItemsControl.ItemsControlFromItemContainer(control) as ListBox;
        if (owner is null || owner.IndexFromContainer(control) < 0)
            return add && peer.GetProvider<ISelectionItemProvider>() is not null
                ? Err($"'{LabelOf(control, peer)}' is in a list that holds one selection at a time, so "
                      + "add_to_selection cannot add another item; use select")
                : Unsupported(verb);
        if (add && MultiSelectOwner(control) is null)
            return Err($"'{LabelOf(control, peer)}' is in a list that holds one selection at a time, so "
                       + "add_to_selection cannot add another item; use select");

        var index = owner.IndexFromContainer(control);
        var label = LabelOf(control, peer);
        var was = owner.Selection.IsSelected(index);
        if (add) owner.Selection.Select(index);
        else owner.Selection.Deselect(index);
        if (!add && owner.Selection.IsSelected(index))
            return Err($"remove_from_selection did not clear '{label}'; try click with value 'ctrl'");

        var change = add
            ? was ? $"'{label}' was already selected" : $"added '{label}' to the selection"
            : was ? $"removed '{label}' from the selection" : $"'{label}' was not selected";
        return Ok($"{change}; {SelectionSummary(owner)}");
    }

    /// <summary>
    /// What <paramref name="owner"/> has selected, by the names its rows carry in dump_visual_tree, or an item's own text
    /// where its row does not exist. Read from the list itself: a ListBox's or TabControl's peer offers no selection
    /// provider, so inspect_element reported nothing selected on every one of them (#1263).
    /// </summary>
    /// <summary>
    /// What a tree has selected, each named as its row reads, or as its item does when the row is not realised. A TreeView
    /// is no SelectingItemsControl and its peer offers no selection provider, so nothing else reads it (#2288).
    /// </summary>
    private static List<string> TreeSelectedNames(TreeView tree) =>
        tree.SelectedItems?.Cast<object>().Select(x =>
            tree.GetVisualDescendants().OfType<TreeViewItem>()
                .FirstOrDefault(candidate => ReferenceEquals(tree.TreeItemFromContainer(candidate), x)) is { } row
                ? LabelOf(row) : NameWithoutRow(x, 0)).ToList() ?? [];

    private static List<string> SelectedNames(SelectingItemsControl owner, bool primaryFirst = false) =>
        [.. (owner is ListBox list ? list.Selection.SelectedIndexes : owner.SelectedIndex >= 0 ? [owner.SelectedIndex] : [])
            .OrderBy(i => primaryFirst && i == owner.SelectedIndex ? 0 : 1)
            .Select(i => owner.ContainerFromIndex(i) is Control c
                ? LabelOf(c, ControlAutomationPeer.CreatePeerForElement(c))
                : NameWithoutRow(owner.Items[i], i))];

    /// <summary>
    /// What a selected item is called while it has no row, scrolled out of a long list or in one with no room to show it.
    /// The IDE's view models carry the name their row is given as <c>AccessibleName</c>, which the row binds, so that is
    /// the name the dump would show. Failing that, the item's own text, but never its type's name, which names no row:
    /// a Properties list too short to show a row answered "HexIDE.VisualDesigner.PropertyViewModel" (#2026).
    /// </summary>
    private static string NameWithoutRow(object? item, int index)
    {
        if (Safe(() => item?.GetType().GetProperty("AccessibleName")?.GetValue(item), null) is string { Length: > 0 } accessible)
            return accessible;
        var text = Safe(() => item?.ToString(), null);
        return text is null || IsDefaultText(text, item)
            ? $"#{index}, which has no row to read a name from: a list makes rows only for what it shows"
            : text;
    }

    /// <summary>
    /// Whether <paramref name="text"/> is what <paramref name="item"/> gives with no ToString of its own: its type, as object
    /// gives it, generic arguments and all, or a record's members after its type's name.
    /// </summary>
    private static bool IsDefaultText(string text, object? item) =>
        item is not null
        && (text == item.GetType().ToString() || text.StartsWith(item.GetType().Name + " {", StringComparison.Ordinal));

    /// <summary>
    /// The text <paramref name="box"/> shows for its selection while closed: the text blocks of the presenter that shows
    /// its selection box item, through its item template. Null with nothing selected, or no text to read.
    /// </summary>
    private static string? ShownSelectionOf(ComboBox box) =>
        box.SelectionBoxItem is { } item
        && box.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => ReferenceEquals(p.Content, item)) is { } presenter
            ? NullIfEmpty(string.Join(" ", presenter.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Text)))
            : null;

    /// <summary>What a multi-select list now holds, by the names its rows carry in dump_visual_tree.</summary>
    private static string SelectionSummary(ListBox owner)
    {
        var names = SelectedNames(owner, primaryFirst: true);
        return names.Count switch
        {
            0 => "nothing is selected now",
            1 => $"1 item is selected now: {names[0]}",
            _ => $"{names.Count} items are selected now: {string.Join(", ", names)}; {names[0]} is the primary item (on a form designer, the reference Align and Make Same measure against)",
        };
    }

    /// <summary>
    /// <summary>
    /// Selects <paramref name="item"/> through its provider and reads the result back: null when it took, else the
    /// refusal saying so. The provider's Select does nothing for an item its list cannot place, and a list may put its
    /// selection back, so a select that reported success without looking once named a Call Stack frame as chosen while
    /// the debugger stayed in another (#1182). A list its selection handler replaced has let the item go, so there is
    /// nothing to read back; the selection took if the list announced it. A provider that cannot be read is read through
    /// its list instead, for a row the list still holds; with neither, the refusal says whether it took is not known
    /// (#1259). A list that puts its selection back on a later dispatcher pass is not seen: the read is made when Select
    /// returns.
    /// </summary>
    internal static InteractOutcome? SelectVerified(Control item, ISelectionItemProvider sip, string label)
    {
        var owner = ItemsControl.ItemsControlFromItemContainer(item) as SelectingItemsControl;
        var data = owner is null ? null : Safe(() => owner.ItemFromContainer(item), null);
        var announced = false;
        void Changed(object? sender, SelectionChangedEventArgs e) =>
            announced |= data is not null && e.AddedItems.Contains(data);
        owner?.SelectionChanged += Changed;
        try { sip.Select(); }
        finally { owner?.SelectionChanged -= Changed; }

        // Null when the provider threw: unread, which is neither outcome (#1259). It once fell back to selected, so a
        // throwing peer was replied to as chosen without anything having been read. A row its list still holds is read
        // from the list's own mark on it, which is all Avalonia's peer reads.
        var listed = owner is not null && ItemsControl.ItemsControlFromItemContainer(item) is not null
                                       && owner.IndexFromContainer(item) >= 0;
        var selected = Safe<bool?>(() => sip.IsSelected, null)
                       ?? (listed ? item.GetValue(SelectingItemsControl.IsSelectedProperty) : null);
        if (selected == true || announced && !listed)
            return null;
        var now = owner is ListBox list ? $"; {SelectionSummary(list)}" : "";
        if (selected is null)
            return Err($"'{label}': {SelectionUnread}, and "
                       + (owner is null
                           ? "it has no list that reports its selection, so whether it took is not known. Dump it to see"
                           : $"its list no longer holds it and did not announce it, so whether it took is not known{now}. "
                             + "Dump the list to see what is selected"));
        return Err($"'{label}' was not selected: its list did not take the selection{now}. Dump the list again, as the "
                   + "row may no longer be the one it showed, or select it with press_key, as a person would");
    }

    /// <summary>What a refusal from <see cref="SelectVerified"/> says when the outcome could not be read either way.</summary>
    internal const string SelectionUnread = "select was sent, but its selection could not be read back";

    // The read-backs below follow SelectVerified: an action is replied to with what was read once it returned, and one
    // whose result cannot be read says so rather than claiming it (#1280). A state set in a later dispatcher pass is not
    // seen. On a running form that is every state its program sets, as a VB event is raised through the program's
    // queue, so there toggle and set_value are read once the wait for the program finds it idle (Settled).

    /// <summary>Whether <paramref name="control"/> is on a running program's window, whose events run later.</summary>
    private static bool InProgram(Control control) =>
        TopLevel.GetTopLevel(control) is Window window && ProgramOf(window) is not null;

    /// <summary>
    /// Toggles through <paramref name="toggle"/> and replies with the state it moved to, read back; on a running form,
    /// once its program has run (<paramref name="inProgram"/>).
    /// </summary>
    /// <remarks>
    /// A toggle that did not move is still replied to as done: the control's Click ran, as a radio button's does though
    /// a click never turns it off, so a caller told it failed would click again and run that twice.
    /// </remarks>
    internal static InteractOutcome ToggleVerified(IToggleProvider toggle, string label, bool radio = false, bool inProgram = false)
    {
        var was = Safe<ToggleState?>(() => toggle.ToggleState, null);
        toggle.Toggle();
        if (inProgram)
            return Ok($"toggled '{label}'") with
            {
                Settled = () => ToggleMoved(was, Safe<ToggleState?>(() => toggle.ToggleState, null), radio, "the program"),
            };
        var now = Safe<ToggleState?>(() => toggle.ToggleState, null);
        return now is null || was is null
            ? Err($"'{label}': toggle was sent, but its state could not be read back, so whether it moved is not known. "
                  + "Dump it to see")
            : Ok($"toggled '{label}'{ToggleMoved(was, now, radio, "its handler or a binding")}");
    }

    /// <summary>The clause a toggle's reply ends with: the state it moved to, or that it stayed, and why.</summary>
    private static string ToggleMoved(ToggleState? was, ToggleState? now, bool radio, string who) => (was, now) switch
    {
        (null, _) or (_, null) => ", but whether it moved could not be read back",
        var (from, to) when from != to => $": it is now {ToggleWords(to.Value)}",
        // Only a radio button left on was not turned off by a click; one left off was turned on and put back (#2050 review).
        (_, var to) => radio && to == ToggleState.On
            ? $", and it is still {ToggleWords(to!.Value)}: a click does not turn a radio button off; select another to"
            : $", but {who} put it back: it is still {ToggleWords(to!.Value)}",
    };

    private static string ToggleWords(ToggleState state) => state switch
    {
        ToggleState.On => "on",
        ToggleState.Off => "off",
        _ => "neither on nor off",
    };

    /// <summary>
    /// The reply to an expand or collapse sent through <paramref name="provider"/>, from its state read back. A
    /// provider that took it partly, as a tree that opens one level, counts as expanded.
    /// </summary>
    internal static InteractOutcome ExpandedVerified(IExpandCollapseProvider provider, string label, bool expand)
    {
        var verb = expand ? "expanded" : "collapsed";
        return Safe<ExpandCollapseState?>(() => provider.ExpandCollapseState, null) switch
        {
            null => Err($"'{label}': {(expand ? "expand" : "collapse")} was sent, but its state could not be read back, "
                        + "so whether it took is not known. Dump it to see"),
            ExpandCollapseState.Collapsed when expand => Err($"'{label}' was not expanded: it is still collapsed"),
            ExpandCollapseState.LeafNode when expand => Err($"'{label}' was not expanded: it has nothing to show"),
            ExpandCollapseState.Expanded or ExpandCollapseState.PartiallyExpanded when !expand =>
                Err($"'{label}' was not collapsed: it is still expanded"),
            _ => Ok($"{verb} '{label}'"),
        };
    }

    /// <summary>
    /// The reply to opening or closing a <paramref name="kind"/>, from <paramref name="isOpen"/> read back as the call
    /// returns: a popup cancelled as it opened or closed is seen, one light-dismissed a moment later is not.
    /// </summary>
    internal static InteractOutcome PopupVerified(Func<bool> isOpen, bool open, string kind, string label) =>
        Safe<bool?>(() => isOpen(), null) switch
        {
            null => Err($"{(open ? "open" : "close")} was sent to the {kind} on '{label}', but whether it is open could not "
                        + "be read back. Dump it to see"),
            var now when now == open => Ok($"{(open ? "opened" : "closed")} {kind} on '{label}'"),
            _ => Err(open
                ? $"the {kind} on '{label}' did not open: it was cancelled or closed as it opened"
                : $"the {kind} on '{label}' did not close; press_key Escape closes it, as a person would"),
        };

    /// <summary>
    /// The reply to a <c>set_value</c> of <paramref name="value"/> through <paramref name="provider"/>, from the value
    /// read back: a control that limits or reformats what it is given holds something else (#1280). On a running form,
    /// read once its program has run (<paramref name="inProgram"/>), whose Change event may rewrite it.
    /// </summary>
    internal static InteractOutcome ValueVerified(IValueProvider provider, string value, string label, bool inProgram = false)
    {
        if (inProgram)
            return Ok($"set value of '{label}' to '{value}'") with
            {
                Settled = () => Read() switch
                {
                    (false, _) => ", but what it holds could not be read back",
                    (_, var now) when (now ?? "") == value => null,
                    (_, var now) => $", and the program then made it '{now}'",
                },
            };
        return Read() switch
        {
            (false, _) => Err($"'{label}': set_value was sent, but its value could not be read back, so whether it took "
                              + "is not known. Dump it to see"),
            (_, var now) when (now ?? "") == value => Ok($"set value of '{label}' to '{value}'"),
            (_, var now) => Err($"'{label}' did not take '{value}': it holds '{now}', as a control that limits or "
                                + "reformats what it is given does"),
        };

        (bool Read, string? Value) Read() => Safe<(bool, string?)>(() => (true, provider.Value), (false, null));
    }

    // The window's handling of its platform's close, which its title-bar button and Alt+F4 reach: a close no program
    // asked for (IsProgrammatic false, WindowCloseReason.WindowClosing), which a Yes/No or Abort/Retry/Ignore MsgBox
    // refuses and a form's QueryUnload may refuse. Avalonia keeps it internal; Window.Close is the programmatic close a
    // guard lets through.
    private static readonly System.Reflection.PropertyInfo? PlatformClosing = typeof(Avalonia.Platform.IWindowImpl).GetProperty(
        "Closing", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    /// <summary>
    /// Closes the window holding <paramref name="control"/> as a person does with its close box, which nothing else on
    /// the surface can (#829): the title bar is the platform's, outside the control tree, and the platform takes Alt+F4
    /// before a key reaches a control. Whether it closed is read by <see cref="AfterClose"/>, since a form's close box asks
    /// the program first and closes once it has unloaded.
    /// </summary>
    private static InteractOutcome DoClose(Control control)
    {
        if ((control as Window ?? TopLevel.GetTopLevel(control) as Window) is not { } window)
            return Err($"'{Describe(control)}' is not in a window that can be closed");
        var title = TitleOf(window);
        if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: var main }
            && ReferenceEquals(main, window))
            return Err($"'{title}' is the IDE's main window: closing it shuts the IDE down and this server with it, so "
                       + "close refuses it and nothing was done. shutdown_ide does that and reports how it went. A "
                       + "docked pane is part of that window: its own close button closes it, with invoke");
        // A window with a modal dialog over it takes no close-box click or Alt+F4 until the dialog is answered, so a
        // person cannot close it, and a close from here would close the dialog with it (#829 review).
        if (window.OwnedWindows.FirstOrDefault(w => w.IsDialog && w.IsVisible) is { } over)
        {
            OpenDialogs.NamedInReply(over);
            return Err($"'{title}' has a dialog open over it, {OpenDialogs.DescriptionOf(over)}, and takes no close until that is "
                       + "answered, as a person finds; nothing was done. Answer or close that dialog first");
        }
        if (PlatformClosing?.GetValue(window.PlatformImpl) is not Func<WindowCloseReason, bool> closing)
            return Err($"this build of Avalonia does not expose a window's close-box handling, so '{title}' cannot be "
                       + "closed as a person closes it; nothing was done");
        closing(WindowCloseReason.WindowClosing);
        return Ok($"asked '{title}' to close, as its close box does");
    }

    /// <summary>
    /// <paramref name="outcome"/> of a <c>close</c>, with whether the window it asked is still open now: read once what
    /// the close set off has run, as a form's close box closes the form only once the program has unloaded it.
    /// </summary>
    public static InteractOutcome AfterClose(Control acted, InteractOutcome outcome, ProgramWatch? watch = null)
    {
        if (!outcome.Success)
            return outcome;
        // A closed window has no platform window, and the controls it held are no longer in one. A hidden one keeps its
        // platform window: a form's QueryUnload may hide it, which leaves it loaded and its program running.
        var window = acted as Window ?? TopLevel.GetTopLevel(acted) as Window;
        var state = window is not { PlatformImpl: not null } ? "; it is closed"
            : !window.IsVisible ? "; it is hidden, not closed: the program behind it hid it, and it is still loaded"
            // A program the wait left running has not decided yet: a QueryUnload asking a MsgBox, or paused in it.
            : watch is { Pump.IsIdle: false } ? "; it is still open while the program decides, as the note says"
            : "; it is still open: the window, or the program behind it, refused, as it may a person's close";
        return outcome with { Detail = outcome.Detail + state };
    }

    /// <summary>Whether <paramref name="action"/> names <paramref name="verb"/>, read as <see cref="Interact"/> reads it.</summary>
    public static bool IsVerb(string? action, string verb) => Normalise(action) == Normalise(verb);

    private static string TitleOf(Window window) => string.IsNullOrEmpty(window.Title) ? window.GetType().Name : window.Title;

    private static InteractOutcome DoSelect(Control control, AutomationPeer peer, string? value, bool matchEmptyValue = false)
    {
        // value absent (treating "" as absent, like "omit if the target is the item") → select the target.
        if (!matchEmptyValue) value = NullIfEmpty(value);
        if (value is null)
        {
            // A tree's item is selected through its tree, as before Avalonia 12.0.5 gave its peer a provider of its own.
            if (control is not TreeViewItem && peer.GetProvider<ISelectionItemProvider>() is { } selfSip)
            {
                // Named before the dropdown closes over it, as invoke names a button before it closes its dialog.
                var label = LabelOf(control, peer);
                var dropDown = OpenDropDownHolding(control);
                if (SelectVerified(control, selfSip, label) is { } refused)
                {
                    CloseDropDown(dropDown);
                    return refused;
                }
                return Ok($"selected '{label}'{CloseDropDown(dropDown)}");
            }

            if (TrySelectThroughOwningGrid(control))
                return Ok($"selected '{LabelOf(control, peer)}'");
            if (control is TreeViewItem)
                return TrySelectThroughOwningTree(control, out var refusal)
                    ? Ok($"selected '{LabelOf(control, peer)}'")
                    : Err(refusal);

            return Unsupported("select");
        }

        if (control is ComboBox combo)
            return SelectComboItem(combo, value);

        // value present → match against the realized selectable items (the target itself if it is one, plus
        // its selectable descendants) using the SAME precedence + uniqueness as path resolution, so the two
        // addressing paths agree and an ambiguous text never silently selects the wrong row.
        var candidates = SelectableCandidates(control);
        // Matched among the rows that show, as a person reads them: a hidden row (the Properties window hides those
        // of a collapsed category) is neither selected by its text nor allowed to make a showing one ambiguous. One
        // that only a hidden row matches is refused as addressing it by path is, rather than reported as missing.
        var hiddenRows = candidates.Where(c => Shows(c.Control) is not true).ToList();
        var hiddenNamesake = false;
        if (hiddenRows.Count > 0)
        {
            candidates = candidates.Except(hiddenRows).ToList();
            var hiddenMatches = MatchByDiscriminator(hiddenRows, value).matches;
            hiddenNamesake = hiddenMatches.Count > 0;
            if (MatchByDiscriminator(candidates, value).matches.Count == 0 && hiddenMatches is [var onlyHidden, ..])
                return Err(NotShowingRefusal(onlyHidden, "select", Shows(onlyHidden))
                           + (CollapsedNodesHolding(onlyHidden) is [var outermost, ..] holders && Shows(outermost) is true
                               ? $" Make it show first, as a person would: 'expand' "
                                 + string.Join(", then ", holders.Select(h => $"'{LabelOf(h)}'"))
                                 + (holders.Count == 1 ? ", which holds it" : ", which hold it")
                               : CollapsedPropertyCategoryHolding(onlyHidden) is { } category
                                   ? $" Make it show first, as a person would: 'toggle' the Button in '{category}', which holds it"
                                   : " Make it show first, as a person would, for instance by expanding what holds it"));
        }
        if (candidates.Count == 0)
            return Err(control switch
            {
                TreeView { ItemCount: 0 } => $"'{LabelOf(control, peer)}' has no items to select",
                TreeView tree => $"'{LabelOf(control, peer)}' has {tree.ItemCount} item(s), and none of them is showing",
                _ => $"'{LabelOf(control, peer)}' has no selectable items realized — if it's a dropdown, 'expand' it first, then dump_visual_tree to read the item text",
            });

        var (matches, facet) = MatchByDiscriminator(candidates, value);
        var broughtIntoView = false;
        // Only where a row has no container: the item count holds the hidden rows too, so comparing it with the rows that
        // show sent every miss in a list with a hidden row here, to scroll nothing and say it had (#1256).
        if (matches.Count == 0 && control is ListBox virtualized
            && Enumerable.Range(0, virtualized.ItemCount).Any(i => OutOfView(virtualized, i)))
        {
            HashSet<int> hidden = [.. hiddenRows.Select(r => virtualized.IndexFromContainer(r.Control)).Where(i => i >= 0)];
            (matches, facet, broughtIntoView, var hiddenRefusal) = MatchAmongUnrealized(virtualized, value, hidden);
            if (hiddenRefusal is not null)
                return Err(hiddenRefusal + " Make it show first, as a person would, for instance by expanding what holds it");
            if (matches.Count == 0)
                return Err($"no selectable item matching '{value}' among the {virtualized.ItemCount - hidden.Count} item(s) "
                           + $"showing in '{LabelOf(control, peer)}'{HiddenNote(hidden.Count)} — the ones not on screen were "
                           + "brought into view to check, and the list was put back where it was; check the exact text via "
                           + "dump_visual_tree");
        }
        if (matches.Count == 0)
        {
            // A tree node's children do not exist until it is first expanded, so a name under a node never opened
            // matches nothing and no dump shows it either. Checking the spelling would get nowhere.
            var unopened = candidates.Select(c => c.Control)
                .OfType<TreeViewItem>().Where(n => n is { IsExpanded: false, ItemCount: > 0 }).ToList();
            if (unopened.Count > 0)
                return Err($"no item matching '{value}' among the {candidates.Count} showing in '{LabelOf(control, peer)}'. "
                           + $"{unopened.Count} collapsed node(s) hold items that do not show, and may not exist yet, until "
                           + "the node is expanded: "
                           + string.Join(", ", unopened.Take(5).Select(n => $"'{LabelOf(n)}'"))
                           + (unopened.Count > 5 ? ", …" : "")
                           + ". 'expand' the one that holds it, then select again");
            return Err($"no selectable item matching '{value}' among {candidates.Count} realized item(s){HiddenNote(hiddenRows.Count)} — check the exact text via dump_visual_tree");
        }
        if (matches.Count > 1)
            return Err($"ambiguous select '{value}' ({matches.Count} {facet} matches); target the item directly by path instead");

        var scrolled = broughtIntoView ? " (it was scrolled out of view, so the list was scrolled to it first)" : "";
        // A hidden item of the same name was left out of the match, as a person would not see it; say so, so that a
        // caller who meant that one knows to make it show and address it by path.
        var namesake = hiddenNamesake ? $" (another '{value}' is not showing and was left alone)" : "";
        if (ControlAutomationPeer.CreatePeerForElement(matches[0]).GetProvider<ISelectionItemProvider>() is { } sip)
        {
            var dropDown = OpenDropDownHolding(matches[0]);
            if (SelectVerified(matches[0], sip, value) is { } refused)
            {
                CloseDropDown(dropDown);
                return refused;
            }
            return Ok($"selected '{value}'{scrolled}{namesake}{CloseDropDown(dropDown)}");
        }

        if (TrySelectThroughOwningGrid(matches[0]))
            return Ok($"selected '{value}'{scrolled}{namesake}");
        if (matches[0] is TreeViewItem)
            return TrySelectThroughOwningTree(matches[0], out var refusal)
                ? Ok($"selected '{value}'{scrolled}{namesake}")
                : Err(refusal);

        return Unsupported("select");
    }

    // Snapshot names, not containers: scrolling a dropdown recycles its rows. Read every row before matching,
    // so an offscreen namesake is ambiguous too, and a miss can name all available items (#1672).
    private sealed record ComboRow(int Index, string? AutoId, string? Name, string? Label, string Shown);

    private static InteractOutcome SelectComboItem(ComboBox combo, string value)
    {
        if (!combo.IsDropDownOpen)
            return Err($"'{LabelOf(combo)}' has no selectable items realized — 'expand' it first, then select");
        var scroller = combo.GetRealizedContainers().FirstOrDefault()?.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        var original = scroller?.Offset;
        List<ComboRow> rows = [];
        try
        {
            for (var i = 0; i < combo.ItemCount; i++)
            {
                combo.ScrollIntoView(i);
                combo.UpdateLayout();
                if (combo.ContainerFromIndex(i) is not Control row || Shows(row) is not true) continue;
                var info = Classify(row);
                rows.Add(new ComboRow(i, info.AutoId, row.Name, MeaningfulLabelOf(info.Peer), LabelOf(row)));
            }
            var (matches, facet) = MatchByDiscriminator(rows, value, r => r.AutoId, r => r.Name, r => r.Label);
            if (matches.Count == 0)
                return Err($"no selectable item matching '{value}' among {rows.Count} item(s); available items: "
                           + (rows.Count == 0 ? "none" : string.Join(", ", rows.Take(20).Select(r => $"'{r.Shown}'")))
                           + (rows.Count > 20 ? $"; {rows.Count - 20} more item(s) omitted" : ""));
            if (matches.Count > 1)
                return Err($"ambiguous select '{value}' ({matches.Count} {facet} matches); target the item directly by path instead");
            combo.ScrollIntoView(matches[0].Index);
            combo.UpdateLayout();
            if (combo.ContainerFromIndex(matches[0].Index) is not Control target)
                return Err($"the item matching '{value}' could not be brought into view; nothing was selected");
            return DoSelect(target, ControlAutomationPeer.CreatePeerForElement(target), null);
        }
        finally
        {
            if (scroller is not null && original is { } offset)
                scroller.Offset = offset;
        }
    }

    /// <summary>
    /// Brings a virtualizing list's items into view, a screenful at a time, until one matches
    /// <paramref name="value"/>; if none does, puts the list back where it was.
    /// </summary>
    /// <remarks>
    /// A list only creates rows for what is on screen, so a row scrolled out of view has no container, no
    /// automation name, and nothing to match. The Properties window's <c>(Name)</c> row was out of reach this
    /// way whenever the selected row had been scrolled into a short list, and the refusal counted only the
    /// rows on screen, so it read as if the row did not exist (#627). A person scrolls to find a row; this
    /// does the same. A row is identified by the automation name its container gets once realized, which is
    /// what path addressing and <see cref="MatchByDiscriminator"/> use, so there is no second notion of what
    /// an item is called.
    /// </remarks>
    /// Matched among the rows that show, as <see cref="DoSelect"/> matches the ones on screen (#1256): a hidden row is not
    /// selected by its text. When only hidden ones match, the refusal of the first is returned, worded as it is found:
    /// once the list scrolls on, its container is recycled for another row, and a refusal read later named that one. The
    /// index of each row seen not to show is added to <paramref name="hidden"/>, for a miss to count.
    private static (List<Control> Matches, string Facet, bool BroughtIntoView, string? HiddenRefusal) MatchAmongUnrealized(
        ListBox list, string value, HashSet<int> hidden)
    {
        var scroller = list.Scroll as ScrollViewer;
        var original = scroller?.Offset;
        string? hiddenRefusal = null;
        for (var index = 0; index < list.ItemCount; index++)
        {
            if (!OutOfView(list, index)) continue;
            list.ScrollIntoView(index);
            list.UpdateLayout();
            var rows = SelectableCandidates(list);
            var notShowing = rows.Where(c => Shows(c.Control) is not true).ToList();
            foreach (var row in notShowing)
                if (list.IndexFromContainer(row.Control) is >= 0 and var at)
                    hidden.Add(at);
            var (matches, facet) = MatchByDiscriminator([.. rows.Except(notShowing)], value);
            if (matches.Count > 0) return (matches, facet, true, null);
            if (hiddenRefusal is null && MatchByDiscriminator(notShowing, value).matches is [var only, ..])
                hiddenRefusal = NotShowingRefusal(only, "select", Shows(only));
        }

        if (scroller is not null && original is { } offset)
        {
            scroller.Offset = offset;
            list.UpdateLayout();
        }
        return ([], "", false, hiddenRefusal);
    }

    // A row with no container, or one held out of view, which the walk leaves out (#1981): either is scrolled to to be seen.
    private static bool OutOfView(ListBox list, int index) =>
        list.ContainerFromIndex(index) is not { } row || HeldOutOfView(row);

    // What a miss says of the rows left out as not showing.
    private static string HiddenNote(int count) => count switch
    {
        0 => "",
        1 => "; 1 more is not showing and was not matched",
        _ => $"; {count} more are not showing and were not matched",
    };

    /// <summary>
    /// Double-clicks a control: raises <c>DoubleTapped</c>, which bubbles the way a real one does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Not reachable through any provider, which is why it is its own verb.</b> UI Automation has no
    /// pattern for a double-click — it is a gesture, not a control contract — so a surface whose trigger
    /// is a double-click had no representation here at all. That is not a rare shape: the Project
    /// Explorer opens every form, module and project this way, and none of it could be exercised.
    /// </para>
    /// <para>
    /// <b>It selects first, where the target can be selected.</b> A real double-click does, and a verb
    /// that raised the gesture without moving the selection would fire handlers against whatever happened
    /// to be selected before — the wrong document, silently. The reply says whether the selection moved,
    /// so a caller can tell the two halves apart rather than inferring.
    /// </para>
    /// <para>
    /// Handlers are usually attached to the container rather than the item (the Project Explorer's is on
    /// the <c>TreeView</c>) and read <c>e.Source</c> to find which item was hit, so the event is raised on
    /// the target itself and left to bubble.
    /// </para>
    /// </remarks>
    private static InteractOutcome DoDoubleClick(Control control, AutomationPeer peer)
    {
        if (TopLevel.GetTopLevel(control) is not { } topLevel)
            return Err("the control is not attached to a window, so it cannot be clicked");

        // A real double-click's first click picks a dropdown item and closes the dropdown, so the second lands on
        // whatever lay beneath and no DoubleTapped reaches the item. Do that first half and say the rest didn't happen.
        if (OpenDropDownHolding(control) is not null && peer.GetProvider<ISelectionItemProvider>() is not null)
        {
            var picked = DoSelect(control, peer, null);
            return picked.Success
                ? Ok($"{picked.Detail}; no double-click was raised, as a real one's second click lands on what lay "
                   + "beneath the dropdown")
                : picked;
        }

        var treeRefusal = "";
        var selected = control is TreeViewItem
            ? TrySelectThroughOwningTree(control, out treeRefusal)
            : TrySelectThroughOwningGrid(control);
        if (!selected && control is not TreeViewItem && peer.GetProvider<ISelectionItemProvider>() is { } sip)
        {
            // Read back as select reads it (#1182 review): a selection its list did not take is said, not claimed.
            if (SelectVerified(control, sip, LabelOf(control, peer)) is { Error: { } unselected })
                treeRefusal = unselected;
            else
                selected = true;
        }

        // AvaloniaEdit commits on a pointer press, before DoubleTapped would arrive. Use the same
        // insertion request as its pointer handler, after verifying selection, rather than sending keys.
        if (control is ListBoxItem
            && control.FindAncestorOfType<CompletionList>() is { } completion
            && completion.GetLogicalParent() is CompletionWindow completionWindow)
        {
            if (!selected)
                return Err($"the completion item was not selected, so it was not committed: {treeRefusal}");
            var label = LabelOf(control, peer);
            var document = completionWindow.TextArea.Document;
            var before = document.Text;
            completion.RequestInsertion(EventArgs.Empty);
            return Ok($"requested insertion of completion '{label}'; "
                      + (document.Text != before ? "the document text changed" : "the document text did not change")
                      + (completionWindow.IsOpen ? "; the completion list is still open" : "; the completion list closed"));
        }

        var centre = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        if (control.TranslatePoint(centre, topLevel) is not { } atTopLevel)
            return Err($"could not translate the centre of '{Describe(control)}' into window coordinates");

        var pointer = new Avalonia.Input.Pointer(
            Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);
        var pointerArgs = new PointerEventArgs(
            InputElement.DoubleTappedEvent, control, pointer, topLevel, atTopLevel,
            (ulong)Environment.TickCount64, props, KeyModifiers.None);

        control.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, pointerArgs));

        var note = selected ? " (selected it first, as a real double-click does)"
            : treeRefusal.Contains(SelectionUnread, StringComparison.Ordinal) ? $" (whether it was selected first is not known: {treeRefusal})"
            : treeRefusal.Length > 0 ? $" (but it was not selected first: {treeRefusal})"
            : "";
        return Ok($"raised DoubleTapped on '{LabelOf(control, peer)}'{note}; no pointer press was sent; "
                  + "use 'click' with 'double' in 'value' for a running form's DblClick");
    }

    /// <summary>
    /// Clicks a menu item as Avalonia's menu does when a person clicks it: a checkable item's tick moves, Click is raised,
    /// which runs the item's command, and the menu closes unless the item stays open on click. Returns what a reply adds
    /// for those: the tick's new state when it moved, and the menu's closing; "" when neither happened.
    /// </summary>
    /// <remarks>
    /// Raising Click alone, as this once did, skipped the other two (#947): a checkable item such as View > Minimap
    /// changed nothing, and the menu stayed open over what lay below it. The order is Avalonia 12's own, in
    /// <c>DefaultMenuInteractionHandler.Click</c>. An item with a submenu only raises Click: a click on it opens it.
    /// The menu is found before the click, since a command can rebuild the menu and leave the item without a parent.
    /// </remarks>
    public static string ClickMenuItem(MenuItem item)
    {
        var menu = item.HasSubMenu || item.StaysOpenOnClick ? null : MenuHolding(item);
        var wasChecked = item.IsChecked;
        if (!item.HasSubMenu && item.ToggleType == MenuItemToggleType.CheckBox)
            item.SetCurrentValue(MenuItem.IsCheckedProperty, !item.IsChecked);
        else if (!item.HasSubMenu && item.ToggleType == MenuItemToggleType.Radio && !item.IsChecked)
            item.SetCurrentValue(MenuItem.IsCheckedProperty, true);

        item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));

        // A running form's item is put back before its Click, and the program sets its tick when it runs, which the caller
        // reads once it has (#1255): read now, it said the tick stayed where the program was about to move it.
        var tick = item is HexIDE.Runtime.BuiltinControls.VBMenu || item.ToggleType == MenuItemToggleType.None
                   || item.IsChecked == wasChecked ? ""
            : item.IsChecked ? " (now ticked)" : " (now unticked)";
        return tick + (CloseMenu(menu) ? " and closed the menu, as a click does" : "");
    }

    /// <summary>
    /// Reads the tick of every item in the running form's menu that <paramref name="item"/> is in, and returns what says,
    /// once its program has run, which ticks it moved: "; the program ticked 'Intermediate' and unticked 'Beginner'", or
    /// null when it moved none.
    /// </summary>
    /// <remarks>
    /// The whole menu, not the item alone: a level menu ticks the chosen item and unticks the others, and naming only the
    /// chosen one left the others' change unsaid (#1255 review).
    /// </remarks>
    private static Func<string?> TicksMoved(HexIDE.Runtime.BuiltinControls.VBMenu item)
    {
        HexIDE.Runtime.BuiltinControls.VBMenu[] items = MenuHolding(item) is { } menu
            ? [.. menu.GetLogicalDescendants().OfType<HexIDE.Runtime.BuiltinControls.VBMenu>()]
            : [item];
        var was = items.Select(i => i.IsChecked).ToArray();
        return () =>
        {
            var now = items.Select((i, at) => Safe(() => i.IsChecked, was[at])).ToArray();
            string Named(bool ticked) => string.Join(", ", items.Where((_, at) => now[at] != was[at] && now[at] == ticked)
                .Select(i => $"'{LabelOf(i, ControlAutomationPeer.CreatePeerForElement(i))}'"));
            string[] said = [.. new[] { ("ticked", Named(true)), ("unticked", Named(false)) }
                .Where(p => p.Item2.Length > 0).Select(p => $"{p.Item1} {p.Item2}")];
            return said.Length == 0 ? null : $"; the program {string.Join(" and ", said)}";
        };
    }

    /// <summary>The menu bar, context menu or flyout menu an item is in, or null.</summary>
    private static MenuBase? MenuHolding(MenuItem item)
    {
        for (var at = item.Parent; at is not null; at = at.Parent)
            if (at is MenuBase menu) return menu;
        return null;
    }

    /// <summary>
    /// Closes the menu a header or an item is in, whole, as Avalonia's menu does after a click; false when it was not
    /// open, or when it stayed open.
    /// </summary>
    /// <remarks>
    /// Closing only the header's own submenu, as <c>collapse</c> once did, left the bar open: the header stayed lit and
    /// the bar kept taking keys (#947). <c>Menu.Close</c> does nothing to a bar that does not count itself open, so the
    /// headers are closed and unlit here as well.
    /// </remarks>
    private static bool CloseMenu(MenuBase? menu)
    {
        if (menu is null) return false;
        var items = menu.GetRealizedContainers().OfType<MenuItem>().ToList();
        if (!MenuIsOpen(menu))
            return false;
        menu.Close();
        foreach (var header in items)
        {
            if (header.IsSubMenuOpen) header.Close();
            header.SetCurrentValue(SelectingItemsControl.IsSelectedProperty, false);
        }
        return !MenuIsOpen(menu);
    }

    private static bool MenuIsOpen(MenuBase menu) =>
        menu.IsOpen || menu.GetRealizedContainers().OfType<MenuItem>().Any(i => i.IsSubMenuOpen)
                    || menu.FindLogicalAncestorOfType<Popup>() is { IsOpen: true };

    /// <summary>Closes the menu a header belongs to, whole: <see cref="CloseMenu"/> on its <see cref="MenuHolding"/>.</summary>
    private static bool CloseMenuHolding(MenuItem header) => CloseMenu(MenuHolding(header));

    /// <summary>What <c>click</c>'s value may hold, quoted in its refusals and in the tool's description.</summary>
    public const string ClickValue =
        "a point 'x,y' relative to the target's top-left ('.' for decimals; the centre when omitted), a button, left, "
        + "right or middle (left when omitted), any of shift, ctrl and alt to hold, and double for two clicks, in any order and separated by "
        + "spaces, as in '10,5 right double' or 'double'; append to x,y or to <path>:x,y to drag "
        + "(paste a dump_visual_tree path, including spaces); release coordinates are relative to that control, "
        + "or the target without a path; to cannot accompany double; add hold to a drag to stop before its release, with "
        + "the button still down at the to point until action release";

    /// <summary>What <c>release</c>'s value may hold, quoted in its refusals.</summary>
    private const string ReleaseValue =
        "release takes x,y relative to the target, or <path>:x,y, to move the held button there before letting go, or no "
        + "value to let go where it is held";

    /// <summary>
    /// Press and release a pointer at a point on the target (#1033). PointerPressed then PointerReleased are raised on
    /// the element a real press there would reach, so a control's own press and release handlers run as they do for a
    /// person: a Button raises Click from its release, and a running form raises MouseDown, Click and MouseUp in the
    /// order VB6 does (#974). A PointerMoved there comes first, as a person's pointer arrives before it presses, so a
    /// running form's MouseMove does too, unless the last click left the pointer at that point (#2407). <c>invoke</c> goes through an automation peer, which raises Click and no pointer event, and
    /// <c>double_click</c> raises DoubleTapped, so this is the only verb that presses.
    /// </summary>
    /// <remarks>
    /// Raised on the element, as <see cref="Hover(Control, double?, double?)"/> raises its events, not through the input
    /// manager: nothing sets <c>IsPointerOver</c>. A drag establishes the mouse press's implicit capture before
    /// raising the press, then routes held moves and the release to the capturer, or the hit-tested element.
    ///
    /// <para>
    /// Only the press is raised here. The release is the outcome's <see cref="InteractOutcome.Release"/>, which
    /// <see cref="FinishAsync"/> raises as input of its own, after whatever the press set off has run.
    /// </para>
    /// </remarks>
    private static InteractOutcome DoClick(Control control, string? value)
    {
        static InteractOutcome Fail(string error) => new(false, "pointer", null, error);

        if (!TryParseClick(value, out var at, out var button, out var keys, out var twice, out var destination, out var hold, out var problem))
            return Fail(problem);
        if (TopLevel.GetTopLevel(control) is not { } topLevel)
            return Fail("the control is not attached to a window, so it has no coordinates to click at");
        var local = at ?? new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        if (control.TranslatePoint(local, topLevel) is not { } inWindow)
            return Fail($"could not translate ({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}) on '{Describe(control)}' into window coordinates");
        var where = $"({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}) on '{Describe(control)}', which is ({inWindow.X.ToString("0.#", CultureInfo.InvariantCulture)}, {inWindow.Y.ToString("0.#", CultureInfo.InvariantCulture)}) in the window";
        // What a real press there reaches: the topmost element at that point that takes the pointer.
        if (topLevel.InputHitTest(inWindow) is not Avalonia.Interactivity.Interactive hit)
            return Fail($"{NothingTakesThePointer}{where}, so nothing was pressed: the point is outside the target, "
                        + "or on a part of it that is not hit-tested");

        var releasePoint = inWindow;
        var releaseWhere = where;
        if (destination is not null)
        {
            if (ReleasePointOf(control, topLevel, destination, $"click takes {ClickValue}", out var unreadable) is not { } release)
                return Fail($"{unreadable}; nothing was pressed");
            (releasePoint, releaseWhere) = release;
        }

        var watch = WatchProgram(topLevel as Window);
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
        var (held, pressKind, releaseKind, name) = button switch
        {
            MouseButton.Right => (RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed,
                PointerUpdateKind.RightButtonReleased, "right"),
            MouseButton.Middle => (RawInputModifiers.MiddleMouseButton, PointerUpdateKind.MiddleButtonPressed,
                PointerUpdateKind.MiddleButtonReleased, "middle"),
            _ => (RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed,
                PointerUpdateKind.LeftButtonReleased, "left"),
        };
        var rawKeys = (keys.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
                      | (keys.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
                      | (keys.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None);
#if DEBUG
        var saveButton = (hit as Control)?.GetSelfAndVisualAncestors().OfType<Button>().FirstOrDefault();
        var saveWatch = button == MouseButton.Left && saveButton is { IsEffectivelyEnabled: true }
            ? UiSaveFeedback.Watch(saveButton.Command) : null;
#endif
        // A drag in Dock's layout says what it moved, or that nothing moved: one whose drop found no target read the same
        // as one that docked (#2175).
        var dockLayout = destination is null ? null : (hit as Visual)?.FindAncestorOfType<Dock.Avalonia.Controls.DockControl>()?.Layout;
        var placedBefore = dockLayout is null ? null : DockPlacement(dockLayout);
        // Dock starts a drag only from what it marks as a drag area: a tab, a tool window's title strip. Anything else in its
        // layout, a designer drag among them, could not have docked, and saying it had not read as a failure (#2232).
        var pressedDockHandle = dockLayout is not null && (hit as Visual)?.GetSelfAndVisualAncestors()
            .Any(Dock.Settings.DockProperties.GetIsDragArea) == true;
        // What an abandoned hold asks whether its release committed anything: the designer the press landed in (#2231).
        var surface = hold ? (hit as Visual)?.FindAncestorOfType<FormEditView>()?.DataContext as FormEditViewModel : null;
        // A person's pointer arrives before it presses, so the element sees a move first and a running form raises
        // MouseMove before MouseDown. A click raised no move, so a program's MouseMove never ran from one (#2407). A still
        // pointer sends none, so a click where the last one left it moves nothing.
        var arrived = !PointerAlreadyAt(topLevel, inWindow);
        if (arrived)
            hit.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, hit, pointer, topLevel, inWindow,
                (ulong)Environment.TickCount64, new PointerPointProperties(rawKeys, PointerUpdateKind.Other), keys));
        var arrival = arrived ? "; the pointer moved there first, as a person's does"
            : "; the pointer was already there from the last click, so it did not move first";
        // MouseDevice implicitly captures the hit element before a real press. A routed event alone does not:
        // without this, a Label loses its MouseUp when the drag leaves it (#1373). A handler can replace or drop it.
        if (destination is not null)
            pointer.Capture(hit as IInputElement);
        hit.RaiseEvent(new PointerPressedEventArgs(hit, pointer, topLevel, inWindow, (ulong)Environment.TickCount64,
            new PointerPointProperties(held | rawKeys, pressKind), keys));

        var landed = hit as Control;
        var landing = landed is null || ReferenceEquals(landed, control) ? ""
            : control.IsVisualAncestorOf(landed) ? $", landing on its part '{Describe(landed)}'"
            : !new Rect(control.Bounds.Size).Contains(local)
                ? $"; ({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}) is outside '{Describe(control)}', which is "
                  + $"{control.Bounds.Width.ToString("0.#", CultureInfo.InvariantCulture)} × {control.Bounds.Height.ToString("0.#", CultureInfo.InvariantCulture)}, and the press landed on '{Describe(landed)}'"
                : $"; the press landed on '{Describe(landed)}', which covers the target there";
        var holding = keys == KeyModifiers.None ? "" : " holding " + string.Join(" and ",
            new[] { (KeyModifiers.Shift, "shift"), (KeyModifiers.Control, "ctrl"), (KeyModifiers.Alt, "alt") }
                .Where(k => keys.HasFlag(k.Item1)).Select(k => k.Item2));
        var pressed = $"the {name} button{holding} at {where}{landing}";
        // Each held move is input of its own, raised by FinishAsync with the dispatcher's layout run between them, as a
        // real pointer's moves are. Dock adds its drop target on the first move past its threshold and finds the drop
        // from that target's layout, so eight moves raised in one burst left the release over a target with no size,
        // and nothing docked (#2175). A control that captured the pointer on its press takes them, as it would.
        Exception? moveFailed = null;
        // Between any two points, since a held drag moves on from where it stopped, and an abandoned one back to its press.
        List<Action> MovesBetween(Point from, Point to) => [.. Enumerable.Range(1, 8).Select(step => (Action)(() =>
        {
            try
            {
                var point = from + (to - from) * (step / 8.0);
                var receiver = pointer.Captured as Avalonia.Interactivity.Interactive
                    ?? topLevel.InputHitTest(point) as Avalonia.Interactivity.Interactive;
                receiver?.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent, receiver, pointer, topLevel,
                    point, (ulong)Environment.TickCount64, new PointerPointProperties(held | rawKeys, PointerUpdateKind.Other), keys));
            }
            catch (Exception ex) { moveFailed ??= ex; }
        }))];
        List<Action>? moves = destination is null ? null : MovesBetween(inWindow, releasePoint);
        // Where the second press of a double click went, when not where the first did.
        var secondLanding = "";
        InteractOutcome Release(bool second)
        {
            try
            {
                if (moveFailed is not null)
                    return new InteractOutcome(false, "pointer", null,
                        $"pressed {pressed}, and a held move threw, so the button was let go without a release: {moveFailed.Message}");
                // A control that captured the pointer on its press takes the release, as it would from a real pointer.
                var captured = pointer.Captured is not null;
                var releasedOn = pointer.Captured as Avalonia.Interactivity.Interactive
                    ?? topLevel.InputHitTest(releasePoint) as Avalonia.Interactivity.Interactive;
                if (releasedOn is null)
                    return Fail($"pressed {pressed}; nothing takes the pointer at the release point {releaseWhere}");
                releasedOn.RaiseEvent(new PointerReleasedEventArgs(releasedOn, pointer, topLevel, releasePoint,
                    (ulong)Environment.TickCount64, new PointerPointProperties(rawKeys, releaseKind), keys, button));
                PointerNowAt(topLevel, releasePoint);
                var recipient = destination is null ? "" : releasedOn is Control releasedControl
                    ? $"'{Describe(releasedControl)}'{AddressOf(topLevel, "Window", releasedControl)}"
                    : $"'{releasedOn.GetType().Name}'";
                pointer.Capture(null);
                var docked = placedBefore is null ? "" : DockMoved(placedBefore, DockPlacement(dockLayout!), pressedDockHandle);
                var outcome = new InteractOutcome(true, "pointer", $"{(destination is not null ? "dragged" : second ? "double-clicked" : "clicked")} {pressed}{arrival}{(second ? secondLanding : "")}"
                    + (destination is null ? "" : $"; released at {releaseWhere}; release received by {recipient} ({(captured ? "pointer capturer" : "under the release point, no capture")}){docked}"), null,
                    NextClick: twice && !second ? SecondPress : null, ClickWatch: watch);
#if DEBUG
                return UiSaveFeedback.After(saveWatch, outcome);
#else
                return outcome;
#endif
            }
            catch (Exception ex)
            {
                return new InteractOutcome(false, "pointer", null, $"pressed {pressed}, and the release threw: {ex.Message}");
            }
            finally { pointer.Capture(null); }
        }

        // A person's second press lands where the first did, on whatever is there by then. A control that rebuilds its visuals
        // on a click, as a Toolbar rebuilds its buttons on each release, has replaced what the first press landed on, and
        // requiring that to be still attached sent no second click at all (#2324).
        async Task<InteractOutcome> SecondPress()
        {
            // A person's second press comes at least a frame after the first release, so what that release rebuilt has been
            // laid out and drawn and the press finds it. Hit-tested sooner, a Toolbar's new button had no bounds yet and the
            // press fell through to the bar itself (#2324 live check).
            var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            topLevel.RequestAnimationFrame(_ => frame.TrySetResult());
            await Task.WhenAny(frame.Task, Task.Delay(SecondPressFrameWait));
            // And for the render thread to have applied that frame: hit-testing reads the composition the render thread holds,
            // which lagged the layout often enough that one double click in three missed the new button (#2324 live check).
            if (Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(topLevel)?.Compositor is { } compositor)
                await Task.WhenAny(compositor.RequestCompositionBatchCommitAsync().Processed, Task.Delay(SecondPressFrameWait));
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            if (!topLevel.IsVisible)
                return Fail($"clicked {pressed}; the second click was not sent because the window is hidden or closed");
            if (topLevel.InputHitTest(inWindow) is not Avalonia.Interactivity.Interactive again)
                return Fail($"clicked {pressed}; the second click was not sent because nothing takes the pointer at that point now");
            again.RaiseEvent(new PointerPressedEventArgs(again, pointer, topLevel, inWindow, (ulong)Environment.TickCount64,
                new PointerPointProperties(held | rawKeys, pressKind), keys, clickCount: 2));
            if (!ReferenceEquals(again, hit))
                secondLanding = again is Control replacement
                    ? $"; the first click replaced what it landed on, so the second press went to '{Describe(replacement)}'"
                      + $"{AddressOf(topLevel, "Window", replacement)}, which is at that point now"
                    : $"; the first click replaced what it landed on, so the second press went to the '{again.GetType().Name}' now at that point";
            return new InteractOutcome(true, "pointer", $"pressed the second click of {pressed}{secondLanding}; the release is still to come",
                null, Release: () => Release(second: true));
        }

        // A held drag stops where a release would be, with the button still down, and a later release finishes it (#2231).
        InteractOutcome Hold()
        {
            if (moveFailed is not null)
            {
                pointer.Capture(null);
                return Fail($"pressed {pressed}, and a held move threw, so the button was let go without a release: {moveFailed.Message}");
            }
            HeldButton.Begin(new HeldButton(topLevel, pointer, name, inWindow, where, releasePoint, releaseWhere, MovesBetween,
                (point, pointWhere) =>
                {
                    (releasePoint, releaseWhere) = (point, pointWhere);
                    return Release(second: false);
                },
                Captured, surface));
            return new InteractOutcome(true, "pointer",
                $"pressed {pressed}, and moved to {releaseWhere}; the {name} button is still held there, and {Captured()}; "
                + "interact 'release' lets it go", null);
        }

        // What holds the pointer while the button is down, which is what a release will go to.
        string Captured()
        {
            if (pointer.Captured is Control capturer)
                return $"{Named(capturer)} has the pointer capture";
            return topLevel.InputHitTest(releasePoint) is Control under
                ? $"nothing has the pointer capture, so a release goes to {Named(under)} under it"
                : "nothing has the pointer capture, and nothing under it takes the pointer";
        }
        string Named(Control named) => AddressOf(topLevel, "Window", named) is { Length: > 0 } address
            ? $"'{Describe(named)}'{address},"
            : $"'{Describe(named)}'";

        if (hold)
            return new InteractOutcome(true, "pointer", $"pressed {pressed}; the held moves are still to come", null,
                Release: Hold, ClickWatch: watch, HeldMoves: moves);
        return new InteractOutcome(true, "pointer", $"pressed {pressed}; the release is still to come", null,
            Release: () => Release(second: false), ClickWatch: destination is null ? null : watch, HeldMoves: moves);
    }

    /// <summary>
    /// A release point: 'x,y' relative to <paramref name="control"/>, or '&lt;path&gt;:x,y' relative to the control at that
    /// path, in <paramref name="topLevel"/>'s coordinates and in words; or null with why it is not one, ending with
    /// <paramref name="takes"/> when the text could not be read.
    /// </summary>
    private static (Point Point, string Where)? ReleasePointOf(Control control, TopLevel topLevel, string destination, string takes,
        out string? error)
    {
        error = null;
        var colon = destination.LastIndexOf(':');
        var releaseControl = control;
        if (colon >= 0)
        {
            var resolved = Resolve(topLevel, destination[..colon]);
            if (resolved.control is not { } found)
            {
                error = $"could not resolve the release path: {resolved.error}";
                return null;
            }
            releaseControl = found;
        }
        var coordinates = destination[(colon + 1)..].Split(',');
        if (coordinates.Length != 2
            || !double.TryParse(coordinates[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rx)
            || !double.TryParse(coordinates[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ry)
            || !double.IsFinite(rx) || !double.IsFinite(ry))
        {
            error = $"'{destination}' is not a release point; {takes}";
            return null;
        }
        if (releaseControl.TranslatePoint(new Point(rx, ry), topLevel) is not { } translated)
        {
            error = "could not translate the release point into window coordinates";
            return null;
        }
        return (translated, FormattableString.Invariant($"({rx:0.#}, {ry:0.#}) on '{Describe(releaseControl)}', which is ({translated.X:0.#}, {translated.Y:0.#}) in the window"));
    }

    /// <summary>
    /// Lets go of the button <c>click … to x,y hold</c> left down (#2231): at the point <paramref name="value"/> names, moved
    /// to first as a drag's held moves are, or where it is held. The reply is an unheld drag's, so a held drag ends exactly
    /// as an ordinary one does: release point, recipient, capture, and what Dock moved.
    /// </summary>
    private static InteractOutcome DoRelease(Control control, string? value)
    {
        if (HeldButton.Current is not { } held)
            return Err("no button is held, so there is nothing to release: release lets go of the button that "
                       + "click … to x,y hold left down, and a click without hold lets go by itself");
        var (point, where) = (held.At, held.AtWhere);
        IReadOnlyList<Action>? moves = null;
        if (NullIfEmpty(value) is { } destination)
        {
            if (!ReferenceEquals(TopLevel.GetTopLevel(control), held.Window))
                return Err($"the {held.Button} button is held in '{HeldButton.Title(held.Window)}', and '{Describe(control)}' is "
                           + "not in that window, so nothing was done: give a target there, or no value to let go where it is held");
            if (ReleasePointOf(control, held.Window, destination.Trim(), ReleaseValue, out var unreadable) is not { } release)
                return Err($"{unreadable}; nothing was done, and the button is still held");
            (point, where) = release;
            moves = held.MovesTo(point);
        }
        // No longer the held button from here, so neither the idle limit nor a closing window acts on it while the moves and
        // the release, still to come, are raised.
        held.End();
        return new InteractOutcome(true, "pointer", "the release is still to come", null,
            Release: () => held.ReleaseAt(point, where), HeldMoves: moves);
    }

    /// <summary>Where a tool or document sits in a Dock layout, read before and after a drag (#2175).</summary>
    private sealed record DockPlace(Dock.Model.Core.IDockable? Owner, int Index, bool Floating, string[] Beside);

    /// <summary>
    /// Every tool and document in <paramref name="layout"/>, floating windows included, with the dock that holds it, its
    /// tab position there, and what else that dock holds, which is how a dock the IDE gives no name is told apart.
    /// </summary>
    private static Dictionary<Dock.Model.Core.IDockable, DockPlace> DockPlacement(Dock.Model.Core.IDock layout)
    {
        var placed = new Dictionary<Dock.Model.Core.IDockable, DockPlace>(ReferenceEqualityComparer.Instance);
        void Walk(Dock.Model.Core.IDock dock, bool floating)
        {
            var children = dock.VisibleDockables?.ToList() ?? [];
            var leaves = children.Where(c => c is Dock.Model.Controls.ITool or Dock.Model.Controls.IDocument).ToList();
            foreach (var child in children)
            {
                if (child is Dock.Model.Core.IDock inner)
                    Walk(inner, floating);
                else if (leaves.Contains(child))
                    placed[child] = new DockPlace(dock, leaves.IndexOf(child), floating,
                        [.. leaves.Where(l => !ReferenceEquals(l, child)).Select(DockableName)]);
            }
            if (dock is Dock.Model.Controls.IRootDock { Windows: { } windows })
                foreach (var window in windows)
                    if (window.Layout is { } floated)
                        Walk(floated, true);
        }
        Walk(layout, false);
        return placed;
    }

    /// <summary>What a drag changed in a Dock layout, as a clause for its reply: what moved where, or, for a drag pressed on
    /// one of Dock's drag areas (<paramref name="couldDock"/>), that nothing did. Otherwise nothing is said.</summary>
    private static string DockMoved(Dictionary<Dock.Model.Core.IDockable, DockPlace> before,
        Dictionary<Dock.Model.Core.IDockable, DockPlace> after, bool couldDock)
    {
        var moved = new List<string>();
        foreach (var (dockable, now) in after)
        {
            if (!before.TryGetValue(dockable, out var was))
                continue;
            if (!ReferenceEquals(was.Owner, now.Owner) || was.Floating != now.Floating)
                moved.Add($"'{DockableName(dockable)}' from {At(was)} to {At(now)}");
            else if (was.Index != now.Index)
                moved.Add($"'{DockableName(dockable)}' within {At(now)}, from tab {was.Index + 1} to tab {now.Index + 1}");
        }
        if (moved.Count > 0)
            return "; Dock moved " + string.Join(", and ", moved);
        return couldDock
            ? "; Dock's layout is unchanged, so nothing was docked: the release was not over one of Dock's drop targets, "
              + "or dropped what it dragged where it was"
            : "";

        static string At(DockPlace place) =>
            (place.Beside.Length == 0 ? "a dock of its own" : "the dock with " + string.Join(", ", place.Beside.Select(b => $"'{b}'")))
            + (place.Floating ? ", in a floating window" : "");
    }

    private static string DockableName(Dock.Model.Core.IDockable dockable) =>
        !string.IsNullOrEmpty(dockable.Title) ? dockable.Title
        : !string.IsNullOrEmpty(dockable.Id) ? dockable.Id
        : dockable.GetType().Name;

    /// <summary>How long <c>click</c> waits for its window to draw its next frame before it hit-tests (#1065).</summary>
    public static readonly TimeSpan ClickFrameWait = TimeSpan.FromSeconds(1);

    /// <summary>
    /// <see cref="Interact"/>, for a caller that can wait. A <c>click</c> first waits, up to <paramref name="frameWait"/>
    /// (<see cref="ClickFrameWait"/> when omitted), for the target's window to draw its next frame. A <c>toggle</c> reads its
    /// state once the work it set off has run (<see cref="ToggleSettledAsync"/>). Every other action is
    /// <see cref="Interact"/> as it is.
    /// </summary>
    /// <remarks>
    /// A press is hit-tested as a real one is, against the frame the window last drew. A window opened a moment ago has
    /// drawn none, so a click on it found nothing to press however its controls were laid out, and the same call worked
    /// once the window had been drawn (#1065). Waiting for the next frame also means the point the target's layout gives
    /// is the point that frame shows, where a control the program has just moved would otherwise be pressed where it was.
    /// </remarks>
    public static async Task<InteractOutcome> InteractAsync(Control control, string action, string? value, TimeSpan? frameWait = null)
    {
        if (string.Equals(Normalise(action), "toggle", StringComparison.Ordinal))
            return await ToggleSettledAsync(control, action, value);
        if (!string.Equals(Normalise(action), "click", StringComparison.Ordinal))
            return Interact(control, action, value);
        var wait = frameWait ?? ClickFrameWait;
        var drawn = await NextFrameDrawnAsync(control, wait);
        var outcome = Interact(control, action, value);
        if (!drawn && outcome is { Success: false, Error: { } error } && error.StartsWith(NothingTakesThePointer, StringComparison.Ordinal))
            outcome = outcome with
            {
                Error = error + $"; or its window, which drew no frame in the {wait.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms the click waited, "
                        + "has nothing drawn there yet, since a press is hit-tested against what was drawn: try again once it shows",
            };
        return outcome;
    }

    /// <summary>
    /// A toggle, replied to with its state once the work it set off has run. Its own read comes straight after it, so a
    /// refusal that put the box back from a posted callback, as a binding's write does, was reported as done (#2050). On
    /// the IDE's own windows the state is read again once the dispatcher has run what is queued ahead of Background; a
    /// running form's is read by the wait for its program. A dialog the toggle opened may still change it, so the reply
    /// says so.
    /// </summary>
    private static async Task<InteractOutcome> ToggleSettledAsync(Control control, string action, string? value)
    {
        var peer = Safe<AutomationPeer?>(() => ControlAutomationPeer.CreatePeerForElement(control), null);
        var toggle = peer is null ? null : Safe<IToggleProvider?>(() => peer.GetProvider<IToggleProvider>(), null);
        var before = toggle is null ? null : Safe<ToggleState?>(() => toggle.ToggleState, null);
        // Named before the toggle, as invoke names its target (#578): what the toggle sets off may close its window, or
        // change a caption that follows the state.
        var label = peer is null ? null : LabelOf(control, peer);
        // Known by what they show, as typing knows them (#1257): a MsgBox whose form hides moves to a window of its own.
        static object Shown(Window dialog) => dialog.Content ?? dialog;
        var dialogsBefore = OpenDialogs.Among(OpenWindows()).Select(Shown).ToHashSet();
        var outcome = Interact(control, action, value);
        if (toggle is null || before is null || InProgram(control) || outcome is not { Success: true, Settled: null })
            return outcome;

        // Bounded: a program that keeps the dispatcher busy above Background would otherwise hold the reply for as long as
        // it runs, and an earlier read is better than none.
        var settled = Avalonia.Threading.Dispatcher.UIThread
            .InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background).GetTask();
        var ran = await Task.WhenAny(settled, Task.Delay(ToggleSettleWait)) == settled;
        var now = Safe<ToggleState?>(() => toggle.ToggleState, null);
        if (now is null)
            return Err($"'{label}': toggle was sent, but its state could not be read back, so whether it moved is not known. "
                       + "Dump it to see");
        var opened = OpenDialogs.Among(OpenWindows()).Any(d => !dialogsBefore.Contains(Shown(d)));
        return outcome with
        {
            Detail = $"toggled '{label}'"
                     + ToggleMoved(before, now, control is RadioButton, "its handler or a binding")
                     + (ran ? "" : $"; read before the work it set off had finished, which took longer than "
                                   + $"{ToggleSettleWait.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms")
                     + (opened ? "; a dialog opened during this call, which modalDialog names, and the state may change "
                                 + "again once it is answered" : ""),
        };
    }

    /// <summary>How long a toggle's reply waits for the work it set off before reading its state anyway.</summary>
    public static readonly TimeSpan ToggleSettleWait = TimeSpan.FromSeconds(2);

    /// <summary>Whether the control's window drew a frame within the wait: the next batch the compositor commits, once rendered.</summary>
    public static async Task<bool> NextFrameDrawnAsync(Control control, TimeSpan wait)
    {
        if (TopLevel.GetTopLevel(control) is not { } topLevel
            || Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(topLevel)?.Compositor is not { } compositor)
            return false;
        var rendered = compositor.RequestCompositionBatchCommitAsync().Rendered;
        return await Task.WhenAny(rendered, Task.Delay(wait)) == rendered;
    }

    private const string NothingTakesThePointer = "nothing takes the pointer at ";

    /// <summary>How long <c>click</c> holds the button down, about as long as a person does.</summary>
    public const int ClickHoldMs = 50;

    /// <summary>How long a double click's second press waits for the window's next frame, where none comes, as headlessly.</summary>
    internal static readonly TimeSpan SecondPressFrameWait = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Completes an action that comes in two parts, as <c>click</c> does. Its release is raised as input of its own,
    /// <see cref="ClickHoldMs"/> after the press and at input priority, so whatever the press set off has run first, as it
    /// has by the time a person lifts a finger. An outcome with nothing still to come is returned as it is.
    /// </summary>
    /// <remarks>
    /// Raising both in one dispatcher job is an order real input cannot produce. It is how <c>click</c> first worked, and
    /// on a running form it put MouseUp before MouseDown: the MouseDown handler had yielded its time slice (#766), and the
    /// release ran before the rest of it.
    /// </remarks>
    public static async Task<InteractOutcome> FinishAsync(InteractOutcome outcome)
    {
        if (outcome.Release is not { } release)
            return outcome;
        if (outcome.ClickWatch is { } pressWatch)
            await WaitForProgramAsync(pressWatch, outcome);
        await Task.Delay(ClickHoldMs);
        foreach (var move in outcome.HeldMoves ?? [])
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(move, Avalonia.Threading.DispatcherPriority.Input);
            // Everything queued above Background, the layout a move asked for among it, has run before the next one (#2175).
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        }
        var released = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(release, Avalonia.Threading.DispatcherPriority.Input);
        if (!released.Success || released.NextClick is not { } next)
            return released;
        // The first release can run Click, which may yield, unload or hide its form. Wait just as the tool does
        // after a single click, before checking the target and sending the second press (#1389).
        released = await WaitForProgramAsync(released.ClickWatch, released);
        await Task.Delay(ClickHoldMs);
        var second = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(next, Avalonia.Threading.DispatcherPriority.Input);
        return await FinishAsync(second);
    }

    /// <summary>
    /// Abandons the held button (#2231), as <see cref="HeldButton.IdleLimit"/> with no call does: Escape to whatever has the
    /// keys in its window, then the held moves back to the press and a release there, which is the only way a real mouse
    /// gets back. The next reply says so, and what the surface did with it. With <paramref name="idleOnly"/>, a hold a call
    /// has touched since the timer fired is kept.
    /// </summary>
    /// <remarks>
    /// "Where the press started" means the moves too: a Thumb, as Dock's and a resize handle's drags are, takes its offset
    /// from the moves and not from the release's position, so a release at the press after no move back would leave it where
    /// it was held. Not every surface takes this as cancelled. The designer has no Escape: a draw let go where it began
    /// places a control 2 by 2 pixels, and a resize returns to the grid, so a control drawn a pixel larger than its grid
    /// cells, as every drawn one is, records a resize (both measured, 2026-10-27). The reply names what the designer's undo
    /// stack recorded rather than calling the drag cancelled.
    /// </remarks>
    internal static async Task AbandonHeldAsync(bool idleOnly = false)
    {
        if (HeldButton.Current is not { } held || idleOnly && !HeldButton.IdleElseRearm())
            return;
        held.End();
        var keyed = held.Window.FocusManager?.GetFocusedElement() as Control ?? held.Pointer.Captured as Control ?? held.Window;
        string escaped;
        try
        {
            DeliverKey(keyed, Key.Escape, KeyModifiers.None, null);
            escaped = $"Escape went to '{Describe(keyed)}'";
        }
        catch (Exception ex)
        {
            escaped = $"Escape threw at '{Describe(keyed)}': {ex.Message}";
        }

        // The designer pushes one undo step per gesture that changed the form, so a step pushed now is a commit (#2231).
        var undo = (held.Surface as FormEditViewModel)?.UndoStack;
        var recorded = new List<string>();
        void Recorded()
        {
            if (undo!.UndoDescription is { } step)
                recorded.Add(step);
        }
        if (undo is not null)
            undo.Changed += Recorded;
        InteractOutcome released;
        try
        {
            foreach (var move in held.MovesBack())
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(move, Avalonia.Threading.DispatcherPriority.Input);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            }
            released = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                () => held.ReleaseAt(held.PressedAt, held.PressedWhere), Avalonia.Threading.DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            released = Err($"the moves back or the release threw: {ex.Message}");
        }
        finally
        {
            if (undo is not null)
                undo.Changed -= Recorded;
        }
        var outcome = released.Success ? $"the release reports: {released.Detail}" : $"the release failed: {released.Error}";
        var designer = undo is null ? ""
            : recorded.Count > 0
                ? $". The designer recorded {string.Join(" and ", recorded.Select(r => $"'{r}'"))} anyway, so it did not cancel; Undo takes it back"
                : ". The designer recorded nothing, so nothing was placed, moved or resized";
        HeldButton.Ended($"The held {held.Button} button was abandoned at {HeldButton.Clock()}, with no call for "
                         + $"{HeldButton.IdleLimit.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s: {escaped}, "
                         + $"then it moved back to where it was pressed, {held.PressedWhere}, and was let go there; {outcome}{designer}.");
    }

    /// <summary>How long a reply waits, by default, for a running program to finish what an action set off.</summary>
    public static readonly TimeSpan ProgramWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// What <see cref="WaitForProgramAsync"/> needs of the program behind the window an action was taken in, read before
    /// the action: once it has run, a MsgBox it answered has closed and lost its owner (#1232).
    /// </summary>
    public sealed class ProgramWatch
    {
        internal ProgramWatch(
            HexIDE.Runtime.EventPump pump, Window? form, HashSet<Window> before, Func<HexIDE.Runtime.Debugging.StoppedInfo?> paused)
        {
            Pump = pump;
            WaitingOnWindowBefore = pump.IsWaitingOnWindow;
            LiveBefore = [.. pump.Live];
            LoopsBefore = [.. pump.Loops];
            Form = form;
            Before = before;
            Paused = paused;
        }

        internal HexIDE.Runtime.EventPump Pump { get; }
        internal Window? Form { get; }
        internal HashSet<Window> Before { get; }
        // An old window can hold the program only if a walk was waiting on a window before the action.
        internal bool WaitingOnWindowBefore { get; }
        // The loops running before the action: one of them parked at DoEvents is the program waiting for input, but a loop
        // the action started, or set going by answering a box its handler waited at, is its own work, waited for as a loop
        // with no DoEvents is (#1254).
        internal HashSet<object> LoopsBefore { get; }
        // Every walk running before the action, which a reply that cannot wait leaves unnamed (#1254 review).
        internal HashSet<object> LiveBefore { get; }
        internal Func<HexIDE.Runtime.Debugging.StoppedInfo?> Paused { get; }
    }

    /// <summary>
    /// The program behind <paramref name="window"/>, for <see cref="WaitForProgramAsync"/>: one of its forms, a window
    /// such a form owns, or its MsgBox or InputBox wherever that is shown; or null for any other window, the IDE's among
    /// them. Called on the UI thread just before the action. <paramref name="paused"/> says where the debugger has the
    /// program stopped, or null.
    /// </summary>
    public static ProgramWatch? WatchProgram(Window? window, Func<HexIDE.Runtime.Debugging.StoppedInfo?>? paused = null)
    {
        if (window is null || ProgramOf(window) is not var (pump, form))
            return null;
        return new ProgramWatch(pump, form, [.. OpenWindows()], paused ?? (() => null));
    }

    /// <summary>
    /// Once an action has been raised, waits for the program behind its window (<see cref="WatchProgram"/>) to finish
    /// what the action set off and go back to waiting for input, up to <paramref name="limit"/> (<see
    /// cref="ProgramWait"/> when omitted). A reply that comes back sooner says why: the program is paused in the
    /// debugger, has a window open over its form, or is still running past the limit. It names the forms the program
    /// opened.
    /// </summary>
    /// <remarks>
    /// A handler gives up the UI thread at the debugger's time slice (#766), so without this a reply went out while the
    /// handler was part way through: after Game > Intermediate, a snapshot showed the new field with the old mine count
    /// (#1232).
    /// </remarks>
    public static async Task<InteractOutcome> WaitForProgramAsync(
        ProgramWatch? watch, InteractOutcome outcome, TimeSpan? limit = null, CancellationToken ct = default)
    {
        if (!outcome.Success || watch is null)
            return outcome;

        var wait = limit ?? ProgramWait;
        var deadline = Environment.TickCount64 + (long)wait.TotalMilliseconds;
        while (true)
        {
            // Read at Send priority and waited off the UI thread: a handler that yields at its time slice keeps the
            // dispatcher busy at its own priority for as long as it runs, and a check queued behind it, or a delay that
            // resumes on that thread, would wait for it and never reach the limit.
            var state = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var dialog = DialogOf(watch);
                    return new
                    {
                        Idle = watch.Pump.IsIdle && watch.Pump.AtDoEvents.All(watch.LoopsBefore.Contains),
                        Started = string.Join(", ", watch.Pump.BusySince(watch.LiveBefore).Distinct()),
                        Busy = string.Join(", ", watch.Pump.Busy.Distinct()),
                        Waiting = string.Join(", ", watch.Pump.WaitingHandlers.Distinct()),
                        Stop = watch.Paused(),
                        Dialog = dialog,
                        // What it is, as the note says it: the title alone is 'HexIDE' for two of the IDE's dialogs (#1975).
                        Description = dialog is null ? null : OpenDialogs.DescriptionOf(dialog),
                        // A MsgBox builds its text and buttons once it has loaded, after it shows, so a poll that sees it
                        // first waits for the next one rather than describe an empty box (#1975 review).
                        Built = dialog?.Content is not Control { IsLoaded: false },
                        // A program's own box holds its handler; another window, such as the IDE's compile error, may not.
                        Box = dialog?.Content is HexIDE.Runtime.Dialogs.MessageBox or HexIDE.Runtime.Dialogs.InputBox,
                        Opened = OpenedForms(watch),
                    };
                },
                Avalonia.Threading.DispatcherPriority.Send).GetTask().ConfigureAwait(false);
            var opened = state.Opened.Count == 0
                ? ""
                : $"; the program opened {string.Join(", ", state.Opened.Select(t => $"'{t}'"))}";
            var waiting = state.Waiting.Length == 0 ? "" : $", with {state.Waiting} waiting to run";
            if (state.Idle)
            {
                // What the action left on the form, read only now that the program has run what it set off (#1280).
                var settled = outcome.Settled is { } read
                    ? await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(read, Avalonia.Threading.DispatcherPriority.Send)
                        .GetTask().ConfigureAwait(false)
                    : null;
                return outcome with
                {
                    Detail = outcome.Detail + settled + (opened.Length == 0 ? "" : $" ({opened[2..]})"),
                    Settled = null,
                };
            }
            // A program stopped in the debugger goes no further until it is continued, so the reply goes now.
            if (state.Stop is { } stop)
                return outcome with
                {
                    Detail = outcome.Detail + $" (the program is paused at {stop.Module} line {stop.Line} ({stop.Reason}), "
                             + $"with {state.Busy} not finished{waiting}; it runs on, and the form may change, once the program "
                             + $"continues{opened})",
                };
            // A handler held by a window it opened, a MsgBox most often, waits for an answer, not for time, so the reply
            // goes now. Worded to hold for a window that is not modal too, such as a form the handler showed.
            if (state.Dialog is { } dialog && state.Built)
            {
                // Named here, so the reply's note of open dialogs leaves it out (#1253).
                OpenDialogs.NamedInReply(dialog);
                return outcome with
                {
                    Detail = outcome.Detail + $" (the program is still running {state.Busy}{waiting}, and has a window open over "
                             + "its form, " + state.Description
                             + (state.Box ? "; a MsgBox holds its handler until it is answered, so the form may change after that"
                                 : "; it may hold the program until it is answered, so the form may change after that")
                             + $"{opened})",
                };
            }
            if (Environment.TickCount64 >= deadline)
                return outcome with
                {
                    Detail = outcome.Detail + $" (the program was still running {state.Started} {wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)}s "
                             + $"later{waiting}, so the form may still change{opened})",
                };
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
    }

    /// <summary>The program behind a running form or a box raised by its MsgBox or InputBox call.</summary>
    internal static (HexIDE.Runtime.EventPump Pump, Window? Form)? ProgramOf(Window window)
    {
        if (window is HexIDE.Runtime.IModuleExecutionRoot { Events: { } events })
            return (events, window);
        if (window.Content is not (HexIDE.Runtime.Dialogs.MessageBox { IsProgramDialog: true }
            or HexIDE.Runtime.Dialogs.InputBox { IsProgramDialog: true }))
            return null;
        for (var at = window.Owner as Window; at is not null; at = at.Owner as Window)
            if (at is HexIDE.Runtime.IModuleExecutionRoot { Events: { } ownerEvents })
                return (ownerEvents, at);
        return OpenWindows().FirstOrDefault(w => w is HexIDE.Runtime.IModuleExecutionRoot { Events: not null }) is { } form
            ? (((HexIDE.Runtime.IModuleExecutionRoot)form).Events!, form)
            : null;
    }

    // The windows open now. A host without a desktop lifetime, the headless one the tests run in, keeps no list of them,
    // so the windows seen to open since this class was first used are counted too.
    private static IEnumerable<Window> OpenWindows() =>
        ((Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.Windows ?? []).Concat(Opened).Distinct().Where(w => w.IsVisible);

    private static readonly HashSet<Window> Opened = Track();

    /// <summary>
    /// Starts tracking the windows that open, beside the lifetime's list, which a host without a classic desktop lifetime has
    /// not got: a window shown before the first call to the driver went untracked there. For such a host to call at its start.
    /// </summary>
    internal static void TrackWindows() => _ = Opened;

    private static HashSet<Window> Track()
    {
        var opened = new HashSet<Window>();
        Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        Window.WindowClosedEvent.AddClassHandler<Window>((w, _) => opened.Remove(w));
        return opened;
    }

    /// <summary>
    /// A window, not one of the program's forms, open over the form: one that opened since the action began, such as a
    /// MsgBox when the startup form that owns it is not this one, else one the form owns, the program's own box first, as
    /// that is what holds a handler. One open before the action is left out unless the program was waiting on a window
    /// then: an error dialog an earlier call left open was named in place of the MsgBox the action raised, before that
    /// had opened (#1253, #1318). A form of the program is left out: a form shown modally is the program waiting for input once
    /// its own events have run, which the wait sees, and one shown otherwise holds nothing. Null when there is none.
    /// </summary>
    private static Window? DialogOf(ProgramWatch watch) =>
        OpenWindows().Where(w => !watch.Before.Contains(w))
            .Concat((watch.Form?.OwnedWindows ?? []).OrderBy(w => w.Content is HexIDE.Runtime.Dialogs.MessageBox
                                                                       or HexIDE.Runtime.Dialogs.InputBox ? 0 : 1))
            .FirstOrDefault(w => w.IsVisible && w != watch.Form && w is not HexIDE.Runtime.IModuleExecutionRoot
                                 && (watch.WaitingOnWindowBefore || !watch.Before.Contains(w)));

    /// <summary>The titles of the program's forms that have opened since the action began.</summary>
    private static List<string> OpenedForms(ProgramWatch watch) =>
        [.. OpenWindows().Where(w => w is HexIDE.Runtime.IModuleExecutionRoot && !watch.Before.Contains(w)).Select(w => w.Title ?? "")];

    // `click`'s value: an optional point, a button and the keys held, in any order (ClickValue).
    private static bool TryParseClick(string? value, out Point? at, out MouseButton button, out KeyModifiers keys, out bool twice, out string? destination, out bool hold, out string problem)
    {
        destination = null;
        hold = false;
        var match = System.Text.RegularExpressions.Regex.Match(value ?? "", @"(?:^|\s)to\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            destination = match.Groups[1].Value.Trim();
            value = value![..match.Index];
            // Tokens after the release point are unambiguous; spaces within a pasted path stay intact.
            while (destination.LastIndexOf(' ') is var space && space >= 0)
            {
                var tail = destination[(space + 1)..];
                if (tail.ToLowerInvariant() is not ("double" or "left" or "right" or "middle" or "shift" or "ctrl" or "control" or "alt" or "hold"))
                    break;
                value += " " + tail;
                destination = destination[..space].TrimEnd();
            }
        }
        at = null;
        button = MouseButton.Left;
        keys = KeyModifiers.None;
        problem = string.Empty;
        twice = false;
        var buttonGiven = false;
        foreach (var token in (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var word = token.ToLowerInvariant();
            if (word == "double")
            {
                twice = true;
                continue;
            }
            if (word == "hold")
            {
                hold = true;
                continue;
            }
            if (word.Contains(','))
            {
                var parts = word.Split(',');
                if (at is not null || parts.Length != 2
                    || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                    || !double.IsFinite(x) || !double.IsFinite(y))
                {
                    problem = $"'{token}' is not a point; click takes {ClickValue}";
                    return false;
                }
                at = new Point(x, y);
                continue;
            }
            MouseButton? named = word switch { "left" => MouseButton.Left, "right" => MouseButton.Right, "middle" => MouseButton.Middle, _ => null };
            if (named is { } pressed)
            {
                if (buttonGiven)
                {
                    problem = $"click presses one button, and '{value}' names two; it takes {ClickValue}";
                    return false;
                }
                (button, buttonGiven) = (pressed, true);
                continue;
            }
            KeyModifiers? key = word switch { "shift" => KeyModifiers.Shift, "ctrl" or "control" => KeyModifiers.Control, "alt" => KeyModifiers.Alt, _ => null };
            if (key is not { } held)
            {
                problem = $"'{token}' is not a point, a button, a key, double or hold; click takes {ClickValue}";
                return false;
            }
            keys |= held;
        }
        if (twice && destination is not null)
        {
            problem = "double cannot be combined with to: a double click repeats at one point, while to drags to a release point";
            return false;
        }
        if (hold && destination is null)
        {
            problem = "hold keeps the button down at the end of a drag, so it needs to, as in '20,20 to 100,60 hold'; "
                      + "nothing was pressed";
            return false;
        }
        return true;
    }

    private const string ScrollDirections = "up|down|left|right (a page), line_up|line_down|line_left|line_right, home|end";

    /// <summary>
    /// Scrolls the target, or the nearest control containing it that can scroll the way asked. The fallback
    /// is what makes the verb usable: the content a caller wants to see more of (a card, a tree node, a row)
    /// is what the tree offers to address, while the <see cref="ScrollViewer"/> around it is often a
    /// template part that never appears. The reply names what actually moved and where it now is, and says
    /// so when it could not move, rather than reporting a scroll that changed nothing.
    /// </summary>
    private static InteractOutcome DoScroll(Control control, string? value)
    {
        var direction = new string((value ?? string.Empty).Where(char.IsLetter).ToArray()).ToLowerInvariant();
        (bool Vertical, ScrollAmount Amount, double? Percent)? requested = direction switch
        {
            "up" => (true, ScrollAmount.LargeDecrement, null),
            "down" => (true, ScrollAmount.LargeIncrement, null),
            "left" => (false, ScrollAmount.LargeDecrement, null),
            "right" => (false, ScrollAmount.LargeIncrement, null),
            "lineup" => (true, ScrollAmount.SmallDecrement, null),
            "linedown" => (true, ScrollAmount.SmallIncrement, null),
            "lineleft" => (false, ScrollAmount.SmallDecrement, null),
            "lineright" => (false, ScrollAmount.SmallIncrement, null),
            "home" => (true, ScrollAmount.NoAmount, 0),
            "end" => (true, ScrollAmount.NoAmount, 100),
            _ => null,
        };
        if (requested is not { } move)
            return Err($"scroll requires 'value', one of {ScrollDirections} (got '{value}')");

        var axis = move.Vertical ? "vertically" : "horizontally";
        if (ScrollerFor(control, move.Vertical) is not { } found)
            return Err($"nothing to scroll {axis}: neither '{Describe(control)}' nor anything containing it " +
                       $"has content {(move.Vertical ? "taller" : "wider")} than its viewport");

        var (scroller, owner) = found;
        var before = PercentAlong(scroller, move.Vertical);
        const double none = ScrollPatternIdentifiers.NoScroll;
        if (move.Percent is { } percent)
            scroller.SetScrollPercent(move.Vertical ? none : percent, move.Vertical ? percent : none);
        else if (move.Vertical)
            scroller.Scroll(ScrollAmount.NoAmount, move.Amount);
        else
            scroller.Scroll(move.Amount, ScrollAmount.NoAmount);
        var after = PercentAlong(scroller, move.Vertical);

        var where = owner == control
            ? $"'{Describe(owner)}'"
            : owner.TemplatedParent == control
                ? $"'{Describe(owner)}' (the target's own {(owner is ScrollBar ? "scroll bar" : "scroller")})"
                : $"'{Describe(owner)}' (the nearest container of the target that scrolls {axis})";
        var position = $"now {after.ToString("0.#", CultureInfo.InvariantCulture)}% of the way along, showing {ViewSizeAlong(scroller, move.Vertical).ToString("0.#", CultureInfo.InvariantCulture)}% of the content at a time";
        return Math.Abs(after - before) < 0.01
            ? Err($"did not scroll {where} {value}: it is already at that end ({position})")
            : Ok($"scrolled {where} {value}: {position}");
    }

    /// <summary>
    /// The target's own scroller if it can move along the requested axis, then a scroller in the target's own
    /// template, then the nearest ancestor's. An axis, not "anything scrollable": a strip that scrolls only
    /// sideways must not swallow a request to page down through the pane around it.
    /// </summary>
    /// <remarks>
    /// The template step is what an editor or a list needs: its scroller is a template part, a descendant, so
    /// walking only upward from the control a caller naturally aims at never found it and answered "nothing to
    /// scroll" about a document showing a sixth of itself (#545). Only the target's own template is searched,
    /// not every descendant, so aiming at a pane full of lists cannot pick one of them at random.
    /// </remarks>
    private static (IScrollProvider Scroller, Control Owner)? ScrollerFor(Control control, bool vertical)
    {
        if (OwnScrollerFor(control, vertical) is { } own)
            return own;
        foreach (var candidate in control.GetVisualAncestors().OfType<Control>())
            if (PeerScroller(candidate, vertical) is { } outer)
                return (outer, candidate);
        return null;
    }

    /// <summary>
    /// A scroller that belongs to the control itself: its own peer's, a ScrollViewer in its template, or, for a
    /// DataGrid, which scrolls through bare scroll bars, the grid itself driven by row.
    /// </summary>
    private static (IScrollProvider Scroller, Control Owner)? OwnScrollerFor(Control control, bool vertical)
    {
        if (PeerScroller(control, vertical) is { } own)
            return (own, control);
        var parts = TemplatePartsOf(control);
        foreach (var viewer in parts.OfType<ScrollViewer>())
            if (PeerScroller(viewer, vertical) is { } templated)
                return (templated, viewer);
        // A DataGrid's peer offers no scroll provider and its template has no ScrollViewer: it owns two scroll
        // bars and acts only on their Scroll events, which nothing outside the bar can raise. Without this step,
        // "scroll" on a grid of two hundred rows answered that nothing was taller than its viewport. Vertical
        // only, because rows are what ScrollIntoView reaches. (#545)
        if (vertical && control is DataGrid grid && VerticalBarOf(grid) is { } bar)
            return (new DataGridScroller(grid, bar), grid);
        return null;
    }

    private static IScrollProvider? PeerScroller(Control candidate, bool vertical)
    {
        var peer = Safe(() => ControlAutomationPeer.CreatePeerForElement(candidate), null);
        return peer?.GetProvider<IScrollProvider>() is { } scroller
               && Safe(() => vertical ? scroller.VerticallyScrollable : scroller.HorizontallyScrollable, false)
            ? scroller
            : null;
    }

    /// <summary>
    /// The scrolling parts of a control's own template, found without walking into what it presents: a list's
    /// items or an editor's text area have no templated parent, and the walk stops at them and at each
    /// scroller it collects. That pruning is what keeps this cheap enough to ask of every node in a tree dump.
    /// </summary>
    /// <remarks>
    /// The walk passes through a nested control's own template rather than stopping at it, because a part can
    /// sit behind one: a TextBox's ScrollViewer is presented by the DataValidationErrors that wraps it.
    /// </remarks>
    private static List<Control> TemplatePartsOf(Control control)
    {
        var parts = new List<Control>();
        var pending = new Stack<Visual>(control.GetVisualChildren());
        while (pending.Count > 0)
        {
            if (pending.Pop() is not Control node || node.TemplatedParent is null)
                continue;
            if (node.TemplatedParent == control && node is ScrollViewer or ScrollBar)
            {
                parts.Add(node);
                continue;
            }
            foreach (var child in node.GetVisualChildren())
                pending.Push(child);
        }
        return parts;
    }

    private static ScrollBar? VerticalBarOf(DataGrid grid) =>
        TemplatePartsOf(grid).OfType<ScrollBar>().FirstOrDefault(b =>
            b.Orientation == Avalonia.Layout.Orientation.Vertical && b.IsEffectivelyVisible && b.Maximum > b.Minimum);

    /// <summary>
    /// Scrolls a DataGrid by bringing a row into view with its public <see cref="DataGrid.ScrollIntoView"/>, so
    /// the grid moves by whole rows and the caller is told the offset it landed on.
    /// </summary>
    /// <remarks>
    /// Setting the grid's scroll bar moved the bar and left the rows where they were, while the reply said it
    /// had worked: the grid acts only on the bar's Scroll event, which the bar raises from a non-public method.
    /// ScrollIntoView brings a row to the nearer edge, and leaves a row that is already partly shown where it
    /// is. So a row is always brought in from below the view, which lands it at the top: going down, the grid is
    /// first taken to its last row. Near the end, where no scroll can put the row at the top, the grid stays at
    /// the end, which is where the bar would stop too.
    /// <para>
    /// Rows are indexed in <c>ItemsSource</c> order. A grid the user has sorted by a column header shows another
    /// order, and then the row brought in, and the last row taken first, are not the ones on screen, so the
    /// grid can land somewhere other than the row asked for. What the caller is told stays true, because every
    /// reply reads the bar after the move rather than assuming where it went.
    /// </para>
    /// </remarks>
    private static void ShowRowAtTop(DataGrid grid, ScrollBar bar, int target)
    {
        var rows = grid.ItemsSource?.Cast<object>().ToList() ?? [];
        if (rows.Count == 0)
            return;
        target = Math.Clamp(target, 0, rows.Count - 1);
        if (target > TopRowOf(bar, rows.Count))
            grid.ScrollIntoView(rows[^1], null);
        grid.ScrollIntoView(rows[target], null);
    }

    /// <summary>The row at the top when the bar is at <paramref name="offset"/>: rows are taken to share one height.</summary>
    private static int RowAt(ScrollBar bar, double offset, int rowCount) =>
        rowCount == 0 ? 0 : (int)Math.Floor(offset / ((bar.Maximum + bar.ViewportSize) / rowCount) + 0.01);

    private static int TopRowOf(ScrollBar bar, int rowCount) => RowAt(bar, bar.Value, rowCount);

    private static int RowCountOf(DataGrid grid) => grid.ItemsSource?.Cast<object>().Count() ?? 0;

    /// <summary>A DataGrid presented as a vertical scroll provider, so scroll treats it like any other scroller.</summary>
    private sealed class DataGridScroller(DataGrid grid, ScrollBar bar) : IScrollProvider
    {
        private double Span => bar.Maximum - bar.Minimum;

        public bool HorizontallyScrollable => false;
        public bool VerticallyScrollable => Span > 0;
        public double HorizontalScrollPercent => ScrollPatternIdentifiers.NoScroll;
        public double VerticalScrollPercent => Span > 0 ? (bar.Value - bar.Minimum) / Span * 100 : 0;
        public double HorizontalViewSize => 100;
        public double VerticalViewSize => Span + bar.ViewportSize > 0 ? bar.ViewportSize / (Span + bar.ViewportSize) * 100 : 100;

        public void Scroll(ScrollAmount horizontalAmount, ScrollAmount verticalAmount)
        {
            var count = RowCountOf(grid);
            if (count == 0)
                return;
            var top = TopRowOf(bar, count);
            var perPage = Math.Max(1, (int)Math.Floor(bar.ViewportSize / ((bar.Maximum + bar.ViewportSize) / count)));
            var target = verticalAmount switch
            {
                ScrollAmount.LargeDecrement => top - perPage,
                ScrollAmount.LargeIncrement => top + perPage,
                ScrollAmount.SmallDecrement => top - 1,
                ScrollAmount.SmallIncrement => top + 1,
                _ => top,
            };
            ShowRowAtTop(grid, bar, target);
        }

        public void SetScrollPercent(double horizontalPercent, double verticalPercent)
        {
            if (verticalPercent < 0)
                return;
            // The ends by name, so a rounding in the row estimate cannot stop short of either.
            var count = RowCountOf(grid);
            ShowRowAtTop(grid, bar, verticalPercent >= 100 ? count - 1 : verticalPercent <= 0 ? 0
                : RowAt(bar, bar.Minimum + Span * verticalPercent / 100, count));
        }
    }

    private static bool CanScroll(IScrollProvider scroller) =>
        scroller.VerticallyScrollable || scroller.HorizontallyScrollable;

    private static double PercentAlong(IScrollProvider scroller, bool vertical) =>
        vertical ? scroller.VerticalScrollPercent : scroller.HorizontalScrollPercent;

    private static double ViewSizeAlong(IScrollProvider scroller, bool vertical) =>
        vertical ? scroller.VerticalViewSize : scroller.HorizontalViewSize;

    private static bool IsSettable(IRangeValueProvider range) => !range.IsReadOnly && range.Maximum > range.Minimum;

    /// <summary>
    /// Sets a slider, scroll bar or numeric spinner to a number. Out-of-range is refused rather than clamped:
    /// a caller who asked for 150 on a 0..100 control has the wrong control or the wrong unit, and a clamped
    /// success would hide both.
    /// </summary>
    private static InteractOutcome DoSetRangeValue(Control control, AutomationPeer peer, string? value)
    {
        if (peer.GetProvider<IRangeValueProvider>() is not { } range) return Unsupported("set_range_value");
        var label = LabelOf(control, peer);
        var bounds = $"{range.Minimum.ToString(CultureInfo.InvariantCulture)}..{range.Maximum.ToString(CultureInfo.InvariantCulture)}";
        if (range.IsReadOnly)
            return Err($"'{label}' is read-only: it displays a value in {bounds} and cannot be set");
        if (range.Maximum <= range.Minimum)
            return Err($"'{label}' has nothing to set: its range is {bounds}, so it cannot move (a scroll bar is like this while its content fits)");
        // IsFinite as well: "NaN" parses, and neither bound comparison below is true for it, so it used to
        // reach the control. (#545)
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            return Err($"set_range_value requires 'value' as a number with '.' for decimals (got '{value}'); '{label}' takes {bounds}");
        if (number < range.Minimum || number > range.Maximum)
            return Err($"{number.ToString(CultureInfo.InvariantCulture)} is outside '{label}''s range {bounds}; nothing was changed");

        // A ScrollViewer's own scroll bar is moved through the viewer, and anything else with SetCurrentValue.
        // The peer's SetValue writes a local value, which outranks the binding that keeps a bar in step with
        // its viewer: the bar then stayed where it was put while the view moved on, for the rest of the
        // session (#545). SetCurrentValue changes the value and leaves every binding in place.
        if (control is ScrollBar { TemplatedParent: ScrollViewer viewer } bar)
        {
            viewer.Offset = bar.Orientation == Avalonia.Layout.Orientation.Vertical
                ? new Vector(viewer.Offset.X, number)
                : new Vector(number, viewer.Offset.Y);
            return Ok($"scrolled '{Describe(viewer)}' to {number.ToString(CultureInfo.InvariantCulture)} through its scroll bar '{label}' (range {bounds})");
        }
        // A DataGrid's bar is moved through the grid, a row at a time, and the reply says where it landed:
        // setting the bar itself moved the bar and left the rows where they were. (#545) Its horizontal bar has
        // no such route, since ScrollIntoView reaches rows, so it is refused rather than moved on its own.
        if (control is ScrollBar { TemplatedParent: DataGrid, Orientation: Avalonia.Layout.Orientation.Horizontal })
            return Err($"'{label}' is a DataGrid's horizontal scroll bar, which cannot be driven: setting it would move the bar " +
                       "and leave the columns where they are. Nothing was changed");
        if (control is ScrollBar { TemplatedParent: DataGrid grid, Orientation: Avalonia.Layout.Orientation.Vertical } gridBar)
        {
            var count = RowCountOf(grid);
            ShowRowAtTop(grid, gridBar, RowAt(gridBar, number, count));
            return Ok($"scrolled '{Describe(grid)}' through its scroll bar '{label}' to show row {TopRowOf(gridBar, count)} at the top: " +
                      $"the bar is now at {gridBar.Value.ToString("0.#", CultureInfo.InvariantCulture)}, asked for {number.ToString(CultureInfo.InvariantCulture)} " +
                      $"(range {bounds}; a DataGrid scrolls by whole rows)");
        }
        if (control is RangeBase rangeBase)
            rangeBase.SetCurrentValue(RangeBase.ValueProperty, number);
        else
            range.SetValue(number);
        return Ok($"set '{label}' to {range.Value.ToString(CultureInfo.InvariantCulture)} (range {bounds})");
    }

    /// <summary>
    /// Expands or collapses a tree node, as its own arrow keys and double-tap do, and reads the state back.
    /// </summary>
    /// <remarks>
    /// The node's peer offers no <see cref="IExpandCollapseProvider"/>. <c>SetCurrentValue</c>, as the node's own
    /// handlers use, keeps a TwoWay style binding of <c>IsExpanded</c> to the view model (the Project Explorer's,
    /// Options') alive; a local value would cut it.
    /// </remarks>
    private static InteractOutcome SetTreeNodeExpanded(TreeViewItem node, AutomationPeer peer, bool expanded)
    {
        var label = LabelOf(node, peer);
        var was = node.IsExpanded;
        node.SetCurrentValue(TreeViewItem.IsExpandedProperty, expanded);
        var verb = expanded ? "expanded" : "collapsed";
        if (node.IsExpanded != expanded) return Err($"'{label}' was not {verb}; it is still {(was ? "expanded" : "collapsed")}");
        return Ok(was == expanded ? $"'{label}' was already {verb}" : $"{verb} '{label}'");
    }

    /// <summary>
    /// Selects a <see cref="TreeViewItem"/> through the tree that owns it, as a click does, and reads the
    /// tree's <c>SelectedItem</c> back.
    /// </summary>
    /// <remarks>
    /// <c>SelectedItem</c> is what a view model is bound to, so it is what a caller is trying to change.
    /// Setting <c>item.IsSelected</c> instead looked right and was not: Avalonia adds the item to
    /// <c>SelectedItems</c> and moves <c>SelectedItem</c> only when nothing was selected, so with a
    /// selection already there, both items were highlighted, the bound property stayed put, and the item's
    /// own <c>IsSelected</c> read back true (#714). The item's peer offers no
    /// <see cref="ISelectionItemProvider"/>, so this is the only route.
    /// <para>
    /// The read-back confirms the tree took the item, and no more. Avalonia's <c>SelectedItem</c> setter keeps
    /// what it was given even when a TwoWay-bound view model refuses it, and the binding's own record of the
    /// source moves only when the source raises a change, so neither can tell a refusal from an accepted value
    /// on a property that raises nothing. No tree in the IDE has a view model that refuses, except Watches for its
    /// child nodes (#1229), which is a bug in that view model rather than something to report around here.
    /// </para>
    /// </remarks>
    private static bool TrySelectThroughOwningTree(Control control, out string refusal)
    {
        refusal = "";
        if (control is not TreeViewItem item || item.FindAncestorOfType<TreeView>() is not { } tree
            || tree.TreeItemFromContainer(item) is not { } data)
        {
            refusal = $"'{Describe(control)}' is not an item of a tree, so it cannot be selected through one";
            return false;
        }

        tree.SelectedItem = data;
        if (ReferenceEquals(tree.SelectedItem, data)) return true;
        refusal = $"the tree did not take '{LabelOf(item)}' as its selection";
        return false;
    }

    // The collapsed tree nodes above a control, outermost first: what a person would expand, in order, to show it. The
    // nearest alone was refused as not showing when one above it was collapsed too (#1256).
    private static List<TreeViewItem> CollapsedNodesHolding(Control control) =>
        [.. control.GetVisualAncestors().OfType<TreeViewItem>().Where(n => !n.IsExpanded).Reverse()];

    private static string? CollapsedPropertyCategoryHolding(Control control)
    {
        if (control.DataContext is not PropertyViewModel property
            || ItemsControl.ItemsControlFromItemContainer(control) is not ListBox list)
            return null;

        for (var i = 0; i < list.ItemCount; i++)
            if (list.Items[i] is PropertyCategoryViewModel { IsExpanded: false } category
                && category.Category == property.PropertyClass.Category
                && list.ContainerFromIndex(i) is { } row && Shows(row) is true
                && row.GetVisualDescendants().OfType<ToggleButton>().Any(t => Shows(t) is true))
                return category.Header;
        return null;
    }

    /// <summary>
    /// The open dropdown <paramref name="item"/> is in, or null. Found before the item is selected: a selection
    /// handler that replaces the list recycles the item's container and clears its parent, and the dropdown would then
    /// go unfound and stay open with nothing said.
    /// </summary>
    private static ComboBox? OpenDropDownHolding(Control item) =>
        ItemsControl.ItemsControlFromItemContainer(item) as ComboBox is { IsDropDownOpen: true } combo ? combo : null;

    /// <summary>
    /// Closes the dropdown an item was just selected in, as clicking the item does, and says so for the reply; ""
    /// when there was none, or it has closed already.
    /// </summary>
    /// <remarks>
    /// The item's peer sets the ComboBox's selection and nothing else. Avalonia's ComboBox closes its popup from
    /// pointer, key and visibility handling, never from a selection change, so the popup stayed open over whatever
    /// lay below it, and the reply did not say so (#907). Whether a caller noticed depended on the IDE window
    /// keeping focus: a light-dismiss popup closes when its window deactivates, which is why it was first recorded
    /// as closing between calls.
    /// </remarks>
    private static string CloseDropDown(ComboBox? combo)
    {
        if (combo is not { IsDropDownOpen: true }) return "";
        combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
        return combo.IsDropDownOpen
            ? " (the dropdown is still open; 'collapse' the ComboBox to close it)"
            : " and closed the dropdown, as a click does";
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
    // ISelectionItemProvider, plus every descendant that does. Grid rows and tree items expose none, and are
    // selected through their owner, so they count too, as DescribeProviders already advertises.
    private static List<MeaningfulChild> SelectableCandidates(Control container)
    {
        var result = new List<MeaningfulChild>();
        foreach (var c in new[] { container }.Concat(Descendants(container)))
        {
            var info = Classify(c);
            if (info.Peer is not null
                && (info.Peer.GetProvider<ISelectionItemProvider>() is not null || c is DataGridRow or TreeViewItem)
                // A row a list has let go of, as scrolling leaves a recycled one holding the last item it showed: it
                // stood in for that item, so a select refused it, or found it twice (#1256 review).
                && !Released(c) && !HeldOutOfView(c))
            {
                result.Add(new MeaningfulChild(c, info));
            }
        }
        return result;
    }

    // A row container its list has let go of: a virtualizing panel keeps one it recycled, holding the last item it showed,
    // with no owner and no index, and a visibility bound to that item, as the Properties window's rows are, keeps it
    // showing. Only containers: a control inside a row has no index of its own either. Only an items panel's own children:
    // a container written into a template, or set as content, is no list's row and no list released it (#1292 review).
    // DataGrid keeps recycled rows in its presenter with their old Index and DataContext. Slot is the
    // membership marker (-1 after detach); it is internal, so read it without treating every hidden row as released.
    private static readonly PropertyInfo? GridRowSlot = typeof(DataGridRow).GetProperty("Slot", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool Released(Control c) =>
        c is DataGridRow && c.GetVisualParent() is DataGridRowsPresenter
        ? Safe(() => GridRowSlot?.GetValue(c) is int slot && slot < 0, false)
        : c is ListBoxItem or TreeViewItem or TabItem
        && c.GetVisualParent() is Panel { IsItemsHost: true }
        && (ItemsControl.ItemsControlFromItemContainer(c) ?? c.FindAncestorOfType<ItemsControl>()) is { } owner
        && owner.IndexFromContainer(c) < 0;

    // A row a virtualizing list keeps though it is scrolled out of view: the list's tab-once row, the one a Tab into the list
    // lands on, which selecting a row makes it. The panel keeps it for that Tab, apart from the rows it has realized for
    // the view, and arranges it where it would be, outside the view, so no person sees it: it is left out as rows out of
    // view are. Its index is real, so Released keeps it. Told by membership, not geometry: a hidden row's bounds are stale,
    // and the held row's place is an estimate (#1981 review). Kept while focus is in it, a person's keys going there, or
    // while a dropdown it holds is open, which is on screen (#1981).
    private static bool HeldOutOfView(Control c) =>
        c is ListBoxItem or TreeViewItem or TabItem
        && c.GetVisualParent() is VirtualizingPanel { IsItemsHost: true }
        && !c.IsKeyboardFocusWithin
        && (ItemsControl.ItemsControlFromItemContainer(c) ?? c.FindAncestorOfType<ItemsControl>()) is { } owner
        && owner.IndexFromContainer(c) >= 0
        && !owner.GetRealizedContainers().Contains(c)
        && !c.GetVisualDescendants().OfType<Popup>().Any(p => p.IsOpen);

    private static bool InReleasedRow(Control c) =>
        c.GetSelfAndVisualAncestors().OfType<Control>().Any(r => Released(r) || HeldOutOfView(r));

    private sealed record ControlCommand(System.Windows.Input.ICommand Command, object? Parameter, string Binding);

    // Read the effective value, including compiled bindings and bindings through a different DataContext.
    // The binding description is diagnostic text, not a stable grammar from which to guess a member name.
    private static ControlCommand? ResolvedCommand(Control control)
    {
        (System.Windows.Input.ICommand? Command, object? Parameter, AvaloniaProperty? Property) resolved = control switch
        {
            MenuItem item => (item.Command, item.CommandParameter, MenuItem.CommandProperty),
            Button button => (button.Command, button.CommandParameter, Button.CommandProperty),
            _ => (null, null, (AvaloniaProperty?)null),
        };
        var (command, parameter, property) = resolved;
        if (command is null || property is null) return null;
        var binding = Avalonia.Data.BindingOperations.GetBindingExpressionBase(control, property);
        // Description is available at runtime but absent from Avalonia's reference assembly. Treat it as optional:
        // diagnostic metadata must never stop the already resolved command from running.
        var description = Safe(() => binding?.GetType().GetProperty("Description", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(binding) as string, null);
        return new ControlCommand(command, parameter, description
            ?? (binding is null ? "a directly assigned command" : "a binding whose source description is unavailable"));
    }

    private static string BoundCommandNote(ControlCommand bound) =>
        $"The target's Command is bound to {bound.Binding}. "
        + (bound.Binding.StartsWith("#", StringComparison.Ordinal) && bound.Binding.Contains("DataContext", StringComparison.Ordinal)
            ? "It is bound through another element's DataContext. "
            : "This is the resolved control command; its source need not be the target's own DataContext. ")
        + "Use invoke_command with value 'Command' (with the target's CommandParameter), or invoke on this control to run it.";

    // value = Command for the control’s resolved command, a public ICommand property on its DataContext, or a public parameterless
    // method, which is what a Command bound to a method calls (#836).
    private static InteractOutcome ReflectInvokeCommand(Control control, string? value)
    {
        try
        {
            if (NullIfEmpty(value) is not { } name) return ReflectErr("invoke_command requires 'value' = the command name");
            var bound = ResolvedCommand(control);
            if (name == "Command" && bound is not null)
            {
                if (!bound.Command.CanExecute(bound.Parameter))
                    return ReflectErr("'Command'.CanExecute(CommandParameter) returned false");
                bound.Command.Execute(bound.Parameter);
                return new InteractOutcome(true, "reflection", "executed target's Command with its CommandParameter", null);
            }
            if (control.DataContext is not { } dc)
                return ReflectErr("target has no DataContext" + (bound is null ? "" : ". " + BoundCommandNote(bound)));
            if (dc.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is not { } prop)
            {
                var result = ReflectInvokeMethod(dc, name);
                return !result.Success && bound is not null
                    ? result with { Error = result.Error + ". " + BoundCommandNote(bound) }
                    : result;
            }
            if (prop.GetValue(dc) is not System.Windows.Input.ICommand cmd)
                return ReflectErr($"'{name}' on {dc.GetType().Name} is not an ICommand"
                    + (bound is null ? "" : ". " + BoundCommandNote(bound)));
            if (!cmd.CanExecute(null))
                return ReflectErr($"'{name}'.CanExecute(null) returned false");
            cmd.Execute(null);
            return new InteractOutcome(true, "reflection", $"executed command '{name}' on {dc.GetType().Name}", null);
        }
        catch (Exception ex) { return ReflectErr($"invoke_command '{value}' threw: {ex.Message}"); }
    }

    /// <summary>
    /// The view-model methods compiled XAML binds as a Command with no parameter: what a person can click, and so what
    /// invoke_command calls and inspect_element lists (#836).
    /// </summary>
    /// <remarks>
    /// A Command bound to a method, as the Project Explorer's Remove is to <c>DeleteForm</c>, has no ICommand for
    /// invoke_command to find, and inspect_element listed only properties. Which methods are bound comes from the
    /// XAML compiler's own record, <see cref="XamlCommandBindings"/>; a filter by shape reached plumbing no UI exposes.
    /// </remarks>
    internal static IEnumerable<MethodInfo> CommandMethods(Type type) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(m => m.GetParameters().Length == 0 && XamlCommandBindings.Arity(type, m.Name) == 0)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(m => Depth(m.DeclaringType)).First())
            .OrderBy(m => m.Name, StringComparer.Ordinal);

    // How far down the hierarchy a type is, so a method a derived type declares wins over the one it hides.
    private static int Depth(Type? type)
    {
        var depth = 0;
        for (; type is not null; type = type.BaseType) depth++;
        return depth;
    }

    // A method a Command is bound to, called as Avalonia calls it: refused when its Can{name}(object) says no.
    private static InteractOutcome ReflectInvokeMethod(object dc, string name)
    {
        var type = dc.GetType().Name;
        if (CommandMethods(dc.GetType()).FirstOrDefault(m => m.Name == name) is not { } method)
        {
            return ReflectErr(XamlCommandBindings.Arity(dc.GetType(), name) == 1
                ? $"'{name}' on {type} is bound as a Command with a CommandParameter, which invoke_command cannot supply: "
                  + "drive the control bound to it"
                : dc.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(m => m.Name == name)
                    ? $"'{name}' on {type} is a method no Command is bound to, so invoke_command does not call it"
                    : $"no ICommand property or Command-bound method '{name}' on {type}; inspect_element on the control "
                      + "that owns the DataContext lists both");
        }

        // Avalonia resolves bool Can{name}(object) from the bound method's declaring type upwards,
        // at every visibility, not from the DataContext's runtime type. Use the type the compiled binding
        // matched so a method hidden with new does not move the guard lookup below that binding.
        // The CommandParameter passed to the guard is null here.
        XamlCommandBindings.Arity(dc.GetType(), name, out var boundType);
        MethodInfo? can = null;
        for (var t = boundType ?? method.DeclaringType; t is not null; t = t.BaseType)
        {
            can = t.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "Can" + name && m.ReturnType == typeof(bool)
                    && m.GetParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(object));
            if (can is not null) break;
        }
        try
        {
            if (can is not null && !(bool)can.Invoke(dc, [null])!)
                return ReflectErr($"'Can{name}'(null) on {type} returned false");

            var result = method.Invoke(dc, null);
            var guarded = can is null ? "; no Can method guards it, so check that it did what was meant" : "";
            switch (result)
            {
                case System.Threading.Tasks.Task { IsFaulted: true } failed:
                    return ReflectErr($"invoke_command '{name}' failed: {failed.Exception?.InnerException?.Message}");
                case System.Threading.Tasks.Task { IsCanceled: true }:
                    return ReflectErr($"invoke_command '{name}' was cancelled");
                case System.Threading.Tasks.Task { IsCompleted: false } running:
                    // A failure after the reply is logged, as a Command's would be.
                    running.ListenErrors();
                    return new InteractOutcome(true, "reflection",
                        $"called method '{name}' on {type}; it returned a task that is still running{guarded}", null);
                default:
                    return new InteractOutcome(true, "reflection", $"called method '{name}' on {type}{guarded}", null);
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return ReflectErr($"invoke_command '{name}' threw: {inner.Message}");
        }
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
            // The view can reject a caret offset without correcting its view model (#1326).
            var now = name == "CaretOffset"
                && dc is HexIDE.Forms.ViewModels.ISearchableDocument
                ? FindTextSurface(control) switch
                {
                    TextEditor editor => editor.CaretOffset.ToString(CultureInfo.InvariantCulture),
                    TextArea area => area.Caret.Offset.ToString(CultureInfo.InvariantCulture),
                    _ => null,
                }
                : ReadValue(dc, prop);
            var member = $"{dc.GetType().Name}.{name}";
            if (now is null)
                return new InteractOutcome(true, "reflection", $"set {member}; its value could not be read back", null);
            return new InteractOutcome(true, "reflection", now == raw
                ? $"set {member} = {now}"
                : $"set {member}: sent {raw}, now {now}", null);
        }
        catch (Exception ex)
        {
            var reason = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
            return ReflectErr($"set_property '{value}' was refused: {reason.Message}");
        }
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
    /// auto-indent/IntelliSense don't garble it). Multi-line text is inserted verbatim, except at the end of a
    /// code window's described line, where it is placed as a paste there is (#273 task 4.5).</summary>
    /// <remarks>
    /// The insert is a document edit, not input, so it would go through a read-only editor that a person's
    /// keystrokes could not change. It is refused there instead, and the mechanism is reported as
    /// <see cref="TypedMechanism"/>: this used to say "keyboard", which is how a read-only gate that held was
    /// taken for one that had failed (#649).
    /// </remarks>
    public static InteractOutcome TypeText(Control control, string text, string? controlPath = null)
    {
        try
        {
            if (Shows(control) is not true and var shows)
                return new InteractOutcome(false, TypedMechanism, null, NotShowingRefusal(control, "type_text", shows));
            if (FindTextSurface(control) is not { } surface)
                return new InteractOutcome(false, TypedMechanism, null,
                    NoTextSurface(control, keyboard: false));
            if (AmbiguousSurface(control, surface, controlPath) is { } ambiguous)
                return new InteractOutcome(false, TypedMechanism, null, ambiguous);

            // The code window's check runs first because it is the more specific of the two: it names the
            // header and gives the nearest offset that can be edited, or says why the whole file refuses typing,
            // where WhyNotTypable can only say "is read-only". It returns null for every surface that is not a
            // code window, so the general check below still covers a disabled control, a read-only TextBox and
            // any other wholly read-only editor.
            // A standing selection is what the typing writes over, so it is the span judged, as press_key judges a key. A box
            // is judged where it is written, a line at a time, by the read-only sections, as a person's typing over one is.
            if (AreaOf(surface) is { } typedInto
                && PartialSelection(typedInto, deletes: false) is null
                && RefusedInReadOnlyRegion(typedInto, typedInto.Selection is not RectangleSelection && typedInto.Selection.SurroundingSegment is { Length: > 0 } over
                    ? new EditedSpan(over.Offset, over.Length, Deletes: false)
                    : new EditedSpan(typedInto.Caret.Offset, 0, Deletes: false), "type_text", TypedMechanism) is { } refused)
                return refused;

            if (WhyNotTypable(surface) is { } refusal)
                return new InteractOutcome(false, TypedMechanism, null,
                    $"{surface.GetType().Name}{(ReferenceEquals(surface, control) ? string.Empty : AddressOf(control, controlPath, surface))} "
                    + $"{refusal}, so a person could not type there either; nothing was inserted");

            // AvaloniaEdit replaces the last deletable segment and removes the others in one update.
            // Keep that placement (including when the selection starts in the header), rather than
            // taking the selection and inserting at its original, possibly protected, start.
            if (AreaOf(surface) is { } replacementArea
                && PartialSelection(replacementArea, deletes: false) is { } replacement)
            {
                surface.Focus();
                replacementArea.Selection.ReplaceSelectionWithText(text);
                return new InteractOutcome(true, TypedMechanism,
                    $"typed {text.Length} char(s), replacing {replacement.Deletable} selected char(s); "
                    + replacement.Left + SelectionState(replacementArea), null);
            }

            surface.Focus();

            var insertedText = AreaOf(surface) is { } textArea
                ? DocumentNewLines.Normalize(text, textArea.Document.Text) : text;

            // A person's typing writes over a standing selection (#1040), as one change.
            using var change = AreaOf(surface)?.Document.RunUpdate();

            // At the end of a line a member's Attribute lines describe, the text from its first line break on goes
            // after them, as a person's paste does there, or the run would be left hanging from the text's last line
            // (#273 task 4.5). Its line breaks are then written as the file's, as a paste's are. Placed with the
            // selection it replaces in hand, since taking the selection first would leave the run under nothing.
            // A box selection is written over as typing writes over it, on each of its lines, by the editor's own
            // replacement; its surrounding span is more than the box, so it is not placed past a run (#1040 review).
            if (AreaOf(surface) is { Selection: RectangleSelection { IsEmpty: false } box } boxArea)
            {
                HexIDE.Forms.Views.CodeEditorView.WriteOverBox(boxArea, box, insertedText);
                return new InteractOutcome(true, TypedMechanism,
                    $"typed {text.Length} char(s) into {surface.GetType().Name}"
                    + $"{(ReferenceEquals(surface, control) ? string.Empty : AddressOf(control, controlPath, surface))} "
                    + "over a box selection, on each of its lines, as typing there does", null);
            }

            if (AreaOf(surface) is { } codeArea && CodeWindowOf(codeArea) is { } window
                && (codeArea.Selection.IsEmpty ? (codeArea.Caret.Offset, 0)
                    : (codeArea.Selection.SurroundingSegment.Offset, codeArea.Selection.SurroundingSegment.Length)) is var (from, span)
                && window.InsertLineBreakPastRun(from, span, insertedText) is { } placed)
            {
                // The replaced text's selection would carry over onto what replaced it, for the next write to go over.
                codeArea.ClearSelection();
                codeArea.Caret.Offset = placed.End;
                var replaced = span > 0 ? $", replacing the {span} selected char(s)" : "";
                // The count is of what was sent, as everywhere else this reports one; its line breaks may have been
                // written in a newline of another length.
                return new InteractOutcome(true, TypedMechanism,
                    $"typed {text.Length} char(s) into {surface.GetType().Name}"
                    + $"{(ReferenceEquals(surface, control) ? string.Empty : AddressOf(control, controlPath, surface))}{replaced}. "
                    + $"The caret was at the end of the declaration of {placed.Run.Member?.Name}, so the text from its "
                    + "first line break on went after that member's Attribute lines, which follow the line they "
                    + $"describe; its line breaks were written as the file's. The caret is now at offset {placed.End}.",
                    null);
            }

            // Anywhere else the selection goes first, and the text goes where it was.
            var taken = TakeSelection(surface);
            var replacing = taken > 0 ? $", replacing the {taken} selected char(s)" : "";
            switch (surface)
            {
                case TextEditor editor:
                    var eo = editor.CaretOffset;
                    editor.Document.Insert(eo, insertedText);
                    editor.CaretOffset = eo + insertedText.Length;
                    break;
                case TextArea area:
                    var ao = area.Caret.Offset;
                    area.Document.Insert(ao, insertedText);
                    area.Caret.Offset = ao + insertedText.Length;
                    break;
                case TextBox textBox:
                    // One assignment, so what is bound to the box sees one change and one undo takes it back.
                    var current = textBox.Text ?? string.Empty;
                    var (start, end) = (Math.Clamp(Math.Min(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length),
                                        Math.Clamp(Math.Max(textBox.SelectionStart, textBox.SelectionEnd), 0, current.Length));
                    if (start == end)
                        start = end = Math.Clamp(textBox.CaretIndex, 0, current.Length);
                    textBox.Text = current.Remove(start, end - start).Insert(start, text);
                    textBox.CaretIndex = start + text.Length;
                    textBox.SelectionStart = textBox.SelectionEnd = start + text.Length;
                    replacing = end > start ? $", replacing the {end - start} selected char(s)" : "";
                    break;
            }
            return new InteractOutcome(true, TypedMechanism,
                $"typed {text.Length} char(s) into {surface.GetType().Name}{(ReferenceEquals(surface, control) ? string.Empty : AddressOf(control, controlPath, surface))}{replacing}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, TypedMechanism, null, $"type_text threw: {ex.Message}"); }
    }

    /// <summary>What <see cref="TypeText"/> reports as its mechanism: an edit through the control's own API.</summary>
    public const string TypedMechanism = "document";

    /// <summary>
    /// Types <paramref name="text"/> as a person does, a key at a time (#943): KeyDown, TextInput and KeyUp for each
    /// character, and Enter and Tab as the keys, so what typing drives runs: the completion request a letter makes,
    /// parameter info on <c>(</c> and <c>,</c>, the list closing on a character that cannot be part of a name,
    /// auto-indent and the block a line break closes.
    /// </summary>
    /// <remarks>
    /// <para>Each key goes where the keyboard sends it, to the focused control, with the key bindings on it and its
    /// ancestors tried first, as Avalonia's keyboard device delivers one (<see cref="DeliverKey"/>). So a Tab that
    /// moves focus sends the next key to the control it moved to, and an Enter bound to a command runs it.</para>
    /// <para>After each key the code window's completion and parameter-info requests are waited for, up to
    /// <see cref="PopupWait"/>, as a person's next key comes after the list a letter opened has appeared; a later key
    /// otherwise overtook the server's answer, and the list opened after the typing was over (#943 review).</para>
    /// <para>The typing stops, and says how far it got, at a key a protected line refuses, where a person's would be
    /// dropped without a word; when a dialog opens, over this window or another, which a person's keys would go to;
    /// when focus leaves the window; and when the request is cancelled.</para>
    /// </remarks>
    public static async Task<InteractOutcome> TypeKeysAsync(
        Control control, string text, string? controlPath = null, CancellationToken ct = default)
    {
        try
        {
            if (Shows(control) is not true and var shows)
                return new InteractOutcome(false, KeyboardMechanism, null, NotShowingRefusal(control, "type_text", shows));
            // A control that takes focus is sent the keys when nothing under it is a text box, as a person's keyboard sends
            // them, and what it did with them is read from what came back, not judged from its class: a running form's
            // RichTextBox takes typing in a TextInput handler of its own, and was refused as having no text surface (#2460).
            if ((FindTextSurface(control) ?? (control.Focusable ? control : null)) is not { } surface)
                return new InteractOutcome(false, KeyboardMechanism, null,
                    NoTextSurface(control, keyboard: true));
            var textBox = surface is TextEditor or TextArea or TextBox;
            if (AmbiguousSurface(control, surface, controlPath) is { } ambiguous)
                return new InteractOutcome(false, KeyboardMechanism, null, ambiguous);
            var address = ReferenceEquals(surface, control) ? string.Empty : AddressOf(control, controlPath, surface);
            var keys = KeysOf(text);
            var start = surface is TextEditor editor ? editor.TextArea : surface;

            // Judged before each key, where it goes, as the caret moves. The code window's check is the more specific,
            // so it runs ahead of WhyNotTypable for the first key, as type_text's does.
            InteractOutcome? Refused(Control at, int i)
            {
                if (AreaOf(at) is not { } area)
                    return null;
                var (key, mods, typed) = keys[i];
                var span = EditSpan(area, key, mods) ?? (area.Selection.IsEmpty
                    ? new EditedSpan(area.Caret.Offset, 0, Deletes: false)
                    : new EditedSpan(area.Selection.SurroundingSegment.Offset, area.Selection.SurroundingSegment.Length, Deletes: false));
                if (typed is not null && PartialSelection(area, deletes: false) is not null)
                    return null;
                return RefusedInReadOnlyRegion(area, span, "type_text", KeyboardMechanism, i == 0 ? null
                    : $"Key {i + 1} of {keys.Count} wrote nothing, and the rest were not sent; the {i} before it went in.");
            }

            // Ahead of the rest: under a dialog the key goes to the dialog, whatever the line or the control (#1290 review).
            if (KeysTakenBy(start, $"{surface.GetType().Name}{address}", "typed") is { } held)
                return new InteractOutcome(false, KeyboardMechanism, null, held);
            if (keys.Count > 0 && Refused(start, 0) is { } first)
                return first;
            if (WhyNotTypable(surface) is { } refusal)
                return new InteractOutcome(false, KeyboardMechanism, null,
                    $"{surface.GetType().Name}{address} {refusal}, so a person could not type there either; nothing was typed");
            if (TopLevel.GetTopLevel(start) is not { } top)
                return new InteractOutcome(false, KeyboardMechanism, null, $"'{Describe(start)}' is not in a window; nothing was typed");

            var replacedSelection = AreaOf(start) is { } initialArea ? PartialSelection(initialArea, deletes: false) : null;
            start.Focus();
            // Every window's dialogs, not the typing window's alone: a program's own MsgBox may open over its form as the keys
            // go in, and a person's next key would go to it, as to the reset prompt over this window, so the typing stops for
            // both (#1257). The reset prompt was owned by whichever window was active, often the running form, until #1290.
            // Known by what they show: a MsgBox whose form is hidden moves to a window of its own, which is no new dialog.
            static object Shown(Window dialog) => dialog.Content ?? dialog;
            var dialogsBefore = OpenDialogs.Among(OpenWindows()).Select(Shown).ToHashSet();
            var unanswered = 0;
            var (sentText, tookText) = (0, 0);
            Control at = start;
            for (var i = 0; i < keys.Count; i++)
            {
                string Stopped(string why) =>
                    $"typed {i} of {keys.Count} key(s) into {surface.GetType().Name}{address}, then stopped: {why}";
                if (ct.IsCancellationRequested)
                    return new InteractOutcome(false, KeyboardMechanism, null, Stopped("the request was cancelled"));
                // Where the keyboard would send this key: focus, which a key before it may have moved.
                if (top.FocusManager?.GetFocusedElement() is not Control focused || TopLevel.GetTopLevel(focused) != top)
                    return new InteractOutcome(false, KeyboardMechanism, null,
                        Stopped("focus has left the window, so a person's next key would not reach it"));
                at = focused;
                if (i > 0 && Refused(at, i) is { } refused)
                    return refused;

                var (key, mods, typed) = keys[i];
                if (DeliverKey(at, key, mods, typed) is { } handled)
                    (sentText, tookText) = (sentText + 1, tookText + (handled ? 1 : 0));
                // The next key once what this one set off ahead of input has run, and the list or parameter info it
                // asked the language server for has come back.
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Input);
                if (FocusedEditor(top) is { } codeEditor && HexIDE.Forms.Views.CodeEditorView.PendingPopupsOf(codeEditor) is { } pending
                    && await Task.WhenAny(pending, Task.Delay(PopupWait, ct)) != pending)
                    unanswered++;

                if (OpenDialogs.Among(OpenWindows()).FirstOrDefault(d => !dialogsBefore.Contains(Shown(d))) is { } dialog)
                {
                    // Named here, so the reply's note of open dialogs does not name it a second time (#1257 review).
                    OpenDialogs.NamedInReply(dialog);
                    var where = dialog.Owner is null ? "in a window of its own"
                        : ReferenceEquals(dialog.Owner, top) ? "over the window" : $"over {OpenDialogs.OwnerOf(dialog)}";
                    return new InteractOutcome(false, KeyboardMechanism, null,
                        $"key {i + 1} of {keys.Count} was sent to {surface.GetType().Name}{address} and raised "
                        + $"{OpenDialogs.DescriptionOf(dialog)} {where}; that key may not have landed. Nothing after it was sent. "
                        + "Answer the dialog, then check the text before deciding what to send next");
                }
            }
            var caret = AreaOf(at) is { } typedInto ? $"; the caret is at offset {typedInto.Caret.Offset}" : "";
            var moved = ReferenceEquals(at, start) ? "" : $"; focus moved on to {Describe(at)}, where the keys after it went";
            var slow = unanswered == 0 ? ""
                : $"; the language server had not answered {unanswered} key(s)' completion or parameter-info request "
                  + $"within {PopupWait.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)} ms, so a list may still open";
            var selectionNote = replacedSelection is not null && AreaOf(start) is { } finalArea
                ? "; " + replacedSelection.Left + SelectionState(finalArea) : "";
            // Shift's own key around a capital, as DeliverKey sends it (#2422).
            var capitals = keys.Count(k => k.Mods.HasFlag(KeyModifiers.Shift));
            var shifted = capitals switch
            {
                0 => "",
                1 => "; LeftShift went down before the capital and up after it",
                _ => $"; LeftShift went down before each of the {capitals} capitals and up after it",
            };
            // A control that is no text box is judged by what it did with the keys' text: a TextInput it handled is text it
            // took, whatever it then made of it.
            if (!textBox && sentText > 0 && tookText == 0)
                return new InteractOutcome(false, KeyboardMechanism, null,
                    $"sent {keys.Count} key(s) to {Describe(surface)} as a person types them, and it took the text of none of "
                    + $"them: it handled no TextInput, so it takes no typing{moved}. Their KeyDown and KeyUp were raised, and "
                    + "anything those drive ran");
            var tookNote = textBox || tookText == sentText ? ""
                : $"; it took the text of {tookText} of the {sentText} key(s) that type a character, handling no TextInput for the rest";
            return new InteractOutcome(true, KeyboardMechanism,
                $"typed {keys.Count} key(s) into {surface.GetType().Name}{address} as a person types them{caret}{shifted}{tookNote}{moved}{slow}{selectionNote}", null);
        }
        catch (OperationCanceledException)
        {
            return new InteractOutcome(false, KeyboardMechanism, null, "type_text was cancelled part way; read the text back");
        }
        catch (Exception ex)
        {
            return new InteractOutcome(false, KeyboardMechanism, null, $"type_text threw: {ex.Message}; read the text back");
        }
    }

    /// <summary>How long <see cref="TypeKeysAsync"/> waits after a key for the language server's completion answer.</summary>
    internal static TimeSpan PopupWait = TimeSpan.FromSeconds(2);

    // The code editor whose text area has focus, for the requests its keys make.
    private static TextEditor? FocusedEditor(TopLevel top) =>
        top.FocusManager?.GetFocusedElement() is TextArea area ? area.FindAncestorOfType<TextEditor>() : null;

    /// <summary>
    /// Delivers a key to <paramref name="at"/> as Avalonia's keyboard device delivers a real one: the key bindings on it
    /// and its ancestors are tried first, nearest first, and KeyDown is raised only when none took it; then the text
    /// it types, and KeyUp. Raising KeyDown alone skipped the bindings, so an Enter bound to a search did not search
    /// (#943 review). Returns whether the TextInput was handled, or null when the key types nothing (#2460).
    /// </summary>
    private static bool? DeliverKey(Control at, Key key, KeyModifiers mods, string? typed)
    {
        bool? took = null;
        if (key == Key.None)
        {
            if (typed is not null)
                took = RaiseText(at, typed);
            return took;
        }
        // Each modifier's own key around it, as a person holds a chord (#2422).
        foreach (var (stroke, held, isDown) in Chord(key, mods))
        {
            if (!isDown)
            {
                at.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = stroke, KeyModifiers = held, Source = at });
                continue;
            }
            var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = stroke, KeyModifiers = held, Source = at };
            for (Visual? v = at; v is not null && !down.Handled; v = v.GetVisualParent())
                if (v is InputElement { KeyBindings: { Count: > 0 } bindings })
                    foreach (var binding in bindings.ToArray())
                    {
                        if (down.Handled) break;
                        binding.TryHandle(down);
                    }
            at.RaiseEvent(down);
            if (stroke == key && typed is not null)
                took = RaiseText(at, typed);
        }
        return took;

        static bool RaiseText(Control at, string typed)
        {
            var input = new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = typed, Source = at };
            at.RaiseEvent(input);
            return input.Handled;
        }
    }

    /// <summary>The keys a person holds for each modifier, in the order they press them; released in reverse.</summary>
    private static readonly (KeyModifiers Flag, Key Key)[] ModifierKeys =
        [(KeyModifiers.Control, Key.LeftCtrl), (KeyModifiers.Shift, Key.LeftShift), (KeyModifiers.Alt, Key.LeftAlt), (KeyModifiers.Meta, Key.LWin)];

    /// <summary>The modifier <paramref name="key"/> is itself, or None.</summary>
    private static KeyModifiers FlagOf(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => KeyModifiers.Control,
        Key.LeftShift or Key.RightShift => KeyModifiers.Shift,
        Key.LeftAlt or Key.RightAlt => KeyModifiers.Alt,
        Key.LWin or Key.RWin => KeyModifiers.Meta,
        _ => KeyModifiers.None,
    };

    /// <summary>
    /// The key events of <paramref name="key"/> pressed with <paramref name="mods"/> held, as a person's chord sends them
    /// (#2422): each modifier's own key down, holding what is down so far, then the key down and up, then each modifier's
    /// key up in reverse, holding what is still down. A modifier key's own down reports itself held and its up does not,
    /// as Windows reports them and VB6 shows them (Shift 16 with mask 1, then mask 0). Only the key went before, with its
    /// modifiers as flags, so a program never saw the modifier's own KeyDown and KeyUp.
    /// </summary>
    internal static List<(Key Key, KeyModifiers Held, bool Down)> Chord(Key key, KeyModifiers mods)
    {
        var own = FlagOf(key);
        var pressed = ModifierKeys.Where(m => mods.HasFlag(m.Flag) && m.Flag != own).ToList();
        var held = KeyModifiers.None;
        var strokes = new List<(Key, KeyModifiers, bool)>();
        foreach (var (flag, modifier) in pressed)
        {
            held |= flag;
            strokes.Add((modifier, held, true));
        }
        strokes.Add((key, held | own, true));
        strokes.Add((key, held, false));
        for (var i = pressed.Count - 1; i >= 0; i--)
        {
            held &= ~pressed[i].Flag;
            strokes.Add((pressed[i].Key, held, false));
        }
        return strokes;
    }

    /// <summary>
    /// The character <paramref name="key"/> types with <paramref name="mods"/> held, on the US layout press_key assumes;
    /// null when it types none (#2428). A platform sends a key's character as text of its own between its KeyDown and
    /// KeyUp: none with Ctrl, Alt or Meta held, whose chords are commands, and none for a control character such as
    /// Enter's or Backspace's, which a running program's KeyPress takes from the key itself.
    /// </summary>
    internal static string? TypedBy(Key key, KeyModifiers mods)
    {
        if ((mods & ~KeyModifiers.Shift) != KeyModifiers.None)
            return null;
        var physical = Enum.GetValues<PhysicalKey>().FirstOrDefault(p => p.ToQwertyKey() == key);
        if (physical == PhysicalKey.None)
            return null;
        var symbol = physical.ToQwertyKeySymbol(mods.HasFlag(KeyModifiers.Shift));
        return string.IsNullOrEmpty(symbol) || symbol.Any(char.IsControl) ? null : symbol;
    }

    /// <summary>
    /// Where a key that types <paramref name="typed"/> writes in a code window, for the protected-line check: over the
    /// selection, or at the caret. Null when it types nothing, or over a selection only partly protected, whose editable
    /// parts take it as type_text's typing does (#1040).
    /// </summary>
    private static EditedSpan? TypingSpan(TextArea area, string? typed) =>
        typed is null || PartialSelection(area, deletes: false) is not null ? null
        : area.Selection.IsEmpty ? new EditedSpan(area.Caret.Offset, 0, Deletes: false)
        : new EditedSpan(area.Selection.SurroundingSegment.Offset, area.Selection.SurroundingSegment.Length, Deletes: false);

    /// <summary>
    /// What a reply says of the modifier keys <see cref="Chord"/> pressed around <paramref name="key"/>; "" when none.
    /// </summary>
    internal static string HeldNote(Key key, KeyModifiers mods)
    {
        var names = Chord(key, mods).Where(s => s.Down && s.Key != key).Select(s => s.Key.ToString()).ToList();
        return names.Count switch
        {
            0 => "",
            1 => $"; {names[0]} went down before {key} and up after it, as a person holds it",
            _ => $"; {string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} went down before {key}, in that order, "
                 + "and up after it in reverse, as a person holds them",
        };
    }

    /// <summary>What <see cref="TypeKeysAsync"/> reports as its mechanism: key events, as a keyboard sends them.</summary>
    public const string KeyboardMechanism = "keyboard";

    /// <summary>
    /// The keys that type <paramref name="text"/>: a line break is Enter and a tab is Tab, which AvaloniaEdit binds as
    /// keys and writes no TextInput for; anything else is its text, with the key a US layout types it with when there
    /// is one, Shift for a capital, as the code window's key handlers see a person's.
    /// </summary>
    private static List<(Key Key, KeyModifiers Mods, string? Typed)> KeysOf(string text)
    {
        var keys = new List<(Key, KeyModifiers, string?)>();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                continue;
            keys.Add(c switch
            {
                '\r' or '\n' => (Key.Enter, KeyModifiers.None, null),
                '\t' => (Key.Tab, KeyModifiers.None, null),
                >= 'a' and <= 'z' => (Key.A + (c - 'a'), KeyModifiers.None, c.ToString()),
                >= 'A' and <= 'Z' => (Key.A + (c - 'A'), KeyModifiers.Shift, c.ToString()),
                >= '0' and <= '9' => (Key.D0 + (c - '0'), KeyModifiers.None, c.ToString()),
                ' ' => (Key.Space, KeyModifiers.None, " "),
                _ when char.IsSurrogatePair(text, i) => (Key.None, KeyModifiers.None, text.Substring(i++, 2)),
                _ => (Key.None, KeyModifiers.None, c.ToString()),
            });
        }
        return keys;
    }

    /// <summary>Why a person could not type into <paramref name="surface"/> at its caret; null when they could.</summary>
    private static string? WhyNotTypable(Control surface)
    {
        if (Shows(surface) is not { } shows) return "is not known to be showing (whether it or a container is hidden could not be read)";
        if (!shows) return "is not showing (it or a container is hidden, which dump_visual_tree marks \"isHidden\": true)";
        if (!surface.IsEffectivelyEnabled) return "is disabled";
        return surface switch
        {
            TextEditor editor when !CanTypeInto(editor.TextArea) => "is read-only",
            TextArea area when !CanTypeInto(area) => "is read-only",
            TextBox { IsReadOnly: true } => "is read-only",
            _ => null,
        };
    }

    // SimpleSelection replaces deletable segments without asking CanInsert at the caret. That
    // caret may be in a protected run even though part of the standing selection is editable.
    private static bool CanTypeInto(TextArea area) => area.Selection.IsEmpty || area.Selection is RectangleSelection
        ? area.ReadOnlySectionProvider.CanInsert(area.Caret.Offset)
        : area.Selection.Segments.Any(s => area.ReadOnlySectionProvider.GetDeletableSegments(s).Any());

    /// <summary>Presses a key (optionally with modifiers) through the window input path, with a routed
    /// KeyDown/KeyUp fallback — for navigation/commands (Enter, Tab, Backspace, Escape, Ctrl+S, …) that
    /// `type_text` doesn't cover.</summary>
    public static InteractOutcome PressKey(Control control, string key, string? modifiers, string? controlPath = null)
    {
        var mechanism = "keyboard"; // No input path has been used yet.
        try
        {
            // A chord written as one key, as a person writes it, was refused as an unknown key, which sent the caller to the
            // key names when 'H' was never the problem (#2189). Read it as the key and its modifiers, and say so.
            var asWritten = key.Trim();
            var chordRead = string.Empty;
            if (ChordAsWritten(key) is { } written)
            {
                if (!TryParseModifiers(written.Modifiers, out _, out _))
                    return new InteractOutcome(false, "keyboard", null,
                        $"'{asWritten}' is not a key, and not a chord either: '{written.Modifiers}' is not a modifier. press_key presses "
                        + "one key: pass it as 'key', and any of Ctrl, Shift, Alt and Meta as 'modifiers'");
                chordRead = $"; '{asWritten}' was read as key '{written.Key}' with modifiers '{written.Modifiers}'"
                            + (string.IsNullOrWhiteSpace(modifiers) ? "" : $", added to '{modifiers}'");
                key = written.Key;
                modifiers = string.IsNullOrWhiteSpace(modifiers) ? written.Modifiers : $"{modifiers}+{written.Modifiers}";
            }
            if (!Enum.TryParse<Key>((key ?? string.Empty).Trim(), ignoreCase: true, out var parsedKey))
                return new InteractOutcome(false, "keyboard", null,
                    $"unknown key '{key}'{(chordRead.Length > 0 ? $" in '{asWritten}'" : "")} — use an Avalonia Key name (Enter, Tab, Back, Escape, Down, S, …)");
            if (!TryParseModifiers(modifiers, out var mods, out var modError))
                return new InteractOutcome(false, "keyboard", null, modError);

            // FindKeyTarget passes over what is hidden, so a hidden container, or one whose only key targets are
            // hidden, would otherwise be told it holds nothing focusable, sending the caller looking for a control
            // that isn't there. Say what is hidden instead, as type_text does.
            if (Shows(control) is not true and var shows)
                return new InteractOutcome(false, "keyboard", null, NotShowingRefusal(control, "press_key", shows));
            var top = TopLevel.GetTopLevel(control);
            var before = top?.FocusManager?.GetFocusedElement() as Control;
            // A container means the keyboard as it stands, not its first focusable descendant.
            var retained = before is not null && (ReferenceEquals(before, control) || IsUnderInView(control, before));
            if ((retained ? before : FindKeyTarget(control)) is not { } target)
                return new InteractOutcome(false, "keyboard", null,
                    control.GetVisualDescendants().OfType<Control>()
                            .FirstOrDefault(c => c is TextArea or TextEditor or TextBox || c.Focusable) is { } hidden
                        ? NotShowingRefusal(hidden, "press_key")
                        : $"'{Describe(control)}' has nothing under it that can take keyboard focus, so a key "
                        + "press there would go nowhere. Address a focusable control, or a text surface.");

            // The routed fallback can reach the target even if it cannot take focus, and Enter clicks a Button:
            // refuse a hidden target before either input path can press it (#1069).
            if (Shows(target) is not true and var targetShows)
                return new InteractOutcome(false, "keyboard", null, NotShowingRefusal(target, "press_key", targetShows));

            if (KeysTakenBy(target, Describe(target), "pressed") is { } held)
                return new InteractOutcome(false, "keyboard", null, held);
            // A disabled control can't take focus, so the routed fallback below pressed the key on it all the same and
            // reported success, where interact's click and invoke refuse it (#2366).
            if (DisabledInteractionRefusal(target, "press_key") is { } disabled)
                return new InteractOutcome(false, "keyboard", null, disabled);
            var deletionArea = AreaOf(target);
            var selectionDelete = parsedKey is Key.Delete or Key.Back
                && mods is KeyModifiers.None or KeyModifiers.Control;
            var partial = selectionDelete && deletionArea is not null
                ? PartialSelection(deletionArea, deletes: true) : null;
            var lengthBefore = deletionArea?.Document.TextLength ?? 0;
            // A key that types a character writes it, as a person's does, so it is judged as typing is (#2428).
            var typedText = TypedBy(parsedKey, mods);
            if (AreaOf(target) is { } pressedIn
                && (EditSpan(pressedIn, parsedKey, mods) ?? TypingSpan(pressedIn, typedText)) is { } span
                && partial is null
                && RefusedInReadOnlyRegion(pressedIn, span, "press_key") is { } refused)
                return selectionDelete && !pressedIn.Selection.IsEmpty
                    ? refused with { Error = refused.Error + SelectionState(pressedIn) } : refused;

            // From the target's window, which a flyout's item is in as the dump shows it, not from the item's own root,
            // whose path no tool can resolve (#2030).
            var beforePath = before is null ? "nothing"
                : (top is Control window ? PathWithin(window, "Window", before) : null)
                  ?? PathWithin(TopLevel.GetTopLevel(before) ?? before, "Window", before) ?? Describe(before);
            // Named before the key goes, as the receiver was when it was pressed: Escape on a flyout's item closes the flyout,
            // and the item has no path once it has (#2030).
            var where = ReferenceEquals(target, control)
                ? string.Empty
                : $" on {Describe(target)}{AddressOf(control, controlPath, target)}";
            var focused = target.IsFocused || target.Focus();
            var after = top?.FocusManager?.GetFocusedElement() as Control;
            var focusMove = !ReferenceEquals(before, after)
                ? $"moved focus from {beforePath} to {(after is null ? "nothing" : PathWithin(top!, "Window", after) ?? Describe(after))}; "
                : string.Empty;
            mechanism = focused ? "window-input" : "routed-key";
            var throughWindow = focused && RawKeyboard.Press(target, parsedKey, mods);
            // Keep delivery to a target that cannot take focus, or whose platform input is unavailable.
            if (!throughWindow)
            {
                mechanism = "routed-key";
                DeliverKey(target, parsedKey, mods, typedText);
            }

            // Naming the receiver is half the point. Both gaps this closes presented as "reports success and
            // nothing happens", and in each the answer was that the event went to a control other than the
            // one the caller meant — which the old reply had no way to say.
            // Flags print as "Control, Shift", which read as two presses (#2189).
            var chord = mods == KeyModifiers.None ? string.Empty : mods.ToString().Replace(", ", "+") + "+";
            var changed = partial is not null && deletionArea is not null
                ? $"; deleted {lengthBefore - deletionArea.Document.TextLength} char(s) from the selection; "
                  + partial.Left + SelectionState(deletionArea)
                : string.Empty;
            return new InteractOutcome(true, mechanism,
                $"{focusMove}pressed {chord}{parsedKey}{where}{HeldNote(parsedKey, mods)}"
                + (typedText is null ? "" : $"; it typed \"{typedText}\", as the key does") + $"{changed}{chordRead}", null);
        }
        catch (Exception ex) { return new InteractOutcome(false, mechanism, null, $"press_key threw: {ex.Message}"); }
    }

    /// <summary>
    /// Why a person's keys would not reach <paramref name="at"/>: a modal dialog open over its window, or over a window that
    /// holds it, disables the window and takes the keys. Null when none is. The window's focus stays where it was, so a
    /// check on focus passes: keys went into the code under the reset prompt, to be reverted with it (#1290). A dialog over
    /// another window is left to the reply's note, as a person could click past it. All blocking dialogs are recorded as
    /// named: those under the top one cannot be answered before it either.
    /// </summary>
    private static string? KeysTakenBy(Control at, string what, string done)
    {
        // A control in a native popup, a completion list or a dropdown, is in the window the popup opened from.
        if ((TopLevel.GetTopLevel(at) as Window ?? at.FindLogicalAncestorOfType<Window>()) is not { } window)
            return null;
        // The one on top, which no other open dialog holds: a person's keys go to it, and the one under it cannot be
        // answered before it is (#1290 review).
        var open = OpenDialogs.Among(OpenWindows());
        if (open.Where(d => OpenDialogs.Blocks(d, window)).FirstOrDefault(d => !open.Any(o => OpenDialogs.Blocks(o, d)))
            is not { } holding)
            return null;
        foreach (var dialog in open.Where(d => OpenDialogs.Blocks(d, window)))
            OpenDialogs.NamedInReply(dialog);
        return $"{OpenDialogs.DescriptionOf(holding)}, a modal dialog over {OpenDialogs.OwnerOf(holding)}, is open and waits for an answer, so "
            + $"a person's keys would go to it, not to {what}; nothing was {done}. Answer it first: dump_visual_tree with "
            + "'window' set to its title shows it, and interact answers it";
    }

    /// <summary>The code editor's text area behind a surface, when it is one.</summary>
    private static TextArea? AreaOf(Control surface) => surface switch
    {
        TextEditor editor => editor.TextArea,
        TextArea area => area,
        _ => null,
    };

    /// <summary>
    /// Takes <paramref name="surface"/>'s selection out, leaving the caret where it began, ahead of typing that writes
    /// over it (#1040). Typing is not a deletion, so in a code window a described line's Attribute lines stay, as a
    /// person's typing over it leaves them. The number of characters taken; 0 with nothing selected.
    /// </summary>
    private static int TakeSelection(Control surface)
    {
        // A TextBox's selection is replaced with the text in one assignment, where it is written.
        if (AreaOf(surface) is not { Selection.IsEmpty: false } area)
            return 0;
        var offset = area.Selection.SurroundingSegment.Offset;
        var before = area.Document.TextLength;
        area.Selection.ReplaceSelectionWithText(string.Empty);
        area.Caret.Offset = offset;
        return before - area.Document.TextLength;
    }

    /// <summary>
    /// The span a key would change in a text area: the selection when there is one, otherwise what that key
    /// writes or deletes at the caret. Null for a key that changes no text (a navigation key, a command).
    /// </summary>
    /// <remarks>
    /// <see cref="EditedSpan.Deletes"/> says the key deletes the span rather than writing over it: Backspace and
    /// Delete, a cut, and Ctrl+D. A cut and a paste are the keys the IDE binds to its Cut and Paste commands, Ctrl+X
    /// and Ctrl+V on Windows and Linux and Cmd on macOS, and Delete Line is the library's; matching Ctrl alone let a
    /// Cmd+X on macOS through unjudged (#273, found by CI's macOS run). A deletion is judged as the code window's keys
    /// delete (#273 task 4.5a): one that
    /// takes a described line takes its Attribute lines with it, which is allowed, where writing over the span would
    /// not be. Ctrl+D deletes every line the selection touches, as the library's does, so that is its span. A cut with
    /// nothing selected is still judged at the caret, as it was: regions are whole lines, so the caret's line, which
    /// that cut takes, is in one exactly when the caret is, and on a described line the window takes the run with it.
    /// </remarks>
    private static EditedSpan? EditSpan(TextArea area, Key key, KeyModifiers mods)
    {
        var caret = area.Caret.Offset;
        var selection = area.Selection.SurroundingSegment;
        var chord = mods & ~KeyModifiers.Shift;
        var probe = new KeyEventArgs { Key = key, KeyModifiers = mods };
        var cut = HexIDE.ApplicationCommands.CutCommand.Gestures.Any(g => g.Matches(probe));
        var paste = HexIDE.ApplicationCommands.PasteCommand.Gestures.Any(g => g.Matches(probe));
        var deleteLine = AvaloniaEditCommands.DeleteLine.Gesture?.Matches(probe) == true;

        var edits = cut || paste || deleteLine || (key, chord) switch
        {
            (Key.Enter or Key.Return or Key.Tab, KeyModifiers.None) => true,
            (Key.Back or Key.Delete, KeyModifiers.None or KeyModifiers.Control) => true,
            _ => false,
        };
        if (!edits)
            return null;
        var deletes = cut || deleteLine || key is Key.Back or Key.Delete;

        if (deleteLine)
        {
            var first = area.Document.GetLineByOffset(selection is { Length: > 0 } ? selection.Offset : caret);
            var last = area.Document.GetLineByOffset(selection is { Length: > 0 } ? selection.EndOffset : caret);
            return new EditedSpan(first.Offset, Math.Max(last.Offset + last.TotalLength - first.Offset, 1), deletes);
        }
        if (selection is { Length: > 0 })
            return new EditedSpan(selection.Offset, selection.Length, deletes);
        return key switch
        {
            Key.Back => caret > 0 ? new EditedSpan(caret - 1, 1, deletes) : null,
            Key.Delete => caret < area.Document.TextLength ? new EditedSpan(caret, 1, deletes) : null,
            _ => new EditedSpan(caret, 0, deletes),
        };
    }

    private sealed record PartialEdit(int Deletable, string Left);

    // This only observes the provider's answer. The actual edit still goes through the key handler
    // (including the code window's widening) or Selection.ReplaceSelectionWithText for typing.
    private static PartialEdit? PartialSelection(TextArea area, bool deletes)
    {
        if (area.Selection is RectangleSelection || area.Selection.IsEmpty
            || CodeWindowOf(area) is not { IsReadOnly: false } editor)
            return null;
        var selected = area.Selection.SurroundingSegment;
        var (offset, length) = deletes
            ? editor.ReadOnlySections.WidenDeletion(selected.Offset, selected.Length)
            : (selected.Offset, selected.Length);
        var segments = area.ReadOnlySectionProvider.GetDeletableSegments(
            new AvaloniaEdit.Document.SimpleSegment(offset, length)).ToList();
        var deletable = segments.Sum(s => s.Length);
        if (deletable == 0)
            return null;
        if (deletable == length)
            return !deletes && editor.IsReadOnlyRegion(offset, length)
                ? new PartialEdit(deletable, "no protected text was left in the replaced span. ") : null;
        var names = editor.ReadOnlyRegionsNow.Where(r =>
        {
            var from = Math.Max(offset, r.EditStart);
            var to = Math.Min(offset + length, r.End);
            return to > from && segments.Sum(s => Math.Max(0, Math.Min(to, s.EndOffset) - Math.Max(from, s.Offset))) < to - from;
        }).Select(r => editor.MemberNamedBy(r) is { } name
            ? "the Attribute lines describing " + name : "the file's header").Distinct();
        return new PartialEdit(deletable,
            $"left {length - deletable} char(s) from the original selection in {string.Join(" and ", names)}, which only the IDE changes (the designer, a save, a rename). ");
    }

    private static string SelectionState(TextArea area) =>
        $" The caret is now at offset {area.Caret.Offset}; " + (area.Selection.IsEmpty
            ? "no selection remains."
            : $"selection remains from offset {area.Selection.SurroundingSegment.Offset} to {area.Selection.SurroundingSegment.EndOffset}.");

    /// <summary>What a key would change: <see cref="Length"/> characters at <see cref="Offset"/>, deleted or written over.</summary>
    private readonly record struct EditedSpan(int Offset, int Length, bool Deletes);

    /// <summary>
    /// The code window whose buffer <paramref name="area"/> shows, when it shows one: the view model it
    /// inherits, provided that is the document it is showing, so a text surface nested in a code window's
    /// view for some other purpose is not judged by the code window's regions.
    /// </summary>
    private static HexIDE.Forms.ViewModels.CodeEditorViewModel? CodeWindowOf(TextArea area) =>
        area.DataContext is HexIDE.Forms.ViewModels.CodeEditorViewModel editor
        && ReferenceEquals(editor.Document, area.Document)
            ? editor
            : null;

    /// <summary>
    /// A refusal, when <paramref name="span"/> is in a region of a code window only the IDE may change
    /// (#273 task 3.9), or anywhere in a code window whose file is held read-only as a whole. Null when the
    /// write may go ahead.
    /// </summary>
    /// <remarks>
    /// The reply says where the code starts, because a caller that has just been refused needs to know where
    /// it may type instead, and "move the caret" alone does not say to what.
    /// </remarks>
    /// <param name="mechanism">
    /// What the caller would have used to make the change, reported back on the refusal. It is not always
    /// <c>"keyboard"</c>: <c>type_text</c> inserts into the document rather than sending keystrokes, and
    /// saying otherwise is how a read-only gate that held was once read as one that had failed (#649).
    /// </param>
    private static InteractOutcome? RefusedInReadOnlyRegion(TextArea area, EditedSpan span, string tool, string mechanism = "keyboard", string? written = null)
    {
        if (CodeWindowOf(area) is not { } editor)
            return null;

        // Asked first, because no offset helps: the window's banner says typing is refused anywhere in it.
        // Without this a press_key Enter reported "pressed" and its tunnel handler then wrote nothing, which
        // reads as a key that went astray rather than one that was refused.
        if (editor.IsReadOnly)
            return new InteractOutcome(false, mechanism, null,
                $"{tool} refused: this file will not be saved, because HexIDE cannot yet reproduce it faithfully"
              + (editor.ReadOnlyReason is { Length: > 0 } why ? $" ({why})" : string.Empty)
              + ". Its code is part of the file, so typing in its code window is refused anywhere, as the "
              + "window's banner says. " + (written ?? "Nothing was written."));

        // A deletion by the window's own rule (#273 task 4.5a): one taking a described line whole takes its Attribute
        // lines with it, which a person's key does, so only what it would still change of a region refuses it.
        var changes = span.Deletes
            ? editor.ReadOnlySections.DeletionChangesARegion(span.Offset, span.Length)
            : editor.IsReadOnlyRegion(span.Offset, span.Length);
        if (!changes)
            return null;

        var affected = editor.ReadOnlyRegionsNow.Where(r => r.Changes(span.Offset, span.Length)).ToList();
        var names = affected.Select(r => editor.MemberNamedBy(r)).ToList();
        var header = names.Any(n => n is null);
        var region = header ? "the file's header" : "the Attribute lines describing " + string.Join(", ", names);
        var unchanged = written ?? "Nothing was written.";
        var prefix = $"{tool} refused: ";
        var ownership = ", which only the IDE changes (the designer, a save, a rename). ";
        const string offsets = " Offsets count from the start of the file, in the text get_file_content returns.";
        if (span.Deletes && span.Length > 0 && span.Offset + span.Length == area.Caret.Offset)
            return new InteractOutcome(false, mechanism, null,
                prefix + "the text before the caret belongs to " + region + ownership + unchanged
              + (header
                  ? " Edit the code after the header instead; the header is changed through the designer, a save or a rename."
                  : " Deleting the member's whole declaration (all physical lines if continued) removes its Attribute lines too; "
                    + "select the declaration's whole text and press_key Delete to remove both."));

        var past = editor.PastReadOnlyRegion(span.Offset);
        // A selection starting outside a region, or an offset already at the caret, offers no useful move.
        if (past == span.Offset || past == area.Caret.Offset)
            return new InteractOutcome(false, mechanism, null,
                prefix + $"the selection from offset {span.Offset} to {span.Offset + span.Length} reaches into "
              + region + ownership + unchanged + " Select less, or collapse the selection (press_key Left or Right) "
              + "and write at the caret." + offsets);
        return new InteractOutcome(false, mechanism, null,
            prefix + $"offset {span.Offset} is in " + region + ownership + unchanged + " The nearest place "
          + $"that can be edited starts at offset {past}; put the caret there with interact set_property "
          + $"CaretOffset={past}." + offsets);
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
    /// Sends a move through the window's input, as a real one arrives, so the tooltip service sees it and the editor's
    /// own handler gets its <c>PointerMoved</c>; else raises <c>PointerEntered</c> then <c>PointerMoved</c> on the control. Neither the dwell nor whatever the
    /// tip needs afterwards happens here — this returns as soon as the events are delivered, and the
    /// caller waits.
    /// </para>
    /// </remarks>
    public static InteractOutcome Hover(Control control, double? x, double? y) =>
        Hover(control, x, y, null, out _);

    /// <summary>Where a hover landed: the control the pointer events were raised on, and the point on it, and whether the
    /// move went through the window's input, as a real one does, or was raised on the control.</summary>
    public readonly record struct HoverLanding(Control Receiver, Point Point, bool ThroughWindowInput = false);

    // The ToolTip a control's tip is shown in. Avalonia keeps it in an internal attached property, which has no public
    // reader; null if a later Avalonia renames it, and the reply then gives no bounds.
    private static readonly AvaloniaProperty? ToolTipInstanceProperty =
        typeof(ToolTip).GetField("ToolTipProperty", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as AvaloniaProperty;

    /// <summary>
    /// Where the tip open on <paramref name="owner"/> is, in <paramref name="window"/>'s device-independent units from its
    /// top-left; null when it isn't showing in a popup, or can't be reached.
    /// </summary>
    /// <remarks>
    /// Measured where the tip is, not predicted from its placement, so a caller can compare it with the point hovered: the
    /// tip closes when the hover ends, before any snapshot could show it (#1303). On a desktop it is a window of its own, so
    /// its position comes from the screen.
    /// </remarks>
    public static Rect? OpenTipBounds(Control owner, Visual window)
    {
        if (ToolTipInstanceProperty is null || !ToolTip.GetIsOpen(owner)
            || owner.GetValue(ToolTipInstanceProperty) is not ToolTip { IsEffectivelyVisible: true } tip
            || TopLevel.GetTopLevel(tip) is null)
            return null;
        return Safe<Rect?>(() => new Rect(window.PointToClient(tip.PointToScreen(default)), tip.Bounds.Size), null);
    }

    /// <inheritdoc cref="Hover(Control, double?, double?)"/>
    /// <param name="controlPath">The path <paramref name="control"/> was resolved from. When the pointer goes to
    /// an editor under it, the reply gives that editor's path from this one.</param>
    public static InteractOutcome Hover(
        Control control, double? x, double? y, string? controlPath, out HoverLanding? landing)
    {
        landing = null;
        TopLevel? sentTo = null;
        try
        {
            // The window's own pointer is not the held drag's, and a mouse has one (#2231).
            if (HeldButton.Current is { } held)
                return new InteractOutcome(false, "pointer", null, held.Refusal("hover"));
            if (TopLevel.GetTopLevel(control) is not { } topLevel)
                return new InteractOutcome(false, "pointer", null,
                    "the control is not attached to a window, so it has no coordinates to hover at");

            var target = FindHoverTarget(control);
            if (Shows(target) is not true and var shows)
                return new InteractOutcome(false, "pointer", null, NotShowingRefusal(target, "hover", shows));
            var local = HoverPoint(target, x, y);
            if (target.TranslatePoint(local, topLevel) is not { } atTopLevel)
                return new InteractOutcome(false, "pointer", null,
                    $"could not translate ({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}) on '{Describe(target)}' into window coordinates");

            // Through the window's input, as the platform delivers a real move: hit-tested against what was drawn, routed,
            // and seen by the input manager, which a declared ToolTip.Tip opens from (#1265). Events raised on the control
            // reach its own handlers, the code editor's quick info among them, but never the tooltip service, so they are
            // only the fallback: for a window with nothing drawn at the point, or an Avalonia without the members a move
            // through its input needs.
            var enabled = target.IsEffectivelyEnabled;
            // What it hit is what the window routed it to, as seen at the window: Avalonia's own hit test is not asked, as it
            // answers from the layout and the input from what was drawn, and on a window not drawn yet they disagree.
            Control? hit = null;
            void Saw(object? sender, PointerEventArgs e) =>
                hit ??= (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault();
            topLevel.AddHandler(InputElement.PointerMovedEvent, Saw, Avalonia.Interactivity.RoutingStrategies.Tunnel,
                handledEventsToo: true);
            bool sent;
            try
            {
                sentTo = topLevel;
                sent = RawPointer.Move(topLevel, atTopLevel);
                if (!sent) sentTo = null;
            }
            finally { topLevel.RemoveHandler(InputElement.PointerMovedEvent, Saw); }

            string missed;
            if (!sent)
                missed = "could not be sent, as Avalonia's members for it could not be reached";
            else if (hit is null)
                missed = "found nothing drawn at the point, which is outside the window or not drawn yet";
            else
            {
                // What the pointer is over is what a person's would be: the target, or whatever is drawn over it there. A
                // disabled target is reached when input goes to its nearest enabled ancestor; a cover still takes the move.
                var reached = ReferenceEquals(hit, target) || target.IsVisualAncestorOf(hit)
                              || (!enabled && hit.IsVisualAncestorOf(target));
                landing = reached
                    ? new HoverLanding(target, local, ThroughWindowInput: true)
                    : new HoverLanding(hit, topLevel.TranslatePoint(atTopLevel, hit) ?? local, ThroughWindowInput: true);
                var landed = reached
                    ? enabled ? string.Empty : "; the target is disabled, so it takes no pointer input and shows a tip only "
                                               + "when it sets ToolTip.ShowOnDisabled"
                    : $"; it landed on {Describe(hit)}, not the target, as that is what is drawn at the point, so it is what a "
                      + "person's pointer there would be over";
                return new InteractOutcome(true, "pointer",
                    $"hovered ({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}){Where()} through the window's input{landed}", null);
            }

            var pointer = new Avalonia.Input.Pointer(
                Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);
            var props = new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.Other);

            foreach (var routed in new[] { InputElement.PointerEnteredEvent, InputElement.PointerMovedEvent })
            {
                target.RaiseEvent(new PointerEventArgs(
                    routed, target, pointer, topLevel, atTopLevel,
                    (ulong)Environment.TickCount64, props, KeyModifiers.None));
            }

            landing = new HoverLanding(target, local);
            return new InteractOutcome(true, "pointer",
                $"hovered ({local.X.ToString("0.#", CultureInfo.InvariantCulture)}, {local.Y.ToString("0.#", CultureInfo.InvariantCulture)}){Where()} (raised on the control, as the move through the window's input "
                + $"{missed}, so a declared tooltip will not open)", null);

            string Where() => ReferenceEquals(target, control)
                ? string.Empty
                : $" on {Describe(target)}{AddressOf(control, controlPath, target)}";
        }
        catch (Exception ex)
        {
            // A handler can throw after the input manager has already set pointer-over and scheduled a tip.
            // The tool has no landing to end on failure, so the driver owns this cleanup.
            // The leave raises PointerExited, whose handler can throw too; the reply is the first failure.
            if (sentTo is { } topLevel)
                try { RawPointer.Leave(topLevel); } catch { }
            return new InteractOutcome(false, "pointer", null, $"hover threw: {ex.Message}");
        }
    }

    /// <summary>
    /// Ends a hover that went through the window's input: the pointer leaves the window, as a person's does when they
    /// move away, so the tip closes and nothing is left pointer-over. A later click, snapshot or hover then finds the
    /// window as a person would, with no tip over it.
    /// </summary>
    public static void EndHover(Control control)
    {
        if (TopLevel.GetTopLevel(control) is { } topLevel)
        {
            RawPointer.Leave(topLevel);
            ClickPointer.Remove(topLevel);
        }
    }

    // Where each window last had a click's pointer, so a click there again moves nothing first, as a still mouse sends no
    // move. A hover's end takes the pointer out of the window, and forgets it (#2407).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TopLevel, System.Runtime.CompilerServices.StrongBox<Point>>
        ClickPointer = new();

    private static bool PointerAlreadyAt(TopLevel topLevel, Point at) =>
        ClickPointer.TryGetValue(topLevel, out var last) && Math.Abs(last.Value.X - at.X) < 0.5 && Math.Abs(last.Value.Y - at.Y) < 0.5;

    private static void PointerNowAt(TopLevel topLevel, Point at) =>
        ClickPointer.AddOrUpdate(topLevel, new System.Runtime.CompilerServices.StrongBox<Point>(at));

    /// <summary>
    /// Watches the input <paramref name="topLevel"/> receives while hover waits for a tip, for pointer input this hover did
    /// not send: the real mouse's, or another hover's away from <paramref name="hovered"/>, the point hovered, in the
    /// window's coordinates. Either takes the tip with it.
    /// </summary>
    /// <remarks>
    /// Read from the input manager's stream, which is what Avalonia's tooltip service follows, so the routed pointer events
    /// that click and drag raise on a control, which cannot move a tip, are not counted. The real mouse, resting over the
    /// window, gets moves from the platform as windows change around it, a tip's own popup among them, and the reply said no
    /// tip opened without being able to say why (#2376). Tool calls run concurrently, so another hover can move the pointer
    /// too, and that is not the real mouse's doing. Created, read and disposed on the UI thread, where input arrives.
    /// </remarks>
    public sealed class ForeignPointerWatch : IDisposable
    {
        private readonly TopLevel _topLevel;
        private readonly Point _hovered;
        private readonly object? _root;
        private readonly IDisposable? _subscription;

        public ForeignPointerWatch(TopLevel topLevel, Point hovered)
        {
            (_topLevel, _hovered) = (topLevel, hovered);
            _root = RawPointer.InputRootOf(topLevel);
            _subscription = _root is null ? null : RawPointer.Input?.Subscribe(new Observer(Saw));
        }

        /// <summary>What last moved the pointer during the watch, said for the reply; null when nothing did.</summary>
        public string? Moved { get; private set; }

        private void Saw(Avalonia.Input.Raw.RawInputEventArgs e)
        {
            if (RawPointer.Read(e) is not { } raw || !ReferenceEquals(raw.Root, _root))
                return;
            var anotherHover = RawPointer.Sent(raw.Device);
            if (raw.Type == Avalonia.Input.Raw.RawPointerEventType.LeaveWindow && anotherHover)
                Moved = "another hover ended during the watch, taking the pointer out of the window, and the tip went with it. "
                        + "Hover one control at a time";
            var at = raw.Position;
            if (raw.Type != Avalonia.Input.Raw.RawPointerEventType.Move
                || Math.Abs(at.X - _hovered.X) < 1 && Math.Abs(at.Y - _hovered.Y) < 1)
                return;
            // What it is over as a caller would name it: the control whose tip a person would now be shown, else the nearest
            // named one. The innermost part under the point is something like a button's AccessText.
            var under = (_topLevel.InputHitTest(at) as Visual)?.GetSelfAndVisualAncestors().OfType<Control>().ToList() ?? [];
            var over = under.FirstOrDefault(c => ToolTip.GetTip(c) is not null)
                       ?? under.FirstOrDefault(c => !string.IsNullOrEmpty(c.Name) || !string.IsNullOrEmpty(AutomationProperties.GetAutomationId(c)))
                       ?? under.FirstOrDefault();
            var where = $"the pointer to ({at.X.ToString("0.#", CultureInfo.InvariantCulture)}, "
                        + $"{at.Y.ToString("0.#", CultureInfo.InvariantCulture)}) in the window"
                        + (over is null ? string.Empty : $", over {Describe(over)},");
            Moved = anotherHover
                ? $"another hover moved {where} during the watch, and the tip went with it. Hover one control at a time"
                : $"the real {(raw.Device is IMouseDevice ? "mouse" : "pointer")} moved {where} during the watch, and the tip "
                  + "went with it, as it would for a person: one resting over the window is sent such moves as windows change "
                  + "around it. Move it off the window and hover again";
        }

        public void Dispose() => _subscription?.Dispose();

        private sealed class Observer(Action<Avalonia.Input.Raw.RawInputEventArgs> next) : IObserver<Avalonia.Input.Raw.RawInputEventArgs>
        {
            public void OnNext(Avalonia.Input.Raw.RawInputEventArgs value) => next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }
    }

    /// <summary>Uses the same platform callback as hover. Reflection is needed for Avalonia's private input API.</summary>
    private static class RawKeyboard
    {
        private static class Members
        {
            private const System.Reflection.BindingFlags Instance =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            public static readonly System.Reflection.PropertyInfo? InputRoot = typeof(TopLevel).GetProperty("InputRoot", Instance);
            public static readonly System.Reflection.PropertyInfo? PlatformInput = typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input");
            public static readonly System.Reflection.PropertyInfo? Keyboard = typeof(InputElement).Assembly
                .GetType("Avalonia.Input.KeyboardDevice")?.GetProperty("Instance",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            public static readonly System.Reflection.ConstructorInfo? NewEvent = typeof(Avalonia.Input.Raw.RawKeyEventArgs)
                .GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 9);
            public static readonly System.Reflection.ConstructorInfo? NewText = typeof(Avalonia.Input.Raw.RawTextInputEventArgs)
                .GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 4);
            static Members() { }
        }

        public static bool Press(Control target, Key key, KeyModifiers mods)
        {
            Action<Avalonia.Input.Raw.RawInputEventArgs> input;
            List<Avalonia.Input.Raw.RawInputEventArgs> strokes;
            try
            {
                if (TopLevel.GetTopLevel(target) is not { PlatformImpl: { } impl } topLevel
                    || Members.PlatformInput?.GetValue(impl) is not Action<Avalonia.Input.Raw.RawInputEventArgs> found
                    || Members.InputRoot?.GetValue(topLevel) is not IInputRoot root
                    || Members.Keyboard?.GetValue(null) is not IInputDevice keyboard
                    || Members.NewEvent is not { } constructor)
                    return false;
                // press_key names logical keys, not a keyboard layout. Supply their QWERTY physical key and symbol:
                // AccessKeyHandler needs KeySymbol, Alt without Control, and focus inside its owner on every OS.
                var physical = Enum.GetValues<PhysicalKey>().FirstOrDefault(p => p.ToQwertyKey() == key);
                var symbol = physical.ToQwertyKeySymbol(mods.HasFlag(KeyModifiers.Shift));
                static RawInputModifiers Raw(KeyModifiers held) =>
                    (held.HasFlag(KeyModifiers.Control) ? RawInputModifiers.Control : RawInputModifiers.None)
                    | (held.HasFlag(KeyModifiers.Shift) ? RawInputModifiers.Shift : RawInputModifiers.None)
                    | (held.HasFlag(KeyModifiers.Alt) ? RawInputModifiers.Alt : RawInputModifiers.None)
                    | (held.HasFlag(KeyModifiers.Meta) ? RawInputModifiers.Meta : RawInputModifiers.None);
                // Each modifier's own key around the key, as a person's chord sends them (#2422).
                strokes = Chord(key, mods).Select(s =>
                {
                    var (strokePhysical, strokeSymbol) = s.Key == key ? (physical, symbol)
                        : (Enum.GetValues<PhysicalKey>().FirstOrDefault(p => p.ToQwertyKey() == s.Key), null);
                    object[] args = [keyboard, (ulong)Environment.TickCount64, root,
                        s.Down ? Avalonia.Input.Raw.RawKeyEventType.KeyDown : Avalonia.Input.Raw.RawKeyEventType.KeyUp,
                        s.Key, Raw(s.Held), strokePhysical, strokeSymbol!, KeyDeviceType.Keyboard];
                    return (Avalonia.Input.Raw.RawInputEventArgs)constructor.Invoke(args);
                }).ToList();
                // The key's character, as text of its own after its KeyDown, where the platform sends it (#2428). Without a
                // way to make one, the routed fallback delivers the key and its text instead.
                if (TypedBy(key, mods) is { } text)
                {
                    if (Members.NewText is not { } newText)
                        return false;
                    var after = Chord(key, mods).FindIndex(s => s.Key == key && s.Down) + 1;
                    strokes.Insert(after, (Avalonia.Input.Raw.RawInputEventArgs)newText.Invoke(
                        [keyboard, (ulong)Environment.TickCount64, root, text]));
                }
                input = found;
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or TypeInitializationException
                                           or MemberAccessException or InvalidCastException or ArgumentException)
            {
                return false;
            }
            // A program exception must not cause a second delivery through the fallback.
            foreach (var stroke in strokes)
                input(stroke);
            return true;
        }
    }

    /// <summary>
    /// Pointer input sent through a window's own input, as the platform sends it. None of what this needs is public in
    /// Avalonia 12.0.4: the reference assemblies hide the platform's input callback and the constructors of the raw event
    /// and the mouse device, and the window's input root is non-public. So each is found by reflection, in a class of its
    /// own, so that an Avalonia that has changed them fails only this, and a hover falls back to raising on the control.
    /// </summary>
    private static class RawPointer
    {
        // Found when first used, inside the catch below: the static constructor keeps the lookups from running any earlier,
        // where what one threw would escape it.
        private static class Members
        {
            private const System.Reflection.BindingFlags Instance =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

            public static readonly System.Reflection.PropertyInfo? InputRoot = typeof(TopLevel).GetProperty("InputRoot", Instance);
            public static readonly System.Reflection.PropertyInfo? PlatformInput =
                typeof(Avalonia.Platform.ITopLevelImpl).GetProperty("Input");
            public static readonly System.Reflection.ConstructorInfo? NewMouse = typeof(MouseDevice).GetConstructors()
                .FirstOrDefault(c => c.GetParameters() is [{ ParameterType: var p }] && p == typeof(Avalonia.Input.Pointer));
            // A pointer event's members, which the reference assemblies leave out as private API.
            public static readonly System.Reflection.PropertyInfo? EventRoot =
                typeof(Avalonia.Input.Raw.RawInputEventArgs).GetProperty("Root");
            public static readonly System.Reflection.PropertyInfo? EventDevice =
                typeof(Avalonia.Input.Raw.RawInputEventArgs).GetProperty("Device");
            public static readonly System.Reflection.PropertyInfo? EventType =
                typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetProperty("Type");
            public static readonly System.Reflection.PropertyInfo? EventPosition =
                typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetProperty("Position");
            // The input manager every window's input passes through, which Avalonia's tooltip service follows. Its class is
            // internal; its interface is public.
            public static readonly System.Reflection.PropertyInfo? InputManager = typeof(Avalonia.Input.Raw.RawInputEventArgs)
                .Assembly.GetType("Avalonia.Input.InputManager")
                ?.GetProperty("Instance", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            public static readonly System.Reflection.ConstructorInfo? NewEvent =
                typeof(Avalonia.Input.Raw.RawPointerEventArgs).GetConstructors().FirstOrDefault(c => c.GetParameters() is
                    [_, { ParameterType: var t }, _, _, { ParameterType: var p }, _] && t == typeof(ulong) && p == typeof(Point));

            static Members() { }
        }

        // One device for every hover. A new device is a different pointer, and the window tells whatever the last one was
        // over that it has left: the code editor closed its tip, and a second hover at the same point was then taken for
        // jitter and opened none (#1265 review).
        private static IInputDevice? mouse;

        /// <summary>Sends a move to <paramref name="at"/>, in the window's coordinates. False, having sent nothing, when a
        /// member it needs cannot be reached.</summary>
        public static bool Move(TopLevel topLevel, Point at) => Send(topLevel, Avalonia.Input.Raw.RawPointerEventType.Move, at);

        public static void Leave(TopLevel topLevel) => Send(topLevel, Avalonia.Input.Raw.RawPointerEventType.LeaveWindow, default);

        /// <summary>Whether <paramref name="device"/> is the one every hover sends through.</summary>
        public static bool Sent(IInputDevice? device) => device is not null && ReferenceEquals(device, mouse);

        /// <summary>The input root of <paramref name="topLevel"/> that its input events carry; null when it cannot be read.</summary>
        public static object? InputRootOf(TopLevel topLevel) => Reach(() => Members.InputRoot?.GetValue(topLevel));

        /// <summary>The input manager's stream of every window's input; null when it cannot be reached.</summary>
        public static IObservable<Avalonia.Input.Raw.RawInputEventArgs>? Input => Reach(() =>
            Members.InputManager?.GetValue(null) is { } manager
                ? manager.GetType().GetProperty("Process")?.GetValue(manager) as IObservable<Avalonia.Input.Raw.RawInputEventArgs>
                : null);

        /// <summary>A pointer event's root, device, type and position; null for any other input, or when one cannot be
        /// read.</summary>
        public static (object? Root, IInputDevice? Device, Avalonia.Input.Raw.RawPointerEventType Type, Point Position)? Read(
            Avalonia.Input.Raw.RawInputEventArgs e)
        {
            if (e is not Avalonia.Input.Raw.RawPointerEventArgs)
                return null;
            try
            {
                return Members.EventType?.GetValue(e) is Avalonia.Input.Raw.RawPointerEventType type
                       && Members.EventPosition?.GetValue(e) is Point position
                    ? (Members.EventRoot?.GetValue(e), Members.EventDevice?.GetValue(e) as IInputDevice, type, position)
                    : null;
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or TypeInitializationException
                                           or MemberAccessException or InvalidCastException or ArgumentException)
            {
                return null;
            }
        }

        private static T? Reach<T>(Func<T?> read) where T : class
        {
            try { return read(); }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or TypeInitializationException
                                           or MemberAccessException or InvalidCastException or ArgumentException)
            {
                return null;
            }
        }

        private static bool Send(TopLevel topLevel, Avalonia.Input.Raw.RawPointerEventType type, Point at)
        {
            Action<Avalonia.Input.Raw.RawInputEventArgs> input;
            Avalonia.Input.Raw.RawInputEventArgs args;
            try
            {
                if (Members.NewEvent is not { } newEvent || Members.NewMouse is not { } newMouse
                    || topLevel.PlatformImpl is not { } impl
                    || Members.PlatformInput?.GetValue(impl) is not Action<Avalonia.Input.Raw.RawInputEventArgs> found
                    || Members.InputRoot?.GetValue(topLevel) is not IInputRoot root)
                    return false;
                mouse ??= (IInputDevice)newMouse.Invoke(
                    [new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true)]);
                input = found;
                args = (Avalonia.Input.Raw.RawInputEventArgs)newEvent.Invoke(
                    [mouse, (ulong)Environment.TickCount64, root, type, at, RawInputModifiers.None]);
            }
            catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or TypeInitializationException
                                           or MemberAccessException or InvalidCastException or ArgumentException)
            {
                return false;
            }
            // Outside the catch: what a handler throws is the program's, and is reported as any other action reports it.
            input(args);
            return true;
        }
    }

    /// <summary>
    /// The tooltip text declared for the point a hover landed on, open or not: the tip of the control under
    /// that point, else of its nearest ancestor that has one. Null when none does.
    /// </summary>
    /// <remarks>
    /// <b>Found from the point, never from the receiver's descendants.</b> Searching descendants reported
    /// whichever control happened to come first in the tree: a hover on the window that landed in a code
    /// editor answered with the Standard toolbar's "Add Project" (#610). The hit test still finds a tip
    /// set on a part inside the receiver, because that part is what is under the point.
    ///
    /// <para>
    /// <b>By bounds, not by Avalonia's hit test.</b> Both <c>InputHitTest</c> and <c>GetVisualAt</c> skip a
    /// disabled button (measured in the headless tests, with <c>enabledElementsOnly: false</c> too), and a
    /// disabled toolbar button is exactly where a caller asks what the tip says.
    /// </para>
    /// </remarks>
    public static string? DeclaredToolTipAt(Control receiver, Point point)
    {
        Visual under = receiver;
        while (under.GetVisualChildren().Where(v => v.IsEffectivelyVisible).Reverse()
                   .FirstOrDefault(v => receiver.TranslatePoint(point, v) is { } p && new Rect(v.Bounds.Size).Contains(p))
               is { } topmost)
            under = topmost;

        foreach (var c in under.GetSelfAndVisualAncestors().OfType<Control>())
            if (TipText(ToolTip.GetTip(c)) is { } text) return text;
        return null;
    }

    /// <summary>
    /// The text a tooltip shows, or null for no tip: a string as it is, and a tip that is a control by the text of its
    /// visible text blocks, in order, joined with " / ".
    /// </summary>
    /// <remarks>
    /// A control's ToString is its type's name, so a designer box's tip, a StackPanel of three text blocks, was reported
    /// as "tip: Avalonia.Controls.StackPanel" (#2336). One with no text to read says so, rather than pass a type name off
    /// as its text: a control's blocks bind to its owner's data, which they have once the tip has opened.
    /// </remarks>
    /// <summary>How <see cref="TipText"/> ends for a tip it found no text in.</summary>
    public const string NoTextToRead = "with no text to read";

    public static string? TipText(object? tip)
    {
        switch (tip)
        {
            case null:
                return null;
            case string text:
                return NullIfEmpty(text.Trim());
            case Control control:
                var shown = control.GetSelfAndLogicalDescendants().OfType<TextBlock>()
                    .Where(block => ShownWithin(block, control))
                    .Select(block => (block.Text ?? block.Inlines?.Text)?.Trim())
                    .Where(line => !string.IsNullOrEmpty(line))
                    .ToList();
                return shown.Count > 0 ? string.Join(" / ", shown) : $"a {control.GetType().Name} {NoTextToRead}";
            default:
                return Safe(() => tip.ToString(), null) is { } other && !IsDefaultText(other, tip)
                    ? NullIfEmpty(other.Trim())
                    : $"a {tip.GetType().Name} {NoTextToRead}";
        }
    }

    // Visible itself and through each logical parent up to the tip's own root, which a tip that has not opened is not in a
    // window to make effectively visible.
    private static bool ShownWithin(Control block, Control root)
    {
        for (ILogical? at = block; at is not null; at = at.LogicalParent)
        {
            if (at is Control { IsVisible: false })
                return false;
            if (ReferenceEquals(at, root))
                return true;
        }
        return true;
    }

    /// <summary>The element a hover should land on — the editor's text view, else the control itself.</summary>
    private static Control FindHoverTarget(Control control)
    {
        if (control is TextEditor or TextArea) return control;
        var editors = control.GetVisualDescendants().OfType<TextEditor>().Where(c => !InReleasedRow(c)).ToList();
        return editors.FirstOrDefault(c => Shows(c) is true) ?? editors.FirstOrDefault() ?? control;
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

        // Only what shows. A ComboBox that is not editable keeps a TextBox in its template, hidden, and choosing that
        // over the ComboBox had every key refused as aimed at a hidden control, once #1069 refused those; a person's
        // key goes to the ComboBox.
        var descendants = control.GetVisualDescendants().OfType<Control>().Where(c => Hidden(c) is not true && !InReleasedRow(c)).ToList();

        // An AvaloniaEdit surface, innermost first: its TextArea is where key handling lives.
        if (descendants.FirstOrDefault(c => c is TextArea) is { } area) return area;
        if (descendants.FirstOrDefault(c => c is TextEditor) is TextEditor editor) return editor.TextArea;

        if (descendants.FirstOrDefault(c => c is TextBox) is { } box) return box;

        // Nothing text-shaped: any focusable descendant will at least receive the event, and the caller is
        // told which one. Falling back to the addressed control is last, and only when it can take focus —
        // returning a container that cannot is what made a dead key press look like a successful one.
        return descendants.FirstOrDefault(c => c.Focusable)
            ?? (control.Focusable ? control : null);
    }

    /// <summary>A control named the way a caller would recognise it: type plus whatever identity it carries.</summary>
    private static string Describe(Control control)
    {
        var id = NullIfEmpty(control.Name) ?? NullIfEmpty(AutomationProperties.GetAutomationId(control));
        return string.IsNullOrEmpty(id) ? control.GetType().Name : $"{control.GetType().Name}[{id}]";
    }

    // The nearest editable text surface: the control itself, else its first AvaloniaEdit editor/area, else
    // a plain TextBox descendant (editor surfaces are preferred over incidental TextBoxes like combo edits).
    // Used by type_text, which drives each surface's own insert API — see FindKeyTarget for why press_key
    // must not share this.
    internal static Control? FindTextSurface(Control control)
    {
        if (control is TextEditor or TextArea or TextBox) return control;
        // One that shows first. A surface hidden by anything but its own template is still found, last, so the refusal
        // names it as not showing.
        var surfaces = TextSurfacesUnder(control);
        return PickSurface(surfaces.Where(c => Shows(c) is true)) ?? PickSurface(surfaces);
    }

    private static Control? PickSurface(IEnumerable<Control> found) =>
        found.FirstOrDefault(c => c is TextEditor) ?? found.FirstOrDefault(c => c is TextArea) ?? found.FirstOrDefault(c => c is TextBox);

    // The text surfaces under a control, less a part its own template hides, which is no surface: a ComboBox that is not
    // editable keeps a TextBox in its template, hidden, and choosing it had type_text refused as aimed at a hidden
    // control, where press_key passed it over (#1256).
    private static List<Control> TextSurfacesUnder(Control control) =>
        [.. control.GetVisualDescendants().OfType<Control>()
            .Where(c => c is TextEditor or TextArea or TextBox && !HiddenByItsTemplate(c) && !InReleasedRow(c))];

    // Hidden within its own control's template, whatever that control's own state: read on the chain from the part up to
    // the control, so a hidden pane holding a dropdown still has its dropdown's part left out (#1256 review).
    private static bool HiddenByItsTemplate(Control part) =>
        part.TemplatedParent is Visual owner && part.GetVisualAncestors().Any(v => ReferenceEquals(v, owner))
        && part.GetSelfAndVisualAncestors().TakeWhile(v => !ReferenceEquals(v, owner)).Any(v => !v.IsVisible);

    /// <summary>
    /// A refusal when <paramref name="control"/> is not a text surface itself and more than one of the kind chosen shows
    /// under it, or null. The first was typed into: the Properties pane's first row took a form's new name that way, and
    /// the form was renamed when focus left it (#1256 review). A person clicks the one they mean first.
    /// </summary>
    private static string? AmbiguousSurface(Control control, Control surface, string? controlPath)
    {
        if (ReferenceEquals(surface, control) || Shows(surface) is not true)
            return null;
        var rivals = TextSurfacesUnder(control).Where(c => Kind(c) == Kind(surface) && Shows(c) is true).ToList();
        if (rivals.Count < 2)
            return null;
        var named = rivals.Take(5).Select(c => AddressOf(control, controlPath, c) is { Length: > 0 } at ? at[", at ".Length..] : Describe(c));
        return $"'{Describe(control)}' holds {rivals.Count} {Kind(surface) switch { 0 => "code editors", 1 => "text areas", _ => "text boxes" }} that show, so which one to type into is "
               + $"not clear; nothing was typed. Address the one you mean: {string.Join(", ", named)}"
               + (rivals.Count > 5 ? ", …" : "");

        static int Kind(Control c) => c switch { TextEditor => 0, TextArea => 1, _ => 2 };
    }

    // Why there is nothing to type into: a dropdown that is not editable takes its text by select, once it is open. In
    // keyboard mode a control that takes focus is sent the keys, so it is refused only when it cannot take focus (#2460).
    private static string NoTextSurface(Control control, bool keyboard) =>
        $"'{control.GetType().Name}' has no text surface (TextEditor/TextArea/TextBox) to type into"
        + (keyboard ? ", and does not take focus, so a person's keys could not reach it"
            : control.Focusable ? "; mode \"keyboard\" sends it keys as a person types, for a control that takes typing itself" : "")
        + (control is ComboBox { IsEditable: false }
            ? "; it is a dropdown that is not editable: 'expand' it, then 'select' its item by text" : "");

    // "Ctrl+H" as its key and modifiers, or null for a key with no '+' between two parts. "Ctrl++" presses '+'.
    private static (string Key, string Modifiers)? ChordAsWritten(string? key)
    {
        var text = (key ?? string.Empty).Trim();
        var split = text.EndsWith('+') ? text.Length - 2 : text.LastIndexOf('+');
        return split > 0 && text[split] == '+' ? (text[(split + 1)..].Trim(), text[..split].Trim()) : null;
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

    private static string LabelOf(Control control, AutomationPeer peer) => MeaningfulLabelOf(peer) ?? NameOf(control, peer) ?? ControlTypeOf(peer);

    private static string LabelOf(Control control) => LabelOf(control, ControlAutomationPeer.CreatePeerForElement(control));

    private static InteractOutcome Ok(string detail) => new(true, "peer", detail, null);
    private static InteractOutcome Err(string error) => new(false, "peer", null, error);
    private static InteractOutcome Unsupported(string action) => new(false, "peer", null, $"element does not support '{action}'");
    private static InteractOutcome ReflectErr(string error) => new(false, "reflection", null, error);

    // ── addressing ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ", at &lt;path&gt;" naming where <paramref name="receiver"/> sits, for a reply that went somewhere other
    /// than the control addressed; empty when there is no path to give.
    /// </summary>
    /// <remarks>
    /// A class name alone did not identify the receiver: the IDE holds a TextArea for the Immediate window and
    /// one per open code editor, and a key pressed on a container went to whichever came first (#611). The
    /// path is one <c>dump_visual_tree</c> would print, so it can be fed straight back as a target.
    /// </remarks>
    private static string AddressOf(Control addressed, string? addressedPath, Control receiver) =>
        addressedPath is { Length: > 0 }
        && AnchorOf(addressed, receiver) is { } anchor
        && PathWithin(addressed, addressedPath, anchor) is { } path
            ? $", at {path}"
            : string.Empty;

    /// <summary>
    /// The control in <paramref name="addressed"/>'s tree to name for <paramref name="receiver"/>: the receiver
    /// itself when it is there, else the TextEditor whose TextArea it is. FindKeyTarget reaches an editor's
    /// TextArea through the property, and before the editor's template is applied that TextArea is in no tree.
    /// </summary>
    private static Control? AnchorOf(Control addressed, Control receiver)
    {
        if (ReferenceEquals(addressed, receiver) || IsUnderInView(addressed, receiver))
            return receiver;
        return addressed.GetVisualDescendants().OfType<TextEditor>()
            .FirstOrDefault(e => ReferenceEquals(e.TextArea, receiver));
    }

    // Asked of the TARGET's ancestor chain, explicitly. `IsVisualAncestorOf` was tried first and answered false for a
    // window over a control inside it (measured in the headless tests), so it is not relied on.
    /// <summary>
    /// Whether <paramref name="target"/> is below <paramref name="ancestor"/> in the control view, which nests an open
    /// popup's content under the control that owns it, as the dump does. On the desktop a flyout's items have a visual
    /// root of their own, so a visual walk from one never reached the window: focus in a text box's Cut/Copy/Paste
    /// flyout read as no focus in the dialog, and press_key moved it out before pressing (#2030). So the walk goes from a
    /// popup's content to the control the popup is placed at, as the dump finds that content from the control.
    /// </summary>
    /// <remarks>
    /// Not through the popup root's logical parent: on the desktop it leads nowhere (measured live, #2030). The content's
    /// logical parent is its <see cref="Popup"/>, whose <c>PlacementTarget</c> is the owner for a flyout and a context
    /// menu alike; a menu-bar dropdown's popup is a visual child of its item, the fallback.
    /// </remarks>
    internal static bool IsUnderInView(Visual ancestor, Visual target)
    {
        var steps = 0;
        for (var at = Up(target); at is not null && steps++ < 1000; at = Up(at))
        {
            if (ReferenceEquals(at, ancestor))
                return true;
        }
        return false;

        // An adorner counts as under the element it adorns, where the control view grafts it (#2231).
        static Visual? Up(Visual v) =>
            v is Control { Parent: Popup popup } && ReferenceEquals(popup.Child, v)
                ? popup.PlacementTarget ?? popup.GetVisualParent() ?? popup.Parent as Visual
                : v.GetVisualParent() is AdornerLayer && AdornerLayer.GetAdornedElement(v) is { } adorned
                    ? adorned
                    : v.GetVisualParent();
    }

    /// <summary>
    /// The control-view path of <paramref name="target"/>, walked down from <paramref name="from"/> (whose own
    /// path is <paramref name="fromPath"/>) choosing each segment exactly as <c>dump_visual_tree</c> does, so it
    /// round-trips through <see cref="Resolve"/>. A target the control view folds into a wrapper -- a TextArea
    /// inside its TextEditor -- gets the path of its nearest addressable ancestor, which press_key and
    /// type_text resolve back to the same receiver. Null when the target is not below <paramref name="from"/>.
    /// </summary>
    internal static string? PathWithin(Control from, string fromPath, Control target)
    {
        if (!ReferenceEquals(from, target) && !IsUnderInView(from, target))
            return null;

        var current = from;
        var path = fromPath;
        while (!ReferenceEquals(current, target))
        {
            var siblings = new List<MeaningfulChild>();
            CollectMeaningfulChildren(current, siblings);
            var next = siblings.FirstOrDefault(m =>
                ReferenceEquals(m.Control, target) || IsUnderInView(m.Control, target));
            if (next is null)
                break;
            path += "/" + BestSegment(next, siblings);
            current = next.Control;
        }
        return path;
    }

    /// <summary>Resolves a control-view slash path (rooted at <paramref name="root"/>, first segment
    /// "Window") to a single control, or an error explaining the miss / ambiguity.</summary>
    public static (Control? control, string? error) Resolve(Control root, string targetPath)
    {
        var segs = (targetPath ?? string.Empty)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Neither is a path at all, so each says where paths come from: a caller who guessed one from a snapshot, as
        // "Editor/General" from the Options dialog's tree, was told only what was wrong with it (#2080).
        if (segs.Length == 0)
            return (null, EmptyPath + "; " + WherePathsComeFrom(root, null));
        if (!string.Equals(StripUnderscore(segs[0]), "Window", StringComparison.OrdinalIgnoreCase))
            return (null, $"{NotFromWindow} (got '{segs[0]}'); {WherePathsComeFrom(root, segs[^1])}");

        Control current = root;
        var traversed = "Window";
        for (var i = 1; i < segs.Length; i++)
        {
            var (next, err) = ResolveSegment(current, segs[i], traversed);
            if (next is null)
            {
                // Diagnose a saved path that omitted a wrapper. Do not silently retarget an action;
                // only suggest complete paths whose remaining segments resolve unambiguously.
                if (err?.StartsWith("ambiguous segment", StringComparison.Ordinal) == true) return (null, err);
                var children = new List<MeaningfulChild>();
                CollectMeaningfulChildren(current, children);
                var corrections = new List<(string Wrapper, string Path)>();
                foreach (var child in children)
                {
                    var wrapper = BestSegment(child, children);
                    var corrected = traversed + "/" + wrapper;
                    Control? candidate = child.Control;
                    for (var j = i; j < segs.Length && candidate is not null; j++)
                    {
                        var resolved = ResolveSegment(candidate, segs[j], corrected);
                        if (resolved.control is null) { candidate = null; break; }
                        candidate = resolved.control;
                        corrected += "/" + segs[j];
                    }
                    if (candidate is not null) corrections.Add((wrapper, corrected));
                }
                if (corrections.Count == 1)
                    return (null, $"'{traversed}' now holds '{corrections[0].Wrapper}'; the path continues under it: '{corrections[0].Path}'");
                return (null, err);
            }
            current = next;
            traversed += "/" + segs[i];
        }
        return (current, null);
    }

    /// <summary>The start of the refusal for a target that is empty.</summary>
    public const string EmptyPath = "empty target path";

    /// <summary>The start of the refusal for a target whose first segment is not <c>Window</c>.</summary>
    public const string NotFromWindow = "path must start with 'Window'";

    // The example names the window the path was looked for in, so it can be sent as it stands: "ide" for the IDE's own,
    // else the window's title, which 'window' takes too.
    private static string WherePathsComeFrom(Control root, string? guess)
    {
        var window = root is not Window w ? null
            : Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime
              { MainWindow: { } main } && ReferenceEquals(w, main) ? "ide"
            : string.IsNullOrWhiteSpace(w.Title) ? null : w.Title;
        static string Json(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return "a path comes from dump_visual_tree, whose every node carries its 'path', and its 'match' finds a control by "
               + "name" + (string.IsNullOrWhiteSpace(guess) ? ""
                   : ", as dump_visual_tree {" + (window is null ? "" : $"\"window\":\"{Json(window)}\",")
                     + $"\"match\":\"{Json(SearchTextOf(guess))}\"}}");
    }

    // What a guessed segment names: the text in its brackets, an id, or the segment itself.
    private static string SearchTextOf(string segment)
    {
        var seg = ParseSegment(segment);
        return seg.Discriminator ?? seg.IdShortcut ?? seg.Type ?? segment;
    }

    private static (Control? control, string? error) ResolveSegment(Control parent, string raw, string parentPath)
    {
        var seg = ParseSegment(raw);

        // #AutomationId — search descendants at any depth (structural or not).
        if (seg.IdShortcut is not null)
        {
            var hits = Descendants(parent)
                .Where(c => string.Equals(NullIfEmpty(AutomationProperties.GetAutomationId(c))?.Trim(), seg.IdShortcut,
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
        var (matches, facet) = MatchByDiscriminator(typed, disc,
            m => m.Info.AutoId, m => m.Control.Name, m => MeaningfulLabelOf(m.Info.Peer));
        return ([.. matches.Select(m => m.Control)], facet);
    }

    private static (List<T> Matches, string Facet) MatchByDiscriminator<T>(List<T> items, string disc,
        Func<T, string?> id, Func<T, string?> name, Func<T, string?> label)
    {
        var byId = items.Where(m => string.Equals(id(m)?.Trim(), disc, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count > 0) return (byId, "automationId");
        var byName = items.Where(m => NameEquals(name(m), disc)).ToList();
        if (byName.Count > 0) return (byName, "name");
        var byLabel = items.Where(m => NameEquals(label(m), disc)).ToList();
        return (byLabel, "label");
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

    private static UiNode? BuildNode(MeaningfulChild node, string path, int depth, int maxDepth, bool interactiveOnly,
        object? parentContext)
    {
        var info = node.Info;
        // A control a running program names, a Label among them, is kept: the program may handle its Click, and its caption
        // is often what the program reports through (#1076). It has no provider and cannot take focus.
        var selfInteractive = info.Providers.Length > 0 || info.Focusable || IsProgramControl(node.Control);

        var children = new List<UiNode>();
        var atCap = depth >= maxDepth;
        var metas = new List<MeaningfulChild>();
        CollectMeaningfulChildren(node.Control, metas, firstOnly: atCap);
        // A node at the cap still has its children counted, without walking them. An empty list there read as a
        // leaf, and under interactiveOnly as "nothing interactive below", which pruned the node and every
        // non-interactive ancestor in turn: the designer pane dumped as a bare tab strip (#953).
        var truncated = atCap && metas.Count > 0;
        if (!truncated)
        {
            foreach (var m in metas)
            {
                UiNode? child;
                try { child = BuildNode(m, path + "/" + BestSegment(m, metas), depth + 1, maxDepth, interactiveOnly, node.Control.DataContext); }
                catch { child = null; }   // one pathological control must not abort the whole dump
                if (child is not null) children.Add(child);
            }
        }

        // Kept when its children were not walked: whether anything interactive lies below is unknown, not absent.
        if (interactiveOnly && !selfInteractive && children.Count == 0 && !truncated)
            return null;

        return MakeNode(node, path, [.. children], parentContext, truncated);
    }

    // Belonging to a running program as its event dispatch decides it, by logical ancestry, so a control in an open menu,
    // whose popup is a window of its own, and one on an MDI child hosted in a pane count too. The form designer's
    // controls carry the same names but no running form above them. A Timer and a CommonDialog never show at run time.
    private static bool IsProgramControl(Control control) =>
        HexIDE.Runtime.BuiltinControls.VBProps.GetName(control) is not null
        && control is not (HexIDE.Runtime.BuiltinControls.VBTimer or HexIDE.Runtime.BuiltinControls.VBCommonDialog)
        && control.FindLogicalAncestorOfType<HexIDE.Runtime.IModuleExecutionRoot>() is not null;

    private static UiNode BareNode(MeaningfulChild node, string path, object? parentContext) =>
        MakeNode(node, path, [], parentContext);

    private static UiNode MakeNode(MeaningfulChild node, string path, UiNode[] children, object? parentContext,
        bool truncated = false)
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
            children,
            TypeNameAsNameOf(info.Peer),
            truncated ? true : null,
            node.Control.DataContext is { } context && ReferenceEquals(context, parentContext),
            DistinctLabelOf(info.Name, info.Peer));
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
    ///
    /// <para><b>Also hidden: under a container that paints nothing and takes no pointer.</b> That is how a running
    /// VB6 form hides a Frame or PictureBox (<c>VBVisibility</c>): it stays visible to Avalonia, so that the code of
    /// the controls inside keeps running as in VB6, at opacity zero with hit-testing off. Its children read as
    /// effectively visible, and a hidden Command1 inside one was pressed. Either property alone is not enough: the
    /// IDE turns hit-testing off on decorations that are drawn, and fades things it still lets a person click.</para>
    /// </remarks>
    private static bool? Hidden(Control control) => Shows(control) is false ? true : null;

    /// <summary>
    /// Whether <paramref name="control"/> shows, as <see cref="Hidden"/> reads it, or null when that could not be read.
    /// The guards that refuse to act on what is hidden (#1069) refuse on null too: a read that threw once counted as
    /// showing, so a hidden control could be acted on (#1280). A dump, which only reports, marks only what was read.
    /// </summary>
    private static bool? Shows(Control control) =>
        Safe<bool?>(() => control.IsEffectivelyVisible
                          && !control.GetSelfAndVisualAncestors().Any(v => v.Opacity == 0 && v is InputElement { IsHitTestVisible: false }),
                    null);

    // Nearest control-view children: descends transparently through non-Control visuals AND structural
    // control wrappers, collecting the closest meaningful Control descendants. With firstOnly it stops at the
    // first one, for a caller asking only whether there are any: the dump at its maxDepth cut-off (#953).
    private static void CollectMeaningfulChildren(Visual parent, List<MeaningfulChild> acc, bool firstOnly = false)
    {
        // Asked of the PARENT, not of each child, and the difference is where the items end up. A flyout's
        // popup is not a visual child of its button, so grafting it while iterating the button's siblings
        // put eight Add commands next to the toolbar buttons instead of inside the one that owns them --
        // and left a dump rooted AT the button reporting no children at all, because the button's own
        // popup was never considered. Here they are children of their owner, which is both where they
        // appear on screen and what makes the path round-trip.
        if (parent is Control owner)
            foreach (var attached in AttachedPopupsOf(owner))
                CollectPopupChildren(attached, acc, firstOnly);

        // Virtualizing panels keep containers in creation order, which changes after scrolling (#1966).
        var children = parent.GetVisualChildren();
        if (parent is Panel { IsItemsHost: true } panel && panel.FindAncestorOfType<ItemsControl>() is { } items)
            children = children.OrderBy(v => v is Control c && items.IndexFromContainer(c) is >= 0 and var index
                ? index : int.MaxValue);

        foreach (var v in children)
        {
            if (firstOnly && acc.Count > 0) return;
            // An adorner is drawn in a layer of its own, far from the element it adorns. It is grafted under that
            // element below instead, so its paths read the way the screen does: a selected control's grab handles under
            // the control, not at the window's root (#2231).
            if (v is AdornerLayer)
                continue;
            if (v is Popup popup)
            {
                CollectPopupChildren(popup, acc, firstOnly);
            }
            // A row its list has let go of is no row of it, though it still shows the item it last held: listed, it was a
            // second copy of an item, or a row for one out of view, and a path to it reached a row no person sees (#1292).
            else if (v is Control released && (Released(released) || HeldOutOfView(released)))
            {
            }
            else if (v is Control c)
            {
                var info = Classify(c);
                if (info.Structural) CollectMeaningfulChildren(c, acc, firstOnly);
                else acc.Add(new MeaningfulChild(c, info));
            }
            else
            {
                CollectMeaningfulChildren(v, acc, firstOnly);
            }
        }

        // After the element's own children, as it is drawn over them. One with no AutomationId folds away as any
        // wrapper does, so a focus adorner adds nothing.
        if (parent is Control adorned && AdornerLayer.GetAdorner(adorned) is Control adorner && !(firstOnly && acc.Count > 0))
        {
            var info = Classify(adorner);
            if (info.Structural) CollectMeaningfulChildren(adorner, acc, firstOnly);
            else acc.Add(new MeaningfulChild(adorner, info));
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
    private static void CollectPopupChildren(Popup popup, List<MeaningfulChild> acc, bool firstOnly = false)
    {
        // A closed popup has no realised content: its items genuinely do not exist yet, so there is
        // nothing to report and nothing to address. Open it first (interact expand).
        if (!popup.IsOpen) return;
        if (popup.Child is Visual content) CollectMeaningfulChildren(content, acc, firstOnly);
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
    /// <see cref="AttachedPopupsOf"/>, for the snapshot composer: the same question, asked by the code that
    /// renders rather than the code that walks. One definition, so a popup a caller can address is always a
    /// popup they can also see.
    /// </summary>
    internal static IReadOnlyList<Popup> AttachedPopupsOfPublic(Control c) => AttachedPopupsOf(c);

    // One popup for a menu or a flyout; the code window can have two open at once, its completion list under the
    // parameter info of the call being typed into, and reporting only the list left the signature unseen (#942 review).
    private static IReadOnlyList<Popup> AttachedPopupsOf(Control c)
    {
        if (c.ContextMenu is { } menu && IsOpenOn(menu, c))
            return menu.GetLogicalParent() is Popup menuPopup ? [menuPopup] : [];

        // The code window's completion list and parameter info are the editor's own popups, held by its view.
        if (c is TextEditor editor && HexIDE.Forms.Views.CodeEditorView.OpenPopupsOf(editor) is { Count: > 0 } editorPopups)
            return editorPopups;

        // AvaloniaEdit parents its description popup logically to CompletionWindow, alongside the
        // CompletionList. Graft it under the list box that owns the selected item (#1334). This same
        // route is used by SnapshotComposer when it walks the completion popup's realised content.
        if (c is CompletionListBox
            && c.FindAncestorOfType<CompletionList>()?.GetLogicalParent() is CompletionWindow completionWindow)
            return completionWindow.GetLogicalChildren().OfType<Popup>().Where(p => p.IsOpen).ToList();

        foreach (var f in FlyoutsOf(c))
            if (f is PopupFlyoutBase open && IsOpenOn(open, c))
                return [open.Popup];
        return [];
    }

    // Dock shares one tab ContextMenu across tab bodies. The popup records the current owner.
    private static bool IsOpenOn(ContextMenu menu, Control control) =>
        menu.IsOpen && menu.GetLogicalParent() is Popup popup && ReferenceEquals(popup.Parent, control);

    // One flyout can serve many controls: the theme's TextBox ContextFlyout is a single shared resource, open on
    // whichever box opened it, so open is asked of this control (#949 review).
    private static bool IsOpenOn(FlyoutBase flyout, Control control) =>
        flyout.IsOpen && (flyout.Target is null || ReferenceEquals(flyout.Target, control));

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
    /// <c>expand</c> opens a <c>ContextMenu</c> before any of these, so a Button with both a Flyout and a ContextMenu
    /// opens the ContextMenu, and only the first flyout here is ever opened by it.
    /// </remarks>
    private static IEnumerable<FlyoutBase> FlyoutsOf(Control c)
    {
        var primary = c switch
        {
            Button b => b.Flyout,
            SplitButton sb => sb.Flyout,
            _ => null,
        };
        if (primary is not null) yield return primary;
        // Any control can carry an attached flyout, a Button too: a running Toolbar's dropdown is a MenuFlyout attached to
        // its button and shown with ShowAt, and read only for controls other than buttons it was in neither the dump nor
        // the snapshot, though open (#2323).
        var attached = FlyoutBase.GetAttachedFlyout(c);
        if (attached is not null && !ReferenceEquals(attached, primary)) yield return attached;
        if (c.ContextFlyout is { } context && !ReferenceEquals(context, primary) && !ReferenceEquals(context, attached))
            yield return context;
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
            // As the control-view walk leaves it out: a released row reached by its id was the row the dump no longer lists (#1292 review).
            else if (v is Control released && (Released(released) || HeldOutOfView(released)))
            {
            }
            else if (v is Control c)
            {
                yield return c;
                // Same crossing as the control-view walk: without it #SomeId finds an item on the menu bar
                // but never one inside an open context menu or toolbar flyout.
                foreach (var attachedPopup in AttachedPopupsOf(c))
                    if (attachedPopup is { Child: Visual attached })
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
        foreach (var candidate in CandidateDiscriminators(m))
        {
            // ParseSegment trims bracket contents; use that same name for uniqueness checks and emission.
            var disc = candidate.Trim();
            if (disc.Length == 0 || !IsPathSafe(disc)) continue;
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
        if (MeaningfulLabelOf(m.Info.Peer) is { } label) yield return label;
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

        // Keep the common surfaces' existing collapsed paths, including named TextEditors. Scrolling is
        // content-dependent (OwnScrollerFor starts succeeding on overflow), so it cannot establish a node.
        // rangeValue is also advertised only for a usable range: use the peer's capability, not today's
        // bounds/read-only state. An AutomationId remains an explicit request to expose a wrapper;
        // x:Name alone remains transparent, consistently before and after overflow (#1436).
        var hasCapability = providers.Any(p => p is not ("scroll" or "rangeValue" or "multiSelectItem"))
                            || peer?.GetProvider<IRangeValueProvider>() is not null;
        var structural = type == "None" && !hasCapability && !focusable && autoId is null;

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

    private static string? DistinctLabelOf(string? name, AutomationPeer? peer) =>
        MeaningfulLabelOf(peer) is { } label && label != name ? label : null;

    private static string? NameOf(Control control, AutomationPeer peer)
        => NullIfEmpty(control.Name) ?? MeaningfulLabelOf(peer);

    /// <summary>The peer's automation name, unless all it says is the .NET type of the control's content.</summary>
    /// <remarks>
    /// An icon button's peer names it by its content's <c>ToString()</c>, which for a Path is
    /// "Avalonia.Controls.Shapes.Path" -- the same on every such button, so it neither describes the control nor
    /// tells it from its siblings, and a path built on it cannot be trusted (#542). Such a name is dropped here,
    /// so the path falls back to an index, and <see cref="TypeNameAsNameOf"/> reports what it was.
    /// </remarks>
    private static string? MeaningfulLabelOf(AutomationPeer? peer) =>
        Safe<string?>(() => peer?.GetName(), null) is { Length: > 0 } name && !IsTypeName(name) ? name : null;

    /// <summary>The peer's automation name when <see cref="MeaningfulLabelOf"/> dropped it for being a type name.</summary>
    private static string? TypeNameAsNameOf(AutomationPeer? peer) =>
        NullIfEmpty(Safe<string?>(() => peer?.GetName(), null)) is { } name && IsTypeName(name) ? name : null;

    private static readonly Dictionary<string, bool> s_typeNames = new(StringComparer.Ordinal);

    /// <summary>True for a dotted name that resolves to a type in a loaded assembly.</summary>
    private static bool IsTypeName(string name)
    {
        if (!name.Contains('.') || name.Contains(' ')) return false;
        if (s_typeNames.TryGetValue(name, out var known)) return known;
        var found = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => Safe(() => a.GetType(name, throwOnError: false) is not null, false));
        s_typeNames[name] = found;
        return found;
    }

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
        string.Equals(StripUnderscore(a.Trim()), StripUnderscore(b.Trim()), StringComparison.OrdinalIgnoreCase);

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
    UiNode[] Children,
    string? TypeNameAsName = null,
    // True when the node has children the walk did not reach because it stopped at maxDepth; absent otherwise, so
    // an empty Children is a leaf only when this is absent (#953).
    bool? Truncated = null,
    // True when the node's DataContext is the same object as its parent's in the tree, so a search by its type finds
    // where a context starts, as a nested tree item's own of the same type does (#699). Not part of the reply.
    [property: System.Text.Json.Serialization.JsonIgnore] bool InheritsDataContext = false,
    string? Label = null);

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
    bool IsFocused,
    bool IsOffscreen,
    bool? IsHidden,
    double[] BoundingRect,
    string[] SelectionItems,
    string? Value,
    bool? ToggleState,
    VmMember[] DataContextMembers,
    RangeState? Range = null,
    string? DataContextOwner = null,
    string? DataContextNote = null,
    // How many items a list, dropdown or tab control holds, whether or not their containers exist, hidden ones too:
    // SelectionItems is only what is selected, so a list lacking an item read the same as one not choosing it (#1263).
    // Absent for anything else: a menu or the dock's lists count separators and splitters a dump does not show.
    int? ItemCount = null,
    string? Label = null,
    // The value pattern's writability, independent of the range pattern's flag. Null without that pattern.
    bool? IsReadOnly = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    WindowFrameState? Frame = null,
    string? ExpandCollapseState = null,
    // The text of each item of a list, dropdown or tab control, as select matches it, rows scrolled out of view included,
    // and what was left out (#2450). Absent for a dropdown that is closed, which the note explains.
    string[]? Items = null,
    string? ItemsNote = null,
    // The mouse pointer it shows: a running program's MousePointer by number and name, and where it is set (#2453). Absent
    // when none is set on it or above it, and the platform's arrow shows.
    PointerState? Pointer = null);

/// <summary>The inspected Window's Avalonia frame properties and, on Windows, its native style masks.</summary>
public record WindowFrameState(bool CanResize, string WindowDecorations, bool CanMinimize, bool CanMaximize,
    bool ShowInTaskbar, string WindowState,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? NativeStyleNote,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    uint? NativeStyle = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    uint? NativeExStyle = null);

/// <summary>A range control's state: a scroll bar's or slider's position, its bounds, and whether it can be set.</summary>
public record RangeState(double Value, double Minimum, double Maximum, bool IsReadOnly);

/// <summary>A reflectable public member of a control's DataContext.</summary>
/// <param name="Value">
/// The member's current value where it is readable as text, else null. Null means "not read" — a command,
/// an unsupported type, a getter that threw — never "the value is empty", which reads back as <c>""</c>.
/// </param>
public record VmMember(string Name, string Kind, string? TypeName, bool CanWrite, string? Value = null);

/// <summary>Outcome of <see cref="UiAutomationDriver.Interact"/>. <c>Mechanism</c> is "peer" for
/// provider-backed actions (Phase 6) and "reflection" for the DataContext fallback (Phase 7).</summary>
/// <param name="Committed">
/// A <c>set_value</c> pushed its text to a bound source, so whether the source kept it is still to be asked
/// (<see cref="UiAutomationDriver.RefusedCommit"/>). Not part of the reply; the detail says it in words.
/// </param>
/// <param name="Release">
/// The second part of an action that comes in two, still to be done: a <c>click</c>'s release, which
/// <see cref="UiAutomationDriver.FinishAsync"/> raises and whose outcome is the reply. Not part of the reply.
/// </param>
/// <param name="NextClick">The second press of a double click, after the first release has settled. Not serialized.</param>
/// <param name="HeldMoves">
/// A drag's held moves, which <see cref="UiAutomationDriver.FinishAsync"/> raises one by one before the release, with the
/// dispatcher's layout run between them (#2175). Not serialized.
/// </param>
/// <param name="ClickWatch">The program before the first press, waited for between clicks. Not serialized.</param>
/// <param name="Settled">
/// What the action left on a running form, read once the program has run what it set off: a clause for the detail, or
/// null for none. <see cref="UiAutomationDriver.WaitForProgramAsync"/> reads it when it finds the program idle (#1280).
/// </param>
public record InteractOutcome(
    bool Success,
    string Mechanism,
    string? Detail,
    string? Error,
    [property: System.Text.Json.Serialization.JsonIgnore] bool Committed = false,
    [property: System.Text.Json.Serialization.JsonIgnore] Func<InteractOutcome>? Release = null,
    [property: System.Text.Json.Serialization.JsonIgnore] Func<string?>? Settled = null,
    [property: System.Text.Json.Serialization.JsonIgnore] Func<Task<InteractOutcome>>? NextClick = null,
    [property: System.Text.Json.Serialization.JsonIgnore] UiAutomationDriver.ProgramWatch? ClickWatch = null,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<Action>? HeldMoves = null,
    // Where a tip hover saw open was: x, y, width, height in the window's device-independent units from its top-left, as
    // inspect_element's boundingRect is (#1303). Absent from every other reply.
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    double[]? TipBounds = null);

public record KeyboardFocusReport(string? FocusedPath, string? FocusReason);
