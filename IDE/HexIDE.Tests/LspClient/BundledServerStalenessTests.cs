namespace HexIDE.Tests.LspClient;

public sealed class BundledServerStalenessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly DateTime _built = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly string _exe;

    public BundledServerStalenessTests()
    {
        Directory.CreateDirectory(_root);
        _exe = Path.Combine(_root, "server");
        File.WriteAllText(_exe, "");
        File.SetLastWriteTimeUtc(_exe, _built);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Source(string relativePath, int minutesAfterBuild)
    {
        var path = Path.Combine(_root, "LspServer", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        File.SetLastWriteTimeUtc(path, _built.AddMinutes(minutesAfterBuild));
    }

    [Fact]
    public void EditingServerTestsDoesNotMakeTheServerStale()
    {
        Source("HexIDE.VbLspServer/Server.cs", -1);
        Source("HexIDE.VbLspServer.Tests/ServerTests.cs", 2);

        BundledServer.StaleReason(_root, _exe).Should().BeNull();
    }

    [Theory]
    [InlineData("VisualBasic6Lexer.g4")]
    [InlineData("VisualBasic6Parser.g4")]
    public void EditingAGrammarMakesTheServerStale(string grammar)
    {
        Source($"HexIDE.VbLspServer/Grammar/{grammar}", 2);

        BundledServer.StaleReason(_root, _exe).Should().Contain("Rebuild it with:");
    }

    [Fact]
    public void EditingServerSourceMakesTheServerStale()
    {
        Source("HexIDE.VbLspServer/Server.cs", 2);

        BundledServer.StaleReason(_root, _exe).Should().Contain("Rebuild it with:");
    }

    [Theory]
    [InlineData("obj/Generated.cs")]
    [InlineData("bin/Generated.cs")]
    [InlineData("obj/Generated.g4")]
    [InlineData("bin/Generated.g4")]
    public void BuildOutputsDoNotMakeTheServerStale(string output)
    {
        Source($"HexIDE.VbLspServer/{output}", 2);

        BundledServer.StaleReason(_root, _exe).Should().BeNull();
    }

    [Theory]
    [InlineData("Server.cs")]
    [InlineData("Grammar/VisualBasic6Parser.g4")]
    public void SourcesWithinTheBuildSlackDoNotMakeTheServerStale(string source)
    {
        Source($"HexIDE.VbLspServer/{source}", 1);

        BundledServer.StaleReason(_root, _exe).Should().BeNull();
    }
}
