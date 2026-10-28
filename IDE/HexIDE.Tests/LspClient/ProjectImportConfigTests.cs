using HexIDE.Lsp;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.LspClient;

public sealed class ProjectImportConfigTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hexide-import-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("{}", "command")]
    [InlineData("{\"command\":\" \"}", "command")]
    [InlineData("{\"command\":\"probe.import\"}", "arguments")]
    [InlineData("{\"command\":\"probe.import\",\"arguments\":{}}", "arguments")]
    [InlineData("{\"command\":\"probe.import\",\"arguments\":null}", "arguments")]
    public void InvalidDeclarationReportsTheSpecificFieldWithoutRejectingTheServer(string declaration, string field)
    {
        var result = Load(declaration);
        result.Entries.Should().ContainSingle();
        result.Problems.Should().Contain(p => p.Kind == LanguageServerConfigProblemKind.Configuration
            && !p.EntryRejected && p.Message.Contains("projectImport") && p.Message.Contains(field));
    }

    [Fact]
    public void ValidDeclarationIsRecognisedWithoutAnIgnoredFieldWarning()
    {
        var result = Load("""{"command":"probe.import","arguments":[{"uri":"{uri}","text":"{text}","overwrite":true}]}""");
        result.Entries.Should().ContainSingle();
        result.Problems.Should().NotContain(p => p.Kind == LanguageServerConfigProblemKind.UnrecognisedField);
        result.Problems.Should().BeEmpty();
        var registrations = new LanguageServerRegistrationFactory(NullLoggerFactory.Instance).Create(result.Entries);
        registrations.Should().ContainSingle();
        registrations[0].ProjectImport!.Command.Should().Be("probe.import");
    }

    private LanguageServerConfigResult Load(string declaration)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "lsp-servers.json");
        File.WriteAllText(path, $$"""
            {"version":1,"servers":[{"id":"probe","extensions":[".bas"],"languageId":"vb6",
            "transport":"stdio","command":"probe","projectImport":{{declaration}}}]}
            """);
        return new LanguageServerConfigLoader(path, NullLogger<LanguageServerConfigLoader>.Instance).Load([]);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
