using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// The measured object model; file writing follows separately (#1085).
public sealed partial class VBListView : Decorator, ICSharpPropertyBag, INamedParameterSource
{
    static VBListView()
    {
        AffectsRender<VBListView>(IsFocusedProperty, IsEnabledProperty);
    }

    public VBListView()
    {
        Items = new ListViewItems(this);
        Columns = new ListViewColumns(InvalidateVisual);
        Focusable = true;
        UpdateClientInset();
    }

    private void UpdateClientInset() =>
        VBProps.SetClientInset(this, Number("BorderStyle") != 0 && Number("Appearance") == 1 ? 3 : 0);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || labelEditor is not null)
            return;
        labelTimer?.Stop();
        DispatchMouse(async () =>
        {
            await RaiseKeyEvent("KeyDown", e);
            Navigate(e);
            await WaitForMouseEvents();
        });
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!e.Handled && labelEditor is null)
            DispatchMouse(() => RaiseKeyEvent("KeyUp", e));
    }

    private void Navigate(KeyEventArgs e)
    {
        if (SelectedItem is not { } item || !Items.Entries.Contains(item))
            return;
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None && Checkboxes)
        {
            item.TryGetProperty("Checked", out var value);
            item.TrySetProperty("Checked", new Vb6Value(!(bool)value.Value!));
            RaiseItemEvent("ItemCheck", item);
            RaiseItemEvent("ItemClick", item);
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down or Key.Home
            && (e.KeyModifiers == KeyModifiers.None
                || MultiSelect && (e.Key is Key.Up or Key.Down) && (e.KeyModifiers is KeyModifiers.Control or KeyModifiers.Meta)))
        {
            var index = e.Key == Key.Home ? 0 : Items.Entries.IndexOf(item) + (e.Key == Key.Up ? -1 : 1);
            if (index < 0 || index >= Items.Entries.Count)
                return;
            var next = Items.Entries[index];
            if (ReferenceEquals(next, item))
                return;
            if (e.KeyModifiers == KeyModifiers.None)
            {
                foreach (var other in Items.Entries)
                    other.IsSelected = false;
                Select(next);
                RaiseItemEvent("ItemClick", next);
            }
            else
                SelectedItem = next;
            e.Handled = true;
        }
    }

    private void RaiseItemEvent(string name, ICSharpProxy item)
    {
        foreach (var handler in ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), name))
            handler.Root.ExecuteEvent(handler.Name, () => handler.Own && VBProps.GetIndex(this) is { } index
                ? [new Vb6Value(index), new Vb6Value(item)] : [new Vb6Value(item)]);
    }

    internal ListViewItems Items { get; }
    internal ListViewColumns Columns { get; }
    private readonly Dictionary<string, Vb6Value> properties = new(StringComparer.OrdinalIgnoreCase)
    {
        ["View"] = new(0L), ["Arrange"] = new(0L), ["LabelEdit"] = new(0L),
        ["SortOrder"] = new(0L), ["TextBackground"] = new(0L), ["PictureAlignment"] = new(0L),
        ["SortKey"] = new(0), ["LabelWrap"] = new(true), ["HideSelection"] = new(true),
        ["TabStop"] = new(true), ["HideColumnHeaders"] = new(false), ["FullRowSelect"] = new(false), ["GridLines"] = new(false),
        ["Sorted"] = new(false), ["AllowColumnReorder"] = new(false), ["FlatScrollBar"] = new(false),
        ["HotTracking"] = new(false), ["HoverSelection"] = new(false),
        ["BorderStyle"] = new(1L), ["Appearance"] = new(1L),
        ["BackColor"] = new((long)unchecked((int)0x80000005)), ["ForeColor"] = new((long)unchecked((int)0x80000008)),
        ["OLEDragMode"] = new(0L), ["OLEDropMode"] = new(0L), ["MousePointer"] = new(0L),
        ["Picture"] = Vb6Value.Nothing, ["DropHighlight"] = Vb6Value.Nothing,
        ["Font"] = new(new ListViewFont()),
        ["Icons"] = Vb6Value.Nothing, ["SmallIcons"] = Vb6Value.Nothing, ["ColumnHeaderIcons"] = Vb6Value.Nothing,
    };
    internal bool Sorted => (bool)properties["Sorted"].Value!;
    private bool pendingSort;
    private NotImplementedException? sortFailure;
    internal void CheckSort()
    {
        if (sortFailure is { } missing)
            throw missing;
    }
    internal void ItemAdded(ListViewItem item)
    {
        if (!Sorted || pendingSort)
            return;
        var key = (int)properties["SortKey"].Value!;
        foreach (var other in Items.Entries)
            Compare(item.SortText(key), other.SortText(key));
        pendingSort = true;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (pendingSort && Sorted)
                    Sort();
            }
            catch (NotImplementedException missing)
            {
                // A deferred comparison can refuse too; report it on the next model access, not on the UI dispatcher.
                sortFailure = missing;
            }
            pendingSort = false;
        });
    }

    private void Sort()
    {
        var key = (int)properties["SortKey"].Value!;
        var direction = (long)properties["SortOrder"].Value! == 0 ? 1 : -1;
        ListViewItem[] sorted;
        try
        {
            sorted = Items.Entries.OrderBy(i => i.SortText(key), Comparer<string>.Create((a, b) => direction * Compare(a, b))).ToArray();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is NotImplementedException missing)
        {
            throw missing;
        }
        Items.Entries.Clear();
        Items.Entries.AddRange(sorted);
        pendingSort = false;
        InvalidateVisual();
    }

    private static int Compare(string a, string b)
    {
        static string Word(string s) => s.Replace("-", "", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal);
        var comparison = CultureInfo.GetCultureInfo("en-US").CompareInfo;
        var word = comparison.Compare(Word(a), Word(b), CompareOptions.IgnoreCase);
        if (word != 0)
            return word;
        if (a.Equals(b, StringComparison.OrdinalIgnoreCase))
            return 0;
        var aPunctuation = a.Length - Word(a).Length;
        var bPunctuation = b.Length - Word(b).Length;
        if (aPunctuation == 0 || bPunctuation == 0)
            return aPunctuation.CompareTo(bPunctuation);
        throw new NotImplementedException("ListView sorting ties between two punctuated words are unmeasured (#1085)");
    }

    internal Vb6Value CallMember(string name, IReadOnlyList<Vb6Value> args)
    {
        VBProps.RefuseIfRemoved(this);
        CheckSort();
        switch (name.ToLowerInvariant())
        {
            case "columnheaders": return Columns.Item(args);
            case "listitems": return Items.Item(args);
            case "getfirstvisible" when args.Count == 0:
                return Items.Entries.FirstOrDefault() is { } first ? new Vb6Value(first) : Vb6Value.Nothing;
            case "refresh" when args.Count == 0:
                if (pendingSort && Sorted)
                    Sort();
                InvalidateVisual();
                return Vb6Value.Nothing;
            case "finditem": return Find(args);
            case "hittest" when args.Count == 2:
                if (ListViewItems.Number(args[0]) < 0 || ListViewItems.Number(args[1]) < 0
                    || ListViewItems.Number(args[0]) == 100000 && ListViewItems.Number(args[1]) == 100000)
                    return Vb6Value.Nothing;
                throw new NotImplementedException("ListView.HitTest item geometry awaits drawing (#1085)");
            default: throw new NotImplementedException($"ListView.{name} is not implemented (#1085)");
        }
    }

    private Vb6Value Find(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count is < 1 or > 4)
            throw new NotImplementedException("FindItem argument count is unmeasured (#1085)");
        Vb6Value Arg(int n) => args.Count > n ? args[n] : Vb6Value.Missing;
        var text = ListViewItems.Text(args[0]);
        var where = Arg(1).IsMissing ? 0 : ListViewItems.Enum(args[1], 2);
        var start = Arg(2).IsMissing ? 1 : ListViewItems.Enum(args[2], Items.Entries.Count);
        if (start == 0)
            throw ListViewItems.Error(380);
        var partial = !Arg(3).IsMissing && ListViewItems.Enum(args[3], 1) == 1;
        if (where == 2 && partial)
            throw new NotImplementedException("ListView.FindItem partial Tag searches are unmeasured (#1085)");
        bool Matches(string candidate) => partial
            ? candidate.StartsWith(text, StringComparison.OrdinalIgnoreCase)
            : candidate.Equals(text, StringComparison.OrdinalIgnoreCase);
        var item = Items.Entries.Skip(start - 1).FirstOrDefault(i => where switch
        {
            0 => Matches(i.Caption),
            1 => !partial && i.SubItems.Entries.Any(s => Matches(s.Text)),
            2 => Matches(i.TagText),
            _ => false,
        });
        return item is null ? Vb6Value.Nothing : new Vb6Value(item);
    }

    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        names = member.ToLowerInvariant() switch
        {
            "finditem" => ["String", "Where", "Index", "fPartial"],
            "hittest" => ["X", "Y"],
            _ => [],
        };
        required = names.Count == 4 ? 1 : names.Count;
        return true;
    }

    internal bool Checkboxes { get; private set; }
    internal bool MultiSelect { get; private set; }
    private ListViewItem? selectedItem;
    internal ListViewItem? SelectedItem
    {
        get => selectedItem;
        set { selectedItem = value; InvalidateVisual(); }
    }

    internal void Select(ListViewItem item)
    {
        if (!MultiSelect)
            foreach (var other in Items.Entries)
                other.IsSelected = false;
        item.IsSelected = true;
        SelectedItem = item;
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        CheckSort();
        if (name.Equals("SelectedItem", StringComparison.OrdinalIgnoreCase)
            && SelectedItem is { } selected && !Items.Entries.Contains(selected))
            throw new NotImplementedException("SelectedItem after deleting the current item is unmeasured (#1085)");
        if (properties.TryGetValue(name, out value))
            return true;
        if (name.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(IsEnabled);
            return true;
        }
        if (name.Equals("ColumnHeaders", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value(Columns);
            return true;
        }
        if (name.Equals("GetFirstVisible", StringComparison.OrdinalIgnoreCase))
        {
            value = CallMember(name, []);
            return true;
        }
        value = name.ToLowerInvariant() switch
        {
            "listitems" => new Vb6Value(Items),
            "checkboxes" => new Vb6Value(Checkboxes),
            "multiselect" => new Vb6Value(MultiSelect),
            "selecteditem" => SelectedItem is { } item ? new Vb6Value(item) : Vb6Value.Nothing,
            _ => default,
        };
        if (name.Equals("ListItems", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Checkboxes", StringComparison.OrdinalIgnoreCase)
            || name.Equals("MultiSelect", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SelectedItem", StringComparison.OrdinalIgnoreCase))
            return true;
        RefusePending(name);
        return false;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        CheckSort();
        InvalidateVisual();
        if (properties.TryGetValue(name, out var previous))
        {
            if (name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException("ListView.TabStop assignment is unmeasured in group 1 (#1085)");
            if (previous.Type == Vb6Value.ValueType.Boolean)
            {
                var boolean = ListViewItems.Boolean(value);
                properties[name] = new Vb6Value(boolean);
                if (name.Equals("Sorted", StringComparison.OrdinalIgnoreCase))
                {
                    if (boolean)
                        Sort();
                    else
                        pendingSort = false;
                }
                return true;
            }
            var maximum = name.ToLowerInvariant() switch
            {
                "view" => 3, "arrange" => 2, "labeledit" or "sortorder" or "textbackground" => 1,
                "picturealignment" => 5, "sortkey" => Math.Max(0, Columns.Entries.Count - 1),
                _ => -1,
            };
            if (maximum >= 0)
            {
                var number = ListViewItems.Enum(value, maximum);
                if (name.Equals("SortOrder", StringComparison.OrdinalIgnoreCase) && Sorted && number != (long)previous.Value!)
                    throw new NotImplementedException("ListView.SortOrder timing while Sorted is unresolved in the measurements (#1085)");
                properties[name] = name.Equals("SortKey", StringComparison.OrdinalIgnoreCase) ? new Vb6Value(number) : new Vb6Value((long)number);
                return true;
            }
            throw new NotImplementedException($"ListView.{name} assignment is unmeasured in group 1 (#1085)");
        }
        switch (name.ToLowerInvariant())
        {
            case "enabled": IsEnabled = ListViewItems.Boolean(value); return true;
            case "checkboxes": Checkboxes = ListViewItems.Boolean(value); return true;
            case "multiselect": MultiSelect = ListViewItems.Boolean(value); return true;
            case "selecteditem":
                if (value.Value is null)
                    SelectedItem = null;
                else if (value.Value is ListViewItem item && Items.Entries.Contains(item))
                    Select(item);
                else
                    throw new NotImplementedException("Assigning a foreign SelectedItem is unmeasured (#1085)");
                return true;
            case "listitems": throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        RefusePending(name);
        return false;
    }

    private static void RefusePending(string name)
    {
        if (Components.InBox.ListViewComponentClass.Instance.Member(name) is not null)
            throw new NotImplementedException($"ListView.{name} is not implemented (#1085)");
    }
}

internal sealed class ListViewFont(string family = "MS Sans Serif", double size = 8.25) : ICSharpProxy, ICSharpPropertyBag
{
    public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException("ListView Font calls are unmeasured (#1085)");
    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "name" => new Vb6Value(family), "size" => new Vb6Value(size),
            _ => throw new NotImplementedException($"ListView Font.{name} is unmeasured (#1085)"),
        };
        return true;
    }
    public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"ListView Font.{name} assignment is unmeasured (#1085)");
}
