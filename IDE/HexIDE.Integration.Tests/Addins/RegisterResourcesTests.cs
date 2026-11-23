using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using HexIDE.Addins;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Integration.Tests.Addins;

/// <summary>A control of the tests' own, whose theme a test dictionary registers.</summary>
public sealed class RegisteredThemeControl : TemplatedControl;

/// <summary>
/// What an add-in's resource dictionary may add to the IDE, and what it may never change (#5396). Each test reads the
/// application's resources after the call, not only what the call returned.
/// </summary>
public class RegisterResourcesTests
{
    /// <summary>A key that is neither a string nor a type, which <c>OtherKeyKind.axaml</c> uses.</summary>
    public static readonly object ObjectKey = new();

    private static readonly Assembly Tests = typeof(RegisterResourcesTests).Assembly;

    private static Uri Dictionary(string name) => new($"avares://HexIDE.Integration.Tests/Addins/Registered/{name}.axaml");

    private static AddinOrigin Owner(string id) => new(id, "Owner " + id) { IsComposed = true };

    // Wider than any real grant: the IDE's text and the consent dialog's banner colours are inside it, so a refusal of
    // either can only come from the rule that protects them, not from the grant.
    private static RegistrationGrant Grant(string id) => new(id, Composed: true)
    {
        ResourceTypeAssemblies = ["HexIDE.Integration.Tests"],
        ResourceKeys = ["RegisterResourcesTests.*", "Str.*", "WarningBanner*"],
    };

    private static AddinResourceService Service(params string[] owners) => new() { Grants = owners.Select(Grant).ToArray() };

    private static object? Find(object key) =>
        Application.Current!.TryGetResource(key, ThemeVariant.Light, out var value) ? value : null;

    [AvaloniaFact]
    public void An_owners_own_theme_and_keys_are_added_and_taken_out_again()
    {
        var registration = Service("a").Register(Dictionary("OwnTheme"), Owner("a"), Tests);
        try
        {
            Find(typeof(RegisteredThemeControl)).Should().BeOfType<ControlTheme>();
            Find("RegisterResourcesTests.OwnBrush").Should().NotBeNull();
        }
        finally
        {
            registration.Dispose();
        }

        Find(typeof(RegisteredThemeControl)).Should().BeNull();
        Find("RegisterResourcesTests.OwnBrush").Should().BeNull();
    }

    [AvaloniaFact]
    public void A_key_another_owner_registered_is_refused_and_logged_naming_both_owners()
    {
        var service = Service("a", "b");
        using var first = service.Register(Dictionary("OwnTheme"), Owner("a"), Tests);
        var held = Find("RegisterResourcesTests.OwnBrush");
        using var log = new CapturedLog();

        FluentActions.Invoking(() => service.Register(Dictionary("OwnTheme"), Owner("b"), Tests))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*Owner b*\"RegisterResourcesTests.OwnBrush\": already registered by Owner a (a)*");

