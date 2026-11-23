using HexIDE.Addins;

namespace HexIDE.AddinPacker.Interactive;

/// <summary>
/// Checks one answer by the rule the command itself applies, so a bad answer is asked for again instead of failing
/// the command after you have answered everything else. Never opens a key file: a key's path is checked for existence
/// only, and the command checks what is in it.
/// </summary>
public static class AnswerChecks
{
    /// <summary>Returns null when <paramref name="value"/> is acceptable, otherwise what is wrong with it.</summary>
    /// <param name="answerOf">The answer already given to another option, or null.</param>
    public static string? Check(PackerOption option, string value, Func<string, string?> answerOf)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "can't be blank";
        switch (option.Kind)
        {
            case OptionKind.Text:
            case OptionKind.OutputFolder:
                return null;
            case OptionKind.PublisherId:
                return PublisherId.IsValid(value) ? null
                    : "expected 3 to 64 lowercase letters or digits, joined by single dots or hyphens, such as acme.tools";
            case OptionKind.DisplayName:
                return PackageSigner.IsValidDisplayName(value) ? null
                    : "expected 1 to 128 characters, with no space at either end and no control or invisible characters";
            case OptionKind.Namespaces:
                var namespaces = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (namespaces.Length == 0)
                    return "expected one or more namespaces, comma-separated, such as acme.";
                var bad = namespaces.FirstOrDefault(ns => !PublisherNamespaces.IsUsable(ns));
                return bad is null ? null
                    : $"'{bad}' isn't a namespace that can be issued: one is a name followed by '.', such as 'acme.', "
                      + $"and '{PublisherNamespaces.Reserved}' is HexIDE's own";
            case OptionKind.HexideVersion:
                return Version.TryParse(value, out _) ? null : "expected a version such as 0.7.0";
            case OptionKind.ExistingFile:
                return File.Exists(value) ? null : $"there's no file at '{value}'";
            case OptionKind.SigningKey:
                return File.Exists(value) ? null : $"there's no file at '{value}'";
            case OptionKind.ExistingFolder:
                return Directory.Exists(value) ? null : $"there's no folder at '{value}'";
            case OptionKind.TrustFolder:
                if (!Directory.Exists(value))
                    return $"there's no folder at '{value}'";
                var missing = PackerCommands.TrustFiles.Where(name => !File.Exists(Path.Combine(value, name))).ToArray();
                return missing.Length == 0 ? null : $"'{value}' has no {string.Join(", ", missing)}";
            case OptionKind.FileInSource:
                return answerOf("src") is not { } src || File.Exists(Path.Combine(src, value)) ? null
                    : $"there's no '{value}' in '{src}'";
            case OptionKind.KeyFolder:
                return KeyFolderProblem(value);
            case OptionKind.Capability:
                return AddinCapabilities.IsKnown(value) ? null
                    : $"'{value}' isn't a capability HexIDE knows: {string.Join(", ", PackerCommands.Capabilities)}";
            default:
                throw new ArgumentOutOfRangeException(nameof(option), option.Kind, null);
        }
    }

    /// <summary>
    /// Returns why an optional option can't be skipped, given the other answers, or null when it can. <c>pack --key</c>
    /// can be skipped only when the trust folder holds <c>firstparty.key</c>, which is checked by name only.
    /// </summary>
    public static string? NeededWhenSkipped(PackerOption option, Func<string, string?> answerOf) =>
        option.Kind == OptionKind.SigningKey && answerOf("trust") is { } trust
            && !File.Exists(Path.Combine(trust, "firstparty.key"))
            ? $"'{trust}' has no firstparty.key, so give your key's file"
            : null;

    /// <summary>
    /// A private key written inside a git clone is one commit from publication, so hexide-pack refuses any folder
    /// there, the development chain's included. The plain packer still writes the development chain where a clone
    /// expects it. The folder is resolved through links first, as the packer resolves its own output folders.
    /// </summary>
    public static string? KeyFolderProblem(string folder)
    {
        string resolved;
        try
        {
            resolved = PackageSigner.ResolveOutputDirectory(folder);
        }
        catch (IOException ex)
        {
            return ex.Message;
        }
        return EnclosingGitClone(resolved) is { } clone
            ? $"'{folder}' is inside a git clone ({clone}), and hexide-pack won't write private keys there, where a "
              + "commit could publish them. Choose a folder outside it. For the development chain, use "
              + "HexIDE.AddinPacker genkeys directly."
            : null;
    }

    /// <summary>The nearest folder at or above <paramref name="folder"/> that git would treat as a working tree: one
    /// holding a <c>.git</c> folder with a <c>HEAD</c>, as a clone has, or a <c>.git</c> file naming its
    /// <c>gitdir:</c>, as a worktree has. An empty <c>.git</c> folder, which git ignores, doesn't count.</summary>
    public static string? EnclosingGitClone(string folder)
    {
        for (var directory = new DirectoryInfo(folder); directory is not null; directory = directory.Parent)
        {
            var git = Path.Combine(directory.FullName, ".git");
            if (File.Exists(Path.Combine(git, "HEAD")) || File.Exists(git) && StartsWithGitDir(git))
                return directory.FullName;
        }
        return null;
    }

    private static bool StartsWithGitDir(string file)
    {
        try
        {
            using var reader = new StreamReader(file);
            return (reader.ReadLine() ?? "").StartsWith("gitdir:", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true; // A .git file it can't read is still most likely a worktree's: refuse rather than guess.
        }
    }

    /// <summary>A folder answer without a trailing separator, so the printed command line can't end a quoted value
    /// with a backslash, which a Windows command line would read as an escaped quote.</summary>
    public static string Normalise(PackerOption option, string value) => option.Kind switch
    {
        OptionKind.ExistingFolder or OptionKind.TrustFolder or OptionKind.OutputFolder or OptionKind.KeyFolder
            when value.Length > 1 && Path.GetPathRoot(value) != value => Path.TrimEndingDirectorySeparator(value),
        _ => value,
    };
}
