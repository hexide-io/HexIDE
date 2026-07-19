using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HexIDE.Tools;

/// <summary>
/// Builds a project's Project Explorer children as a filesystem hierarchy anchored at the
/// <c>.vbp</c> directory: directory nodes first (case-insensitive alphabetical), then leaves
/// (by <see cref="IProjectFileNode.Name"/>), recursively. The tree is derived exclusively from
/// project membership — never from disk enumeration — so empty directories cannot exist and
/// non-member files never appear. Members outside the project cone (leading <c>..</c>, other
/// drive, UNC) and unsaved members render at the root.
/// </summary>
public static class ProjectTreeBuilder
{
    public static List<IProjectTreeElement> BuildChildren(
        IEnumerable<IProjectFileNode> items,
        string? anchorDir,
        IReadOnlyDictionary<string, bool>? expansionState = null,
        ProjectViewModel? owner = null)
    {
        var root = new DirNode("");
        foreach (var item in items)
        {
            var node = root;
            if (TryGetDirectorySegments(anchorDir, item.AbsolutePath, out var segments))
            {
                foreach (var segment in segments)
                {
                    if (!node.Dirs.TryGetValue(segment, out var child))
                    {
                        child = new DirNode(segment);
                        node.Dirs[segment] = child;
                    }
                    node = child;
                }
            }
            node.Leaves.Add(item);
        }
        return Materialize(root, parentKey: null, expansionState, owner);
    }

    /// <summary>
    /// Splits a member's path into directory segments below the project anchor. Returns false —
    /// the member renders at the container root — when the member or project is unsaved, the
    /// member sits directly beside the <c>.vbp</c>, or the path is outside the project cone
    /// (leading <c>..</c>, other drive, UNC).
    /// </summary>
    internal static bool TryGetDirectorySegments(string? anchorDir, string? absolutePath, out string[] segments)
    {
        segments = [];
        if (string.IsNullOrEmpty(anchorDir) || string.IsNullOrEmpty(absolutePath))
            return false;

        string? dir;
        try
        {
            var rel = Path.GetRelativePath(anchorDir, Path.GetFullPath(absolutePath));
            if (Path.IsPathRooted(rel))
                return false; // cross-drive or UNC — GetRelativePath returns the input path itself
            dir = Path.GetDirectoryName(rel);
        }
        catch (Exception)
        {
            return false; // unparseable path text — render at root rather than crash
        }

        if (string.IsNullOrEmpty(dir))
            return false;

        var parts = dir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] == "..")
            return false;

        segments = parts;
        return true;
    }

    private static List<IProjectTreeElement> Materialize(
        DirNode node,
        string? parentKey,
        IReadOnlyDictionary<string, bool>? expansionState,
        ProjectViewModel? owner)
    {
        var result = new List<IProjectTreeElement>();
        foreach (var child in node.Dirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            var key = parentKey == null ? child.Name : parentKey + Path.DirectorySeparatorChar + child.Name;
            var vm = new DirectoryViewModel(owner, child.Name, key);
            if (expansionState != null && expansionState.TryGetValue(key, out var expanded))
                vm.IsExpanded = expanded;
            foreach (var grandChild in Materialize(child, key, expansionState, owner))
                vm.Children.Add(grandChild);
            result.Add(vm);
        }
        foreach (var leaf in node.Leaves.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase))
            result.Add(leaf);
        return result;
    }

    // Case-insensitive directory accumulation: case-only differing spellings merge, first-seen
    // casing is displayed.
    private sealed class DirNode(string name)
    {
        public string Name { get; } = name;
        public Dictionary<string, DirNode> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<IProjectFileNode> Leaves { get; } = new();
    }
}
