using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Profiles;

namespace HexIDE.Tests.Localization;

public class ProviderPackTests
{
    [AvaloniaFact]
    public void Every_declared_language_and_region_resolves_the_provider_area()
    {
        var target = Environment.GetEnvironmentVariable("HEXIDE_MAP_DUMP");
        if (target is not null) Directory.CreateDirectory(target);
        foreach (var name in new[] { "HexIDE", "Acme" })
        {
            var service = InTreeLanguagePacks.Service(name == "HexIDE" ? DistributionIdentity.Stock : new DistributionIdentity("acme", name));
            foreach (var id in LanguageManifest.Packs.Select(p => p.Id)
                         .Concat(LanguageManifest.Regions.Values.SelectMany(r => r).Select(r => r.Id)).Distinct())
            {
                service.TryResolveMap(id, out var map, out _).Should().BeTrue(id);
                map.Should().ContainKey("Str.PropDesc.Caption").And.ContainKey("Str.Standalone.WaylandUnavailableEnvironment");
                if (target is not null) File.WriteAllText(Path.Combine(target, name + "-" + id + ".json"), JsonSerializer.Serialize(map));
            }
        }
    }
}
