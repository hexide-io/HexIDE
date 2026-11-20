using HexIDE.Runtime.BuiltinControls;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;

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

    public async Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        var name = method.ToLowerInvariant();
        var expected = name switch { "add" => 5, "item" or "remove" => 1, "clear" or "count" => 0, _ => -1 };
        if (expected >= 0 && args.Count > expected) throw Error(name == "count" ? 451 : 450);
        if (expected < 0 && args.Count != 0)
            throw new NotImplementedException("This Panels argument count is unmeasured (#4882)");
        Vb6Value? result = null;
        switch (name)
        {
            case "add":
            {
                var index = Argument(args, 0);
                var key = Argument(args, 1).IsMissing ? "" : Text(Argument(args, 1));
                CheckKey(key, null);
                var at = index.IsMissing ? Items.Count : PositionNumber(index, adding: true) - 1L;
                if (at < 0 || at > Items.Count) throw Error(35600);
                var style = Argument(args, 3).IsMissing ? 0 : PanelChoice(Argument(args, 3), 7, adding: true);
                var panel = new StatusBarPanel(this) { Key = key, Style = style,
                    Text = Argument(args, 2).IsMissing ? "" : Text(Argument(args, 2)) };
                if (!Argument(args, 4).IsMissing) panel.SetPicture(Argument(args, 4), adding: true);
                Items.Insert((int)at, panel);
                result = new Vb6Value(panel);
                break;
            }
            case "item": result = new Vb6Value(Item(args)); break;
            case "remove":
            {
                var panel = Item(args);
                Items.Remove(panel);
                await panel.ReleaseTagIfUnowned();
                break;
            }
            case "clear":
            {
                var removed = Items.ToArray();
                Items.Clear();
                foreach (var panel in removed)
                    await panel.ReleaseTagIfUnowned();
                break;
            }
            case "count": TryGetProperty("Count", out var count); result = count; break;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        if (name is "add" or "remove" or "clear")
        {
            Owner.LayoutPanels(true);
            Owner.InvalidateVisual();
        }
        return result;
    }

    internal StatusBarPanel Item(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1 || args[0].IsMissing) throw Error(450);
        var index = args[0];
        if (index.Type == Vb6Value.ValueType.String)
        {
            var key = (string)index.WithoutOddBytes("StatusBarPanels text use").Value!;
            if (key.Length == 0) throw Error(35603);
            if (Items.FirstOrDefault(p => p.Key == key) is { } found) return found;
            throw Error(35601);
        }
        var at = PositionNumber(index, adding: false) - 1L;
        return at >= 0 && at < Items.Count ? Items[(int)at] : throw Error(35600);
    }

    internal void CheckKey(string key, StatusBarPanel? except)
    {
        if (key.Length == 0) return;
        if (double.TryParse(key, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            throw Error(35603);
        if (Items.Any(p => p != except && p.Key == key)) throw Error(35602);
    }

    // Late-bound Panel conversions and errors measured in #4882 (SP6 native and p-code).
    private static int PositionNumber(Vb6Value value, bool adding)
    {
        if (value.Type == VT.EmptyVariant)
            throw Error(adding ? 35600 : 35601);
        if (value.Type == VT.Error)
            throw Error(adding ? 13 : 35601);
        if (value.IsNull || value.Type.IsArray || value.Type == VT.Object && value.Value == null)
            throw Error(adding ? 13 : 35603);
        if (value.Type == VT.Boolean)
            throw Error(35600);
        if (!Vb6Value.TryNumericToDouble(value, out var number))
            throw new NotImplementedException("This Panels index conversion is unmeasured (#4882)");
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw Error(adding ? 13 : 35603);
        return (int)rounded;
    }

    internal static long PanelChoice(Vb6Value value, int max, bool adding = false)
    {
        var number = PanelNumber(value, VT.Long);
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (!double.IsFinite(rounded) || rounded < int.MinValue || rounded > int.MaxValue)
            throw Error(adding ? 13 : 6);
        if (rounded < 0 || rounded > max) throw Error(380);
        return (long)rounded;
    }

    internal static double PanelNumber(Vb6Value value, VT target)
    {
        if (value.Type == VT.EmptyVariant) return 0;
        if (value.Type == VT.Boolean && (target == VT.Long || value.Value is true))
            return (bool)value.Value! ? -1 : 0;
        if (Vb6Value.TryNumericToDouble(value, out var number)) return number;
        if (value.Type == VT.String)
        {
            // Use the VB parser for locale and radix notation without narrowing before the panel's own checks.
            return VB6BuiltIns.ToNum(value, target, allowCurrency: target == VT.Long);
        }
        if (value.IsNull || value.Type == VT.Error || target == VT.Single && value.Type.IsArray
            || value.Type == VT.Object && value.Value == null)
            throw Error(13);
        throw new NotImplementedException(target == VT.Single
            ? "This panel length conversion is unmeasured (#4882)"
            : "This StatusBar enum conversion is unmeasured (#4882)");
    }

    internal static long Choice(Vb6Value value, int max)
    {
        if (!Vb6Value.TryNumericToDouble(value, out var number) || number != Math.Truncate(number))
            throw new NotImplementedException("This StatusBar enum conversion is unmeasured (#4882)");
        if (number < 0) throw new NotImplementedException("Negative StatusBar enum values are unmeasured (#4882)");
        if (number > max) throw Error(380);
        return (long)number;
    }

    internal static string Text(Vb6Value value) => VbDisplay.ToText(value);
    internal static VBRunTimeException Error(int number) => new(new VBStandardError(number, number switch
    {
        6 => VBStandardError.Overflow.Description,
        13 => VBStandardError.TypeMismatch.Description,
        450 => VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment.Description,
        451 => VBStandardError.ObjectNotACollection.Description,
        380 => "Invalid property value",
        383 => "Property is read-only",
        35600 => "Index out of bounds",
        35601 => "Element not found",
        35602 => "Key is not unique in collection",
        35603 => "Invalid key",
        35605 => "This item's control has been deleted",
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
    private readonly StatusBarPanels collection = panels;

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
    private BasicInterpreter? tagInterpreter;
    internal int References;

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
                    if (record == null) throw new NotImplementedException("The saved Panel.Picture record is missing (#4882)");
                    pictureRecord = record;
                    break;
                default:
                    TrySetProperty(name.StartsWith("Object.", StringComparison.OrdinalIgnoreCase) ? name[7..] : name,
                        value switch { string text => new Vb6Value(text), int number => new Vb6Value(number),
                            _ => throw new NotImplementedException("This saved panel value is not implemented (#4882)") });
                    break;
            }
        }
    }

    public void Call(string method, List<Vb6Value> args) => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        var member = name.ToLowerInvariant();
        if (member is "index" or "width" or "minwidth" or "left") RequireAttached();
        value = member switch
        {
            "text" => new(Text), "key" => new(Key), "tooltiptext" => new(toolTipText), "tag" => tag,
            "style" => new(Style), "alignment" => new(alignment), "autosize" => new(autoSize), "bevel" => new(bevel),
            "enabled" => new(enabled), "visible" => new(visible),
            "index" => new((short)(collection.Items.IndexOf(this) + 1)),
            "width" => new(CurrentWidth()), "minwidth" => new(minWidth),
            "left" => new(Left()), "picture" => Picture == null ? Vb6Value.Nothing : new Vb6Value(Picture),
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        var member = name.ToLowerInvariant();
        if (member is "text" or "style" or "alignment" or "bevel" or "autosize" or "enabled" or "visible"
            or "width" or "minwidth" or "picture") RequireAttached();
        switch (member)
        {
            case "text":
                Text = StatusBarPanels.Text(value);
                break;
            case "key":
                var key = StatusBarPanels.Text(value);
                collection.CheckKey(key, this);
                Key = key;
                break;
            case "tooltiptext": toolTipText = StatusBarPanels.Text(value); break;
            case "tag":
                if (ReferenceEquals(value.Value, collection.Owner) || ReferenceEquals(value.Value, collection)
                    || value.Value is StatusBarPanel panel && ReferenceEquals(panel.collection, collection))
                    throw StatusBarPanels.Error(35700);
                if (value.Value is ICSharpProxy or Avalonia.Controls.Control or VbObject
                    && value.Value is not (VbCollection or VBStatusBar or StatusBarPanels or StatusBarPanel
                        or PanelPicture or FormControls or AvaloniaInterop.ControlFont or VBLabel
                        or VbObject { ClassDef.Kind: InterpreterModuleKind.Form }))
                    throw new NotImplementedException("Panel.Tag object ownership is not implemented (#4882)");
                tag = value;
                break;
            case "style": Style = StatusBarPanels.PanelChoice(value, 7); break;
            case "alignment": alignment = StatusBarPanels.PanelChoice(value, 2); break;
            case "bevel": bevel = StatusBarPanels.PanelChoice(value, 2); break;
            case "autosize":
                var size = StatusBarPanels.PanelChoice(value, 2);
                if (autoSize == 2 && Text.Length != 0) CurrentWidth();
                if (autoSize == 1) CurrentWidth();
                autoSize = size;
                collection.Owner.LayoutPanels(true);
                if (autoSize == 2 && Text.Length == 0) width = minWidth;
                break;
            case "enabled": enabled = (bool)VB6BuiltIns.CBool(value).Value!; break;
            case "visible": visible = (bool)VB6BuiltIns.CBool(value).Value!; collection.Owner.LayoutPanels(true); break;
            case "width":
                var length = Length(value);
                if (autoSize != 0) throw new NotImplementedException("Setting AutoSize Width is deferred to #1087 part B");
                width = length;
                collection.Owner.LayoutPanels(true);
                break;
            case "minwidth":
                var minimum = Length(value);
                collection.Owner.LayoutPanels();
                minWidth = width = minimum;
                break;
            case "left" or "index": throw StatusBarPanels.Error(383);
            case "picture": SetPicture(value); break;
            default: throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        }
        collection.Owner.InvalidateVisual();
        return true;
    }

    internal async Task SetTag(BasicInterpreter interpreter, Vb6Value value)
    {
        var old = tag;
        var oldInterpreter = tagInterpreter;
        TrySetProperty("Tag", value);
        if (value.Value is VbCollection or VbObject or StatusBarPanel)
        {
            interpreter.AddRef(value);
            tagInterpreter = interpreter;
        }
        else
            tagInterpreter = null;
        if (oldInterpreter is not null)
            await oldInterpreter.ReleaseRef(old);
    }

    internal async Task ReleaseTagIfUnowned()
    {
        if (References != 0 || collection.Items.Contains(this) || tagInterpreter is not { } interpreter)
            return;
        var old = tag;
        tag = new Vb6Value("");
        tagInterpreter = null;
        await interpreter.ReleaseRef(old);
    }

    internal void SetPicture(Vb6Value value, bool adding = false)
    {
        if (value.Value is PanelPicture assigned) picture = assigned;
        else if (value.Type == Vb6Value.Nothing.Type && value.Value == null) picture = null;
        else if (adding) throw new NotImplementedException("This Panel.Picture assignment is unmeasured (#4882)");
        else throw StatusBarPanels.Error(13);
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
        collection.Owner.LayoutPanels();
        return width;
    }

    private void RequireAttached()
    {
        if (!collection.Items.Contains(this))
            throw StatusBarPanels.Error(35605);
    }

    internal void SetText(Vb6Value value) => TrySetProperty("Text", value);

    private float Left()
    {
        collection.Owner.LayoutPanels();
        double pixels = 1;
        foreach (var panel in collection.Items)
        {
            if (ReferenceEquals(panel, this)) return (float)(Math.Round(pixels * 2540 / 96) * 1440 / 2540);
            if (panel.visible) pixels += Math.Floor(panel.CurrentWidth() / 15.0) + 1;
        }
        throw StatusBarPanels.Error(35605);
    }

    private static float Length(Vb6Value value)
    {
        var number = (float)StatusBarPanels.PanelNumber(value, VT.Single);
        if (!float.IsFinite(number)) throw StatusBarPanels.Error(6);
        if (number > 1e7f)
            throw new NotImplementedException("Panel lengths above 1E+7 twips are unmeasured (#4882)");
        // The measured limit is 16384 pixels at 15 twips per pixel, before HIMETRIC quantization.
        if (number <= 0 || number >= 16384 * 15) throw StatusBarPanels.Error(380);
        var himetric = (double)number * 2540 / 1440;
        var units = Math.Ceiling(himetric - 0.5);
        if (units == 0) throw StatusBarPanels.Error(380);
        // The Single scale reproduces the measured last digit at 63 and 444 HIMETRIC.
        return (float)units * (1440f / 2540);
    }
}
