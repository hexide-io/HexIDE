using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using NSubstitute;

namespace HexIDE.Tests.Addins;

/// <summary>
/// A consent prompt still open when the IDE shuts down records nothing.
/// </summary>
/// <remarks>
/// <para>
/// Consent is a trust decision, and an add-in runs in-process with full trust once it is allowed, so a
/// decision the user did not make must never be written. The dialog can only ever resolve a close to "not
/// allowed", which makes the failure fail-safe, but a persisted Block is still an answer the user never gave,
/// and it silently stops the add-in from being offered again.
/// </para>
/// <para>
/// What prevents it is the registry's own dispose: <see cref="AddinRegistry.Dispose"/> marks the registry
/// shut down, and the consent pass checks that after the dialog returns. hexide-io/HexIDE#547 was a shutdown
/// path that skipped the dispose, so this pins the half that makes skipping it dangerous. The registry runs
/// against a temporary directory through its internal constructor, and never touches the real profile.
/// </para>
/// </remarks>
public sealed class ConsentAtShutdownTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-consent-" + Guid.NewGuid().ToString("N"));

    public ConsentAtShutdownTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private const string Hash = "hash-of-a-third-party-manifest";

    [Fact]
    public async Task A_consent_dialog_open_at_shutdown_records_nothing()
    {
        var addins = Path.Combine(_root, "addins");
        var package = Path.Combine(addins, "com.example.thirdparty");
        Directory.CreateDirectory(package);

        // Verified, not first-party, no decision on record: the one shape that waits for the prompt.
        var verifier = Substitute.For<IPackageVerifier>();
        verifier.Verify(Arg.Any<string>()).Returns(new PackageVerificationResult(
            PackageVerdict.Verified,
            new AddinManifest { Id = "com.example.thirdparty", Title = "Third Party", Version = "1.0", PublisherId = "pub_x" },
            "Example Publisher", "pub_x", null) { ManifestHash = Hash });

        var dialog = new TaskCompletionSource<bool>();
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(dialog.Task);

        var consentFile = Path.Combine(_root, "consent.json");
        var registry = new AddinRegistry(
            verifier, Substitute.For<ISettingsService>(), Substitute.For<IDeveloperModeService>(),
            windows, Substitute.For<ILocalizationService>(),
            new ConsentStore(consentFile),
            new RevocationStore(rootPub: null, Path.Combine(_root, "revocations")),
            Path.Combine(_root, "addins.json"),
            addins);

        registry.LoadAll(Substitute.For<IHexIdeHost>());

        // The prompt is shown only for a package awaiting consent, so this is also the proof that the fixture
        // reached that state; without it the assertion below would pass on a registry that never asked.
        var prompt = registry.PromptPendingConsentAsync();
        await windows.Received(1).ShowDialog(Arg.Any<IDialog>());

        // The IDE shuts down while the dialog is open, and closing it answers "not allowed".
        registry.Dispose();
        dialog.SetResult(false);
        await prompt;

        new ConsentStore(consentFile).GetDecision(Hash).Should().BeNull(
            "the user never answered, so no decision, Block included, may be persisted");
    }
}
