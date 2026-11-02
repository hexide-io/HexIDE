using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HexIDE.Runtime.Interpreter;

internal interface IListViewCollection
{
    Vb6Value Item(IReadOnlyList<Vb6Value> args);
}

internal abstract class ListViewTextObject : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal string Key { get; set; } = "";
    internal string Text { get; set; } = "";
    protected Vb6Value Tag = new("");
    internal Vb6Value DefaultText
    {
        get { TryGetProperty("Text", out var value); return value; }
    }
    public abstract bool TryGetProperty(string name, out Vb6Value value);
    public abstract bool TrySetProperty(string name, Vb6Value value);

    protected bool SetTextProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "text": Text = ListViewItems.Text(value, allowInteger42: false); return true;
            case "key" or "tag":
                throw new NotImplementedException($"ListView {name} assignment on this object is unmeasured (#1085)");
            default: return false;
        }
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Await ListView calls");
    public virtual Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        if (args.Count != 0)
            throw new NotImplementedException($"ListView {method} arguments are unmeasured (#1085)");
        TryGetProperty(method, out var value);
        return Task.FromResult<Vb6Value?>(value);
    }
}

internal abstract class ListViewTextCollection<T> : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource, IListViewCollection
    where T : ListViewTextObject
{
    internal List<T> Entries { get; } = [];
    protected abstract IReadOnlyList<string> AddParameters { get; }
    protected abstract T Create(List<Vb6Value> args, int at);
    internal virtual void Alive() { }
    internal virtual void Changed() { }
    protected virtual void Added(T item, int at) { }
    protected virtual void Removing(T item) { }

    internal int Position(Vb6Value value, bool adding = false)
    {
        Alive();
        return ListViewItems.Position(value, Entries.Select(e => e.Key).ToArray(), adding);
    }
    internal void CheckKey(string key, T? except = null) => ListViewItems.CheckKey(key,
        Entries.Where(e => e != except).Select(e => e.Key));

    public Vb6Value Item(IReadOnlyList<Vb6Value> args) => args.Count == 1
        ? new Vb6Value(Entries[Position(args[0])])
        : throw new NotImplementedException("ListView collection argument count is unmeasured (#1085)");
    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Await ListView calls");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Alive();
        var arity = method.ToLowerInvariant() switch { "add" => AddParameters.Count, "item" or "remove" => 1, _ => 0 };
        if (args.Count > arity || arity == 1 && args.Count != 1)
            throw new NotImplementedException("ListView collection argument count is unmeasured (#1085)");
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count": result = new Vb6Value((long)Entries.Count); break;
            case "item": result = Item(args); break;
            case "add":
                var key = ListViewItems.Argument(args, 1).IsMissing ? "" : ListViewItems.Text(args[1]);
                CheckKey(key);
                var at = ListViewItems.Argument(args, 0).IsMissing ? Entries.Count : Position(args[0], true);
                var item = Create(args, at);
                item.Key = key;
                item.Text = ListViewItems.Argument(args, 2).IsMissing ? "" : ListViewItems.Text(args[2], allowInteger42: false);
                Entries.Insert(at, item);
                Added(item, at);
                result = new Vb6Value(item);
                break;
            case "remove":
                var removed = Entries[Position(args[0])];
                Removing(removed);
                Entries.Remove(removed);
                break;
            case "clear":
                foreach (var entry in Entries.ToArray())
                    Removing(entry);
                Entries.Clear();
                break;
            default: throw new NotImplementedException($"ListView collection {method} is not implemented (#1085)");
        }
        Changed();
        return Task.FromResult(result);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        Alive();
        value = new Vb6Value((long)Entries.Count);
        return name.Equals("Count", StringComparison.OrdinalIgnoreCase);
    }
    public bool TrySetProperty(string name, Vb6Value value) => throw (name.Equals("Count", StringComparison.OrdinalIgnoreCase)
        ? new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty)
        : new NotImplementedException($"ListView collection {name} assignment is not implemented (#1085)"));
    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        names = member.ToLowerInvariant() switch { "add" => AddParameters, "item" or "remove" => ["Index"], _ => [] };
        required = names.Count == 1 ? 1 : 0;
        return true;
    }
}

