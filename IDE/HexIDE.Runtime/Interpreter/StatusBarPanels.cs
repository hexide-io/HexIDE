using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

// vb6.exe SP6, native and p-code: #1087, issuecomment-6427372779; types: issuecomment-6428228813.
public sealed class StatusBarPanels : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    internal readonly VBStatusBar Owner;
    internal readonly List<StatusBarPanel> Items = [];

    internal StatusBarPanels(VBStatusBar owner)
    {
        Owner = owner;
        Items.Add(new StatusBarPanel(this));
    }

    internal void Load(IReadOnlyDictionary<string, object> properties, IReadOnlyDictionary<int, byte[]>? pictures)
    {
        var bag = properties.FirstOrDefault(p => p.Key.Equals("Panels", StringComparison.OrdinalIgnoreCase)).Value
            as Dictionary<string, object>;
        if (bag == null) return;
        Items.Clear();
        var count = Convert.ToInt32(bag["NumPanels"], System.Globalization.CultureInfo.InvariantCulture);
        for (var i = 1; i <= count; i++)
        {
            var fields = (Dictionary<string, object>)bag["Panel" + i];
            var panel = new StatusBarPanel(this);
            Items.Add(panel);
            panel.Load(fields, pictures);
        }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase))
        {
            value = new Vb6Value((short)Items.Count);
            return true;
        }
        return false;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("Count", StringComparison.OrdinalIgnoreCase))
            throw Error(383);
        return false;
    }

    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("Panels calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        var expected = method.ToLowerInvariant() switch { "add" => 5, "item" or "remove" => 1, _ => 0 };
        if (args.Count > expected) throw new NotImplementedException("This Panels argument count is unmeasured (#1087)");
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "add":
            {
                var index = Argument(args, 0);
                var key = Argument(args, 1).IsMissing ? "" : Text(Argument(args, 1));
                CheckKey(key, null);
                var at = index.IsMissing ? Items.Count : PositionNumber(index) - 1;
                if (at < 0 || at > Items.Count) throw Error(35600);
                var style = Argument(args, 3).IsMissing ? 0 : Choice(Argument(args, 3), 7);
                var panel = new StatusBarPanel(this) { Key = key, Style = style,
                    Text = Argument(args, 2).IsMissing ? "" : Text(Argument(args, 2)) };
                if (!Argument(args, 4).IsMissing) panel.SetPicture(Argument(args, 4));
                Items.Insert(at, panel);
                result = new Vb6Value(panel);
                break;
            }
            case "item": result = new Vb6Value(Item(args)); break;
            case "remove": Items.Remove(Item(args)); break;
            case "clear": Items.Clear(); break;
            case "count": TryGetProperty("Count", out var count); result = count; break;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        if (method.ToLowerInvariant() is "add" or "remove" or "clear")
        {
            Owner.LayoutPanels(true);
            Owner.InvalidateVisual();
        }
        return Task.FromResult(result);
    }

    internal StatusBarPanel Item(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1) throw new NotImplementedException("Panels.Item argument count is unmeasured (#1087)");
        var index = args[0];
        if (index.Type == Vb6Value.ValueType.String)
        {
            var key = (string)index.Value!;
            if (key.Length == 0) throw Error(35603);
            if (Items.FirstOrDefault(p => p.Key == key) is { } found) return found;
            RefuseUnmeasuredKeyCase(key);
            throw Error(35601);
        }
        var at = PositionNumber(index) - 1;
        return at >= 0 && at < Items.Count ? Items[at] : throw Error(35600);
    }

    internal void CheckKey(string key, StatusBarPanel? except)
    {
        if (key.Length == 0) return;
        if (double.TryParse(key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw Error(35603);
        if (Items.Any(p => p != except && p.Key == key)) throw Error(35602);
        RefuseUnmeasuredKeyCase(key);
    }

    private void RefuseUnmeasuredKeyCase(string key)
    {
        if (Items.Any(p => p.Key != key && p.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException("Panels key case comparison is unmeasured (#1087)");
    }

    private static int PositionNumber(Vb6Value value)
    {
        if (!Vb6Value.TryNumericToDouble(value, out var number) || number < int.MinValue || number > int.MaxValue)
            throw new NotImplementedException("This Panels index conversion is unmeasured (#1087)");
        return (int)Math.Round(number, MidpointRounding.ToEven);
    }

    internal static long Choice(Vb6Value value, int max)
    {
        if (!Vb6Value.TryNumericToDouble(value, out var number) || number != Math.Truncate(number))
            throw new NotImplementedException("This StatusBar enum conversion is unmeasured (#1087)");
        if (number < 0) throw new NotImplementedException("Negative StatusBar enum values are unmeasured (#1087)");
        if (number > max) throw Error(380);
        return (long)number;
    }

    internal static string Text(Vb6Value value) => VbDisplay.ToText(value);
    internal static VBRunTimeException Error(int number) => new(new VBStandardError(number, number switch
    {
        380 => "Invalid property value",
        383 => "Property is read-only",
        35600 => "Index out of bounds",
        35601 => "Element not found",
        35602 => "Key is not unique in collection",
        35603 => "Invalid key",
        35700 => "Circular object referencing is not allowed",
        _ => VBStandardError.ObjectDoesntSupportThisPropertyOrMethod.Description,
    }));
    private static Vb6Value Argument(IReadOnlyList<Vb6Value> args, int i) => i < args.Count ? args[i] : Vb6Value.Missing;

    bool INamedParameterSource.TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        (IReadOnlyList<string> Names, int Required) parameters = member.ToLowerInvariant() switch
        {
            "add" => (["Index", "Key", "Text", "Style", "Picture"], 0),
            "item" or "remove" => (["Index"], 1),
            _ => ([], 0),
        };
        (names, required) = parameters;
        return member.ToLowerInvariant() is "add" or "item" or "remove" or "clear" or "count";
    }
}

public sealed class StatusBarPanel(StatusBarPanels panels) : ICSharpProxy, ICSharpPropertyBag
{
    internal string Key = "";
    internal string Text = "";
    internal long Style;
    private long alignment, bevel = 1;
    internal long Alignment => alignment;
    internal long Bevel => bevel;
    internal bool Enabled => enabled;
    internal long autoSize;
    private bool enabled = true;
    internal bool visible = true;
    private string toolTipText = "";
    private Vb6Value tag = new("");
    internal float width = 1440, minWidth = 1440;

    private byte[]? pictureRecord;
    private PanelPicture? picture;
    internal PanelPicture? Picture => picture ?? (pictureRecord == null ? null : picture = PanelPicture.FromRecord(pictureRecord));

    internal PanelPicture? DrawingPicture
    {
        get
        {
            try { return Picture; }
            catch (NotImplementedException) { return null; } // Preserve opaque saved records as before.
        }
    }

    internal void Load(IReadOnlyDictionary<string, object> fields, IReadOnlyDictionary<int, byte[]>? pictures)
    {
        // Saved lengths are whole HIMETRIC, not twips. Set fields directly: MinWidth's runtime
        // setter would replace a separately saved Width (#1087, issuecomment-6427372779).
        foreach (var (name, value) in fields)
        {
            switch (name.ToLowerInvariant())
            {
                case "object.width": width = Convert.ToSingle(value) * 1440 / 2540; break;
                case "minwidth": minWidth = Convert.ToSingle(value) * 1440 / 2540; break;
                case "autosize": autoSize = Convert.ToInt64(value); break;
                case "textsave": break; // A saved display snapshot is not the panel's Text.
                case "picture":
                    var record = pictures == null ? null : Serialization.FrxDeserializer.TryExtractBlob((string)value, pictures);
                    if (record == null) throw new NotImplementedException("The saved Panel.Picture record is missing (#1087)");
                    pictureRecord = record;
                    break;
                default:
                    TrySetProperty(name.StartsWith("Object.", StringComparison.OrdinalIgnoreCase) ? name[7..] : name,
                        value switch { string text => new Vb6Value(text), int number => new Vb6Value(number),
                            _ => throw new NotImplementedException("This saved panel value is not implemented (#1087)") });
                    break;
            }
        }
    }

    public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        RefuseRemoved();
        value = name.ToLowerInvariant() switch
        {
            "text" => new(Text), "key" => new(Key), "tooltiptext" => new(toolTipText), "tag" => tag,
            "style" => new(Style), "alignment" => new(alignment), "autosize" => new(autoSize), "bevel" => new(bevel),
            "enabled" => new(enabled), "visible" => new(visible),
            "index" => new((short)(panels.Items.IndexOf(this) + 1)),
            "width" => new(CurrentWidth()), "minwidth" => new(minWidth),
            "left" => new(Left()), "picture" => Picture == null ? Vb6Value.Nothing : new Vb6Value(Picture),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        RefuseRemoved();
        switch (name.ToLowerInvariant())
        {
            case "text":
                Text = StatusBarPanels.Text(value);
                break;
            case "key":
                var key = StatusBarPanels.Text(value);
                panels.CheckKey(key, this);
                Key = key;
                break;
            case "tooltiptext": toolTipText = StatusBarPanels.Text(value); break;
            case "tag":
                if (ReferenceEquals(value.Value, panels.Owner)) throw StatusBarPanels.Error(35700);
                if (value.Value is ICSharpProxy or Avalonia.Controls.Control or VbObject)
                    throw new NotImplementedException("Panel.Tag object ownership is not implemented (#1087)");
                tag = value;
                break;
            case "style": Style = StatusBarPanels.Choice(value, 7); break;
            case "alignment": alignment = StatusBarPanels.Choice(value, 2); break;
            case "bevel": bevel = StatusBarPanels.Choice(value, 2); break;
            case "autosize":
                var size = StatusBarPanels.Choice(value, 2);
                if (autoSize == 2 && Text.Length != 0) CurrentWidth();
                if (autoSize == 1) CurrentWidth();
                autoSize = size;
                panels.Owner.LayoutPanels(true);
                if (autoSize == 2 && Text.Length == 0) width = minWidth;
                break;
            case "enabled": enabled = (bool)VB6BuiltIns.CBool(value).Value!; break;
            case "visible": visible = (bool)VB6BuiltIns.CBool(value).Value!; panels.Owner.LayoutPanels(true); break;
            case "width":
                var length = Length(value);
                if (autoSize != 0) throw new NotImplementedException("Setting AutoSize Width is deferred to #1087 part B");
                width = length;
                panels.Owner.LayoutPanels(true);
                break;
            case "minwidth":
                var minimum = Length(value);
                panels.Owner.LayoutPanels();
                minWidth = width = minimum;
                break;
            case "left" or "index": throw StatusBarPanels.Error(383);
            case "picture": SetPicture(value); break;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        panels.Owner.InvalidateVisual();
        return true;
    }

    internal void SetPicture(Vb6Value value)
    {
        if (value.Value is PanelPicture assigned) picture = assigned;
        else if (value.Type == Vb6Value.Nothing.Type && value.Value == null) picture = null;
        else throw new NotImplementedException("This Panel.Picture assignment is unmeasured (#1087)");
        pictureRecord = null;
    }

    internal float CurrentWidth()
    {
        if (autoSize == 2)
        {
            if (Text.Length != 0)
                throw new NotImplementedException("Nonempty Contents Width is deferred to #1087 part B");
            width = minWidth;
        }
        panels.Owner.LayoutPanels();
        return width;
    }

    private void RefuseRemoved()
    {
        if (!panels.Items.Contains(this))
            throw new NotImplementedException("Access to a removed panel is unmeasured (#1087)");
    }

    internal void SetText(Vb6Value value) => TrySetProperty("Text", value);

    private float Left()
    {
        panels.Owner.LayoutPanels();
        double pixels = 1;
        foreach (var panel in panels.Items)
        {
            if (ReferenceEquals(panel, this)) return (float)(Math.Round(pixels * 2540 / 96) * 1440 / 2540);
            if (panel.visible) pixels += Math.Floor(panel.CurrentWidth() / 15.0) + 1;
        }
        throw new NotImplementedException("Reading a removed panel's Left is unmeasured (#1087)");
    }

    private static float Length(Vb6Value value)
    {
        if (!Vb6Value.TryNumericToDouble(value, out var number) || !double.IsFinite(number))
            throw new NotImplementedException("This panel length conversion is unmeasured (#1087)");
        if (number <= 0) throw StatusBarPanels.Error(380);
        var himetric = number * 2540 / 1440;
        if (!double.IsFinite(himetric) || himetric > int.MaxValue || himetric < 0.5
            || himetric - Math.Floor(himetric) == 0.5)
            throw new NotImplementedException("This panel length's HIMETRIC conversion is unmeasured (#1087)");
        return (float)(Math.Round(himetric) * 1440 / 2540);
    }
}
