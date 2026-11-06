using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.Addins;

/// <summary>
/// <see cref="IHexIdeHost.RegisterLanguageServer"/> on the real host (#2760): the server is listed under the add-in that
/// contributed it, and goes with the add-in's other contributions when its initialisation fails.
/// </summary>
public sealed class AddinLanguageServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-addin-server-" + Guid.NewGuid().ToString("N"));
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IProjectRunnerService _runner = Substitute.For<IProjectRunnerService>();
    private readonly EventBus _bus = new();
    private readonly AddinEventService _events;
    private readonly LspClientRegistry _router = new([], NullLogger<LspClientRegistry>.Instance);
    private readonly AddinDiagnosticsService _diagnostics;
    private readonly IAddinRegistry _addins = Substitute.For<IAddinRegistry>();
    private readonly HexIdeHost _host;

    public AddinLanguageServerTests()
    {
        Directory.CreateDirectory(_root);
        _events = new AddinEventService(_projects, _runner, _bus);
        _diagnostics = new AddinDiagnosticsService(_router, _projects);
        // The add-in is this test assembly, as the add-in manager would list it.
        _addins.GetAll().Returns([new AddinInfo(typeof(AddinLanguageServerTests).Assembly.Location, "Probe Add-in", "", "1.0", "")]);
        _host = new HexIdeHost(new AddinMenuService(), new AddinToolWindowService(), _events, _addins,
            new AddinEditorService(Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), _projects),
            new AddinProjectService(_projects), _diagnostics, new AddinCommandService(), new AddinProjectTemplateService(),
            new AddinOptionsService(), new ComponentRegistry(), _runner,
            new LanguageServerContributions(_router, new LanguageServerRegistrationFactory(NullLoggerFactory.Instance),
                new LanguageServerConfigLoader(NullLogger<LanguageServerConfigLoader>.Instance),
                new HashSet<string>(StringComparer.Ordinal), NullLogger<LanguageServerContributions>.Instance),
            new HexIDE.Debugging.DebugLaunchService(new HexIDE.Debugging.ExternalDebugService(new HexIDE.Conversations.ConversationLog())),
            new HexIDE.Projects.ContributedProjects());
    }

    public void Dispose()
    {
        _diagnostics.Dispose();
        _events.Dispose();
        Directory.Delete(_root, true);
    }

    private static AddinLanguageServer Probe() => new(new LanguageServerEntry
    {
        Id = "probe",
        DisplayName = "Probe",
        Extensions = [".md"],
        LanguageId = "markdown",
        Transport = "pipe",
        PipeName = HexIDE.Tests.LspClient.AddinServerRegistrationTests.PipeName(),
    });

    [Fact]
    public void AServerContributedFromOutsideAnyAddin_IsListedUnderItsAssemblysName()
    {
        // This test assembly is in no add-in's load context, whatever the registry lists (AddinOrigin is the add-in's).
        using var connection = _host.RegisterLanguageServer(Probe());

        _router.Connections.Should().ContainSingle().Which.Origin.Should().Be(typeof(AddinLanguageServerTests).Assembly.GetName().Name);
    }

    // An assembly loaded as an add-in's: the DAP test adapter, which nothing shares with the host. From its bytes, so no
    // file stays locked behind the test.
    private static System.Reflection.Assembly LoadedAsAddin(string id, string packageDir)
    {
        var context = new AddinLoadContext(packageDir, new AddinManifest
        {
            Id = id,
            Files = [new AddinFileHash { Path = "HexIDE.Dap.TestAdapter.dll", Sha256 = new string('0', 64) }],
        });
        using var bytes = new MemoryStream(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "HexIDE.Dap.TestAdapter.dll")));
        return context.LoadFromStream(bytes);
    }

    [Fact]
    public void AnAddinIsNamedByTheManifestItsCodeWasLoadedUnder_NotByItsFiles()
    {
        // The registry lists an add-in by its package's folder, never by a DLL, and two add-ins may ship one assembly name.
        var first = LoadedAsAddin("com.example.first", Path.Combine(_root, "first"));
        var second = LoadedAsAddin("com.example.second", Path.Combine(_root, "second"));
        _addins.GetAll().Returns(
        [
            new AddinInfo(Path.Combine(_root, "first"), "First Add-in", "", "1.0", "", Id: "com.example.first"),
            new AddinInfo(Path.Combine(_root, "second"), "", "", "1.0", "", Id: "com.example.second"),
        ]);

        AddinOrigin.Of(first, _addins).Should().Be(new AddinOrigin("com.example.first", "First Add-in"));
        AddinOrigin.Of(second, _addins).Should().Be(new AddinOrigin("com.example.second", "com.example.second"),
            "an untitled add-in is called by its id, and the same assembly name in another add-in is another add-in");
    }

    [Fact]
    public void AnAddinWhoseInitialisationFails_HasTheServerItRegisteredTakenOut()
    {
        using var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(),
            new ConsentStore(Path.Combine(_root, "consent.json")),
            new RevocationStore(null, Path.Combine(_root, "revocations")), Path.Combine(_root, "state.json"), _root);
        var addin = Substitute.For<IAddin>();
        addin.When(a => a.Initialize(_host)).Do(_ =>
        {
            _host.RegisterLanguageServer(Probe());
            throw new InvalidOperationException("initialisation failed");
        });
        var ctx = new AddinContext(new AddinInfo("failed", "Failed", "", "", ""));

        registry.InitializeAddin(ctx, addin, AddinStatus.Loaded, _host);

        ctx.Status.Should().Be(AddinStatus.Error);
        _router.Connections.Should().BeEmpty("the server goes with the add-in's other contributions");
    }
}
