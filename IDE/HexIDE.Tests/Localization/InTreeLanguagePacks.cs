using System.Text.Json;
using HexIDE.Localization;

namespace HexIDE.Tests.Localization;

internal static class InTreeLanguagePacks
{
    internal static string IdeDirectory
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "IDE", "HexIDE", "HexIDE.csproj")))
                    return Path.Combine(dir.FullName, "IDE");
            throw new DirectoryNotFoundException("Could not locate IDE projects.");
        }
    }

    internal static IEnumerable<string> Areas => Directory.EnumerateDirectories(IdeDirectory)
        .Where(dir => !Path.GetFileName(dir).EndsWith(".Tests", StringComparison.Ordinal))
        .Select(dir => Path.Combine(dir, "Localization", "Packs")).Where(Directory.Exists).Order(StringComparer.Ordinal);

    internal static Dictionary<string, string> Read(string file) => JsonSerializer.Deserialize(File.ReadAllText(file),
        LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;

    internal static Dictionary<string, string> Merged(string id) => Areas
        .Select(area => Path.Combine(area, id + ".json")).Where(File.Exists)
        .SelectMany(file => Read(file)).ToDictionary(pair => pair.Key, pair => pair.Value);

    internal static LocalizationService Service(DistributionIdentity? identity = null)
    {
        var areas = new LanguagePackAreas();
        RegisterProvider(areas);
        return new LocalizationService(identity: identity) { Areas = areas };
    }

    private static IDisposable RegisterProvider(LanguagePackAreas areas) => areas
        .Register("hexide.basic", "Basic", new Uri("avares://HexIDE.Runtime/Localization/Packs/"),
            HexIDE.Addins.RegistrationGrants.All.Single(g => g.OwnerId == "hexide.basic").CoversLanguagePackKey);
}
