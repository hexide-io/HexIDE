using HexIDE.Addins;
using HexIDE.Localization;
using HexIDE.Runtime;

namespace HexIDE.Integration.Tests.Localization;

public class BasicProviderLanguagePackTests
{
    [AvaloniaFact]
    public void Provider_initialization_supplies_German_property_descriptions_until_disposed()
    {
        var areas = new LanguagePackAreas();
        var packs = new AddinLanguagePackService { Areas = areas };
        var host = Substitute.For<IHexIdeHost>();
        host.RegisterResources(Arg.Any<Uri>()).Returns(_ => Substitute.For<IDisposable>());
        host.RegisterLanguagePacks(Arg.Any<Uri>()).Returns(call => packs.Register(call.Arg<Uri>(),
            HexIDE.Desktop.BasicProviderComposition.Origin, typeof(BasicProvider).Assembly));
        using var provider = new BasicProvider();
        var localization = new LocalizationService { Areas = areas };
        localization.Apply("de");
        localization.GetPropertyDescription("Caption").Should().BeNull();

        provider.Initialize(host);

        localization.GetPropertyDescription("Caption").Should().Be("Der Text in der Titelleiste des Objekts oder neben seinem Symbol.");
        areas.Areas.Should().ContainSingle().Which.English.Should().HaveCount(113);
        provider.Dispose();
        localization.GetPropertyDescription("Caption").Should().BeNull();
    }

    [Fact]
    public void Standalone_resolves_both_messages_from_its_embedded_provider_English()
    {
        using var stream = typeof(HexIDE.Standalone.App).Assembly
            .GetManifestResourceStream("HexIDE.Standalone.ErrorStrings.json");
        stream.Should().NotBeNull();
        using var pack = System.Text.Json.JsonDocument.Parse(stream!);
        var strings = pack.RootElement.GetProperty("strings");
        strings.EnumerateObject().Should().HaveCount(113);
        foreach (var key in new[] { "Str.Standalone.WaylandUnavailableEnvironment", "Str.ProjectRunner.NotImplementedYet" })
            strings.GetProperty(key).GetString().Should().NotBeNullOrWhiteSpace();
    }
}
