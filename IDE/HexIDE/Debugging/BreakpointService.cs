using System;
using System.Collections.Generic;
using System.Linq;

namespace HexIDE.Debugging;

/// <summary>
/// In-memory breakpoint store: 1-based line sets per document URI. Persistence is owned by
/// <c>UserSidecarService</c>, which loads/saves these alongside bookmarks in the per-user <c>&lt;project&gt;.user.hexproj</c>
/// sidecar (beside the .vbp — the user chooses to commit or ignore it). Mirrors <c>BookmarkService</c> but keeps
/// 1-based lines (matching the runtime and the breakpoint gutter).
/// </summary>
public sealed class BreakpointService : IBreakpointService
{
    // documentUri → 1-based line set.
    private readonly Dictionary<string, SortedSet<int>> _breakpoints = new();

    public event Action<string>? BreakpointsChanged;

    public void Toggle(string documentUri, int line)
    {
        if (!_breakpoints.TryGetValue(documentUri, out var set))
            _breakpoints[documentUri] = set = new SortedSet<int>();
        if (!set.Remove(line))
            set.Add(line);
        if (set.Count == 0)
            _breakpoints.Remove(documentUri);
        BreakpointsChanged?.Invoke(documentUri);
    }

    public void SetDocument(string documentUri, IEnumerable<int> lines)
    {
        var set = new SortedSet<int>(lines);
        if (set.Count == 0)
            _breakpoints.Remove(documentUri);
        else
            _breakpoints[documentUri] = set;
        BreakpointsChanged?.Invoke(documentUri);
    }

    public bool IsBreakpoint(string documentUri, int line)
        => _breakpoints.TryGetValue(documentUri, out var set) && set.Contains(line);

    public IReadOnlyList<int> GetBreakpoints(string documentUri)
        => _breakpoints.TryGetValue(documentUri, out var set) ? set.ToList() : Array.Empty<int>();

    public void ClearDocument(string documentUri)
    {
        if (_breakpoints.Remove(documentUri))
            BreakpointsChanged?.Invoke(documentUri);
    }

    public void ClearAll()
    {
        if (_breakpoints.Count == 0)
            return;
        var uris = _breakpoints.Keys.ToList();
        _breakpoints.Clear();
        foreach (var uri in uris)
            BreakpointsChanged?.Invoke(uri);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<int>> All()
        => _breakpoints.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value.ToList());
}
