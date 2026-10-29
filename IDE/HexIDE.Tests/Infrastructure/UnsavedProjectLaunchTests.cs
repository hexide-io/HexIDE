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

    [Fact]
    public void SavedLaunchSavesTheProjectItCreatedNotTheStartupProject()
    {
        // A project crash recovery restored before the hook ran stays the startup project, and saving that one wrote
        // its recovered work over its files and left the new project unsaved (hexide-io/HexIDE#499).
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs"))
            .ReplaceLineEndings("\n");
        var start = source.IndexOf("else if (ServerOptions.NewProject)", StringComparison.Ordinal);
        var end = source.IndexOf("else if (ServerOptions.ProjectPath", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        end.Should().BeGreaterThan(start);
        var branch = source[start..end];
        branch.Should().Contain("SaveProjectToDirectory");
        branch.Should().NotContain("StartupProject is", "the hook saves the project it created, wherever startup is");
    }
}
