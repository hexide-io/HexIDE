using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// In-memory breakpoint store: 1-based line sets per document. Persistence is owned by
/// <c>UserSidecarService</c>, which loads/saves these alongside bookmarks in the per-user <c>&lt;project&gt;.user.hexproj</c>
/// sidecar (beside the .vbp — the user chooses to commit or ignore it). Mirrors <c>BookmarkService</c> but keeps
/// 1-based lines (matching the runtime and the breakpoint gutter).
/// </summary>
public sealed class BreakpointService : IBreakpointService
{
    // document → 1-based line set. Keyed by identity, so a rename moves nothing.
    private readonly Dictionary<DocumentIdentity, SortedSet<int>> _breakpoints = new();

    public event Action<DocumentIdentity>? BreakpointsChanged;

    public void Toggle(DocumentIdentity document, int line)
    {
        if (!_breakpoints.TryGetValue(document, out var set))
            _breakpoints[document] = set = new SortedSet<int>();
        if (!set.Remove(line))
            set.Add(line);
        if (set.Count == 0)
            _breakpoints.Remove(document);
        BreakpointsChanged?.Invoke(document);
    }

    public void SetDocument(DocumentIdentity document, IEnumerable<int> lines)
    {
        var set = new SortedSet<int>(lines);
        if (set.Count == 0)
            _breakpoints.Remove(document);
        else
            _breakpoints[document] = set;
        BreakpointsChanged?.Invoke(document);
    }

    public bool IsBreakpoint(DocumentIdentity document, int line)
        => _breakpoints.TryGetValue(document, out var set) && set.Contains(line);

    public IReadOnlyList<int> GetBreakpoints(DocumentIdentity document)
        => _breakpoints.TryGetValue(document, out var set) ? set.ToList() : Array.Empty<int>();

    public void ClearDocument(DocumentIdentity document)
    {
        if (_breakpoints.Remove(document))
            BreakpointsChanged?.Invoke(document);
    }

    public void ClearAll()
    {
        if (_breakpoints.Count == 0)
            return;
        var documents = _breakpoints.Keys.ToList();
        _breakpoints.Clear();
        foreach (var document in documents)
            BreakpointsChanged?.Invoke(document);
    }

    public void ClearProject(ProjectDefinition project)
    {
        var mine = _breakpoints.Keys.Where(document => document.IsIn(project)).ToList();
        foreach (var document in mine)
        {
            _breakpoints.Remove(document);
            BreakpointsChanged?.Invoke(document);
        }
    }

    public IReadOnlyDictionary<DocumentIdentity, IReadOnlyList<int>> All()
        => _breakpoints.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value.ToList());
}
