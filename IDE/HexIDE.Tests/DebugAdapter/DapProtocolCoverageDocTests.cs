using System.Text;

namespace HexIDE.Tests.DebugAdapter;

public class DapProtocolCoverageDocTests
{
    [Fact]
    public void SchemaIntegrityAndRequiredAvailability()
    {
        var messages = DapSpecificationModel.All();
        DapSpecificationModel.Failure.Should().BeNull("a corrupt schema must fail even outside CI");
        if (Environment.GetEnvironmentVariable("HEXIDE_REQUIRE_FOREIGN_LSP") is "1" or "true" or "yes")
            messages.Should().NotBeNull("CI must obtain the DAP schema rather than skip its coverage guard");
    }

    [DapSchemaFact]
    public void CoverageMatchesSchemaAndClientCode()
    {
        var path = Path.Combine(DapSpecificationModel.Root(), "docs", "dap-client.md");
        var text = File.ReadAllText(path).ReplaceLineEndings("\n");
        text.Should().Contain(DapSpecificationModel.Commit).And.Contain(DapSpecificationModel.Sha256,
            "schema provenance must stay in step with the fixture pin");
        var start = text.IndexOf(DapCoverageDocument.Start, StringComparison.Ordinal);
        var end = text.IndexOf(DapCoverageDocument.End, StringComparison.Ordinal);
        var generated = DapCoverageDocument.Generate(DapSpecificationModel.All()!);
        if (Environment.GetEnvironmentVariable("HEXIDE_UPDATE_DAP_COVERAGE") == "1" && start >= 0 && end >= start)
        {
            text = text[..start] + generated + text[(end + DapCoverageDocument.End.Length)..];
            File.WriteAllText(path, text);
        }
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThanOrEqualTo(start);
        var actual = text[start..(text.IndexOf(DapCoverageDocument.End, StringComparison.Ordinal) + DapCoverageDocument.End.Length)];
        actual.Should().Be(generated, "regenerate the table: missing, invented, duplicated, misdirected or stale rows/counts are drift");
    }

    [Fact]
    public void CorruptSchemaIsRefused()
    {
        var action = () => DapSpecificationModel.Verify(Encoding.UTF8.GetBytes("{}"));
        action.Should().Throw<InvalidDataException>().WithMessage("*checksum mismatch*");
    }
}
