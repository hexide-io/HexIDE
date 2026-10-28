using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Components.InBox;

internal sealed class TabStripTabs(VBTabStrip owner) : ICSharpAsyncProxy, ICSharpPropertyBag, INamedParameterSource
{
    private readonly Dictionary<TabStripModel.Tab, TabStripTab> wrappers = [];
    internal VBTabStrip Owner => owner;
    internal TabStripTab Wrap(TabStripModel.Tab tab)
    {
        if (!wrappers.TryGetValue(tab, out var wrapper))
            wrappers[tab] = wrapper = new TabStripTab(owner, tab);
        return wrapper;
    }
    internal Vb6Value Item(Vb6Value index) => new(Wrap(owner.Model.Item(index)));
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException();
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        Vb6Value? result = null;
        switch (method.ToLowerInvariant())
        {
            case "count": result = new Vb6Value(owner.Model.Tabs.Count); break;
            case "item": result = Item(args[0]); break;
            case "clear": owner.Model.Clear(); break;
            case "remove": owner.Model.Remove(args[0]); break;
            case "add":
                var index = args.Count > 0 && !args[0].IsMissing ? Index(args[0]) : (int?)null;
                var key = args.Count > 1 && !args[1].IsMissing ? Text(args[1]) : "";
                var caption = args.Count > 2 && !args[2].IsMissing ? Text(args[2]) : "";
                if (args.Count > 3 && !args[3].IsMissing)
                    throw new VBRunTimeException(new VBStandardError(35613, "ImageList must be initialized before it can be used"));
                result = new Vb6Value(Wrap(owner.Model.Add(index, key, caption)));
                break;
            default: throw new NotImplementedException($"TabStrip Tabs.{method} is not implemented (#1089)");
        }
        return Task.FromResult(result);
    }
    bool INamedParameterSource.TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        (IReadOnlyList<string> Names, int Required) signature = member.ToLowerInvariant() switch
        {
            "add" => (["Index", "Key", "Caption", "Image"], 0),
            "item" or "remove" => (["Index"], 1),
            "clear" or "count" => ([], 0),
            _ => ([], 0),
        };
        (names, required) = signature;
        return member.ToLowerInvariant() is "add" or "item" or "remove" or "clear" or "count";
    }
    internal static int Index(Vb6Value value)
    {
        if (!Vb6Value.TryNumericToDouble(value, out var number) || value.Type == Vb6Value.ValueType.Date || !double.IsFinite(number))
            throw new NotImplementedException("TabStrip index conversion is not implemented (#1089)");
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (rounded < 1 || rounded > int.MaxValue)
            throw new VBRunTimeException(new VBStandardError(35600, "Index out of bounds"));
        return (int)rounded;
    }
    internal static string Text(Vb6Value value) => value.Type == Vb6Value.ValueType.String ? (string)value.Value!
        : throw new NotImplementedException("TabStrip string conversion is not implemented (#1089)");
    public bool TryGetProperty(string name, out Vb6Value value) { value = default; return false; }
    public bool TrySetProperty(string name, Vb6Value value) => name.Equals("Count", StringComparison.OrdinalIgnoreCase)
        ? throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty)
        : throw new NotImplementedException($"TabStrip Tabs.{name} assignment is not implemented (#1089)");
}

internal sealed class TabStripTab(VBTabStrip owner, TabStripModel.Tab tab) : ICSharpAsyncProxy, ICSharpPropertyBag
{
    internal VBTabStrip Owner => owner;
    internal TabStripModel.Tab Tab => tab;
    private Vb6Value? tag;
    private string? tip;
    private bool? highlighted;
    public void Call(string method, List<Vb6Value> args) => throw new InvalidOperationException();
    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        var value = method.ToLowerInvariant() switch
        {
            "caption" => new Vb6Value(tab.Caption),
            "key" => new Vb6Value(tab.Key),
            "index" => new Vb6Value(tab.Index),
            "selected" => new Vb6Value(tab.Selected),
            "tag" => tag ?? throw new NotImplementedException("TabStrip unset Tag is unmeasured (#1089)"),
            "tooltiptext" => new Vb6Value(tip ?? throw new NotImplementedException("TabStrip unset ToolTipText is unmeasured (#1089)")),
            "highlighted" => new Vb6Value(highlighted ?? throw new NotImplementedException("TabStrip unset HighLighted is unmeasured (#1089)")),
            _ => throw new NotImplementedException($"TabStrip Tab.{method} is not implemented (#1089)"),
        };
        return Task.FromResult<Vb6Value?>(value);
    }
    public bool TryGetProperty(string name, out Vb6Value value) { value = default; return false; }
    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "caption": tab.Caption = TabStripTabs.Text(value); break;
            case "key": tab.Key = TabStripTabs.Text(value); break;
            case "tooltiptext": tip = TabStripTabs.Text(value); break;
            case "highlighted": highlighted = value.Type == Vb6Value.ValueType.Boolean ? (bool)value.Value!
                : throw new NotImplementedException("TabStrip Boolean conversion is not implemented (#1089)"); break;
            case "tag":
                if (ReferenceEquals(value.Value, owner) || ReferenceEquals(value.Value, owner.Tabs)
                    || value.Value is TabStripTab other && ReferenceEquals(other.Owner, owner))
                    throw new VBRunTimeException(new VBStandardError(35700, "Circular object referencing is not allowed"));
                if (value.Type == Vb6Value.ValueType.Object || value.Type == Vb6Value.ValueType.Control
                    || value.Type == Vb6Value.ValueType.CSharpProxyObject)
                    throw new NotImplementedException("TabStrip object Tag lifetime is not implemented (#1089)");
                tag = value; break;
            case "image": throw new VBRunTimeException(new VBStandardError(35613, "ImageList must be initialized before it can be used"));
            case "index" or "left" or "top" or "width" or "height":
                throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            default: throw new NotImplementedException($"TabStrip Tab.{name} assignment is not implemented (#1089)");
        }
        return true;
    }
}
