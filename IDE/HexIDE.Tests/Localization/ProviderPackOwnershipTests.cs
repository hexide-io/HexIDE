using System.Text.RegularExpressions;
using HexIDE.Addins;
using HexIDE.Localization;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Localization;

public class ProviderPackOwnershipTests
{
    [Fact]
    public void Every_in_tree_area_is_guarded() => InTreeLanguagePacks.Areas
        .Select(area => Path.GetRelativePath(InTreeLanguagePacks.IdeDirectory, area).Replace('\\', '/'))
        .Should().Equal("HexIDE.Runtime/Localization/Packs", "HexIDE/Localization/Packs");

    [Fact]
    public void Areas_never_overlap_and_the_provider_stays_inside_its_grant()
    {
        var keys = new HashSet<string>();
        var grant = RegistrationGrants.All.Single(g => g.OwnerId == RegistrationGrants.BasicProvider);
        foreach (var area in InTreeLanguagePacks.Areas)
        foreach (var key in InTreeLanguagePacks.Read(Path.Combine(area, "en.json")).Keys)
        {
            keys.Add(key).Should().BeTrue($"{key} must belong to exactly one area");
            if (area.Contains("HexIDE.Runtime")) grant.CoversLanguagePackKey(key).Should().BeTrue(key);
        }
    }

    [Fact]
    public void Every_provider_string_literal_is_owned_by_the_provider()
    {
        var provider = ProviderKeys();
        foreach (var file in Sources("HexIDE.Runtime"))
        foreach (var key in Literals(file).Where(key => key.StartsWith("Str.", StringComparison.Ordinal)))
            provider.Should().Contain(key, file);
    }

    [Fact]
    public void IDE_readers_of_provider_text_can_only_shrink()
    {
        string[] allowed =
        [
            "IDE/ProgramHost.cs", "IDE/Vb6ToolchainService.cs", "Localization/BrandText.cs",
            "Projects/ProjectService.cs", "Projects/ProjectTypeNames.cs",
            "Projects/Vb6ProjectType.cs", "Localization/LocalizationService.cs",
            "VisualDesigner/ViewModels/PropertiesToolViewModel.cs",
        ];
        var provider = ProviderKeys();
        foreach (var file in Sources("HexIDE"))
        {
            var relative = Path.GetRelativePath(Path.Combine(InTreeLanguagePacks.IdeDirectory, "HexIDE"), file).Replace('\\', '/');
            if (relative == "Addins/RegistrationGrants.cs") continue; // The grant declares ownership; it doesn't read text.
            if (Literals(file).Any(key => key != "Str." && key.StartsWith("Str.", StringComparison.Ordinal)
                    && provider.Any(owned => owned.StartsWith(key, StringComparison.Ordinal))))
                allowed.Should().Contain(relative, "a new IDE reader couples the shell to provider text");
        }
    }

    [Fact]
    public void Standalone_reads_only_its_two_provider_keys()
    {
        var keys = Sources("HexIDE.Standalone").SelectMany(Literals)
            .Where(key => key.StartsWith("Str.", StringComparison.Ordinal)).ToHashSet();
        keys.Should().BeEquivalentTo(["Str.Standalone.WaylandUnavailableEnvironment", "Str.ProjectRunner.NotImplementedYet"]);
        ProviderKeys().Should().Contain(keys);
    }

    private static HashSet<string> ProviderKeys() => InTreeLanguagePacks.Read(Path.Combine(
        InTreeLanguagePacks.IdeDirectory, "HexIDE.Runtime", "Localization", "Packs", "en.json")).Keys.ToHashSet();

    private static IEnumerable<string> Sources(string project) => Directory.EnumerateFiles(
        Path.Combine(InTreeLanguagePacks.IdeDirectory, project), "*", SearchOption.AllDirectories)
        .Where(file => Path.GetExtension(file) is ".cs" or ".axaml")
        .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static IEnumerable<string> Literals(string file) => Path.GetExtension(file) == ".cs"
        ? CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>().Where(node => node.Kind() == SyntaxKind.StringLiteralExpression)
            .Select(node => node.Token.ValueText)
        : Regex.Matches(File.ReadAllText(file), @"Str\.[A-Za-z0-9_.]+").Select(match => match.Value);
}
