using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using HexIDE.Tests.Infrastructure;
using HexIDE.IDE;
using HexIDE.Runtime.TypeLibrary;

namespace HexIDE.Tests.LspClient;

public sealed class TypeLibraryServicePathTests
{
    [Theory]
    [InlineData(@"\\host.invalid\share\remote.tlb")]
    [InlineData("//host.invalid/share/remote.tlb")]
    [InlineData(@"\\?\C:\remote.tlb")]
    [InlineData(@"\\.\pipe\remote")]
    public void UnsafePathIsMissingWithoutTouchingTheFileOrReader(string path)
    {
        using var log = new HexIDE.Tests.Sidecar.CapturedLog();
        var touches = new List<string>();
        var sut = new TypeLibraryService
        {
            FileExists = p => { touches.Add(p); return true; },
            Read = p => { touches.Add(p); return new("Remote", null, []); }
        };

        sut.LoadByPath(("", "1.0"), path, "Remote reference").Should().BeNull();
        touches.Should().BeEmpty();
        log.Messages.Should().Contain(m => m.Contains("Remote reference") && m.Contains("refused"));
    }

    [Fact]
    public void LocalFileStillLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), "local.tlb");
        var touches = new List<string>();
        var expected = new TypeLibInfo("Local", null, []);
        var sut = new TypeLibraryService
        {
            FileExists = p => { touches.Add(p); return true; },
            Read = p => { touches.Add(p); return expected; }
        };

        sut.LoadByPath(("", "1.0"), path, "Local reference").Should().BeSameAs(expected);
        touches.Should().Equal(path, path);
    }

    [Fact]
    public void ReaderHasNoRegisteringLoadTypeLibImport()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Runtime", "TypeLibrary", "TypeLibReader.cs"));
        var root = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken).GetRoot(TestContext.Current.CancellationToken);
        var read = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(m => m.Identifier.Text == "ReadCore");
        read.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Should().Contain(i => i.Expression.ToString() == "LoadTypeLibEx"
                && i.ArgumentList.Arguments[1].ToString() == "RegKindNone");
        root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(v => v.Identifier.Text == "RegKindNone").Initializer!.Value.ToString().Should().Be("2");
        source.Should().NotContain("EntryPoint = \"LoadTypeLib\"");
    }
}
