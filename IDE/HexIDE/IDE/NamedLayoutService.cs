using System.Collections.Generic;

namespace HexIDE.IDE;

public sealed class NamedLayoutService(IWindowStateService state)
{
    private Dictionary<string, LayoutManifest> _layouts = new(
        state.LoadNamedLayouts() ?? new Dictionary<string, LayoutManifest>(), StringComparer.OrdinalIgnoreCase);

    public IEnumerable<string> Names => _layouts.Keys.Order(StringComparer.CurrentCultureIgnoreCase);
    public LayoutManifest? Get(string name) => _layouts.GetValueOrDefault(name);

    public bool Save(string name, LayoutManifest layout)
    {
        name = name.Trim();
        if (name.Length == 0 || _layouts.ContainsKey(name)) return false;
        var next = new Dictionary<string, LayoutManifest>(_layouts, StringComparer.OrdinalIgnoreCase) { [name] = layout };
        Commit(next);
        return true;
    }

    public bool Rename(string name, string replacement)
    {
        replacement = replacement.Trim();
        if (!_layouts.TryGetValue(name, out var layout) || replacement.Length == 0 ||
            (!string.Equals(name, replacement, StringComparison.OrdinalIgnoreCase) && _layouts.ContainsKey(replacement)))
            return false;
        var next = new Dictionary<string, LayoutManifest>(_layouts, StringComparer.OrdinalIgnoreCase);
        next.Remove(name);
        next.Add(replacement, layout);
        Commit(next);
        return true;
    }

    public void Delete(string name)
    {
        var next = new Dictionary<string, LayoutManifest>(_layouts, StringComparer.OrdinalIgnoreCase);
        if (!next.Remove(name)) return;
        Commit(next);
    }

    private void Commit(Dictionary<string, LayoutManifest> next)
    {
        state.SaveNamedLayouts(next);
        _layouts = next;
    }
}
