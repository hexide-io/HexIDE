using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace HexIDE.IDE.Profiles;

internal sealed record InstalledProfileCandidate(string Id, string DirectoryPath);

internal static class ProfileCatalog
{
    // Explicit resource index: never enumerate an avares directory or search the working directory.
    private static readonly ImmutableArray<string> BuiltinIds = ["vb6", "vbaode", "vba"];

    public static ImmutableArray<ProfileSnapshot> Builtins { get; } = LoadBuiltins();

    public static bool IsBundleId(string id) => id.Length > 0 && id != "none" && ProfilePaths.IsSafeReference(id)
        && id[0] is >= 'a' and <= 'z'
        && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    // Discovery returns untrusted candidates only. No manifest or payload is opened here.
    public static ImmutableArray<InstalledProfileCandidate> DiscoverInstalled(string userDataDirectory)
    {
        var root = Path.Combine(Path.GetFullPath(userDataDirectory), "profiles");
        if (!Directory.Exists(root))
            return [];

        return Directory.EnumerateDirectories(root)
            .Where(path => IsBundleId(Path.GetFileName(path)))
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(path => new InstalledProfileCandidate(Path.GetFileName(path), path))
            .ToImmutableArray();
    }

    internal static ProfileSnapshot ParseManifest(JsonElement manifest, string source, string? installedId = null) =>
        new ProfileManifest(source).Parse(manifest, installedId);

    internal static ProfileSnapshot ParseDirectoryManifest(JsonElement manifest, string directory, string? installedId = null)
    {
        var source = Path.Combine(Path.GetFullPath(directory), "profile.json");
        var parser = new ProfileManifest(source);
        var snapshot = parser.Parse(manifest, installedId);
        parser.ValidatePaths(snapshot, directory);
        return snapshot;
    }

    private static ImmutableArray<ProfileSnapshot> LoadBuiltins()
    {
        var result = ImmutableArray.CreateBuilder<ProfileSnapshot>();
        foreach (var id in BuiltinIds)
        {
            var source = $"HexIDE.IDE.Profiles.Builtins.{id}.json";
            using var stream = typeof(ProfileCatalog).Assembly.GetManifestResourceStream(source)
                ?? throw new InvalidOperationException(source);
            using var document = JsonDocument.Parse(stream);
            var manifest = document.RootElement;
            result.Add(ParseManifest(manifest, source));
        }
        return result.ToImmutable();
    }
}
