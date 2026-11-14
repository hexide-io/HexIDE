using System;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Localization;

namespace HexIDE.Tests.ViewModels.Options;

public class AddinPageLocalizationTests
{
    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("pseudo")]
    public void All_statuses_and_page_strings_follow_the_active_pack(string language)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var registry = Substitute.For<IAddinRegistry>();
        var info = new AddinInfo("/a", "", "", "1.2", "Author");
        using var page = new AddinOptionsPageViewModel(registry, info, localization: localization);
        foreach (var status in Enum.GetValues<AddinStatus>())
        {
            page.Status = status;
            var key = "Str.Options.Addin.Status." + status;
            localization.GetString(key).Should().NotBe(key);
            page.StatusText.Should().Be(localization.GetString(key));
        }
        using var signed = new AddinOptionsPageViewModel(registry, info with { Publisher = "Publisher" }, localization: localization);
        signed.Publisher.Should().Be(string.Format(localization.GetString("Str.Options.Addin.PublisherVerified"), "Publisher"));
        page.Publisher.Should().Be(localization.GetString("Str.Options.Addin.PublisherUnsigned"));
        page.Title.Should().Be(localization.GetString("Str.Options.Addin.Unnamed"));
        page.Description.Should().Be(localization.GetString("Str.Options.Addin.NoDescription"));
        foreach (var status in new[] { AddinStatus.Loaded, AddinStatus.Disabled, AddinStatus.ConsentDenied, AddinStatus.AwaitingConsent, AddinStatus.DeveloperUnsigned, AddinStatus.Untrusted })
        {
            page.Status = status;
            page.ToggleLabel.Should().Be(localization.GetString("Str.Options.Addin." + (status == AddinStatus.Disabled ? "Activate" : "Deactivate")));
            var consent = status switch { AddinStatus.Loaded or AddinStatus.DeveloperUnsigned => "ConsentAllowed", AddinStatus.ConsentDenied => "ConsentBlocked", AddinStatus.AwaitingConsent => "ConsentAwaiting", _ => null };
            page.ConsentText.Should().Be(consent is null ? "—" : localization.GetString("Str.Options.Addin." + consent));
        }
    }

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("pseudo")]
    public void Open_page_notifies_on_switch_preserves_manifest_and_unsubscribes(string language)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var registry = Substitute.For<IAddinRegistry>();
        var info = new AddinInfo("/a", "Signed title", "Signed description", "1.2", "Signed author", "Publisher");
        using var page = new AddinOptionsPageViewModel(registry, info, localization: localization);
        var changed = new List<string?>();
        page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        var before = page.Publisher;
        localization.Apply(language);
        page.Publisher.Should().Be(string.Format(localization.GetString("Str.Options.Addin.PublisherVerified"), "Publisher")).And.NotBe(before);
        changed.Should().Contain(new[] { "Title", "Description", "Publisher", "StatusText", "ToggleLabel", "ConsentText", "TrustChain" });
        page.Title.Should().Be(info.Title);
        page.Description.Should().Be(info.Description);
        page.Version.Should().Be(info.Version);
        page.Author.Should().Be(info.Author);
        page.Dispose();
        changed.Clear();
        localization.Apply("pseudo");
        changed.Should().BeEmpty();
    }
    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("pseudo")]
    public void Trust_reasons_and_every_computed_label_change_while_open(string language)
    {
        var localization = new LocalizationService();
        var registry = Substitute.For<IAddinRegistry>();
        foreach (var status in Enum.GetValues<AddinStatus>())
        {
            localization.Apply("en");
            registry.GetStatus("/a").Returns(status);
            using var page = new AddinOptionsPageViewModel(registry, new AddinInfo("/a", "", "", "", ""), localization: localization);
            var changes = new List<string?>();
            page.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
            var before = new[] { page.Title, page.Description, page.Publisher, page.ToggleLabel, page.ConsentText, page.StatusText, page.TrustChain.UnavailableReason };
            localization.Apply(language);
            var after = new[] { page.Title, page.Description, page.Publisher, page.ToggleLabel, page.ConsentText, page.StatusText, page.TrustChain.UnavailableReason };
            for (var n = 0; n < before.Length; n++)
                if (before[n] is not null && before[n] != "—") after[n].Should().NotBe(before[n]);
            changes.Should().Contain(new[] { "Title", "Description", "Publisher", "ToggleLabel", "ConsentText", "StatusText", "TrustChain" });
            var trustKey = status switch { AddinStatus.DeveloperUnsigned => "TrustUnsigned", AddinStatus.Untrusted => "TrustUnverified", _ => null };
            page.TrustChain.UnavailableReason.Should().Be(trustKey is null ? null : localization.GetString("Str.Options.Addin." + trustKey));
            page.Status = AddinStatus.Disabled;
            page.TrustChain.UnavailableReason.Should().Be(trustKey is null ? null : localization.GetString("Str.Options.Addin." + trustKey));
        }
    }

    [AvaloniaTheory]
    [InlineData(AddinStatus.Incompatible, "Str.Addins.Incompatible")]
    [InlineData(AddinStatus.Error, "Str.Addins.DuplicateId")]
    [InlineData(AddinStatus.Error, "Str.Addins.ChangedAfterVerification")]
    public void Load_messages_format_facts_in_the_display_language(AddinStatus status, string key)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        object[] arguments = key == "Str.Addins.Incompatible" ? [3, "999.0", 2, "0.2.0"] : key == "Str.Addins.DuplicateId" ? ["com.example.test", "/owner"] : [];
        var message = new AddinMessage(key, arguments);
        var original = string.Format(localization.GetString(key), arguments);
        var info = new AddinInfo("/a", "Title", "Description", "1", "Author")
        {
            CompatibilityError = status == AddinStatus.Incompatible ? original : null,
            CompatibilityMessage = status == AddinStatus.Incompatible ? message : null,
            LoadError = status == AddinStatus.Error ? message : null,
        };
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetStatus("/a").Returns(status);
        localization.Apply("de");
        using var page = new AddinOptionsPageViewModel(registry, info, localization: localization);
        foreach (var language in new[] { "de", "pseudo" })
        {
            var changed = new List<string?>();
            page.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            localization.Apply(language);
            page.StatusText.Should().Be(string.Format(localization.GetString(key), arguments)).And.NotBe(original);
            changed.Should().Contain("StatusText");
        }
        if (status == AddinStatus.Incompatible) info.CompatibilityError.Should().Be(original);
    }
}
