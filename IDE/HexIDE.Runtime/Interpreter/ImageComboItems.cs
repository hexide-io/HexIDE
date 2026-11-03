namespace HexIDE.Runtime.Interpreter;

// #1092 group 1: the collection and its owner selection.
internal sealed class ImageComboItems : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    private readonly List<ImageComboItem> items = [];
    internal ImageComboModel? Owner { get; }
    internal ImageComboItems(ImageComboModel? owner = null) => Owner = owner;
    internal int Count => items.Count;
    internal int Index(ImageComboItem item) => items.IndexOf(item) + 1;
    internal ImageComboItem Item(Vb6Value index) => items[Position(index)];
    internal static NotImplementedException Unmeasured() => new("This ImageCombo member or conversion is not implemented (#1092)");
    internal static VBRunTimeException Error(int number) => new(new VBStandardError(number, number switch
    {
        35600 => "Index out of bounds", 35601 => "Element not found", 35602 => "Key is not unique in collection",
        35603 => "Invalid key", 35613 => "ImageList must be initialized before it can be used",
        383 => "Property is read-only", 380 => "Invalid property value", _ => throw new ArgumentOutOfRangeException(nameof(number)),
    }));

    internal static int Integer(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long) throw Unmeasured();
        return Convert.ToInt32(value.Value);
    }

    internal static string Text(Vb6Value value) => value.Type == Vb6Value.ValueType.String ? (string)value.Value! : throw Unmeasured();

    private int Position(Vb6Value index)
    {
        if (index.Type == Vb6Value.ValueType.String)
        {
            var key = Text(index);
            var found = items.FindIndex(i => i.Key == key);
            if (key.Length == 0) throw Unmeasured();
            return found >= 0 ? found : throw Error(35601);
        }
        var at = Integer(index);
        return at > 0 && at <= items.Count ? at - 1 : throw Error(35600);
    }

    internal void SetKey(ImageComboItem item, Vb6Value value)
    {
        var key = Text(value);
        // Only numeric string keys were measured for ImageCombo.
        if (key.Length > 0 && double.TryParse(key, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
        {
            if (!double.IsFinite(number)) throw Unmeasured();
            throw Error(35603);
        }
        if (key.Length > 0 && items.Any(i => i != item && i.Key == key)) throw Error(35602);
        item.Key = key;
    }

    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ComboItems calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Vb6Value Arg(int i) => i < args.Count ? args[i] : Vb6Value.Missing;
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count":
                if (args.Count != 0) throw Unmeasured();
                result = new Vb6Value((long)Count); break;
            case "item": result = new Vb6Value(Item(Arg(0))); break;
            case "remove":
                var removed = items[Position(Arg(0))];
                items.Remove(removed);
                Owner?.Removed(removed);
                break;
            case "clear":
                if (items.Any(i => Owner?.IsSelected(i) == true)) throw Unmeasured();
                items.Clear();
                break;
            case "add":
            {
                var at = Arg(0).IsMissing ? items.Count : Integer(Arg(0)) - 1;
                if (at < 0 || at > items.Count) throw Error(35600);
                var item = new ImageComboItem(this);
                if (!Arg(1).IsMissing) SetKey(item, Arg(1));
                if (!Arg(2).IsMissing) item.SetText(Arg(2));
                if (!Arg(3).IsMissing) throw Error(35613);
                if (!Arg(4).IsMissing) throw Unmeasured();
                item.SetIndentation(Arg(5).IsMissing ? new Vb6Value(Owner?.Indentation ?? 0) : Arg(5));
                items.Insert(at, item);
                result = new Vb6Value(item);
                break;
            }
            default: throw Unmeasured();
        }
        return Task.FromResult(result);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value((long)Count);
            return true;
        }
        return false;
    }
    public bool TrySetProperty(string name, Vb6Value value) => name.Equals("Count", StringComparison.OrdinalIgnoreCase) ? throw Error(383) : false;
    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        names = member.ToLowerInvariant() switch
        {
            "add" => ["Index", "Key", "Text", "Image", "SelImage", "Indentation"],
            "item" or "remove" => ["Index"], _ => [],
        };
        required = member.Equals("Add", StringComparison.OrdinalIgnoreCase) ? 0 : names.Count;
        return true;
    }
}

internal sealed class ImageComboItem(ImageComboItems items) : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal string Key = "";
    private string text = "";
    private Vb6Value tag = new("");
    internal int Indentation { get; private set; }
    internal string Text
    {
        get
        {
            if (items.Index(this) == 0) throw ImageComboItems.Unmeasured();
            return text;
        }
    }
    internal void SetText(Vb6Value value)
    {
        text = ImageComboItems.Text(value);
        items.Owner?.ItemTextChanged(this);
    }
    internal void SetIndentation(Vb6Value value)
    {
        var number = ImageComboItems.Integer(value);
        // The measured accepted range is not known: only 0, 1, 2, 3, 4 and the two refused values are pinned.
        if (number is -1 or 32767) throw ImageComboItems.Error(380);
        if (number is not (0 or 1 or 2 or 3 or 4)) throw ImageComboItems.Unmeasured();
        Indentation = number;
    }
    public bool TryGetProperty(string name, out Vb6Value value)
    {
        if (items.Index(this) == 0) throw ImageComboItems.Unmeasured();
        value = name.ToLowerInvariant() switch
        {
            "text" => new Vb6Value(Text), "key" => new Vb6Value(Key), "tag" => tag,
            "selected" => new Vb6Value(items.Owner?.IsSelected(this) ?? throw ImageComboItems.Unmeasured()),
            "index" => new Vb6Value((long)items.Index(this)), "indentation" => new Vb6Value(Indentation),
            _ => throw ImageComboItems.Unmeasured(),
        };
        return true;
    }
    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (items.Index(this) == 0) throw ImageComboItems.Unmeasured();
        switch (name.ToLowerInvariant())
        {
            case "text": SetText(value); break;
            case "key": items.SetKey(this, value); break;
            case "selected":
                if (value.Type != Vb6Value.ValueType.Boolean || !(bool)value.Value! || items.Owner is null)
                    throw ImageComboItems.Unmeasured();
                items.Owner.Select(this);
                break;
            case "tag":
                if (value.Type != Vb6Value.ValueType.EmptyVariant && value.Type != Vb6Value.ValueType.Null
                    && value != Vb6Value.Nothing && value.Value is not (string or bool) && !Vb6Value.TryNumericToDouble(value, out _))
                    throw ImageComboItems.Unmeasured();
                tag = value.Type == Vb6Value.ValueType.EmptyVariant ? new Vb6Value("") : value; break;
            case "indentation": SetIndentation(value); break;
            case "index": throw ImageComboItems.Error(383);
            case "image": throw ImageComboItems.Error(35613);
            default: throw ImageComboItems.Unmeasured();
        }
        return true;
    }
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("ComboItem calls must be awaited");
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        if (args.Count != 0) throw ImageComboItems.Unmeasured();
        TryGetProperty(method, out var value);
        return Task.FromResult<Vb6Value?>(value);
    }
}
