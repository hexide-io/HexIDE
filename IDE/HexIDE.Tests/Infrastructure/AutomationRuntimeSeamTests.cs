using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Holds automation's existing Runtime dependencies until #3411 supplies provider contracts (#4054).
/// It reads syntax, not a semantic model, so it sees a type only where the source spells its name: written out in
/// full, through a using, a global using, an alias or <c>using static</c>. It can't see a call to a Runtime extension
/// method whose class is never named. Automation imports several Runtime namespaces, so such a call would pass
/// unflagged; a reviewer still has to catch that case.
/// </summary>
public class AutomationRuntimeSeamTests
{
    private const string Provider = "leaves with the provider";
    private const string Controls = "waits on BuiltinControls/";
    private const string Components = "waits on Components/ and Serialization/";
    private const string Server = "HexIDE.Desktop/Server/";
    private const string Automation = "HexIDE/Automation/";

    // Inventory: https://github.com/hexide-io/HexIDE/issues/3411#issuecomment-6904319880
    // Empty since #5758 moved the last, DesignPositions, behind the positions service: automation names no runtime type.
    private static readonly (string Type, string Group, string[] Files)[] Allowed = [];

    [Fact]
    public void Automation_names_only_the_listed_runtime_types_in_the_listed_files()
    {
        var ide = Path.Join(RepoTree.Root(), "IDE");
        var types = SourceFiles(Path.Join(ide, "HexIDE.Runtime"))
            .SelectMany(file => DeclaredTypes(File.ReadAllText(file))).ToHashSet(StringComparer.Ordinal);
        types.Should().NotBeEmpty("the inventory comes from Runtime source, never the shared namespaces in Core");
        var actual = new HashSet<(string File, string Type)>();
        foreach (var folder in new[] { Server, Automation })
        {
            var project = folder.Split('/')[0];
            var globals = SourceFiles(Path.Join(ide, project))
                .SelectMany(file => Parse(File.ReadAllText(file)).DescendantNodes().OfType<UsingDirectiveSyntax>())
                .Where(use => use.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword)).Select(use => use.ToString()).ToArray();
            foreach (var file in SourceFiles(Path.Join(ide, folder)))
                foreach (var type in References(string.Join(Environment.NewLine, globals) + Environment.NewLine + File.ReadAllText(file), types))
                    actual.Add((Path.GetRelativePath(ide, file).Replace('\\', '/'), type));
        }

