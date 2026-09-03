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
            var location = Locate(anchorDir, item.AbsolutePath);
            item.LocationCaption = location.Caption;
            foreach (var segment in location.Segments)
            {
                if (!node.Dirs.TryGetValue(segment, out var child))
                {
                    child = new DirNode(segment);
                    node.Dirs[segment] = child;
                }
                node = child;
            }
            node.Leaves.Add(item);
        }
        return Materialize(root, parentKey: null, expansionState, owner);
    }

    /// <summary>
    /// Where a member sits relative to the project anchor. Exactly one of the two fields is ever
    /// populated: <see cref="Segments"/> for a member below the anchor, <see cref="Caption"/> for one
    /// outside the cone. Both empty means the member belongs at the root with nothing to explain —
    /// it is unsaved, or it sits beside the <c>.vbp</c>.
    /// </summary>
    private readonly record struct MemberLocation(string[] Segments, string? Caption)
    {
        public static readonly MemberLocation AtRoot = new([], null);
    }

    /// <summary>
    /// Classifies a member's path against the project anchor in one pass.
    ///
    /// <para>
    /// The four outcomes were previously collapsed into a single boolean, which is why an out-of-cone
    /// member rendered identically to one sitting beside the <c>.vbp</c> — the tree knew the member was
    /// elsewhere and threw the fact away (#228). Splitting placement from explanation keeps the cone
    /// rule in one place: two copies of it would drift, and the drift would be invisible.
    /// </para>
    /// </summary>
    private static MemberLocation Locate(string? anchorDir, string? absolutePath)
    {
        // Unsaved member or unsaved project: there is no location to report yet, and inventing one
        // would be worse than silence.
        if (string.IsNullOrEmpty(anchorDir) || string.IsNullOrEmpty(absolutePath))
            return MemberLocation.AtRoot;

        string? dir;
        try
        {
            var full = Path.GetFullPath(absolutePath);
            var rel = Path.GetRelativePath(anchorDir, full);
            if (Path.IsPathRooted(rel))
            {
                // Different drive or UNC — GetRelativePath returns the input unchanged because no
                // relative form exists. Measured against vb6.exe: the .vbp carries the absolute path
                // in exactly this case, so the caption shows what the file will contain.
                return new MemberLocation([], Path.GetDirectoryName(full) ?? full);
            }
            dir = Path.GetDirectoryName(rel);
        }
        catch (Exception)
        {
            return MemberLocation.AtRoot; // unparseable path text — render at root rather than crash
        }

        if (string.IsNullOrEmpty(dir))
            return MemberLocation.AtRoot; // beside the .vbp

        var parts = dir.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return MemberLocation.AtRoot;

        // A traversal out of the project. Rendering the `..` as ascending folder nodes would put the
        // tree's root somewhere above the project, so the member stays at the root and the caption
        // carries the truth instead.
        return parts[0] == ".."
            ? new MemberLocation([], dir)
            : new MemberLocation(parts, null);
    }

    /// <summary>
    /// Splits a member's path into directory segments below the project anchor. Returns false —
    /// the member renders at the container root — when the member or project is unsaved, the
    /// member sits directly beside the <c>.vbp</c>, or the path is outside the project cone
    /// (leading <c>..</c>, other drive, UNC).
    /// </summary>
    internal static bool TryGetDirectorySegments(string? anchorDir, string? absolutePath, out string[] segments)
    {
        segments = Locate(anchorDir, absolutePath).Segments;
        return segments.Length > 0;
    }

    /// <summary>
    /// The location text for a member that lives outside the project's directory, or null when the
    /// member's position in the tree already tells the whole story. Relative where a relative form
    /// exists, absolute where none does — which is what the <c>.vbp</c> itself will contain.
    /// </summary>
    internal static string? GetLocationCaption(string? anchorDir, string? absolutePath) =>
        Locate(anchorDir, absolutePath).Caption;

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
