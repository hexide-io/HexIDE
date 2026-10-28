using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

public class AbsoluteProjectLaunchTests
{
    [Theory]
    [InlineData("/tmp/launch project/Project.vbp")]
    [InlineData("/tmp/launch project/Group.vbg")]
    [InlineData("/tmp/launch project/Project.twinproj")]
    public void AbsoluteUnixProjectPathIsAcceptedAlongsideSlashFlags(string path)
    {
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
            typeof(ServerOptions).GetProperty("ProjectPath")!.SetValue(null, previousProject);
            typeof(ServerOptions).GetProperty("CaptureLsp")!.SetValue(null, previousCapture);
        }
    }
}
