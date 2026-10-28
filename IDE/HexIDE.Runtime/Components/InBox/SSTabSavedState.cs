using Avalonia.Controls;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>The saved page lists are independent of the children's positions (#1090).</summary>
internal sealed class SSTabSavedState(IReadOnlyDictionary<string, object> properties)
{
    internal static readonly PropertyClass<SSTabSavedState?> Property =
        new("(SSTabPages)", "Saved SSTab page metadata.", PropertyCategory.Internal);

    internal object? Value(string name) => properties.FirstOrDefault(p =>
        p.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    internal int Number(string name, int fallback) => Value(name) switch
    {
        null => fallback,
        int value => value,
        _ => throw new NotImplementedException("SSTab saved property conversion is not measured"),
    };

    private bool Flag(string name, bool fallback) => Number(name, fallback ? -1 : 0) switch
    {
        0 => false,
        -1 => true,
        _ => throw new NotImplementedException("SSTab saved Boolean conversion is not measured"),
    };

    internal Dictionary<string, object> CopyProperties() => new(properties, StringComparer.OrdinalIgnoreCase);

    // Only the saved dimension conversions recorded by the oracle; no rounding rule was measured.
    private static double Dimension(int himetric, bool allowZero = false) => himetric switch
    {
        0 when allowZero => 0,
        520 => 295d / 15,
        900 => 510d / 15,
        1500 => 850d / 15,
        _ => throw new NotImplementedException("SSTab saved HIMETRIC rounding is not measured"),
    };

    internal bool CanDesign(ComponentInstance instance)
    {
        if (instance.PreservedChildSubtrees.Count > 0)
            return false;
        for (var page = 0; page < Number("Tabs", 3); page++)
            for (var index = 0; index < Number($"Tab({page}).ControlCount", 0); index++)
            {
                var name = Value($"Tab({page}).Control({index})") as string;
                var matches = instance.ContainedControls.Where(c => string.Equals(
                    c.GetPropertyOrDefault(VBProperties.NameProperty), name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || PropertyText.ArrayIndexOf(matches[0]) is not null)
                    return false;
            }
        return true;
    }

    internal void Apply(VBSSTab tab, bool forDesigner = false)
    {
        // Set counts and orientation before placing children: loading trusts the saved positions.
        tab.Tabs = Number("Tabs", 3);
        tab.TabsPerRow = Number("TabsPerRow", 3);
        tab.VbStyle = Number("Style", 0);
        tab.TabOrientation = Number("TabOrientation", 0);
        tab.TabHeight = Dimension(Number("TabHeight", 520));
        tab.TabMaxWidth = Dimension(Number("TabMaxWidth", 0), allowZero: true);
        tab.WordWrap = Flag("WordWrap", true);
        tab.ShowFocusRect = Flag("ShowFocusRect", true);
        for (var i = 0; i < tab.Tabs; i++)
        {
            if (Value($"TabCaption({i})") is not { } value)
                continue;
            if (value is not string caption)
                throw new NotImplementedException("SSTab saved caption conversion is not measured");
            tab.Caption(i, caption);
        }
        // The designer shows the saved page; ControlEnabled's tab-zero quirk is runtime-only.
        if (forDesigner)
            tab.Tab = Number("Tab", 0);
    }

    internal void PlacePages(VBSSTab tab)
    {
        var lists = new List<IReadOnlyList<Control>>();
        var flags = new List<bool>();
        for (var i = 0; i < tab.Tabs; i++)
        {
            var children = new List<Control>();
            for (var j = 0; j < Number($"Tab({i}).ControlCount", 0); j++)
            {
                var name = Value($"Tab({i}).Control({j})") as string;
                if (name is null)
                    continue;
                var matches = tab.ChildHost.Children.Where(c =>
                    string.Equals(VBProps.GetName(c), name, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matches.Length != 1 || VBProps.GetIndex(matches[0]) is not null)
                    throw new NotImplementedException("SSTab array or unresolved saved control references are not measured");
                children.Add(matches[0]);
            }
            lists.Add(children);
            flags.Add(Flag($"Tab({i}).ControlEnabled", true));
        }
        tab.LoadPages(Number("Tab", 0), lists, flags);
    }
}
