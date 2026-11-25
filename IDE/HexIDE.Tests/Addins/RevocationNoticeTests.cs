using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

/// <summary>
/// What the user sees of the hourly revocation check (#5577): a notice when a loaded add-in is revoked, offering to close
/// HexIDE; a line on Options → Add-Ins saying when the list was last checked; and no Activate or Deactivate button on a
/// revoked add-in's page.
/// </summary>
public sealed class RevocationNoticeTests : IDisposable
{
    private const string Url = "https://example.invalid/list";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-revocation-notice-" + Guid.NewGuid().ToString("N"));
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly ILocalizationService _localization = Substitute.For<ILocalizationService>();
    private readonly RevocationStore _store;
    private readonly AddinRegistry _registry;
    private readonly List<AddinContext> _contexts;
    private DateTimeOffset _now = new(2026, 11, 25, 21, 0, 0, TimeSpan.Zero);
    private FetchedList? _next;
    private bool _fetchFails;
    private int _closes;

    public RevocationNoticeTests()
    {
        Directory.CreateDirectory(_root);
        _localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.Dialog.AddinRevoked.Body" => "revoked: {0}",
            "Str.Options.Addins.Revocation.Off" => "off",
            "Str.Options.Addins.Revocation.NotChecked" => "not checked; issued {0}",
            "Str.Options.Addins.Revocation.Checked" => "checked {0}; issued {1}",
            "Str.Options.Addins.Revocation.Failed" => "failed {0}; worked {1}; issued {2}",
            "Str.Options.Addins.Revocation.FailedNever" => "failed {0}; never worked; issued {1}",
            var key => key,
        });
        _settings.RevocationListUrl.Returns(Url);
        _store = new RevocationStore(PackageCrypto.ExportPublicKey(_key), Path.Combine(_root, "cache"),
            (Func<string, TimeSpan, string?, FetchedList?>)((_, _, _) => _fetchFails ? throw new HttpRequestException("offline") : _next))
        {
            Clock = () => _now,
        };
        _registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), _settings, Substitute.For<IDeveloperModeService>(), _windows,
            _localization, new ConsentStore(Path.Combine(_root, "consent.json")), _store, Path.Combine(_root, "state.json"), _root)
        {
            CloseIde = () => _closes++,
        };
        _contexts = (List<AddinContext>)typeof(AddinRegistry).GetField("_contexts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_registry)!;
        typeof(AddinRegistry).GetField("_hasPackages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(_registry, true);
    }

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private AddinContext Add(string id, bool loaded, AddinStatus status = AddinStatus.Loaded)
    {
        var info = new AddinInfo(Path.Combine(_root, id), id + " Add-in", "", "1", "", Id: id, ManifestHash: id + "-hash",
            Chain: new TrustChain
            {
                BuildVersion = "1", BuildHash = id + "-hash", IsFirstParty = false,
                Publisher = new TrustLink("Publisher", "acme", "key"),
                Intermediate = new TrustLink("Marketplace", "marketplace", "key"),
                Root = new TrustLink("Root", "root", "key"),
            });
        var context = new AddinContext(info) { Status = status, Instance = loaded ? Substitute.For<IAddin>() : null };
        _contexts.Add(context);
        return context;
    }

    private void Revoke(DateTimeOffset issued, params string[] ids)
    {
        var json = Encoding.UTF8.GetBytes("{\"schema\":1,\"issuedUtc\":\"" + issued.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            + "\",\"revoked\":{\"manifestHashes\":[" + string.Join(",", ids.Select(id => $"\"{id}-hash\"")) + "],\"publisherIds\":[],\"intermediateIds\":[]}}");
        _next = new FetchedList(json, PackageCrypto.Sign(_key, json), null);
        _store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Applied);
    }

    private static string Shown(DateTimeOffset time) => time.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    // ── The notice ──────────────────────────────────────────────

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task A_loaded_add_in_revoked_mid_session_brings_a_notice_whose_close_button_closes_HexIDE(bool answer, int closes)
    {
        Add("probe", loaded: true);
        AddinRevokedNoticeViewModel? notice = null;
        _windows.ShowDialog(Arg.Do<IDialog>(dialog => notice = dialog as AddinRevokedNoticeViewModel)).Returns(answer);
        Revoke(_now, "probe");

        await _registry.ApplyRevocationsAndNotifyAsync();

        notice.Should().NotBeNull();
        notice!.AddinTitles.Should().Equal("probe Add-in");
        notice.Message.Should().Be("revoked: probe Add-in");
        _closes.Should().Be(closes, "only the notice's close button closes HexIDE, through the main window's own close");
    }

    [Fact]
    public async Task An_add_in_whose_code_never_loaded_is_revoked_without_a_notice()
    {
        var waiting = Add("waiting", loaded: false, AddinStatus.AwaitingConsent);
        Revoke(_now, "waiting");

        await _registry.ApplyRevocationsAndNotifyAsync();

        waiting.Status.Should().Be(AddinStatus.Revoked);
        await _windows.DidNotReceive().ShowDialog(Arg.Any<IDialog>());
    }

    [Fact]
    public async Task A_revocation_landing_while_the_notice_is_open_is_named_in_the_next_one()
    {
        Add("first", loaded: true);
        Add("second", loaded: true);
        var shown = new List<IReadOnlyList<string>>();
        var firstAnswer = new TaskCompletionSource<bool>();
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(
            call => { shown.Add(((AddinRevokedNoticeViewModel)call.Arg<IDialog>()).AddinTitles); return firstAnswer.Task; },
            call => { shown.Add(((AddinRevokedNoticeViewModel)call.Arg<IDialog>()).AddinTitles); return Task.FromResult(false); });

        Revoke(_now, "first");
        var firstNotice = _registry.ApplyRevocationsAndNotifyAsync();
        Revoke(_now.AddMinutes(1), "first", "second");
        await _registry.ApplyRevocationsAndNotifyAsync();
        shown.Should().HaveCount(1, "one notice at a time");

        firstAnswer.SetResult(false);
        await firstNotice;

        shown.Should().HaveCount(2);
        shown[0].Should().Equal("first Add-in");
        shown[1].Should().Equal("second Add-in");
    }

    [Fact]
    public async Task A_notice_that_could_not_be_shown_is_shown_with_the_next_revocation()
    {
        Add("first", loaded: true);
        Add("second", loaded: true);
        var shown = new List<IReadOnlyList<string>>();
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(
            _ => Task.FromException<bool>(new InvalidOperationException("no owner window")),
            call => { shown.Add(((AddinRevokedNoticeViewModel)call.Arg<IDialog>()).AddinTitles); return Task.FromResult(false); });

        Revoke(_now, "first");
        await _registry.ApplyRevocationsAndNotifyAsync();
        Revoke(_now.AddMinutes(1), "first", "second");
        await _registry.ApplyRevocationsAndNotifyAsync();

        shown.Should().ContainSingle().Which.Should().Equal("first Add-in", "second Add-in");
    }

    // ── The line on Options → Add-Ins ────────────────────────────

    [Fact]
    public void The_line_says_when_the_list_was_last_checked_and_which_list_is_in_use()
    {
        var issued = _now.AddHours(-2);
        Revoke(issued);

        _registry.RevocationStatusLine().Should().Be($"checked {Shown(_now)}; issued {Shown(issued)}");

        _now = _now.AddHours(1);
        _next = null;   // not modified: the check still worked
        _store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Unchanged);
        _registry.RevocationStatusLine().Should().Be($"checked {Shown(_now)}; issued {Shown(issued)}");
    }

    [Fact]
    public void A_failed_check_says_so_and_when_one_last_worked()
    {
        var issued = _now.AddHours(-2);
        var worked = _now;
        Revoke(issued);
        _now = _now.AddHours(1);
        _fetchFails = true;
        _store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Failed);

        _registry.RevocationStatusLine().Should().Be($"failed {Shown(_now)}; worked {Shown(worked)}; issued {Shown(issued)}");
    }

    [Fact]
    public void A_session_whose_checks_all_failed_never_says_checked()
    {
        _fetchFails = true;
        _store.RefreshFromUrl(Url, TimeSpan.FromSeconds(3)).Should().Be(RevocationRefresh.Failed);

        _registry.RevocationStatusLine().Should().StartWith($"failed {Shown(_now)}; never worked; issued ");
    }

    [Fact]
    public void The_line_says_checking_is_off_when_the_address_is_empty_and_is_absent_with_no_add_in_installed()
    {
        _registry.RevocationStatusLine().Should().StartWith("not checked; issued ");

        _settings.RevocationListUrl.Returns("");
        _registry.RevocationStatusLine().Should().Be("off");

        typeof(AddinRegistry).GetField("_hasPackages", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(_registry, false);
        _registry.RevocationStatusLine().Should().BeNull("nothing is checked, so there is nothing to say");
        new AddinsOverviewPageViewModel([], _registry.RevocationStatusLine()).HasRevocationStatus.Should().BeFalse();
    }

    // ── A revoked add-in's page ──────────────────────────────────

    [Fact]
    public async Task A_revoked_add_in_s_page_offers_no_activate_or_deactivate_button()
    {
        var probe = Add("probe", loaded: true);
        using (var loaded = new AddinOptionsPageViewModel(_registry, probe.Info))
            loaded.CanToggle.Should().BeTrue();
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(false);
        Revoke(_now, "probe");
        await _registry.ApplyRevocationsAndNotifyAsync();

        using var page = new AddinOptionsPageViewModel(_registry, probe.Info);

        page.Status.Should().Be(AddinStatus.Revoked);
        page.CanToggle.Should().BeFalse("toggling a revoked add-in would only change a choice for a start that refuses it anyway");
    }

    [Fact]
    public async Task A_page_open_when_its_add_in_is_revoked_shows_it_at_once()
    {
        var probe = Add("probe", loaded: true);
        using var page = new AddinOptionsPageViewModel(_registry, probe.Info);
        page.Status.Should().Be(AddinStatus.Loaded);
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(false);

        Revoke(_now, "probe");
        await _registry.ApplyRevocationsAndNotifyAsync();

        page.Status.Should().Be(AddinStatus.Revoked, "the Options page stays open over the notice, and must not go on saying Loaded");
        page.CanToggle.Should().BeFalse();
        page.CanRevokeConsent.Should().BeFalse();
    }

    [Fact]
    public async Task An_open_page_that_fails_to_follow_doesn_t_keep_the_notice_from_showing()
    {
        var probe = Add("probe", loaded: true);
        _registry.RevocationsApplied += () => throw new InvalidOperationException("a page that throws");
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(false);
        Revoke(_now, "probe");

        await _registry.ApplyRevocationsAndNotifyAsync();

        probe.Status.Should().Be(AddinStatus.Revoked);
        await _windows.Received(1).ShowDialog(Arg.Any<AddinRevokedNoticeViewModel>());
    }
}
