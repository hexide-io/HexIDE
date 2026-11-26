namespace HexIDE.Runtime.Integration.Tests.Localization;

public class BasicProviderLanguagePackTests
{
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
