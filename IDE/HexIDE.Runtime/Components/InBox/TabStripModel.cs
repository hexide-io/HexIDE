using System.Globalization;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// The TabStrip's collection and code-driven selection (#1089). Measured with MSCOMCTL.OCX, native and p-code
/// (the oracle's "In-box controls: the TabStrip"). The runtime control observes model changes to repaint.
/// </summary>
internal sealed class TabStripModel
{
    private readonly List<Tab> tabs = [];

    internal TabStripModel() => Add();

    internal IReadOnlyList<Tab> Tabs => tabs.AsReadOnly();
    internal Tab? SelectedItem { get; private set; }
    internal Func<bool>? BeforeClick { get; set; }
    internal Action? Click { get; set; }
    internal event Action? Changed;
    private void NotifyChanged() => Changed?.Invoke();

    internal Tab Add(int? index = null, string key = "", string caption = "")
    {
        var at = index ?? tabs.Count + 1;
        if (at < 1 || at > tabs.Count + 1)
            throw Error(35600, "Index out of bounds");
        CheckKey(key, null);
        var selectedIndex = SelectedItem?.Index;
        var tab = new Tab(this, caption, key);
        tabs.Insert(at - 1, tab);
        if (tabs.Count == 1 || selectedIndex == at)
            SelectedItem = tab;
        NotifyChanged();
        return tab;
    }

    internal Tab Item(Vb6Value index)
    {
        if (index.Type == Vb6Value.ValueType.String)
        {
            var key = (string)index.Value!;
            foreach (var tab in tabs)
                if (tab.Key.Length != 0 && string.Equals(tab.Key, key, StringComparison.Ordinal))
                    return tab;
            throw Error(35601, "Element not found");
        }
        // Only numeric indexes were measured. Do not borrow Collection's unrelated coercion rules.
        if (index.Type == Vb6Value.ValueType.Date || !Vb6Value.TryNumericToDouble(index, out var number) || !double.IsFinite(number))
            throw new NotImplementedException("TabStrip index conversion is not implemented (#1089)");
        var rounded = Math.Round(number, MidpointRounding.ToEven);
        if (rounded < 1 || rounded > tabs.Count)
            throw Error(35600, "Index out of bounds");
        return tabs[(int)rounded - 1];
    }

    internal void Remove(Vb6Value index)
    {
        additionalSelection.Clear();
        tabs.Remove(Item(index));
        SelectedItem = tabs.Count == 0 ? null : tabs[0];
        NotifyChanged();
    }

    internal void Clear()
    {
        additionalSelection.Clear();
        tabs.Clear();
        SelectedItem = null;
        NotifyChanged();
    }

    internal void Select(Tab? tab)
    {
        additionalSelection.Clear();
        if (tab is null)
        {
            SelectedItem = null;
            NotifyChanged();
            return;
        }
        if (!tabs.Contains(tab))
            throw new NotImplementedException("Selecting a foreign or removed Tab is not implemented (#1089)");
        var cancel = SelectedItem is not null && (BeforeClick?.Invoke() ?? false);
        if (!cancel)
            SelectedItem = tab;
        NotifyChanged();
        Click?.Invoke();
    }

    private readonly HashSet<Tab> additionalSelection = [];

    internal void SelectFromMouse(Tab tab, bool multi, bool control)
    {
        if (multi && control)
            additionalSelection.Add(tab);
        else
        {
            additionalSelection.Clear();
            SelectedItem = tab;
        }
        NotifyChanged();
    }

    internal void DeselectAll(int style)
    {
        if (style == 1)
        {
            additionalSelection.Clear();
            SelectedItem = null;
            NotifyChanged();
        }
        else if (style != 0)
            throw new NotImplementedException("TabStrip DeselectAll for this style is not implemented (#1089)");
    }

    private void CheckKey(string key, Tab? except)
    {
        if (key.Length == 0)
            return;
        if (double.TryParse(key, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            throw Error(35603, "Invalid key");
        foreach (var tab in tabs)
            if (!ReferenceEquals(tab, except) && string.Equals(tab.Key, key, StringComparison.Ordinal))
                throw Error(35602, "Key is not unique in collection");
    }

    private static VBRunTimeException Error(int number, string description) => new(new VBStandardError(number, description));

    internal sealed class Tab(TabStripModel owner, string caption, string key)
    {
        private string key = key;

        private string caption = caption;
        internal string Caption
        {
            get => caption;
            set { caption = value; owner.NotifyChanged(); }
        }
        internal int Index => owner.tabs.Contains(this)
            ? owner.tabs.IndexOf(this) + 1
            : throw new NotImplementedException("Reading a removed Tab is not implemented (#1089)");
        internal string Key
        {
            get => key;
            set
            {
                owner.CheckKey(value, this);
                key = value;
            }
        }
        internal bool Selected
        {
            get => ReferenceEquals(owner.SelectedItem, this) || owner.additionalSelection.Contains(this);
            set
            {
                if (value || ReferenceEquals(owner.SelectedItem, this))
                    owner.Select(this);
                else
                    throw new NotImplementedException("Deselecting an unselected Tab is not implemented (#1089)");
            }
        }
    }
}
