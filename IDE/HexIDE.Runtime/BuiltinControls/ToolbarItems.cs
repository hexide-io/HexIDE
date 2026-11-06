using System.Globalization;

namespace HexIDE.Runtime.BuiltinControls;

// Runtime object model measured in #1088, issuecomment-6428845889. Writing designer edits belongs to a later slice.
public sealed class ToolbarItems(VBToolbar toolbar, ToolbarItem? parent = null) : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    internal readonly List<ToolbarItem> Items = [];
    public string TypeName => parent is null ? "IButtons" : "IButtonMenus";

    internal static NotImplementedException Unmeasured() => new("Toolbar item conversions or geometry are not implemented (#1088)");
    internal static VBRunTimeException Error(int number) => number switch
    {
        380 => new(VBStandardError.InvalidPropertyValue),
        383 => new(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty),
        _ => new(new VBStandardError(number, number switch
        {
            35600 => "Index out of bounds",
            35601 => "Element not found",
            35602 => "Key is not unique in collection",
            35603 => "Invalid key",
            35610 => "Invalid object",
            35613 => "ImageList must be initialized before it can be used",
            35700 => "Circular object referencing is not allowed",
            _ => throw Unmeasured(),
        })),
    };

    internal static long Number(Vb6Value value) => value.Type == Vb6Value.ValueType.Integer || value.Type == Vb6Value.ValueType.Long
        ? Convert.ToInt64(value.Value, CultureInfo.InvariantCulture) : throw Unmeasured();
    internal static string Text(Vb6Value value) => value.Type == Vb6Value.ValueType.String ? (string)value.Value! : throw Unmeasured();
    internal static bool Boolean(Vb6Value value) => value.Type == Vb6Value.ValueType.Boolean ? (bool)value.Value! : throw Unmeasured();

    internal void ValidateKey(string key, ToolbarItem? except = null)
    {
        if (key.Length == 0) return;
        if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw Error(35603);
        if (Items.Any(i => i != except && i.Key == key)) throw Error(35602);
    }

    private int Position(Vb6Value value, bool adding = false)
    {
        if (!adding && value.Type == Vb6Value.ValueType.String)
        {
            var key = Text(value);
            if (key.Length == 0) throw Error(35603);
            var found = Items.FindIndex(i => i.Key == key);
            return found >= 0 ? found : throw Error(35601);
        }
        double number;
        if (value.Type == Vb6Value.ValueType.Single || value.Type == Vb6Value.ValueType.Double)
            number = Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
        else number = Number(value);
        var position = Math.Round(number, MidpointRounding.ToEven);
        return position >= 1 && position <= Items.Count + (adding ? 1 : 0) ? (int)position - 1 : throw Error(35600);
    }

    public Vb6Value Item(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1) throw Unmeasured();
        return new(Items[Position(args[0])]);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (!name.Equals("Count", StringComparison.OrdinalIgnoreCase)) return false;
        value = new((int)Items.Count);
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase)) throw Error(383);
        return false;
    }
    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw Unmeasured();
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        var maximum = method.ToLowerInvariant() switch { "add" => parent is null ? 5 : 3, "item" or "remove" => 1, _ => 0 };
        if (args.Count > maximum || (method.ToLowerInvariant() is "item" or "remove" && (args.Count != 1 || args[0].IsMissing)))
            throw Unmeasured();
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count": result = new Vb6Value((int)Items.Count); break;
            case "item": result = Item(args); break;
            case "remove": Items.RemoveAt(Position(args.Single())); break;
            case "clear": Items.Clear(); break;
            case "add":
                Vb6Value Arg(int index) => index < args.Count ? args[index] : Vb6Value.Missing;
                var at = Arg(0).IsMissing ? Items.Count : Position(Arg(0), true);
                var key = Arg(1).IsMissing ? "" : Text(Arg(1));
                ValidateKey(key);
                var text = Arg(2).IsMissing ? "" : Text(Arg(2));
                var style = parent is not null || Arg(3).IsMissing ? 0 : Number(Arg(3));
                if (style is < 0 or > 5) throw Error(380);
                if (parent is null && !Arg(4).IsMissing && toolbar.ImageList is null) throw Error(35613);
                var image = parent is null && !Arg(4).IsMissing ? toolbar.ValidateImage(Arg(4)) : new Vb6Value(0);
                var item = new ToolbarItem(this, parent, key, text, style) { Image = image };
                Items.Insert(at, item);
                if (parent is null) toolbar.HasHadButtons = true;
                result = new Vb6Value(item);
                break;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        if (parent is null && method.ToLowerInvariant() is "add" or "remove" or "clear") Refresh();
        return Task.FromResult(result);
    }

    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        required = member.ToLowerInvariant() is "item" or "remove" ? 1 : 0;
        names = member.ToLowerInvariant() switch
        {
            "add" => parent is null ? ["Index", "Key", "Caption", "Style", "Image"] : ["Index", "Key", "Text"],
            "item" or "remove" => ["Index"],
            _ => [],
        };
        return true;
    }

    internal static int FileInteger(object value) => value is int number ? number : throw Unmeasured();
    internal static bool FileBoolean(object value) => FileInteger(value) switch
    {
        0 => false,
        -1 => true,
        _ => throw Unmeasured(),
    };

    // The persisted property bag uses Object.* aliases and numeric image indexes (#1088).
    internal void Load(Dictionary<string, object> bag)
    {
        static string FileText(object field) => field is string text ? text : throw Unmeasured();
        var countName = parent is null ? "NumButtons" : "NumButtonMenus";
        var prefix = parent is null ? "Button" : "ButtonMenu";
        var count = bag.TryGetValue(countName, out var size) ? FileInteger(size) : 0;
        for (var index = 1; index <= count; index++)
        {
            if (!bag.TryGetValue(prefix + index, out var entry) || entry is not Dictionary<string, object> fields)
                throw Unmeasured();
            string TextField(string name) => fields.TryGetValue(name, out var field) ? FileText(field) : "";
            var style = fields.TryGetValue("Style", out var kind) ? FileInteger(kind) : 0;
            if (style is < 0 or > 5) throw Error(380);
            var key = TextField("Key");
            ValidateKey(key);
            var item = new ToolbarItem(this, parent, key, TextField(parent is null ? "Caption" : "Text"), style);
            Items.Add(item);
            foreach (var (name, field) in fields)
            {
                var property = name.StartsWith("Object.", StringComparison.OrdinalIgnoreCase) ? name[7..] : name;
                switch (property.ToLowerInvariant())
                {
                    case "enabled": case "visible": case "mixedstate":
                        item.TrySetProperty(property, new Vb6Value(FileBoolean(field))); break;
                    case "description": case "tooltiptext": case "tag":
                        item.TrySetProperty(property, new Vb6Value(FileText(field))); break;
                    case "value": case "width":
                        item.TrySetProperty(property, new Vb6Value((long)FileInteger(field))); break;
                    case "imageindex": item.Image = new Vb6Value(FileInteger(field)); break;
                    case "buttonmenus": item.LoadMenus(field as Dictionary<string, object> ?? throw Unmeasured()); break;
                }
            }
        }
        if (parent is null && count > 0) toolbar.HasHadButtons = true;
    }

    internal Vb6Value ValidateImage(Vb6Value image) => toolbar.ValidateImage(image);
    internal void Refresh() => toolbar.RefreshButtons();

    internal float ButtonWidth => toolbar.ButtonWidth;
    internal float DropdownWidth => toolbar.DropdownWidth;
    internal float ButtonHeight => toolbar.ButtonHeight;
    private (float Left, float Top, float Height) Position(ToolbarItem? item)
    {
        float left = 0, top = toolbar.ButtonTop;
        foreach (var button in Items)
        {
            if (!button.IsVisible) continue;
            if (toolbar.Wrappable && !double.IsNaN(toolbar.Width) && left > 0 && left + button.LayoutWidth > toolbar.Width * 15)
            {
                left = 0;
                top += ButtonHeight;
            }
            if (button == item) return (left, top, 0);
            left += button.LayoutWidth;
        }
        if (item is not null) throw Unmeasured();
        return (0, 0, top - toolbar.ButtonTop + ButtonHeight);
    }
    internal float Left(ToolbarItem item) => Position(item).Left;
    internal float Top(ToolbarItem item) => Position(item).Top;
    internal float RowHeight => Position(null).Height;

    internal ToolbarItems Menus(ToolbarItem item) => new(toolbar, item);
}

