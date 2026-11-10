namespace HexIDE.Repack;

/// <summary>Adopter files add to the program directory without replacing published files or granting trust.</summary>
internal static class IncludedFiles
{
    // TrustAnchors' embedded keys and RevocationStore's embedded floor and user-data cache.
    private static readonly HashSet<string> ProtectedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "hexide-root.pub", "hexide-firstparty.pub", "revocations.json", "revocations.sig",
        "revocations-cache.json", "revocations-cache.sig",
    };

    public static int Copy(string source, string destination, string stock)
    {
        source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var root = new DirectoryInfo(source);
        RefuseLink(root);
        if (!root.Exists)
            throw new RepackException($"The folder to include, {source}, does not exist.");
        return CopyDirectory(root, destination, stock, source);
    }

    private static int CopyDirectory(DirectoryInfo directory, string destination, string stock, string root)
    {
        var count = 0;
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            RefuseLink(entry);
            var relative = Path.GetRelativePath(root, entry.FullName);
            if (ProtectedNames.Contains(entry.Name))
                throw new RepackException($"{relative} can't be included: HexIDE would read it as a trust anchor or as revocation data, and an included file can't grant trust.");

            var target = Path.Combine(destination, entry.Name);
            var original = Path.Combine(stock, entry.Name);
            if (entry is DirectoryInfo child)
            {
                if (File.Exists(target) || File.Exists(original))
                    throw new RepackException($"{relative} can't be included, because it would replace something already in the build. Included files only add to it.");
                Directory.CreateDirectory(target);
                count += CopyDirectory(child, target, original, root);
            }
            else
            {
                if (Path.Exists(target) || Path.Exists(original))
                    throw new RepackException($"{relative} can't be included, because it would replace something already in the build. Included files only add to it.");
                File.Copy(entry.FullName, target);
                count++;
            }
        }
        return count;
    }

    private static void RefuseLink(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null || (entry.Exists && entry.Attributes.HasFlag(FileAttributes.ReparsePoint)))
            throw new RepackException($"{entry.FullName} is a link, and a link can't be included. Include the files themselves.");
    }
}
