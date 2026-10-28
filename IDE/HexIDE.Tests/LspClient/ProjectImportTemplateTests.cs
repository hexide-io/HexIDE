using System.Text.Json;
using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

public sealed class ProjectImportTemplateTests
{
    [Fact]
    public void ExpansionPreservesTypesAndEscapesProjectTextWithoutRecursiveSubstitution()
    {
        using var document = JsonDocument.Parse("""
            [{"uri":"{uri}","text":"{text}","overwrite":true,"nested":[null,12,"literal"],"{uri}":"prefix {uri}"}]
            """);
        var spec = new ProjectImportSpec("probe.import", document.RootElement);
        var text = "Name=\"{uri}\"\r\nModule=Module1; sub\\Module1.bas\r\n";
        var expanded = spec.ExpandArguments("file:///project/Test.vbp", text).Single();
        expanded.GetProperty("uri").GetString().Should().Be("file:///project/Test.vbp");
        expanded.GetProperty("text").GetString().Should().Be(text);
        expanded.GetProperty("overwrite").GetBoolean().Should().BeTrue();
        expanded.GetProperty("nested")[0].ValueKind.Should().Be(JsonValueKind.Null);
        expanded.GetProperty("nested")[1].GetInt32().Should().Be(12);
        expanded.GetProperty("nested")[2].GetString().Should().Be("literal");
        expanded.GetProperty("{uri}").GetString().Should().Be("prefix {uri}");
        document.RootElement[0].GetProperty("text").GetString().Should().Be("{text}");
    }

    [Fact]
    public void ReaderOwnsTheTemplateAfterTheConfigurationDocumentIsDisposed()
    {
        ProjectImportSpec? spec;
        using (var document = JsonDocument.Parse("""[{"text":"{text}"}]"""))
        {
            ProjectImportSpecReader.TryRead("probe", new ProjectImportEntry
            {
                Command = " probe.import ", Arguments = document.RootElement,
            }, out spec, out var problems).Should().BeTrue();
            problems.Should().BeEmpty();
        }
        spec!.Command.Should().Be("probe.import");
        spec.ExpandArguments("uri", "unsaved")[0].GetProperty("text").GetString().Should().Be("unsaved");
    }
}