        var expected = Allowed.SelectMany(entry => entry.Files.Select(file => (File: file, entry.Type))).ToHashSet();
        var errors = actual.Except(expected).OrderBy(hit => hit.File).ThenBy(hit => hit.Type)
            .Select(hit => $"{hit.File}: {hit.Type} needs a Core contract or a line on the allowed list with a reason. "
                + $"Justify it under: {Provider}; {Controls}; {Components}.")
            .Concat(expected.Except(actual).OrderBy(hit => hit.File).ThenBy(hit => hit.Type)
                .Select(hit => $"{hit.File}: {hit.Type} no longer occurs; remove its allowed reference "
                    + $"({Allowed.Single(entry => entry.Type == hit.Type).Group}) so the list ratchets down."));
        errors.Should().BeEmpty("automation must not grow new provider dependencies or retain stale allowances");
    }

    [Theory]
    [InlineData("class C { HexIDE.Runtime.VBDebugConsole field; }", true)]
    [InlineData("class C { void M() => global::HexIDE.Runtime.VBDebugConsole.Clear(); }", true)]
    [InlineData("using HexIDE.Runtime; class C { VBDebugConsole field; }", true)]
    [InlineData("using Console = HexIDE.Runtime.VBDebugConsole; class C { }", true)]
    [InlineData("using R = HexIDE.Runtime; class C { R.VBDebugConsole field; }", true)]
    [InlineData("using static HexIDE.Runtime.VBDebugConsole; class C { }", true)]
    [InlineData("using HexIDE.Runtime; class C { void M() => VBDebugConsole.Clear(); }", true)]
    [InlineData("// HexIDE.Runtime.VBDebugConsole\nclass C { string S = \"HexIDE.Runtime.VBDebugConsole\"; }", false)]
    [InlineData("class C { VBDebugConsole field; }", false)]
    [InlineData("using HexIDE.Runtime; class C { object VBDebugConsole { get; } void M(C c) => c.VBDebugConsole.ToString(); }", false)]
    [InlineData("using HexIDE.Runtime; class C { HexIDE.Dialogs.VBDebugConsole field; }", false)]
    public void Guard_distinguishes_type_uses_from_comments_strings_and_member_names(string source, bool expected)
        => References(source, new HashSet<string> { "HexIDE.Runtime.VBDebugConsole" }).Any().Should().Be(expected);

    [Fact]
    public void Inventory_reads_namespaces_nested_types_and_delegates()
        => DeclaredTypes("namespace A { namespace B { class C { struct D {} } delegate void E(); } }")
            .Should().BeEquivalentTo("A.B.C", "A.B.C.D", "A.B.E");

    private static IEnumerable<string> SourceFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "obj" or "bin"));

    private static SyntaxNode Parse(string source) => CSharpSyntaxTree.ParseText(source,
        new CSharpParseOptions(preprocessorSymbols: ["DEBUG"]), cancellationToken: TestContext.Current.CancellationToken)
        .GetRoot(TestContext.Current.CancellationToken);

    private static IEnumerable<string> DeclaredTypes(string source) => Parse(source).DescendantNodes()
        .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
        .Select(node => string.Join(".", node.Ancestors().Reverse().Select(ancestor => ancestor switch
        {
            BaseNamespaceDeclarationSyntax ns => ns.Name.ToString(),
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            _ => null
        }).OfType<string>().Append(node is BaseTypeDeclarationSyntax declaration
            ? declaration.Identifier.ValueText : ((DelegateDeclarationSyntax)node).Identifier.ValueText)));

    private static string QualifiedName(SyntaxNode node) => node switch
    {
        SimpleNameSyntax name => name.Identifier.ValueText,
        QualifiedNameSyntax name => QualifiedName(name.Left) + "." + QualifiedName(name.Right),
        AliasQualifiedNameSyntax name when name.Alias.Identifier.ValueText == "global" => QualifiedName(name.Name),
        MemberAccessExpressionSyntax access => QualifiedName(access.Expression) + "." + QualifiedName(access.Name),
        _ => ""
    };

    private static HashSet<string> References(string source, IReadOnlySet<string> types)
    {
        var root = Parse(source);
        var usings = root.DescendantNodes().OfType<UsingDirectiveSyntax>().ToArray();
        var imports = usings
            .Where(use => use.Alias == null && use.StaticKeyword.IsKind(SyntaxKind.None))
            .Select(use => use.Name?.ToString()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var aliases = usings.Where(use => use.Alias != null && use.Name != null)
            .ToDictionary(use => use.Alias!.Name.Identifier.ValueText, use => QualifiedName(use.Name!), StringComparer.Ordinal);
        var found = usings.Where(use => use.Name != null).Select(use => QualifiedName(use.Name!))
            .Where(types.Contains).ToHashSet(StringComparer.Ordinal);
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (name.Ancestors().Any(node => node is UsingDirectiveSyntax))
                continue;
            SyntaxNode qualified = name;
            while (qualified.Parent is QualifiedNameSyntax or AliasQualifiedNameSyntax
                || qualified.Parent is MemberAccessExpressionSyntax access && access.Name == qualified)
                qualified = qualified.Parent;
            var spelling = QualifiedName(qualified);
            var first = spelling.Split('.')[0];
            if (aliases.TryGetValue(first, out var target))
                spelling = target + spelling[first.Length..];
            if (types.Contains(spelling))
                found.Add(spelling);
            else if (qualified == name && (name.Parent is not MemberAccessExpressionSyntax accessName || accessName.Expression == name))
            {
                foreach (var ns in imports)
                    if (types.Contains(ns + "." + name.Identifier.ValueText))
                        found.Add(ns + "." + name.Identifier.ValueText);
            }
        }
        return found;
    }
}
