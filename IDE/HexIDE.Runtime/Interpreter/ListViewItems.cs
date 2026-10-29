using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

// Measured on vb6.exe SP6, native and p-code (docs/vb6-fidelity-oracle/in-box-controls-the-listview.md).
internal sealed class ListViewItems(VBListView owner) : ICSharpAsyncProxy, INamedParameterSource, ICSharpPropertyBag, IListViewCollection
{
    internal List<ListViewItem> Entries { get; } = [];

    internal static VBRunTimeException Error(int number) => new ListViewError(number, number switch
    {
        35604 => "The first column in a ListView control must be left aligned",
        382 => "Property cannot be set at runtime",
        35605 => "This item's control has been deleted",
        35613 => "ImageList must be initialized before it can be used",
        // Only the numbers of these errors have been measured; do not pin their descriptions.
        _ => "",
    });

    internal sealed class ListViewError(int number, string description) : VBRunTimeException(new VBStandardError(number, description))
    {
        internal bool HasDescription => Error.Description.Length > 0;
    }

    internal static bool Boolean(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Boolean)
            throw new NotImplementedException("ListView Boolean conversion is unmeasured (#1085)");
        return (bool)value.Value!;
    }

    internal static string Text(Vb6Value value, bool allowInteger42 = true)
    {
        if (value.Type == Vb6Value.ValueType.String)
            return (string)value.Value!;
        if (allowInteger42 && value.Type == Vb6Value.ValueType.Integer && Vb6Value.TryNumericToDouble(value, out var number) && number == 42)
            return "42";
        throw new NotImplementedException("This ListView text conversion is unmeasured (#1085)");
    }

    internal static double Number(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long
            && value.Type != Vb6Value.ValueType.Single && value.Type != Vb6Value.ValueType.Double
            || !Vb6Value.TryNumericToDouble(value, out var number) || !double.IsFinite(number))
            throw new NotImplementedException("This ListView numeric conversion is unmeasured (#1085)");
        return number;
    }

    internal static int Enum(Vb6Value value, int maximum)
    {
        var number = Number(value);
        if (number != Math.Truncate(number))
            throw new NotImplementedException("This ListView enum conversion is unmeasured (#1085)");
        return number >= 0 && number <= maximum ? (int)number : throw Error(380);
    }

    internal int Position(Vb6Value index, bool adding = false) => Position(index, Entries.Select(e => e.Key).ToArray(), adding);

    internal static int Position(Vb6Value index, IReadOnlyList<string> keys, bool adding = false)
    {
        if (index.Type == Vb6Value.ValueType.String && !adding)
        {
            var key = (string)index.Value!;
            if (key.Length == 0)
                throw Error(35603);
            var at = keys.ToList().IndexOf(key);
            if (at < 0 && keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
                throw new NotImplementedException("ListView key case comparison is unmeasured (#1085)");
            return at >= 0 ? at : throw Error(35601);
        }
        if (index.Type == Vb6Value.ValueType.Date || !Vb6Value.TryNumericToDouble(index, out var numeric))
            throw new NotImplementedException("This ListView index conversion is unmeasured (#1085)");
        var rounded = Math.Round(numeric, MidpointRounding.ToEven);
        return rounded >= 1 && rounded <= keys.Count + (adding ? 1 : 0)
            ? (int)rounded - 1 : throw Error(35600);
    }

    internal void CheckKey(string key, ListViewItem? except = null) => CheckKey(key, Entries.Where(e => e != except).Select(e => e.Key));

    internal static void CheckKey(string key, IEnumerable<string> keys)
    {
        if (key.Length == 0)
            return;
        if (key == "5")
            throw Error(35603);
        if (keys.Contains(key))
            throw Error(35602);
        if (keys.Any(k => k.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException("ListView key case comparison is unmeasured (#1085)");
        if (double.TryParse(key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw new NotImplementedException("This numeric ListView key is unmeasured (#1085)");
    }

    public Vb6Value Item(IReadOnlyList<Vb6Value> args)
    {
        owner.CheckSort();
        if (args.Count != 1)
            throw new NotImplementedException("ListItems argument count is unmeasured (#1085)");
        return new Vb6Value(Entries[Position(args[0])]);
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Await ListItems calls");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        owner.CheckSort();
        var arity = method.ToLowerInvariant() switch { "add" => 5, "item" or "remove" => 1, _ => 0 };
        if (args.Count > arity || (arity == 1 && args.Count != 1))
            throw new NotImplementedException("ListItems argument count is unmeasured (#1085)");
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count": result = new Vb6Value((long)Entries.Count); break;
            case "item": result = Item(args); break;
            case "add":
            {
                var key = Argument(args, 1).IsMissing ? "" : Text(args[1]);
                CheckKey(key);
                var at = Argument(args, 0).IsMissing ? Entries.Count : Position(args[0], adding: true);
                if (!Argument(args, 3).IsMissing || !Argument(args, 4).IsMissing)
                    throw Error(35613);
                var item = new ListViewItem(owner, this, key, Argument(args, 2).IsMissing ? "" : Text(args[2]), !owner.Checkboxes);
                var first = Entries.Count == 0;
                Entries.Insert(owner.Sorted ? Entries.Count : at, item);
                try { owner.ItemAdded(item); }
                catch { Entries.Remove(item); throw; }
                if (first)
                    owner.Select(item);
                result = new Vb6Value(item);
                break;
            }
            case "remove": Entries.RemoveAt(Position(args[0])); break;
            case "clear": Entries.Clear(); break;
            default: throw new NotImplementedException($"ListItems.{method} is not implemented (#1085)");
        }
        owner.InvalidateVisual();
        return Task.FromResult(result);
    }

    internal static Vb6Value Argument(List<Vb6Value> args, int at) => at < args.Count ? args[at] : Vb6Value.Missing;

    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        required = 0;
        names = member.ToLowerInvariant() switch
        {
            "add" => ["Index", "Key", "Text", "Icon", "SmallIcon"],
            "item" or "remove" => ["Index"],
            _ => [],
        };
        if (names.Count == 1)
            required = 1;
        return true;
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = new Vb6Value((long)Entries.Count);
        return name.Equals("Count", StringComparison.OrdinalIgnoreCase);
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
        throw new NotImplementedException($"ListItems.{name} assignment is not implemented (#1085)");
    }
}

internal sealed class ListViewItem(VBListView owner, ListViewItems items, string key, string text, bool isChecked)
    : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal string Key { get; private set; } = key;
    private bool selected;
    internal bool IsSelected
    {
        get => selected;
        set { selected = value; owner.InvalidateVisual(); }
    }
    private string caption = text;
    private Vb6Value tag = new("");
    private bool check = isChecked;
    internal bool HasCheckbox { get; private set; } = !isChecked;
    private ListViewSubItems? subItems;
    internal ListViewSubItems SubItems => subItems ??= new(() =>
    {
        if (!items.Entries.Contains(this))
            throw new NotImplementedException("ListSubItems after removing its owner is unmeasured (#1085)");
    }, owner.InvalidateVisual);
    private bool ghosted;
    private bool bold;
    private long foreColor = unchecked((int)0x80000008);
    internal string Caption => caption;
    internal string SortText(int key) => key == 0 ? caption : SubItems.Entries.ElementAtOrDefault(key - 1)?.Text ?? "";
    internal string TagText => (string)tag.Value!;

    private int SubIndex(IReadOnlyList<Vb6Value> args)
    {
        Alive();
        if (args.Count != 1)
            throw new NotImplementedException("SubItems argument count is unmeasured (#1085)");
        var index = ListViewItems.Enum(args[0], Math.Max(0, owner.Columns.Entries.Count - 1));
        return index > 0 ? index - 1 : throw ListViewItems.Error(380);
    }

    internal void SetSubItem(IReadOnlyList<Vb6Value> args, Vb6Value value)
    {
        var at = SubIndex(args);
        var text = ListViewItems.Text(value);
        while (SubItems.Entries.Count <= at)
            SubItems.Entries.Add(new ListViewSubItem(SubItems));
        SubItems.Entries[at].Text = text;
        owner.InvalidateVisual();
    }

    private void Alive()
    {
        owner.CheckSort();
        if (!items.Entries.Contains(this))
            throw ListViewItems.Error(35605);
    }

    internal Vb6Value DefaultText
    {
        get { Alive(); return new Vb6Value(caption); }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        Alive();
        value = name.ToLowerInvariant() switch
        {
            "listsubitems" => new Vb6Value(SubItems),
            "ghosted" => new Vb6Value(ghosted),
            "bold" => new Vb6Value(bold),
            "forecolor" => new Vb6Value(foreColor),
            "tooltiptext" => new Vb6Value(""),
            "text" => new Vb6Value(caption),
            "key" => new Vb6Value(Key),
            "tag" => tag,
            "index" => new Vb6Value((long)items.Entries.IndexOf(this) + 1),
            "selected" => new Vb6Value(IsSelected),
            "checked" => new Vb6Value(check),
            "icon" or "smallicon" => Vb6Value.Variant,
            _ => throw new NotImplementedException($"ListItem.{name} is not implemented (#1085)"),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        Alive();
        switch (name.ToLowerInvariant())
        {
            case "text": caption = ListViewItems.Text(value); break;
            case "key":
                var next = ListViewItems.Text(value);
                items.CheckKey(next, this);
                Key = next;
                break;
            case "tag":
                if (ReferenceEquals(value.Value, owner))
                    throw ListViewItems.Error(35700);
                if (value.Type != Vb6Value.ValueType.String)
                    throw new NotImplementedException("This ListItem Tag value is unmeasured (#1085)");
                tag = value;
                break;
            case "selected":
                if (ListViewItems.Boolean(value))
                    owner.Select(this);
                else
                    IsSelected = false;
                break;
            case "checked": check = ListViewItems.Boolean(value); HasCheckbox = true; break;
            case "ghosted": ghosted = ListViewItems.Boolean(value); break;
            case "bold": bold = ListViewItems.Boolean(value); break;
            case "forecolor":
                if (ListViewItems.Number(value) != 255)
                    throw new NotImplementedException("This ListItem ForeColor is unmeasured (#1085)");
                foreColor = 255;
                break;
            case "tooltiptext": throw new NotImplementedException("ListItem.ToolTipText assignment is unmeasured (#1085)");
            case "icon" or "smallicon": throw ListViewItems.Error(35613);
            case "index" or "width": throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            case "left": break;
            default: throw new NotImplementedException($"ListItem.{name} is not implemented (#1085)");
        }
        owner.InvalidateVisual();
        return true;
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Await ListItem calls");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Alive();
        if (method.Equals("SubItems", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Vb6Value?>(new Vb6Value(SubItems.Entries.ElementAtOrDefault(SubIndex(args))?.Text ?? ""));
        if (method.Equals("ListSubItems", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
            return Task.FromResult<Vb6Value?>(SubItems.Item(args));
        if (method.Equals("EnsureVisible", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Vb6Value?>(new Vb6Value(true));
        if (args.Count != 0)
            throw new NotImplementedException($"ListItem.{method} arguments are not implemented (#1085)");
        TryGetProperty(method, out var value);
        return Task.FromResult<Vb6Value?>(value);
    }
}
