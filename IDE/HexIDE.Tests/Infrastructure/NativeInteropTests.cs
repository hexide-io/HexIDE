using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

public class NativeInteropTests
{
    [Fact]
    public void Toolbar_caption_dispatch_guards_GDI_and_retains_portable_fallback()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Runtime",
            "BuiltinControls", "VBToolbar.cs")).ReplaceLineEndings("\n");
        source.Should().Contain("if (OperatingSystem.IsWindows())\n            return ToolbarCaptionMetrics.Measure");
        source.Should().Contain("extent += Math.Round(advance * scale)");
    }

    [Theory]
    [InlineData("HexIDE", "Controls/MDI/MDIWindow.axaml.cs", 2)]
    [InlineData("HexIDE.Desktop", "ConsoleOutput.cs", 5)]
    [InlineData("HexIDE.Desktop", "Server/ToolFailures.cs", 1)]
    [InlineData("HexIDE.Desktop", "Server/X11WindowManager.cs", 6)]
    [InlineData("HexIDE.Runtime", "TypeLibrary/TypeLibReader.cs", 2)]
    [InlineData("HexIDE.Runtime", "BuiltinControls/ToolbarCaptionMetrics.cs", 6)]
    [InlineData("HexIDE.Lsp", "Transports/LaunchedServerGuard.cs", 2)]
    [InlineData("HexIDE.LspGuard", "Program.cs", 4)]
    public void NativeDeclarationsUseSourceGeneration(string project, string file, int count)
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", project, file));
        source.Should().NotContain("[DllImport(");
        Regex.Matches(source, @"\[LibraryImport\(").Count.Should().Be(count);
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        var imports = tree.GetRoot(TestContext.Current.CancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.AttributeLists.ToString().Contains("LibraryImport"));
        foreach (var method in imports)
        {
            if (method.ParameterList.Parameters.Any(parameter => parameter.Type?.ToString() == "string"))
                method.AttributeLists.ToString().Should().ContainAny("StringMarshalling.Utf16", "StringMarshalling.Utf8");
            if (method.ReturnType.ToString() == "bool")
                method.AttributeLists.ToString().Should().Contain("return: MarshalAs(UnmanagedType.Bool)");
        }
    }

    [Theory]
    [InlineData("HexIDE")]
    [InlineData("HexIDE.Desktop")]
    [InlineData("HexIDE.Runtime")]
    [InlineData("HexIDE.Lsp")]
    [InlineData("HexIDE.LspGuard")]
    public void UnsafeCompilationIsForGeneratedCodeOnly(string project)
    {
        var folder = Path.Combine(RepoTree.Root(), "IDE", project);
        var config = XDocument.Load(Path.Combine(folder, project + ".csproj"));
        config.Descendants("AllowUnsafeBlocks").Should().ContainSingle().Which.Value.Should().Be("true");
        var files = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(folder, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"));
        files.Should().NotBeEmpty();
        foreach (var file in files)
            ContainsUnsafe(File.ReadAllText(file)).Should().BeFalse(file);
    }

    private static bool ContainsUnsafe(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        var root = tree.GetRoot(TestContext.Current.CancellationToken);
        return root.DescendantTokens().Any(token => token.Kind() == SyntaxKind.UnsafeKeyword)
            || root.DescendantTrivia().Where(trivia => trivia.Kind() == SyntaxKind.DisabledTextTrivia)
                .Any(trivia => ContainsUnsafe(trivia.ToFullString()));
    }

    [Theory]
    [InlineData("class C { unsafe void M() {} }", true)]
    [InlineData("class C { void M() { unsafe {} } }", true)]
    [InlineData("#if OTHER\nunsafe class C {}\n#endif", true)]
    [InlineData("// unsafe\nclass C { string S = \"unsafe\"; }", false)]
    public void UnsafeGuardRecognizesCodeRatherThanCommentsOrStrings(string source, bool expected)
        => ContainsUnsafe(source).Should().Be(expected);
}
