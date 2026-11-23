using System.Text.Json;
using Avalonia.Media;
using HexIDE.Localization;
using HexIDE.Profiles;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace HexIDE.Tests.Localization;

/// <summary>
/// Language-pack areas (#5394): what an area adds to each layer of the IDE's text, what it's refused, and that the user's
/// translations keep what no area holds now. Each test registers fixture areas in a registry of its own, so nothing here
/// touches the running IDE's.
/// </summary>
public sealed class LanguagePackAreasTests : IDisposable
{
    private const string Owner = "fixture.owner";
    private const string OwnerTitle = "Fixture Owner";
    private const string Hello = "Str.Fixture.Area.Hello";
    private const string Bye = "Str.Fixture.Area.Bye";

    private readonly string root = Directory.CreateTempSubdirectory("hexide-areas-").FullName;
    private readonly LanguagePackAreas areas = new();

    public void Dispose() => Directory.Delete(root, recursive: true);

    private static Uri Area(string name) => new($"avares://HexIDE.Tests/Localization/Areas/{name}/");

    private IDisposable Register(string name, Func<string, bool>? allowed = null, string owner = Owner, string title = OwnerTitle) =>
        areas.Register(owner, title, Area(name), allowed ?? (_ => true));

    private LocalizationService Service(ProfileSnapshot? profile = null, IUserTranslationsService? user = null) =>
        profile is null
            ? new LocalizationService(user) { Areas = areas }
            : new LocalizationService(profile, user) { Areas = areas };

    [AvaloniaFact]
    public void An_areas_keys_read_in_every_layer_it_has_a_file_for()
    {
        using var _ = Register("Good");
        var service = Service();

        service.Apply("en");
        service.GetString(Hello).Should().Be("Hello {0}");

        service.Apply("de");
        service.GetString(Hello).Should().Be("Hallo {0}", "the area's German file translates it");
        service.GetString(Bye).Should().Be("Bye", "a key the German file leaves out falls back to the area's English");

        service.Apply("fr-CA");
        service.GetString(Bye).Should().Be("Bye-bye", "the region's file is merged over the neutral's");
        service.GetString(Hello).Should().Be("Bonjour {0}", "the neutral's file fills what the region leaves out");

        service.GetString("Str.Menu.File").Should().Be(new LocalizationService().GetStringFrom("fr-CA", "Str.Menu.File"),
            "the IDE's own text is unchanged by an area");
    }

    [AvaloniaFact]
    public void Registering_and_taking_out_an_area_re_applies_the_active_language()
    {
        var service = Service();
        service.Apply("de");
        service.GetString(Hello).Should().Be(Hello, "nothing holds the key yet");

        var registration = Register("Good");
        service.GetString(Hello).Should().Be("Hallo {0}", "the active language picks up the area without another Apply");

        registration.Dispose();
        service.GetString(Hello).Should().Be(Hello, "taking the area out takes its keys out");
        service.ActiveLanguage.Should().Be("de");
    }

    [AvaloniaFact]
    public void An_area_never_sets_the_direction()
    {
        using var _ = Register("Rtl");
        var service = Service();

        service.Apply("de");

        service.GetString("Str.Fixture.Rtl.A").Should().Be("A (de)");
        service.FlowDirection.Should().Be(FlowDirection.LeftToRight, "direction is the IDE pack's");
    }

    [AvaloniaFact]
    public void Pseudo_and_the_secretly_english_rule_cover_an_areas_keys()
    {
        using var _ = Register("Good");
        var service = Service();

        service.Apply("pseudo");
        service.GetString(Hello).Should().NotBe("Hello {0}").And.Contain("{0}", "pseudo text transforms area keys too");

        service.ShippedOwnChainKeys("de").Should().Contain(Hello).And.NotContain(Bye,
            "a key only the area's English holds is English in German, as one of the IDE's own would be");
        service.ShippedOwnChainKeys("fr-CA").Should().Contain([Hello, Bye]);
    }