        log.Events.Should().Contain(e => e.Level == LogEventLevel.Warning
            && e.RenderMessage(null).Contains("Owner b") && e.RenderMessage(null).Contains("Owner a (a)"));
        Find("RegisterResourcesTests.OwnBrush").Should().BeSameAs(held, "the first owner's registration stands");
    }

    [AvaloniaFact]
    public void A_colour_the_IDE_holds_is_refused_and_keeps_the_IDEs_value()
    {
        var before = Find("WarningBannerBackgroundBrush");
        before.Should().NotBeNull("the harness merges the IDE's theme, as the application does");

        FluentActions.Invoking(() => Service("a").Register(Dictionary("HeldThemeKey"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*\"WarningBannerBackgroundBrush\": already held by the IDE*");

        Find("WarningBannerBackgroundBrush").Should().BeSameAs(before);
        Find("RegisterResourcesTests.HeldBrush").Should().BeNull("one refused key refuses the whole dictionary");
    }

    [AvaloniaFact]
    public void A_consent_dialogs_string_is_refused_even_inside_a_grant()
    {
        FluentActions.Invoking(() => Service("a").Register(Dictionary("ConsentString"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*\"Str.Dialog.AddinConsent.Allow\": a Str.* key is the IDE's text*");

        Find("Str.Dialog.AddinConsent.Allow").Should().NotBe("Hijacked");
        Find("RegisterResourcesTests.ConsentBrush").Should().BeNull("one refused key refuses the whole dictionary");
    }

    [AvaloniaFact]
    public void A_key_in_a_merged_or_theme_dictionary_is_checked_as_one_at_the_top_is()
    {
        var service = Service("a");
        FluentActions.Invoking(() => service.Register(Dictionary("NestedConsentString"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>().WithMessage("*\"Str.Dialog.AddinConsent.Title\"*");
        Find("Str.Dialog.AddinConsent.Title").Should().NotBe("Hijacked");
        Find("RegisterResourcesTests.NestedBrush").Should().BeNull();

        var border = Application.Current!.TryGetResource("WarningBannerBorderBrush", ThemeVariant.Dark, out var dark) ? dark : null;
        FluentActions.Invoking(() => service.Register(Dictionary("ThemeHeldKey"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>().WithMessage("*\"WarningBannerBorderBrush\": already held by the IDE*");
        Application.Current!.TryGetResource("WarningBannerBorderBrush", ThemeVariant.Dark, out var after);
        after.Should().BeSameAs(border);
    }

    [AvaloniaFact]
    public void A_theme_for_a_type_the_grant_doesnt_cover_is_refused()
    {
        var button = Find(typeof(Button));

        FluentActions.Invoking(() => Service("a").Register(Dictionary("ForeignType"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*{x:Type Avalonia.Controls.Button}: a type from Avalonia.Controls, which its grant doesn't cover*");

        Find(typeof(Button)).Should().BeSameAs(button, "every Button in the IDE keeps its theme");
    }

    [AvaloniaFact]
    public void A_key_outside_the_grant_is_refused()
    {
        var narrow = new AddinResourceService
        {
            Grants = [new RegistrationGrant("a", Composed: true) { ResourceTypeAssemblies = ["HexIDE.Integration.Tests"] }],
        };

        FluentActions.Invoking(() => narrow.Register(Dictionary("OwnTheme"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("*\"RegisterResourcesTests.OwnBrush\": outside the keys its grant names*");
        Find(typeof(RegisteredThemeControl)).Should().BeNull();
    }

    [AvaloniaFact]
    public void An_owner_with_no_grant_registers_nothing()
    {
        FluentActions.Invoking(() => Service().Register(Dictionary("OwnTheme"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>().WithMessage("*isn't one HexIDE lets register resources*");
        Find("RegisterResourcesTests.OwnBrush").Should().BeNull();
    }

    [AvaloniaFact]
    public void A_key_that_is_neither_a_string_nor_a_type_is_refused()
    {
        FluentActions.Invoking(() => Service("a").Register(Dictionary("OtherKeyKind"), Owner("a"), Tests))
            .Should().Throw<InvalidOperationException>().WithMessage("*only string and type keys*");
        Find(ObjectKey).Should().BeNull();
    }

    [AvaloniaFact]
    public void Styles_are_refused_as_not_a_resource_dictionary()
    {
        var before = Application.Current!.Styles.Count;

        FluentActions.Invoking(() => Service("a").Register(Dictionary("StylesRoot"), Owner("a"), Tests))
            .Should().Throw<ArgumentException>();

        Application.Current!.Styles.Count.Should().Be(before);
    }

    [AvaloniaFact]
    public void A_dictionary_from_another_assembly_is_refused()
    {
        FluentActions.Invoking(() => Service("a").Register(
                new Uri("avares://HexIDE.Runtime/BuiltinControls/VBLabel.axaml"), Owner("a"), Tests))
            .Should().Throw<ArgumentException>().WithMessage("*only from its own assembly*");
    }

    [AvaloniaFact]
    public async Task The_IDEs_own_host_refuses_and_an_owned_host_checks_its_callers_assembly()
    {
        await using var real = new RealComposition();
        var host = real.Composition.HexIdeHost;

        // No owner to check against, or to name in a refusal.
        FluentActions.Invoking(() => host.RegisterResources(Dictionary("OwnTheme")))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host an add-in is given*");

        var tools = (AddinToolWindowService)host.ToolWindows;
        // An add-in's host under a package's identity: deny by default.
        var package = new AddinOrigin("tests.package", "A package");
        FluentActions.Invoking(() => new AddinOwnedHost(host, tools.ForOwner(package), package)
                .RegisterResources(Dictionary("OwnTheme")))
            .Should().Throw<InvalidOperationException>().WithMessage("*isn't one HexIDE lets register resources*");

        // The provider's identity, called from this assembly: the provider's own dictionary isn't this caller's to name.
        var provider = HexIDE.Desktop.BasicProviderComposition.Origin;
        FluentActions.Invoking(() => new AddinOwnedHost(host, tools.ForOwner(provider), provider)
                .RegisterResources(HexIDE.Runtime.BasicProvider.Resources[0]))
            .Should().Throw<ArgumentException>().WithMessage("*only from its own assembly*HexIDE.Integration.Tests*");
    }

    [AvaloniaFact]
    public void The_provider_registered_every_one_of_its_dictionaries_under_its_own_identity()
    {
        var merged = Application.Current!.Resources.MergedDictionaries.OfType<ResourceInclude>()
            .Select(include => include.Source).ToList();
        merged.Should().Contain(HexIDE.Runtime.BasicProvider.Resources);

        // Registering any of them again, as anyone, is a collision.
        FluentActions.Invoking(() => ((TestApp)Application.Current!).ProviderResources.Register(HexIDE.Runtime.BasicProvider.Resources[2],
                HexIDE.Desktop.BasicProviderComposition.Origin, typeof(HexIDE.Runtime.BasicProvider).Assembly))
            .Should().Throw<InvalidOperationException>().WithMessage("*already registered by HexIDE.Basic (hexide.basic)*");
    }

    private sealed class CapturedLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;

        public CapturedLog() => Log.Logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();

        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            lock (Events) Events.Add(logEvent);
        }

        public void Dispose() => Log.Logger = previous;
    }
}
