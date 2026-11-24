using System.Text.RegularExpressions;
using HexIDE.Addins;
using HexIDE.AddinPacker.Interactive;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Addins;

/// <summary>
/// hexide-pack (#5555) holds the prompts and Spectre.Console. HexIDE.AddinPacker, which the Desktop build packs with
/// and the release's signing job runs next to the first-party key, holds neither, and hexide-pack only gathers a
/// command line for <c>PackerCli.Run</c>, so there is one signing path. These tests keep it that way, and keep
/// hexide-pack's table of options in step with what the packer reads.
/// </summary>
public sealed class PackerFrontEndBoundaryTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("packer-table-").FullName;

    public void Dispose() => Directory.Delete(_work, true);

    private static string Read(params string[] path) => File.ReadAllText(Path.Combine([RepoTree.Root(), .. path]));

    [Fact]
    public void The_packer_takes_no_packages()
    {
        Read("IDE", "HexIDE.AddinPacker", "HexIDE.AddinPacker.csproj").Should().NotContain("<PackageReference",
            "every package the packer builds with is code trusted with the first-party key in the release's signing job");
    }

    [Fact]
    public void Neither_the_Desktop_build_nor_any_workflow_builds_or_runs_hexide_pack()
    {
        Read("IDE", "HexIDE.Desktop", "HexIDE.Desktop.csproj").Should().NotContain("AddinPacker.Interactive");
        foreach (var workflow in Directory.GetFiles(Path.Combine(RepoTree.Root(), ".github", "workflows"), "*.y*ml"))
            File.ReadAllText(workflow).Should().NotContain("AddinPacker.Interactive", workflow)
                .And.NotContain("hexide-pack", workflow);
    }

    [Fact]
    public void The_table_names_exactly_the_commands_and_options_PackerCli_reads()
    {
        // PackerCli's single-word string literals are its command names and the option names it reads.
        var literals = Regex.Matches(Read("IDE", "HexIDE.AddinPacker", "PackerCli.cs"), "\"([a-z][a-z0-9-]*)\"")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var commands = PackerCommands.All.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var options = PackerCommands.All.SelectMany(c => c.Options).Where(o => !o.Positional).Select(o => o.Name)
            .ToHashSet(StringComparer.Ordinal);

        commands.Should().BeSubsetOf(literals);
        literals.Except(commands).Should().BeEquivalentTo(options);
    }

    public static TheoryData<string, string> RequiredOptions()
    {
        var data = new TheoryData<string, string>();
        foreach (var command in PackerCommands.All)
            foreach (var option in command.Options.Where(o => o.Required))
                data.Add(command.Name, option.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(RequiredOptions))]
    public void Each_required_option_is_one_the_packer_refuses_to_run_without(string commandName, string optionName)
    {
        var command = PackerCommands.Find(commandName)!;
        var others = command.Options.Where(o => o.Required && o.Name != optionName)
            .ToDictionary(o => o.Name, _ => new List<string> { "x" });

        var (exitCode, _, error) = PackerCliRefusalTests.RunPacker(_work, HexidePack.Arguments(command, others));

        exitCode.Should().Be(1);
        error.Should().Contain(command.Options.Single(o => o.Name == optionName).Display);
        Directory.EnumerateFileSystemEntries(_work).Should().BeEmpty("a refusal writes nothing");
    }

    [Fact]
    public void The_capability_tick_list_is_the_set_HexIDE_knows()
    {
        var source = Read("IDE", "HexIDE.Core", "Addins", "AddinCapabilities.cs");
        var known = Regex.Match(source, @"IsKnown\(string\? capability\) => capability is(?<names>[^;]*);").Groups["names"].Value;

        PackerCommands.Capabilities.Should().OnlyContain(c => AddinCapabilities.IsKnown(c));
        Regex.Matches(known, "\"([^\"]+)\"").Select(m => m.Groups[1].Value).Should()
            .BeEquivalentTo(PackerCommands.Capabilities);
    }
}
