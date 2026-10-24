using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

public class UnsavedProjectLaunchTests
{
    [Theory]
    [InlineData("--newproject-unsaved")]
    [InlineData("/newproject-unsaved")]
    [InlineData("--NEWPROJECT-UNSAVED")]
    public void UnsavedLaunchIsAcceptedAndDiscoverable(string flag)
    {
        ServerOptions.Options.Should().Contain(o => o.Name == "newproject-unsaved",
            "a caller needs an explicit route to a project with no files");
        var unsaved = typeof(ServerOptions).GetProperty("NewProjectUnsaved")!;
        unsaved.SetValue(null, false);
        ServerOptions.ParseArgs([flag], Environment.CurrentDirectory);
        unsaved.GetValue(null).Should().Be(true);
        ServerOptions.HelpText(new ConsoleTarget(false, 120)).Should().Contain("--newproject-unsaved");
    }

    [Fact]
    public void UnsavedLaunchCreatesWithoutSavingBeforeTheSavedLaunchBranch()
    {
        // Like the neighbouring launch-wiring guards: DesktopStartup is not linked into this suite.
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs"))
            .ReplaceLineEndings("\n");
        source.Should().Contain("if (ServerOptions.NewProjectUnsaved)");
        var start = source.IndexOf("if (ServerOptions.NewProjectUnsaved)", StringComparison.Ordinal);
        var end = source.IndexOf("else if (ServerOptions.NewProject)", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var branch = source[start..end];
        branch.Should().Contain("Static.StartupProjectHook = (ps, _) => ps.CreateNewProject(IProjectTemplate.StandardEXE);");
        branch.Should().NotContain("SaveProject");
    }
}
