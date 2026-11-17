using Avalonia.Headless.XUnit;
using HexIDE.Desktop;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Infrastructure;

public class NoVb6LaunchTests : CompositionTestBase
{
    [Theory]
    [InlineData("--no-vb6")]
    [InlineData("/no-vb6")]
    [InlineData("--NO-VB6")]
    public void TheFlagIsReadAndListed(string flag)
    {
        var property = typeof(ServerOptions).GetProperty("NoVb6");
        property.Should().NotBeNull();
        property!.SetValue(null, false);
        try
        {
            ServerOptions.ParseArgs([flag], Environment.CurrentDirectory);
            property.GetValue(null).Should().Be(true);
            ServerOptions.HelpText(new ConsoleTarget(false, 120)).Should().Contain("--no-vb6");
        }
        finally
        {
            property.SetValue(null, false);
        }
    }

    [Fact]
    public void TheFlagReachesTheCompositionInDebugBuildsOnly()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs"))
            .ReplaceLineEndings("\n");
        const string apply = "HexIDE.Static.WithoutVb6 = ServerOptions.NoVb6;";
        var at = source.IndexOf(apply, StringComparison.Ordinal);
        at.Should().BeGreaterThan(0);
        source.LastIndexOf("#if DEBUG", at, StringComparison.Ordinal).Should().BeGreaterThan(
            source.LastIndexOf("#endif", at, StringComparison.Ordinal), "a release build leaves the flag inert");
        source.Split(apply).Should().HaveCount(2, "there is only one startup assignment");
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheCompositionRegistersVb6OnlyWhenRequested(bool withoutVb6)
    {
        var property = typeof(Static).GetProperty("WithoutVb6");
        property.Should().NotBeNull();
        property!.SetValue(null, withoutVb6);
        try
        {
            var types = Composition.ContributedProjects;
            var templates = Composition.AddinProjectTemplateService.FirstParty;
            var project = types.Find("P.vbp");
            var group = types.FindGroup("G.vbg");
            var format = types.FormatForMembers(new ProjectDefinition(VBProjectType.EXE, "P"));
            if (withoutVb6)
            {
                project.Should().BeNull();
                group.Should().BeNull();
                templates.Should().BeEmpty();
                format.Should().BeNull();
            }
            else
            {
                project.Should().NotBeNull();
                group.Should().NotBeNull();
                templates.Should().NotBeEmpty();
                format.Should().NotBeNull().And.BeSameAs(project!.Type.Format);
                group!.Type.Format.Should().BeSameAs(format);
            }
        }
        finally
        {
            property.SetValue(null, false);
        }
    }
}
