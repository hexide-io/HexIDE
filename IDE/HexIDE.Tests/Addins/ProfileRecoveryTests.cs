using System.IO;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

public class ProfileRecoveryTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-addin-recovery-" + Guid.NewGuid().ToString("N"));

    public ProfileRecoveryTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    [Theory]
    [InlineData("record")]
    [InlineData("revoke")]
    public void ConsentChangesRetainPreviousConsentBytesForRecovery(string command)
    {
        var path = Path.Combine(dir, "consent.json");
        var store = new ConsentStore(path);
        store.Record("com.x", "pub_x", "hash", ConsentDecisions.Allow);
        var original = File.ReadAllBytes(path);
        if (command == "record") store.Record("com.x", "pub_x", "hash", ConsentDecisions.Block);
        else store.Revoke("hash");
        var recovery = Directory.GetFiles(dir, "*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(original);
        new ConsentStore(path).GetDecision("hash").Should().Be(command == "record" ? ConsentDecisions.Block : null);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EnableChangesRetainPreviousStateBytesForRecovery(bool enabled)
    {
        var path = Path.Combine(dir, "state.json");
        File.WriteAllText(path, "{\"disabled\":[]}");
        var original = File.ReadAllBytes(path);
        using var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(),
            new ConsentStore(Path.Combine(dir, "consent.json")), new RevocationStore(null, dir), path, dir);
        registry.SetEnabled(Path.Combine(dir, "addin.dll"), enabled);
        var recovery = Directory.GetFiles(dir, "*.recovery").Should().ContainSingle().Subject;
        File.ReadAllBytes(recovery).Should().Equal(original);
        File.ReadAllText(path).Should().Contain("disabled");
    }
}
