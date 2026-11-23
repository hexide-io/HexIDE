using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

public class ResolvedTempGuardTests
{
    private static readonly HashSet<string> AllowedFiles = new(StringComparer.Ordinal);

    [Fact]
    public void Tests_use_resolved_temp_paths_instead_of_hardcoded_macos_paths()
    {
        var root = RepoTree.Root();
        var violations = new List<string>();
        foreach (var tree in new[] { "IDE", "LspServer" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, tree), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var folders = relative.Split('/')[..^1];
            if (!folders.Any(folder => folder.EndsWith("Tests", StringComparison.Ordinal)) ||
                folders.Any(folder => folder is "bin" or "obj") ||
                relative == "IDE/HexIDE.Tests/Infrastructure/ResolvedTemp.cs" || AllowedFiles.Contains(relative))
                continue;

            violations.AddRange(ViolationLines(File.ReadAllText(file)).Select(line => $"{relative}:{line}"));
        }

        violations.Should().BeEmpty("tests should use ResolvedTemp for link-resolved temporary paths{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<int> ViolationLines(string source)
    {
        const string privateRoot = "/private/";
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        foreach (var expression in root.DescendantNodes().OfType<ExpressionSyntax>())
        {
            var value = expression switch
            {
                LiteralExpressionSyntax literal when literal.Kind() is SyntaxKind.StringLiteralExpression or
                    SyntaxKind.Utf8StringLiteralExpression => literal.Token.ValueText,
                InterpolatedStringExpressionSyntax interpolated when
                    interpolated.Contents.FirstOrDefault() is InterpolatedStringTextSyntax text => text.TextToken.ValueText,
                _ => null
            };
            if (value is null) continue;

            if (value.StartsWith(privateRoot + "tmp", StringComparison.Ordinal) ||
                value.StartsWith(privateRoot + "var", StringComparison.Ordinal) ||
                (value.StartsWith(privateRoot, StringComparison.Ordinal) &&
                 expression.Ancestors().OfType<ConditionalExpressionSyntax>()
                     .Any(conditional => conditional.WhenTrue.SpanStart == expression.SpanStart &&
                         conditional.Condition is InvocationExpressionSyntax
                         {
                             Expression: MemberAccessExpressionSyntax
                             {
                                 Expression: IdentifierNameSyntax { Identifier.ValueText: "OperatingSystem" },
                                 Name.Identifier.ValueText: "IsMacOS"
                             }
                         })))
            {
                yield return expression.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            }
        }
    }

    [Theory]
    [InlineData("""var path = "/private/tmp/project";""", 2)]
    [InlineData("""var path = @"/private/var/project";""", 2)]
    [InlineData("""var path = $"/private/var/{name}";""", 2)]
    [InlineData("""var path = OperatingSystem.IsMacOS() ? "/private/other/" + name : Path.GetTempPath();""", 2)]
    [InlineData("""// The expected path begins with "/private/var" on macOS.""", 0)]
    [InlineData("""var uri = "file:///private/project";""", 0)]
    public void Guard_finds_path_literals_and_reports_their_lines(string source, int line)
    {
        var lines = ViolationLines("class Fixture {\n" + source + "\n}");
        lines.Should().Equal(line == 0 ? Array.Empty<int>() : new[] { line });
    }
}
