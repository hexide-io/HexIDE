using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

internal sealed partial class VBTreeView : Decorator, ICSharpPropertyBag
{
    private const double RowHeight = 16;
    private readonly Canvas labelHost = new();

    internal TreeNodes Nodes { get; }

    internal int ClientEdge { get; private set; } = 2;

    // Height is the declared .frm twips converted to pixels by the loader, available before layout.
    internal long GetVisibleCount() => (long)(Math.Max(0, Height - 2 * ClientEdge) / RowHeight);

    internal void LoadEdge(int appearance, int borderStyle)
    {
        sunken = appearance == 1;
        singleBorder = borderStyle == 1;
        ClientEdge = (sunken ? 2 : 0) + (singleBorder ? 1 : 0);
    }

    private bool sunken = true;
    private bool singleBorder;

    public VBTreeView()
    {
        Child = labelHost;
        Focusable = true;
        PointerPressed += HandlePointerPressed;
        Nodes = new TreeNodes(() => Dispatcher.UIThread.Post(() => Nodes!.ProcessPendingSort(), DispatcherPriority.Background));
        Nodes.Changed += InvalidateVisual;
        Nodes.ExpansionChanged += (node, expanded) => Raise(expanded ? "Expand" : "Collapse", new Vb6Value(node));
    }
    private long style = 7;
    private long lineStyle;
    private long labelEdit;
    private double indentation = 1000;
    private bool hideSelection = true;
    private bool singleSel;

    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Index", "Left", "Top", "Width", "Height", "Visible", "Tag", "ToolTipText", "TabIndex", "TabStop",
        "Container", "Parent", "Object",
    };

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (Shared.Contains(name)) return false;
        value = name.ToLowerInvariant() switch
        {
            "nodes" => new(Nodes),
            "selecteditem" => TreeNodes.Wrap(Nodes.SelectedItem),
            "style" => new(style),
            "linestyle" => new(lineStyle),
            "labeledit" => new(labelEdit),
            "indentation" => new((float)(indentation * 1440 / 2540)),
            "hideselection" => new(hideSelection),
            "singlesel" => new(singleSel),
            "checkboxes" => new(Nodes.Checkboxes),
            "sorted" => new(Nodes.Sorted),
            "pathseparator" => new(Nodes.PathSeparator),
            _ => throw new NotImplementedException(),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (Shared.Contains(name)) return false;
        switch (name.ToLowerInvariant())
        {
            case "style": style = Choice(value, 7); break;
            case "linestyle": lineStyle = Choice(value, 1); break;
            case "labeledit": labelEdit = Choice(value, 1); break;
            case "indentation":
                var number = Number(value);
                if (number < 0) throw TreeNodes.Error(380);
                var himetric = number * 2540 / 1440;
                if (!double.IsFinite(himetric) || himetric - Math.Floor(himetric) == 0.5)
                    throw new NotImplementedException();
                indentation = Math.Round(himetric);
                break;
            case "hideselection": hideSelection = TreeNodes.BooleanValue(value); break;
            case "singlesel": singleSel = TreeNodes.BooleanValue(value); break;
            case "checkboxes": Nodes.Checkboxes = TreeNodes.BooleanValue(value); break;
            case "sorted": Nodes.Sorted = TreeNodes.BooleanValue(value); break;
            case "pathseparator": Nodes.PathSeparator = TreeNodes.TextValue(value); break;
            default: throw new NotImplementedException();
        }
        InvalidateVisual();
        return true;
    }

    internal void SetObjectProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "nodes": throw TreeNodes.Error(383);
            case "selecteditem":
                if (value.Value is not null && value.Value is not TreeNode) throw new NotImplementedException();
                Nodes.SelectedItem = (TreeNode?)value.Value;
                break;
            default: throw new NotImplementedException();
        }
    }

    // Only the measured numeric forms: other automation conversions remain refused.
    private static double Number(Vb6Value value) => value.Value switch
    {
        short or int or long or float or double =>
            Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new NotImplementedException(),
    };

    private static long Choice(Vb6Value value, int maximum)
    {
        var number = Number(value);
        if (number != Math.Truncate(number)) throw new NotImplementedException();
        return number >= 0 && number <= maximum ? (long)number : throw TreeNodes.Error(380);
    }

    internal void LoadHimetricIndentation(double value) => indentation = value;
}
