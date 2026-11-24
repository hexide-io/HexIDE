using System.Collections.Immutable;

namespace HexIDE.TwinBasic;

internal enum ArchiveStructureOperation { Rename, Delete, NewFolder, AddModule, AddForm }
internal enum ArchiveStructureFailure { Unmeasured, Name }

internal sealed class ArchiveStructureException(ArchiveStructureFailure failure)
    : IOException(failure == ArchiveStructureFailure.Unmeasured
        ? "This operation is refused because it has not been measured against the twinBASIC IDE."
        : "Use a unique ASCII identifier in this folder, at most 255 bytes; reserved device names are not allowed.")
{
    internal ArchiveStructureFailure Failure { get; } = failure;
}

internal static class ArchiveProjectStructure
{
    internal static bool CanEdit(ArchiveProjectSnapshot snapshot, ArchiveEntryIdentity identity, ArchiveStructureOperation operation)
    {
        if (!snapshot.ContainerPath.EndsWith(".twinproj", StringComparison.OrdinalIgnoreCase)
            || !snapshot.TryGetEntry(identity, out var entry) || identity.Segments.Length == 0
            || identity.Segments[0] != "Sources") return false;
        if (operation == ArchiveStructureOperation.NewFolder) return entry is ArchiveProjectFolder;
        if (operation is not (ArchiveStructureOperation.Rename or ArchiveStructureOperation.Delete)
            || entry is not ArchiveProjectDocument || !entry.Name.EndsWith(".twin", StringComparison.OrdinalIgnoreCase)) return false;
        var parent = Parent(snapshot, identity);
        return !parent.Children.Any(child => child.Name.Equals(entry.Name[..^5] + ".tbform", StringComparison.OrdinalIgnoreCase));
    }

    internal static ArchiveProjectSnapshot Save(ArchiveProjectSnapshot snapshot, ArchiveEntryIdentity identity,
        ArchiveStructureOperation operation, string? name = null)
    {
        if (!CanEdit(snapshot, identity, operation)) throw new ArchiveStructureException(ArchiveStructureFailure.Unmeasured);
        var target = snapshot.TryGetEntry(identity, out var found) ? found! : throw new InvalidOperationException();
        var parent = operation == ArchiveStructureOperation.NewFolder ? (ArchiveProjectFolder)target : Parent(snapshot, identity);
        var storedName = name + (operation == ArchiveStructureOperation.Rename ? target.Name[^5..] : "");
        if (operation != ArchiveStructureOperation.Delete)
        {
            // The 255 bytes are the stored name's, extension included: a module's becomes a file name in #5369's source
            // folder, and a file name is at most 255 characters there.
            if (!ValidName(name) || storedName.Length > 255 || parent.Children.Any(child => !child.Identity.Equals(identity)
                    && child.Name.Equals(storedName, StringComparison.OrdinalIgnoreCase)))
                throw new ArchiveStructureException(ArchiveStructureFailure.Name);
        }
        TwinBasicEntry Edit(ArchiveProjectEntry entry)
        {
            if (entry.Identity.Equals(identity) && operation == ArchiveStructureOperation.Rename)
                return ((ArchiveProjectDocument)entry).File with { Name = storedName };
            if (entry is not ArchiveProjectFolder folder) return entry.Source;
            var children = folder.Children.Where(child => operation != ArchiveStructureOperation.Delete || !child.Identity.Equals(identity))
                .Select(Edit).ToImmutableArray();
            if (entry.Identity.Equals(identity) && operation == ArchiveStructureOperation.NewFolder)
                children = children.Insert(0, new TwinBasicDirectory(storedName, 0, 0, 0, []));
            return ((TwinBasicDirectory)entry.Source) with { Children = children };
        }
        // docs/twinbasic-oracle/twinproj-container-v1.md: 05 -> 06 and 02 -> 07; deleting 05's module restores 02.
        return ArchiveProjectSave.SaveStructure(snapshot, snapshot.Container with { Root = (TwinBasicDirectory)Edit(snapshot.Root) });
    }

    private static ArchiveProjectFolder Parent(ArchiveProjectSnapshot snapshot, ArchiveEntryIdentity identity) =>
        snapshot.TryGetEntry(new(identity.ContainerPath, identity.Segments.RemoveAt(identity.Segments.Length - 1)), out var parent)
            ? (ArchiveProjectFolder)parent : throw new InvalidOperationException();

    private static bool ValidName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 255 || !Letter(name[0]) && name[0] != '_'
            || name.Any(c => !Letter(c) && c != '_' && c is not (>= '0' and <= '9'))) return false;
        return name.ToUpperInvariant() is not ("CON" or "PRN" or "AUX" or "NUL" or "CLOCK$")
            && !(name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '0' and <= '9');
    }

    private static bool Letter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