    [AvaloniaFact]
    public void A_profiles_override_of_an_area_key_applies_once_the_area_registers()
    {
        var profile = Profile(new() { ["de"] = new() { [Hello] = "Grüß dich {0}" } });
        var service = Service(profile);
        service.Apply("de");
        service.GetString(Hello).Should().Be(Hello, "no area holds the key yet, so the profile's text isn't applied");

        using var _ = Register("Good");

        service.GetString(Hello).Should().Be("Grüß dich {0}",
            "the profile's text is kept from load and applied when the key becomes canonical");
    }

    public static TheoryData<string, string, string> Refusals => new()
    {
        { "IdeKey", "Str.Menu.File", "already held by the IDE" },
        { "NotStr", "Ide.FlowDirection", "only Str.* keys are text" },
        { "Stray", "Str.Fixture.Stray.B in de.json", "not in the area's en.json" },
        { "BadPlaceholder", "Str.Fixture.Placeholder.A in de.json", "placeholders differ" },
        { "Blank", "Str.Fixture.Blank.A", "its English is blank" },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Refusals))]
    public void A_refused_key_refuses_the_whole_area_before_anything_merges(string fixture, string key, string reason)
    {
        var service = Service();
        service.Apply("de");
        var english = service.GetString("Str.Menu.File");
        var version = areas.Version;
        using var log = new CapturedLog();

        var register = () => Register(fixture);

        register.Should().Throw<InvalidOperationException>().WithMessage($"*{OwnerTitle}*{key}*{reason}*");
        areas.Areas.Should().BeEmpty();
        areas.Version.Should().Be(version, "a refused area changes nothing");
        service.GetString("Str.Menu.File").Should().Be(english);
        log.Messages.Should().Contain(message => message.Contains(OwnerTitle) && message.Contains(Owner)
            && message.Contains(key) && message.Contains(reason));
    }

    [AvaloniaFact]
    public void An_IDE_key_in_an_area_refuses_its_own_keys_too()
    {
        var service = Service();

        var register = () => Register("IdeKey");

        register.Should().Throw<InvalidOperationException>();
        service.GetString("Str.Fixture.IdeKey.Own").Should().Be("Str.Fixture.IdeKey.Own",
            "half an area is never added");
    }

    [AvaloniaFact]
    public void A_key_another_area_holds_is_refused_naming_both_owners()
    {
        using var _ = Register("Good");
        using var log = new CapturedLog();

        var register = () => Register("Clash", owner: "fixture.second", title: "Second Owner");

        register.Should().Throw<InvalidOperationException>()
            .WithMessage($"*Second Owner*{Hello}*already registered by {OwnerTitle} ({Owner})*");
        log.Messages.Should().Contain(message => message.Contains("Second Owner") && message.Contains("fixture.second")
            && message.Contains($"already registered by {OwnerTitle} ({Owner})"));
        Service().GetString(Hello).Should().Be("Hello {0}", "the first owner's text stands");
    }

    [AvaloniaFact]
    public void A_key_outside_the_grant_is_refused()
    {
        var register = () => Register("Good", allowed: key => key == Hello);

        register.Should().Throw<InvalidOperationException>().WithMessage($"*{Bye}*outside what its grant names*");
        areas.Areas.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void A_folder_with_no_english_is_refused()
    {
        var register = () => Register("NoEnglish");

        register.Should().Throw<ArgumentException>().WithMessage("*holds no en.json*");
        areas.Areas.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Disposing_twice_takes_the_area_out_once()
    {
        var registration = Register("Good");
        var changes = 0;
        areas.Changed += () => changes++;

        registration.Dispose();
        registration.Dispose();

        changes.Should().Be(1);
        areas.Areas.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_users_translation_of_a_key_no_area_holds_is_kept_and_saved()
    {
        WriteOverrides("de", new() { [Hello] = "Servus {0}", ["Str.Menu.File"] = "Datei!" });
        var user = new UserTranslationsService(root, areas);

        user.GetOverrides("de").Should().NotContainKey(Hello, "no area holds the key, so it isn't applied");

        user.SetOverride("de", "Str.Menu.Edit", "Bearbeiten!").Should().BeTrue();
        ReadOverrides("de").Should().Contain(Hello, "Servus {0}",
            "saving the language writes back what it couldn't place; dropping it would lose it from disk");

        using var _ = Register("Good");
        user.GetOverrides("de").Should().Contain(Hello, "Servus {0}", "the key is placed once an area holds it");

        var service = Service(user: user);
        service.Apply("de");
        service.GetString(Hello).Should().Be("Servus {0}", "the user's text beats the area's");
    }

    [AvaloniaFact]
    public void Clearing_the_last_placed_override_keeps_a_file_that_still_holds_kept_ones()
    {
        WriteOverrides("de", new() { [Hello] = "Servus {0}", ["Str.Menu.File"] = "Datei!" });
        var user = new UserTranslationsService(root, areas);

        user.ClearOverride("de", "Str.Menu.File");

        File.Exists(Path.Combine(root, "de.json")).Should().BeTrue();
        ReadOverrides("de").Should().Equal(new Dictionary<string, string> { [Hello] = "Servus {0}" });
    }

    [AvaloniaFact]
    public void Resetting_a_language_keeps_the_overrides_it_doesnt_show()
    {
        WriteOverrides("de", new() { [Hello] = "Servus {0}", ["Str.Menu.File"] = "Datei!" });
        var user = new UserTranslationsService(root, areas);

        user.ResetAll("de");

        user.GetOverrides("de").Should().BeEmpty();
        ReadOverrides("de").Should().Equal(new Dictionary<string, string> { [Hello] = "Servus {0}" });
    }

    [AvaloniaFact]
    public void An_override_of_a_key_whose_area_goes_is_kept_not_lost()
    {
        var registration = Register("Good");
        var user = new UserTranslationsService(root, areas);
        user.SetOverride("de", Hello, "Servus {0}").Should().BeTrue();

        registration.Dispose();
        user.GetOverrides("de").Should().NotContainKey(Hello);
        user.SetOverride("de", "Str.Menu.File", "Datei!").Should().BeTrue();

        ReadOverrides("de").Should().Contain(Hello, "Servus {0}");
        using var _ = Register("Good");
        user.GetOverrides("de").Should().Contain(Hello, "Servus {0}");
    }

    [AvaloniaFact]
    public void The_users_translation_must_keep_an_area_keys_placeholders()
    {
        using var _ = Register("Good");
        var user = new UserTranslationsService(root, areas);

        user.SetOverride("de", Hello, "Servus").Should().BeFalse("the area's English has {0}");
        user.SetOverride("de", Hello, "Servus {0}").Should().BeTrue();
    }

    private void WriteOverrides(string id, Dictionary<string, string> strings) =>
        File.WriteAllText(Path.Combine(root, id + ".json"), JsonSerializer.Serialize(
            new LanguagePackRecord(id, null, strings), LocalizationJsonContext.Default.LanguagePackRecord));

    private Dictionary<string, string> ReadOverrides(string id) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(root, id + ".json")),
            LocalizationJsonContext.Default.LanguagePackRecord)!.Strings!;

    private ProfileSnapshot Profile(Dictionary<string, Dictionary<string, string>> locales)
    {
        var profileRoot = Directory.CreateDirectory(Path.Combine(root, "profile")).FullName;
        var files = new Dictionary<string, string>();
        foreach (var (id, strings) in locales)
        {
            files[id] = id + ".json";
            File.WriteAllText(Path.Combine(profileRoot, files[id]), JsonSerializer.Serialize(
                new LanguagePackRecord(id, null, strings), LocalizationJsonContext.Default.LanguagePackRecord));
        }
        File.WriteAllText(Path.Combine(profileRoot, "profile.json"),
            "{\"schemaVersion\":1,\"id\":\"custom\",\"languageOverrides\":" + JsonSerializer.Serialize(files) + "}");
        return new ProfileResolver(profileRoot, profileRoot).Resolve(profileRoot)!;
    }

    private sealed class CapturedLog : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        private readonly List<string> messages = [];

        public CapturedLog() => Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (messages)
                    return [.. messages];
            }
        }

        public void Emit(LogEvent logEvent)
        {
            lock (messages)
                messages.Add(logEvent.RenderMessage());
        }

        public void Dispose() => Log.Logger = previous;
    }
}
