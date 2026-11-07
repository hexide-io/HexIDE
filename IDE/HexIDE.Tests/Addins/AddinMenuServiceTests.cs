using HexIDE.Addins;
using HexIDE.Tests.Sidecar;

namespace HexIDE.Tests.Addins;

public sealed class AddinMenuServiceTests
{
    [Theory]
    [InlineData("File/Export")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("/")]
    public void Unknown_location_is_logged_and_dropped_without_removing_the_command(string path)
    {
        using var log = new CapturedLog();
        var menus = new AddinMenuService();
        var commands = new AddinCommandService();
        var invoked = false;
        using var command = commands.Register("export", "Export", () => invoked = true, null, null);
        using var placement = menus.Add(path, "Export", null, () => invoked = true);

        menus.ToolsItems.Should().BeEmpty();
        menus.AddInsItems.Should().BeEmpty();
        var warning = log.Messages.Should().ContainSingle().Subject;
        warning.Should().Contain(typeof(AddinMenuServiceTests).Assembly.GetName().Name!)
            .And.Contain($"\"{path}\"");
        commands.Commands.Should().ContainSingle().Which.OnInvoke();
        invoked.Should().BeTrue();
        placement.Dispose();
    }

    [Theory]
    [InlineData("Tools")]
    [InlineData("tools")]
    [InlineData("AddIns")]
    [InlineData("addins/Export")]
    public void Supported_locations_keep_their_placement_and_disposal(string path)
    {
        using var log = new CapturedLog();
        var menus = new AddinMenuService();
        var invoked = false;
        var placement = menus.Add(path, "Export", null, () => invoked = true);
        var parent = path.StartsWith("Tools", StringComparison.OrdinalIgnoreCase)
            ? menus.ToolsItems : menus.AddInsItems;
        var item = parent.Should().ContainSingle().Subject;
        if (path.Contains('/'))
            item = item.SubItems.Should().ContainSingle().Subject;
        item.Command!.Execute(null);
        invoked.Should().BeTrue();
        log.Messages.Should().BeEmpty();
        placement.Dispose();
        parent.Should().BeEmpty();
    }
}
