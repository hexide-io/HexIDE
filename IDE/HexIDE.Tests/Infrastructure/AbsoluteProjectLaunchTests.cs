using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

public class AbsoluteProjectLaunchTests
{
    [Theory]
    [InlineData("Project.vbp")]
    [InlineData("Group.vbg")]
    [InlineData("Project.twinproj")]
    [InlineData("Project.rdproj")]
    [InlineData("Project.unknown")]
    public void AbsoluteUnixProjectPathIsAcceptedAlongsideSlashFlags(string path)
    {
        var directory = Path.Join(Path.GetTempPath(), "hexide-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        path = Path.Join(directory, path);
        File.WriteAllText(path, "");
        var previousProject = ServerOptions.ProjectPath;
        var previousCapture = ServerOptions.CaptureLsp;
        try
        {
            typeof(ServerOptions).GetProperty("ProjectPath")!.SetValue(null, null);
            typeof(ServerOptions).GetProperty("CaptureLsp")!.SetValue(null, false);

            ServerOptions.ParseArgs(["/capture-lsp", path], Environment.CurrentDirectory);

            ServerOptions.ProjectPath.Should().Be(Path.GetFullPath(path));
            ServerOptions.CaptureLsp.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
            typeof(ServerOptions).GetProperty("ProjectPath")!.SetValue(null, previousProject);
            typeof(ServerOptions).GetProperty("CaptureLsp")!.SetValue(null, previousCapture);
        }
    }
    [Theory]
    [InlineData("missing")]
    [InlineData("option-value")]
    [InlineData("dash")]
    public void Nonexistent_files_option_values_and_dash_arguments_are_not_projects(string kind)
    {
        var directory = Path.Join(Path.GetTempPath(), "hexide-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Join(directory, kind == "dash" ? "-Project.rdproj" : "Project.rdproj");
        var previousProject = ServerOptions.ProjectPath;
        var previousUserData = ServerOptions.UserDataDirectory;
        try
        {
            if (kind != "missing") File.WriteAllText(path, "");
            typeof(ServerOptions).GetProperty("ProjectPath")!.SetValue(null, null);
            string[] args = kind switch
            {
                "option-value" => ["--user-data-dir", path],
                "dash" => ["-Project.rdproj"],
                _ => [path],
            };
            ServerOptions.ParseArgs(args, directory);
            ServerOptions.ProjectPath.Should().BeNull();
        }
        finally
        {
            typeof(ServerOptions).GetProperty("ProjectPath")!.SetValue(null, previousProject);
            typeof(ServerOptions).GetProperty("UserDataDirectory")!.SetValue(null, previousUserData);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Startup_path_is_wired_to_discovery_and_window_owned_consent()
    {
        var source = File.ReadAllText(Path.Join(RepoTree.Root(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs"))
            .ReplaceLineEndings("\n");
        ServerOptions.HelpText(new ConsoleTarget(false, 120)).Should().Contain("[<project file>]");
        source.Should().Contain("startupProject.Open(ps, path)");
        source.Should().Contain("startupProject.Attach(desktop.MainWindow!, setup.ContributedProjects, loader.PromptPendingConsentAsync)");
    }

}
