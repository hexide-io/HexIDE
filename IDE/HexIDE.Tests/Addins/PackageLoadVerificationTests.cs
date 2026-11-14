using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Tests.Addins;

public sealed class PackageLoadVerificationTests
{
    [AvaloniaTheory]
    [InlineData("hash", false)]
    [InlineData("verdict", false)]
    [InlineData("manifest", false)]
    [InlineData("hash", true)]
    [InlineData("verdict", true)]
    [InlineData("manifest", true)]
    [InlineData("unchanged", false)]
    [InlineData("unchanged", true)]
    public async Task RechecksBeforeLoadingAndKeepsTheIdReserved(string change, bool prompt)
    {
        var root = Path.Combine(Path.GetTempPath(), "hexide-reverify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var packages = Path.Combine(root, "addins");
            var first = Path.Combine(packages, "a");
            var duplicate = Path.Combine(packages, "b");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(duplicate);
            DuplicateAddinTests.WriteProbe(Path.Combine(first, "DuplicateProbe.dll"), "winner");
            DuplicateAddinTests.WriteProbe(Path.Combine(duplicate, "DuplicateProbe.dll"), "duplicate");
            var original = new PackageVerificationResult(PackageVerdict.Verified,
                new AddinManifest { Id = "com.example.probe", Title = "Probe", Entry = "DuplicateProbe.dll" }, null, null, null)
                { ManifestHash = "original", IsFirstParty = !prompt };
            var fresh = change switch
            {
                "hash" => original with { ManifestHash = "changed" },
                "verdict" => original with { Verdict = PackageVerdict.Untrusted },
                "manifest" => original with { Manifest = null },
                _ => original
            };
            var verifier = Substitute.For<IPackageVerifier>();
            verifier.Verify(first).Returns(original, fresh);
            verifier.Verify(duplicate).Returns(original);
            var windows = Substitute.For<IWindowManager>();
            windows.ShowDialog(Arg.Any<IDialog>()).Returns(Task.FromResult(true));
            var localization = new LocalizationService();
            var tools = new AddinToolWindowService(localization);
            var menus = new AddinMenuService(localization);
            var host = Substitute.For<IHexIdeHost>();
            host.ToolWindows.Returns(tools);
            host.Menus.Returns(menus);
            using var registry = new AddinRegistry(verifier, Substitute.For<ISettingsService>(),
                Substitute.For<IDeveloperModeService>(), windows, localization,
                new ConsentStore(Path.Combine(root, "consent.json")),
                new RevocationStore(null, Path.Combine(root, "revocations")), Path.Combine(root, "state.json"), packages);

            registry.LoadAll(host);
            await registry.PromptPendingConsentAsync();

            verifier.Received(2).Verify(first);
            registry.GetStatus(duplicate).Should().Be(AddinStatus.Error);
            var duplicateInfo = registry.GetAll().Single(info => info.AssemblyPath == duplicate);
            duplicateInfo.LoadError.Should().Contain("com.example.probe").And.Contain(first);
            if (change == "unchanged")
            {
                registry.GetStatus(first).Should().Be(AddinStatus.Loaded);
                host.Received(1).RegisterCommand("initialized", "winner", Arg.Any<Action>(), null, null);
                tools.Tools.Should().ContainSingle();
                menus.ToolsItems.Should().ContainSingle();
            }
            else
            {
                registry.GetStatus(first).Should().Be(AddinStatus.Error);
                var info = registry.GetAll().Single(info => info.AssemblyPath == first);
                var message = localization.GetString("Str.Addins.ChangedAfterVerification");
                info.LoadError.Should().Be(message);
                message.Should().NotBe("Str.Addins.ChangedAfterVerification");
                new AddinOptionsPageViewModel(registry, info).StatusText.Should().Contain(message);
                host.DidNotReceive().RegisterCommand(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action>(), Arg.Any<string?>(), Arg.Any<Func<bool>?>());
                tools.Tools.Should().BeEmpty();
                menus.ToolsItems.Should().BeEmpty();
            }
        }
        finally { try { Directory.Delete(root, true); } catch { /* best effort */ } }
    }
}