internal sealed class ListViewColumns(Action? changed = null) : ListViewTextCollection<ListViewColumn>
{
    internal override void Changed() => changed?.Invoke();
    internal List<ListViewColumn> DisplayOrder { get; } = [];
    protected override IReadOnlyList<string> AddParameters => ["Index", "Key", "Text", "Width", "Alignment", "Icon"];
    protected override ListViewColumn Create(List<Vb6Value> args, int at)
    {
        var column = new ListViewColumn(this);
        if (!ListViewItems.Argument(args, 3).IsMissing)
            column.TrySetProperty("Width", args[3]);
        if (!ListViewItems.Argument(args, 4).IsMissing)
        {
            var alignment = ListViewItems.Enum(args[4], 2);
            if (at == 0 && alignment != 0)
                throw ListViewItems.Error(35604);
            column.Alignment = alignment;
        }
        if (!ListViewItems.Argument(args, 5).IsMissing)
            throw ListViewItems.Error(35613);
        return column;
    }
    protected override void Added(ListViewColumn item, int at) => DisplayOrder.Insert(at, item);
    protected override void Removing(ListViewColumn item)
    {
        item.Removed = true;
        DisplayOrder.Remove(item);
    }
}

internal sealed class ListViewColumn(ListViewColumns columns) : ListViewTextObject
{
    internal bool Removed { get; set; }
    internal int Alignment { get; set; }
    private float width = 1440;
    internal float Width => width;
    internal void LoadProperties(float loadedWidth, string tag)
    {
        width = loadedWidth;
        Tag = new Vb6Value(tag);
    }
    public override bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "text" => new Vb6Value(Text), "key" => new Vb6Value(Key), "tag" => Tag,
            "index" => new Vb6Value((long)Index() + 1),
            "width" => new Vb6Value(width), "alignment" => new Vb6Value((long)Alignment),
            "position" => new Vb6Value(Position()),
            "subitemindex" => new Vb6Value(Index()),
            "left" => new Vb6Value(columns.Entries.Take(Index()).Sum(c => c.Width)),
            "icon" => new Vb6Value(0),
            _ => throw new NotImplementedException($"ColumnHeader.{name} is not implemented (#1085)"),
        };
        return true;
    }
    private int Position()
    {
        Index();
        return columns.DisplayOrder.IndexOf(this) + 1;
    }
    private int Index() => columns.Entries.IndexOf(this) is var at && at >= 0 ? at
        : throw new NotImplementedException("Removed ColumnHeader geometry/index is unmeasured (#1085)");
    public override bool TrySetProperty(string name, Vb6Value value)
    {
        if (Removed)
            throw new NotImplementedException("Removed ColumnHeader assignments are unmeasured (#1085)");
        if (SetTextProperty(name, value))
        {
            columns.Changed();
            return true;
        }
        switch (name.ToLowerInvariant())
        {
            case "width":
                var number = ListViewItems.Number(value);
                if (number < 0)
                    throw ListViewItems.Error(380);
                // These are the measured run-time widths. Other conversions await an oracle probe.
                width = number switch
                {
                    0 => 0, 1440 => 1440, 2000 => 2000.126f, 1200 => 1200.189f,
                    _ => throw new NotImplementedException("This ColumnHeader Width conversion is unmeasured (#1085)"),
                };
                break;
            case "alignment":
                var alignment = ListViewItems.Enum(value, 2);
                if (Index() == 0 && alignment != 0)
                    throw ListViewItems.Error(35604);
                Alignment = alignment;
                break;
            case "position":
                Index();
                var position = ListViewItems.Enum(value, columns.Entries.Count);
                if (position == 0)
                    throw ListViewItems.Error(380);
                columns.DisplayOrder.Remove(this);
                columns.DisplayOrder.Insert(position - 1, this);
                break;
            case "icon": throw ListViewItems.Error(35613);
            case "index" or "left": throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            case "subitemindex": throw ListViewItems.Error(382);
            default: throw new NotImplementedException($"ColumnHeader.{name} is not implemented (#1085)");
        }
        columns.Changed();
        return true;
    }
}
