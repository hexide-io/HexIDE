using System.IO;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class ProfileFileRecoveryTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-profile-recovery-" + Guid.NewGuid().ToString("N"));

    public ProfileFileRecoveryTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    [Fact]
    public void SavingRetainsPreviousSessionBytesForRecovery()
    {
        var store = new DocumentSessionStore(dir);
        var project = Path.Combine(dir, "P.vbp");
        store.Save(project, [new OpenDocumentState("code", "Module1.bas", true)]);
        var path = Directory.GetFiles(dir, "*.json").Should().ContainSingle().Subject;
        var original = File.ReadAllBytes(path);
        store.Save(project, []);
        var recovery = Directory.GetFiles(dir, "*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(original);
        store.Load(project).Should().BeEmpty();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("reset")]
    [InlineData("named")]
    public void LayoutChangesRetainPreviousDiskBytes(string command)
    {
        var service = WindowStateService.ForDirectory(Substitute.For<ISettingsService>(), dir);
        var path = Path.Combine(dir, command == "named" ? "named-layouts.json" : "dock-layout.json");
        File.WriteAllBytes(path, [1, 2, 3]);
        switch (command)
        {
            case "save": service.SaveLayoutManifestJson("{}"); break;
            case "reset": service.ResetLayoutManifest(); break;
            case "named": service.SaveNamedLayouts(new Dictionary<string, LayoutManifest>()); break;
        }
        var recovery = Directory.GetFiles(dir, Path.GetFileName(path) + ".*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(1, 2, 3);
        if (command == "reset") File.Exists(path).Should().BeFalse();
        else File.ReadAllText(path).Should().Be("{}");
    }

    [Theory]
    [InlineData("add")]
    [InlineData("remove")]
    [InlineData("clear")]
    [InlineData("suppress")]
    public void RecentProjectChangesRetainPreviousDiskBytes(string command)
    {
        var service = new RecentProjectsService(dir);
        var path = Path.Combine(dir, "recent-projects.json");
        File.WriteAllBytes(path, [1, 2, 3]);
        switch (command)
        {
            case "add": service.Add(Path.Combine(dir, "P.vbp")); break;
            case "remove": service.Remove(Path.Combine(dir, "P.vbp")); break;
            case "clear": service.Clear(); break;
            case "suppress": service.SuppressNewProjectDialog = !service.SuppressNewProjectDialog; break;
        }
        var recovery = Directory.GetFiles(dir, "recent-projects.json.*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(1, 2, 3);
        File.ReadAllBytes(path).Should().NotEqual(new byte[] { 1, 2, 3 });
    }
}
