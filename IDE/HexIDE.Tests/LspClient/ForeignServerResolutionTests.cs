namespace HexIDE.Tests.LspClient;

public class ForeignServerResolutionTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("yes")]
    public void NoPathUsesThePinnedDownloadWithoutConsultingPath(string setting)
    {
        var lookup = Substitute.For<Func<string, string?>>();
        lookup("clangd").Returns("image-clangd");
        var result = ForeignServer.Cpp.Resolve(
            name => name == ForeignServer.NoPathVariable ? setting : null, lookup, () => "pinned-clangd");

        result.Should().Be(("pinned-clangd", "pinned download"));
        lookup.DidNotReceive().Invoke(Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    public void UnsetOrDisabledKeepsPathAheadOfThePin(string? setting)
    {
        var pinned = Substitute.For<Func<string?>>();
        var result = ForeignServer.Cpp.Resolve(
            name => name == ForeignServer.NoPathVariable ? setting : null, _ => "image-clangd", pinned);

        result.Should().Be(("image-clangd", "PATH"));
        pinned.DidNotReceive().Invoke();
    }

    [Theory]
    [InlineData("1")]
    [InlineData(null)]
    public void ExplicitVariableStillWins(string? setting)
    {
        var configured = typeof(ForeignServerResolutionTests).Assembly.Location;
        var lookup = Substitute.For<Func<string, string?>>();
        var pinned = Substitute.For<Func<string?>>();
        var result = ForeignServer.Cpp.Resolve(name => name == ForeignServer.Cpp.PathVariable ? configured
            : name == ForeignServer.NoPathVariable ? setting : null, lookup, pinned);

        result.Should().Be((configured, "variable HEXIDE_CPP_LSP"));
        lookup.DidNotReceive().Invoke(Arg.Any<string>());
        pinned.DidNotReceive().Invoke();
    }

    [Fact]
    public void MissingExplicitVariableDoesNotSilentlyFallBack()
    {
        var result = ForeignServer.Cpp.Resolve(name => name == ForeignServer.Cpp.PathVariable
            ? Path.Combine(AppContext.BaseDirectory, "absent-clangd") : "1", _ => "image", () => "pin");
        result.Should().Be(((string?)null, "variable HEXIDE_CPP_LSP"));
    }

    [Fact]
    public void MissingPathFallsBackToThePin()
    {
        ForeignServer.Cpp.Resolve(_ => null, _ => null, () => "pin")
            .Should().Be(("pin", "pinned download"));
    }

    [Fact]
    public void JsonKeepsItsPinnedScriptAndNodeRuntimeLookup()
    {
        var lookup = Substitute.For<Func<string, string?>>();
        ForeignServer.Json.Resolve(name => name == ForeignServer.NoPathVariable ? "1" : null, lookup, () => "node")
            .Should().Be(("node", "pinned download"));
        lookup.DidNotReceive().Invoke(Arg.Any<string>());
    }
}
