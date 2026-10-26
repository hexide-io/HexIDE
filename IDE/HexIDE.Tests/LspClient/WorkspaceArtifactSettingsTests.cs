using System.Text.Json;
using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

public sealed class WorkspaceArtifactSettingsTests
{
    [Fact]
    public void ProviderSettingsSurviveReadingAndRegistration()
    {
        const string json = """
            {"version":1,"servers":[{"id":"clangd","transport":"stdio","command":"clangd",
              "workspaceArtifact":{"path":"compile_commands.json","provider":"clangd",
                "settings":{"arguments":["clang","-DHEXIDE_PROBE"]}}}]}
            """;
        var config = JsonSerializer.Deserialize(json, LanguageServerConfigJsonContext.Default.LanguageServerConfigFile)!;
        using var logs = Microsoft.Extensions.Logging.LoggerFactory.Create(_ => { });
        var registration = new LanguageServerRegistrationFactory(logs).Create(config.Servers!).Single();
        registration.WorkspaceArtifact!.Settings!.Value.GetProperty("arguments")[1].GetString()
            .Should().Be("-DHEXIDE_PROBE");
    }
}