public sealed class ToolbarItem : ICSharpProxy, ICSharpPropertyBag
{
    private readonly ToolbarItems owner;
    private readonly ToolbarItem? parent;
    private readonly ToolbarItems? menus;
    internal string Key;
    internal Vb6Value Image = new(0);
    internal void LoadMenus(Dictionary<string, object> bag) => menus!.Load(bag);
    private string text, tag = "", description = "", toolTip = "";
    private long style, value;
    private bool enabled = true, visible = true, mixedState;
    private float width;
    internal bool IsVisible => visible;
    internal bool HasCaptionSpace => style is 0 or 1 or 2 or 5;
    internal string Caption => text;
    internal float LayoutWidth => style switch
    {
        3 => 120f,
        4 => width,
        5 => owner.ButtonWidth + owner.DropdownWidth,
        _ => owner.ButtonWidth,
    };
    public string TypeName => parent is null ? "IButton" : "IButtonMenu";
    internal Vb6Value DefaultValue => new(text);
    internal void SetDefault(Vb6Value next) => TrySetProperty(parent is null ? "Caption" : "Text", next);

    internal ToolbarItem(ToolbarItems owner, ToolbarItem? parent, string key, string text, long style)
    {
        this.owner = owner;
        this.parent = parent;
        Key = key;
        this.text = text;
        this.style = style;
        if (parent is null) menus = owner.Menus(this);
    }

