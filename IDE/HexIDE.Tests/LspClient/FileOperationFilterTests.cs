using System.Text.Json;
using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

public class FileOperationFilterTests
{
    [Theory]
    [InlineData("**/*.bas", "file:///Module.bas", true)]
    [InlineData("**/*.bas", "file:///a/b/Module.bas", true)]
    [InlineData("/*.bas", "file:///a/Module.bas", false)]
    [InlineData("/**/*.bas", "file:///Module.bas", true)]
    [InlineData("**/*.{bas,cls}", "file:///a/Test.cls", true)]
    [InlineData("**/*.{bas,cls}", "file:///a/Test.frm", false)]
    [InlineData("**/Module?.bas", "file:///a/Module1.bas", true)]
    [InlineData("**/Module?.bas", "file:///a/Module12.bas", false)]
    [InlineData("**/Module[0-9].bas", "file:///a/Module2.bas", true)]
    [InlineData("**/Module[!0-9].bas", "file:///a/Modulex.bas", true)]
    [InlineData("**/Module[!0-9].bas", "file:///a/Module2.bas", false)]
    [InlineData("**/A B.bas", "file:///a/A%20B.bas", true)]
    [InlineData("**/*.bas", "file:///a/TEST.BAS", false)]
    [InlineData("**/[", "file:///a/Test.bas", false)]
    [InlineData("**/{bas", "file:///a/Test.bas", false)]
    public void MatchesProtocolGlob(string glob, string uri, bool expected)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["workspace"] = new { fileOperations = new { didCreate = new { filters = new[] { new { pattern = new { glob } } } } } }
        }));
        FileOperationFilters.Matches(doc.RootElement, "didCreate", uri).Should().Be(expected);
    }
}
