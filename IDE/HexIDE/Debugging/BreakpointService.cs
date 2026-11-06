using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Debugging;

/// <summary>
/// In-memory breakpoint store: 1-based line sets per document, counted from the top of the file with its header
/// included (see <see cref="IBreakpointService"/>). Persistence is owned by
/// <c>UserSidecarService</c>, which loads/saves these alongside bookmarks in the per-user <c>&lt;project&gt;.user.hexproj</c>
/// sidecar (beside the .vbp — the user chooses to commit or ignore it). Mirrors <c>BookmarkService</c> but keeps
/// 1-based lines (matching the runtime and the breakpoint gutter).
/// </summary>
public sealed class BreakpointService : IBreakpointService
{
    // document → 1-based line and optional condition. Keyed by identity, so a rename moves nothing.
    private readonly Dictionary<DocumentIdentity, SortedDictionary<int, string?>> _breakpoints = new();

    public event Action<DocumentIdentity>? BreakpointsChanged;

    public void Toggle(DocumentIdentity document, int line)
    {
        if (!_breakpoints.TryGetValue(document, out var set))
            _breakpoints[document] = set = new SortedDictionary<int, string?>();
        if (!set.Remove(line))
            set.Add(line, null);
        if (set.Count == 0)
            _breakpoints.Remove(document);
        BreakpointsChanged?.Invoke(document);
    }

    public void SetDocument(DocumentIdentity document, IEnumerable<int> lines)
    {
        var set = new SortedDictionary<int, string?>();
        foreach (var line in lines)
            set[line] = GetCondition(document, line);
        if (set.Count == 0)
            _breakpoints.Remove(document);
        else
            _breakpoints[document] = set;
        BreakpointsChanged?.Invoke(document);
    }

    public bool IsBreakpoint(DocumentIdentity document, int line)
        => _breakpoints.TryGetValue(document, out var set) && set.ContainsKey(line);

    public IReadOnlyList<int> GetBreakpoints(DocumentIdentity document)
        => _breakpoints.TryGetValue(document, out var set) ? set.Keys.ToList() : Array.Empty<int>();

    public string? GetCondition(DocumentIdentity document, int line)
        => _breakpoints.TryGetValue(document, out var set) && set.TryGetValue(line, out var condition) ? condition : null;

    public void SetCondition(DocumentIdentity document, int line, string? condition)
    {
        if (!_breakpoints.TryGetValue(document, out var set) || !set.ContainsKey(line))
            return;
        condition = string.IsNullOrWhiteSpace(condition) ? null : condition;
        if (set[line] == condition)
            return;
        set[line] = condition;
        BreakpointsChanged?.Invoke(document);
    }

    public void MoveLines(DocumentIdentity document, int firstLine, int delta)
    {
        if (!_breakpoints.TryGetValue(document, out var set) || delta == 0)
            return;
        var moved = new SortedDictionary<int, string?>();
        foreach (var (line, condition) in set)
            moved[line >= firstLine ? line + delta : line] = condition;
        _breakpoints[document] = moved;
        BreakpointsChanged?.Invoke(document);
    }

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
        => _breakpoints.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value.Keys.ToList());
}