    public void Call(string method, List<Vb6Value> args) => throw ToolbarItems.Unmeasured();
    public bool TryGetProperty(string name, out Vb6Value result)
    {
        result = name.ToLowerInvariant() switch
        {
            "index" => new((int)(owner.Items.IndexOf(this) + 1)),
            "key" => new(Key),
            "enabled" => new(enabled),
            "visible" => new(visible),
            "tag" => new(tag),
            "text" when parent is not null => new(text),
            "parent" when parent is not null => new(parent),
            "caption" when parent is null => new(text),
            "description" when parent is null => new(description),
            "tooltiptext" when parent is null => new(toolTip),
            "mixedstate" when parent is null => new(mixedState),
            "style" when parent is null => new(style),
            "value" when parent is null => new(value),
            "image" when parent is null => Image,
            "buttonmenus" when parent is null => new(menus!),
            "width" when parent is null && !visible => new(0f),
            "height" when parent is null && !visible => new(0f),
            "width" when parent is null && style == 4 => new(width),
            "width" when parent is null => new(LayoutWidth),
            "height" when parent is null && style == 0 => new(owner.ButtonHeight),
            "height" when parent is null => throw ToolbarItems.Unmeasured(),
            "left" when parent is null && visible => new(owner.Left(this)),
            "top" when parent is null && visible && style == 0 => new(owner.Top(this)),
            "left" or "top" when parent is null => throw ToolbarItems.Unmeasured(),
            _ => default,
        };
        return result != default;
    }

    public bool TrySetProperty(string name, Vb6Value next)
    {
        var changed = SetProperty(name, next);
        if (changed) owner.Refresh();
        return changed;
    }

    private bool SetProperty(string name, Vb6Value next)
    {
        switch (name.ToLowerInvariant())
        {
            case "index": case "left": case "top": case "height": throw ToolbarItems.Error(383);
            case "key":
                var key = ToolbarItems.Text(next);
                owner.ValidateKey(key, this);
                Key = key; return true;
            case "enabled": enabled = ToolbarItems.Boolean(next); return true;
            case "visible": visible = ToolbarItems.Boolean(next); return true;
            case "tag":
                if (next.Value is VBToolbar && parent is null) throw ToolbarItems.Error(35700);
                if (next.Type == Vb6Value.ValueType.Control || next.Type == Vb6Value.ValueType.Object || next.Value is ICSharpProxy)
                    throw ToolbarItems.Unmeasured();
                tag = ToolbarItems.Text(next); return true;
            case "text" when parent is not null: text = ToolbarItems.Text(next); return true;
            case "caption" when parent is null:
                text = next.Type == Vb6Value.ValueType.Integer || next.Type == Vb6Value.ValueType.Long
                    ? ToolbarItems.Number(next).ToString(CultureInfo.InvariantCulture) : ToolbarItems.Text(next); return true;
            case "description" when parent is null: description = ToolbarItems.Text(next); return true;
            case "tooltiptext" when parent is null: toolTip = ToolbarItems.Text(next); return true;
            case "mixedstate" when parent is null: mixedState = ToolbarItems.Boolean(next); return true;
            case "style" when parent is null:
                var newStyle = ToolbarItems.Number(next);
                if (newStyle is < 0 or > 5) throw ToolbarItems.Error(380);
                style = newStyle; return true;
            case "value" when parent is null:
                var newValue = ToolbarItems.Number(next);
                if (newValue is < 0 or > 1) throw ToolbarItems.Error(380);
                if (style == 2 && newValue == 1)
                {
                    var index = owner.Items.IndexOf(this);
                    for (var i = index - 1; i >= 0 && owner.Items[i].style == 2; i--) owner.Items[i].value = 0;
                    for (var i = index + 1; i < owner.Items.Count && owner.Items[i].style == 2; i++) owner.Items[i].value = 0;
                }
                value = newValue; return true;
            case "image" when parent is null:
                Image = owner.ValidateImage(next);
                return true;
            case "width" when parent is null:
                if (style != 4) throw ToolbarItems.Error(383);
                width = ToolbarItems.Number(next); return true;
        }
        return false;
    }
}
