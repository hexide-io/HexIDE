using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The revocation list is checked every hour while HexIDE runs, not only at startup (#5577). These cover the conditional
/// fetch, the clock that decides when a check is due, and what happens to an add-in that is loaded when a newer list revokes
/// it: everything it contributed is taken out, and nothing it registers or subscribes from then on runs.
/// </summary>
public sealed class PeriodicRevocationTests : IAsyncDisposable
{
    private const string Url = "https://example.invalid/list";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-periodic-revocation-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ConversationLog _log = new();

    public PeriodicRevocationTests() => Directory.CreateDirectory(_root);

    public async ValueTask DisposeAsync()
    {
        _key.Dispose();
        await _log.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private string RootPub => PackageCrypto.ExportPublicKey(_key);

    private static byte[] List(string issued, string[]? hashes = null, string[]? publishers = null) => Encoding.UTF8.GetBytes(
        "{\"schema\":1,\"issuedUtc\":\"" + issued + "\",\"revoked\":{\"manifestHashes\":[" + Quoted(hashes)
        + "],\"publisherIds\":[" + Quoted(publishers) + "],\"intermediateIds\":[]}}");

    private static string Quoted(string[]? values) => string.Join(",", (values ?? []).Select(v => $"\"{v}\""));

    private FetchedList Signed(byte[] json, string? validator) => new(json, PackageCrypto.Sign(_key, json), validator);

    // ── The conditional fetch ───────────────────────────────────

    [Fact]
    public void A_list_not_modified_since_the_last_check_leaves_the_active_list_and_says_unchanged()
    {
        var validators = new List<string?>();
        var answers = new Queue<FetchedList?>([Signed(List("2099-01-01T00:00:00Z", ["bad"]), "\"v1\""), null]);
        var store = new RevocationStore(RootPub, _root, (_, _, validator) => { validators.Add(validator); return answers.Dequeue(); });

        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Applied);
        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Unchanged);

        validators.Should().Equal(null, "\"v1\"");
        store.IsRevoked("bad", null, null, isFirstParty: false).Should().NotBeNull();
    }

    [Fact]
    public void A_pair_that_fails_verification_does_not_keep_its_validator()
    {
        var validators = new List<string?>();
        var tampered = new FetchedList(List("2099-01-01T00:00:00Z", ["bad"]), PackageCrypto.Sign(_key, List("2000-01-01T00:00:00Z")),
            "\"tampered\"");
        var answers = new Queue<FetchedList?>([tampered, Signed(List("2099-01-01T00:00:00Z", ["bad"]), "\"v2\"")]);
        var store = new RevocationStore(RootPub, _root, (_, _, validator) => { validators.Add(validator); return answers.Dequeue(); });

        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Failed);
        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Applied);

        validators.Should().Equal([null, null], "kept from the failed pair, it would be answered 304 and the valid pair never fetched");
    }

    [Fact]
    public void A_fetch_that_fails_says_so_and_keeps_the_active_list()
    {
        var store = new RevocationStore(RootPub, _root, (Func<string, TimeSpan, string?, FetchedList?>)((_, _, _) =>
            throw new HttpRequestException("offline")));

        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Failed);
    }

    // ── When a check is due ─────────────────────────────────────

    [Fact]
    public void A_check_is_due_only_after_the_interval_and_its_offset()
    {
        var now = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
        var checks = 0;
        var monitor = new RevocationMonitor(() => checks++, () => now, () => TimeSpan.FromMinutes(2));

        now += RevocationMonitor.Interval + TimeSpan.FromMinutes(1);
        monitor.TickOnce().Should().BeFalse("the offset isn't up yet");
        now += TimeSpan.FromMinutes(1);
        monitor.TickOnce().Should().BeTrue();
        monitor.TickOnce().Should().BeFalse("the next one is an interval away");

        checks.Should().Be(1);
    }

    [Fact]
    public void A_machine_waking_from_sleep_checks_at_once_and_a_clock_set_back_counts_as_due()
    {
        var now = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
        var checks = 0;
        var monitor = new RevocationMonitor(() => checks++, () => now, () => TimeSpan.Zero);

        now += TimeSpan.FromDays(3);
        monitor.TickOnce().Should().BeTrue("three days asleep is long past due");
        now -= TimeSpan.FromHours(2);
        monitor.TickOnce().Should().BeTrue("a clock that went backwards can't be trusted to say a check isn't due");

        checks.Should().Be(2);
    }

    [Fact]
    public void A_check_that_throws_is_contained_and_the_next_waits_an_interval()
    {
        var now = DateTimeOffset.Parse("2026-11-25T12:00:00Z");
        var monitor = new RevocationMonitor(() => throw new InvalidOperationException("boom"), () => now, () => TimeSpan.Zero);

        now += RevocationMonitor.Interval;
        FluentActions.Invoking(() => monitor.TickOnce()).Should().NotThrow();
        monitor.TickOnce().Should().BeFalse();
    }

    // ── A loaded add-in revoked mid-session ─────────────────────

    private static AddinDebugLaunch Launch(string title) => new(title)
    {
        CreateConfigurationAsync = (_, _) => throw new InvalidOperationException("never started here"),
    };

    private sealed class ProbeAddin : IAddin
    {
        public IHexIdeHost Host = null!;
        public int Ran;
        public void Initialize(IHexIdeHost host)
        {
            Host = host;
            host.RegisterCommand("probe.command", "Probe", () => Ran++);
            host.Menus.Add("Tools", "Probe", null, () => Ran++);
            host.Events.RunStarted += () => Ran++;
            host.RegisterDebugLaunch(Launch("Probe at load"));
        }
        public void Dispose() { }
    }

    private sealed record Session(AddinRegistry Registry, AddinContext Context, ProbeAddin Probe, RevocationStore Store,
        AddinCommandService Commands, AddinMenuService Menus, DebugLaunchService Launches, IProjectRunnerService Runner);

    private Session Load(bool firstParty)
    {
        var store = new RevocationStore(RootPub, Path.Combine(_root, "cache"), (_, _) => throw new IOException("unused"));
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + " {0}");
        var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), localization,
            new ConsentStore(Path.Combine(_root, "consent.json")), store, Path.Combine(_root, "state.json"), _root);
        var info = new AddinInfo(Path.Combine(_root, "probe"), "Probe Add-in", "", "1", "", Id: "probe", ManifestHash: "probe-hash",
            Chain: new TrustChain
            {
                BuildVersion = "1", BuildHash = "probe-hash", IsFirstParty = firstParty,
                Publisher = new TrustLink("Publisher", "acme", "key"),
                Intermediate = new TrustLink("Marketplace", "marketplace", "key"),
                Root = new TrustLink("Root", "root", "key"),
            });
        var context = new AddinContext(info) { Status = AddinStatus.Loaded };
        ((List<AddinContext>)typeof(AddinRegistry).GetField("_contexts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(registry)!).Add(context);

        var runner = Substitute.For<IProjectRunnerService>();
        var projects = Substitute.For<IProjectManager>();
        var router = new LspClientRegistry([], NullLogger<LspClientRegistry>.Instance);
        var commands = new AddinCommandService();
        var menus = new AddinMenuService();
        var launches = new DebugLaunchService(new ExternalDebugService(_log));
        var host = new HexIdeHost(menus, new AddinToolWindowService(), new AddinEventService(projects, runner, new EventBus()), registry,
            new AddinEditorService(Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), projects),
            new AddinProjectService(projects), new AddinDiagnosticsService(router, projects), commands, new AddinProjectTemplateService(),
            new AddinOptionsService(), InBoxRegistry.Create(), runner,
            new LanguageServerContributions(Substitute.For<ISettingsService>(), router,
                new LanguageServerRegistrationFactory(NullLoggerFactory.Instance),
                new LanguageServerConfigLoader(NullLogger<LanguageServerConfigLoader>.Instance), [],
                NullLogger<LanguageServerContributions>.Instance, null),
            launches, new ContributedProjects(), new AddinResourceService(), new AddinLanguagePackService(),
            new AddinLanguageEditingService());
        var probe = new ProbeAddin();
        registry.InitializeAddin(context, probe, AddinStatus.Loaded, host);
        return new Session(registry, context, probe, store, commands, menus, launches, runner);
    }

    private void Publish(RevocationStore store, byte[] list)
    {
        // As the hourly check does: the registry's own store fetches a newer signed list and makes it active.
        typeof(RevocationStore).GetField("_fetch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(store, (Func<string, TimeSpan, string?, FetchedList?>)((_, _, _) => Signed(list, null)));
        store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Applied);
    }

    private static void StartRun(IProjectRunnerService runner)
    {
        runner.IsRunning.Returns(true);
        runner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(runner,
            new PropertyChangedEventArgs(nameof(IProjectRunnerService.IsRunning)));
    }

    [Fact]
    public void A_build_revoked_mid_session_is_marked_revoked_and_everything_it_contributed_is_taken_out()
    {
        var session = Load(firstParty: false);
        session.Commands.Commands.Should().ContainSingle(c => c.Id == "probe.command");
        session.Menus.ToolsItems.Should().ContainSingle();
        session.Launches.Launches.Should().ContainSingle();

        Publish(session.Store, List("2099-01-01T00:00:00Z", ["probe-hash"]));
        var revoked = session.Registry.ApplyRevocations();

        revoked.Select(info => info.Id).Should().Equal("probe");
        session.Registry.GetStatus(session.Context.PackageDirectory).Should().Be(AddinStatus.Revoked);
        session.Context.ErrorMessage.Should().Be("build revoked");
        session.Commands.Commands.Should().NotContain(c => c.Id == "probe.command", "Initialize's command is taken out");
        session.Menus.ToolsItems.Should().BeEmpty("Initialize's menu item is taken out");
        session.Launches.Launches.Should().BeEmpty("a tracked contribution is taken out");
        StartRun(session.Runner);
        session.Probe.Ran.Should().Be(0, "a revoked add-in hears no events");
    }

    [Fact]
    public void After_a_revocation_nothing_the_add_in_registers_or_subscribes_runs()
    {
        var session = Load(firstParty: false);
        Publish(session.Store, List("2099-01-01T00:00:00Z", ["probe-hash"]));
        session.Registry.ApplyRevocations();

        session.Probe.Host.RegisterDebugLaunch(Launch("Probe after revocation"));
        session.Probe.Host.RegisterCommand("probe.late", "Late", () => session.Probe.Ran++, canExecute: () => true);
        session.Probe.Host.Events.RunStarted += () => session.Probe.Ran++;

        session.Launches.Launches.Should().BeEmpty("a tracked contribution registered after the revocation is refused");
        var late = session.Commands.Commands.Should().ContainSingle(c => c.Id == "probe.late").Subject;
        late.CanExecute!().Should().BeFalse();
        late.OnInvoke();
        StartRun(session.Runner);
        session.Probe.Ran.Should().Be(0, "a command registered or an event subscribed after the revocation doesn't run");
    }

    [Fact]
    public void A_publisher_revocation_mid_session_leaves_a_first_party_add_in_loaded()
    {
        var session = Load(firstParty: true);

        Publish(session.Store, List("2099-01-01T00:00:00Z", publishers: ["acme"]));

        session.Registry.ApplyRevocations().Should().BeEmpty();
        session.Registry.GetStatus(session.Context.PackageDirectory).Should().Be(AddinStatus.Loaded);
        session.Commands.Commands.Should().ContainSingle(c => c.Id == "probe.command");
    }

    [Fact]
    public void A_newer_list_that_names_nothing_loaded_changes_nothing()
    {
        var session = Load(firstParty: false);

        Publish(session.Store, List("2099-01-01T00:00:00Z", ["someone-else"]));

        session.Registry.ApplyRevocations().Should().BeEmpty();
        session.Registry.GetStatus(session.Context.PackageDirectory).Should().Be(AddinStatus.Loaded);
        StartRun(session.Runner);
        session.Probe.Ran.Should().Be(1);
    }

    // The supervisor's review of #5660: revocation is a marker nothing clears before restart. Options offers Disable and
    // Enable on a revoked row, and Enable used to set it back to Loaded, which un-withdrew it while its code was loaded.
    [Fact]
    public void Toggling_a_revoked_add_in_off_and_on_does_not_bring_it_back()
    {
        var session = Load(firstParty: false);
        session.Probe.Host.Events.RunStarted += () => session.Probe.Ran++;   // after Initialize: only the owned host's guard stops it
        Publish(session.Store, List("2099-01-01T00:00:00Z", ["probe-hash"]));
        session.Registry.ApplyRevocations();

        session.Registry.SetEnabled(session.Context.PackageDirectory, false);
        session.Registry.SetEnabled(session.Context.PackageDirectory, true);

        session.Registry.GetStatus(session.Context.PackageDirectory).Should().Be(AddinStatus.Revoked);
        session.Probe.Host.RegisterDebugLaunch(Launch("Probe after toggling"));
        session.Launches.Launches.Should().BeEmpty();
        StartRun(session.Runner);
        session.Probe.Ran.Should().Be(0);
    }

    // The supervisor's review of #5660: the hourly check's job runs in the consent modal's frame, so a revocation can land
    // while a prompt is open. Neither that add-in nor a later one in the same pass may load. Without the job having run yet,
    // the active list alone must stop it.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_revocation_landing_while_a_consent_prompt_is_open_loads_nothing(bool jobRanDuringThePrompt)
    {
        var store = new RevocationStore(RootPub, Path.Combine(_root, "cache"), (_, _) => throw new IOException("unused"));
        var windows = Substitute.For<IWindowManager>();
        var consent = new ConsentStore(Path.Combine(_root, "consent.json"));
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), windows, localization, consent, store, Path.Combine(_root, "state.json"), _root);
        var contexts = (List<AddinContext>)typeof(AddinRegistry).GetField("_contexts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(registry)!;
        foreach (var id in new[] { "first", "second" })
        {
            var manifest = new AddinManifest { Id = id, Title = id, Entry = "missing.dll" };
            contexts.Add(new AddinContext(new AddinInfo(Path.Combine(_root, id), id, "", "1", "", Id: id, ManifestHash: id + "-hash"))
            {
                Status = AddinStatus.AwaitingConsent, Manifest = manifest,
                PendingResult = new PackageVerificationResult(PackageVerdict.Verified, manifest, "Publisher", "acme", null)
                    { ManifestHash = id + "-hash", IntermediateId = "marketplace" },
            });
        }
        typeof(AddinRegistry).GetField("_host", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(registry, Substitute.For<IHexIdeHost>());
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(_ =>
        {
            Publish(store, List("2099-01-01T00:00:00Z", ["first-hash", "second-hash"]));
            if (jobRanDuringThePrompt) registry.ApplyRevocations();
            return Task.FromResult(true);
        });

        await registry.PromptPendingConsentAsync();

        await windows.Received(1).ShowDialog(Arg.Any<IDialog>());
        foreach (var ctx in contexts)
        {
            ctx.Status.Should().Be(AddinStatus.Revoked, ctx.Info.Id);
            ctx.Instance.Should().BeNull(ctx.Info.Id);
            consent.GetDecision(ctx.Info.ManifestHash).Should().BeNull("nothing is recorded for a refused build");
        }
    }
}
