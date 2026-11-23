using System.Reflection;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Localization;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Addins;

public sealed class AddinReportedFailureTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hexide-reported-failure-" + Guid.NewGuid().ToString("N"));
    private readonly AddinRegistry registry;
    private readonly AddinContext context;
    private readonly IHexIdeHost owned;
    private readonly HexIdeHost host;

    public AddinReportedFailureTests()
    {
        Directory.CreateDirectory(root);
        registry = NewRegistry();
        context = Seed(registry, "probe", "Probe Add-in");
        var tools = new AddinToolWindowService();
        host = new HexIdeHost(null!, tools, null!, registry, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, new AddinResourceService(), new AddinLanguagePackService());
        var origin = new AddinOrigin("probe", "Probe Add-in");
        owned = new AddinOwnedHost(host, tools.ForOwner(origin), origin);
    }

    private AddinRegistry NewRegistry() => new(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
        Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), new LocalizationService(),
        new ConsentStore(Path.Combine(root, "consent.json")), new RevocationStore(null, root),
        Path.Combine(root, "state.json"), root);

    private AddinContext Seed(AddinRegistry target, string id, string title)
    {
        var contexts = (List<AddinContext>)typeof(AddinRegistry).GetField("_contexts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
        var seeded = new AddinContext(new AddinInfo(root, title, "", "1", "", Id: id)) { Status = AddinStatus.Loaded };
        contexts.Add(seeded);
        return seeded;
    }

    [Fact]
    public void Owned_call_records_its_owner_exception_and_time_without_changing_status()
    {
        var other = Seed(registry, "other", "Other Add-in");
        var before = DateTimeOffset.UtcNow;
        owned.ReportFailure("Registration failed", new InvalidOperationException("Cannot register"));
        var after = DateTimeOffset.UtcNow;

        var report = registry.GetReportedFailure("probe");
        report.Should().NotBeNull();
        report!.Message.Should().Be("Registration failed");
        report.ExceptionType.Should().Be(typeof(InvalidOperationException).FullName);
        report.ExceptionMessage.Should().Be("Cannot register");
        report.Time.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        context.Status.Should().Be(AddinStatus.Loaded);
        registry.GetReportedFailure(other.Info.Id).Should().BeNull();
    }

    [Fact]
    public void A_later_report_replaces_the_message_and_exception()
    {
        owned.ReportFailure("First", new InvalidOperationException("First exception"));
        var first = registry.GetReportedFailure("probe");
        owned.ReportFailure("Second");

        var second = registry.GetReportedFailure("probe");
        second.Should().NotBeNull().And.NotBeSameAs(first);
        second!.Message.Should().Be("Second");
        second.ExceptionType.Should().BeNull();
        second.ExceptionMessage.Should().BeNull();
    }

    [Fact]
    public void Warning_names_the_add_in_and_keeps_the_whole_message_and_exception()
    {
        using var log = new FailureLog();
        var exception = new InvalidOperationException("Cannot register");
        var message = new string('x', 700);
        owned.ReportFailure(message, exception);

        var entry = log.Events.Should().ContainSingle().Which;
        entry.Level.Should().Be(LogEventLevel.Warning);
        entry.Exception.Should().BeSameAs(exception);
        entry.RenderMessage().Should().Contain("Probe Add-in").And.Contain("probe").And.Contain(message);
    }

    [Fact]
    public void Page_shows_the_report_and_local_time_below_an_unchanged_status()
    {
        owned.ReportFailure("Registration failed");
        using var page = new AddinOptionsPageViewModel(registry, context.Info);
        var report = registry.GetReportedFailure("probe");

        page.ReportedFailure.Should().Be($"Reported a problem at {report!.Time.ToLocalTime():g}: Registration failed");
        page.HasReportedFailure.Should().BeTrue();
        page.StatusText.Should().Be("Loaded");
    }

    [Fact]
    public void Page_without_a_report_shows_nothing()
    {
        using var page = new AddinOptionsPageViewModel(registry, context.Info);
        page.ReportedFailure.Should().BeEmpty();
        page.HasReportedFailure.Should().BeFalse();
    }

    [Fact]
    public void Page_truncates_the_message_to_500_characters()
    {
        owned.ReportFailure(new string('x', 700));
        using var page = new AddinOptionsPageViewModel(registry, context.Info);
        page.ReportedFailure.Should().EndWith(new string('x', 500) + "…");
        page.ReportedFailure.Should().NotContain(new string('x', 501));
    }

    [AvaloniaFact]
    public void Page_follows_the_active_language_without_reading_new_reports()
    {
        var localization = new LocalizationService();
        owned.ReportFailure("First");
        using var page = new AddinOptionsPageViewModel(registry, context.Info, localization: localization);
        owned.ReportFailure("Second");
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        localization.Apply("de");

        page.ReportedFailure.Should().StartWith("Problem gemeldet am ").And.EndWith(": First");
        changed.Should().Contain(nameof(page.ReportedFailure));
    }

    [Fact]
    public void Enablement_keeps_the_report_and_a_new_registry_has_none()
    {
        owned.ReportFailure("Registration failed");
        var report = registry.GetReportedFailure("probe");
        report.Should().NotBeNull();
        registry.SetEnabled(root, false);
        registry.GetReportedFailure("probe").Should().BeSameAs(report);
        registry.SetEnabled(root, true);
        registry.GetReportedFailure("probe").Should().BeSameAs(report);
        using var restarted = NewRegistry();
        Seed(restarted, "probe", "Probe Add-in");
        restarted.GetReportedFailure("probe").Should().BeNull();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("disposed")]
    public void Unknown_or_removed_report_is_logged_without_throwing_or_retaining_it(string state)
    {
        var origin = new AddinOrigin("probe", "Probe Add-in");
        if (state == "unknown") origin = new AddinOrigin("unknown", "Unknown Add-in");
        if (state == "disabled") registry.SetEnabled(root, false);
        if (state == "revoked")
        {
            context.Info = context.Info with { ManifestHash = "hash" };
            registry.RevokeConsent("hash");
        }
        if (state == "disposed") registry.Dispose();
        using var log = new FailureLog();
        Action report = () => host.ReportFailure("Too late", null, origin);
        report.Should().NotThrow();
        registry.GetReportedFailure(origin.Id).Should().BeNull();
        log.Events.Should().ContainSingle().Which.Level.Should().Be(LogEventLevel.Warning);
    }

    [Fact]
    public void Owned_host_forwards_to_an_alternate_host_implementation()
    {
        var alternate = Substitute.For<IHexIdeHost>();
        var tools = new AddinToolWindowService();
        IHexIdeHost facade = new AddinOwnedHost(alternate, tools, new AddinOrigin("probe", "Probe Add-in"));
        var exception = new InvalidOperationException("Cannot register");
        facade.ReportFailure("Registration failed", exception);
        alternate.Received(1).ReportFailure("Registration failed", exception);
    }

    [Fact]
    public void Unowned_host_uses_the_calling_assembly()
    {
        var caller = Seed(registry, typeof(AddinReportedFailureTests).Assembly.GetName().Name!, "Calling Add-in");
        host.ReportFailure("Unowned");
        registry.GetReportedFailure(caller.Info.Id).Should().NotBeNull();
        registry.GetReportedFailure("probe").Should().BeNull();
    }

    public void Dispose()
    {
        registry.Dispose();
        Directory.Delete(root, true);
    }

    private sealed class FailureLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        public List<LogEvent> Events { get; } = [];
        public FailureLog() => Log.Logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
        public void Dispose() => Log.Logger = previous;
    }
}
