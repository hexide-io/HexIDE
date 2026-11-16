using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.ViewModels.Options;

public class TrustChainLocalizationTests
{
    private static TrustChain Chain() => new()
    {
        BuildVersion = "1.2.3",
        BuildHash = "abcd1234",
        IsFirstParty = false,
        Publisher = new TrustLink("Publisher data", "publisher.id", "ABCD 1234"),
        Intermediate = new TrustLink("Marketplace data", "marketplace.id", "5678 ABCD"),
        Root = new TrustLink("Root data", "root.id", "1234 5678"),
    };

    [AvaloniaTheory]
    [InlineData("de", true)]
    [InlineData("pseudo", true)]
    [InlineData("de", false)]
    [InlineData("pseudo", false)]
    public async Task Both_entry_points_use_the_active_pack(string language, bool asDialog)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var chain = Chain();
        var registry = Substitute.For<IAddinRegistry>();
        using var page = new AddinOptionsPageViewModel(registry,
            new AddinInfo("/a", "Title", "Description", "1.2.3", "Author") { Chain = chain }, localization: localization);
        TrustChainViewModel? dialog = null;
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Do<IDialog>(value => dialog = (TrustChainViewModel)value)).Returns(Task.FromResult(true));
        var consent = new AddinConsentDialogViewModel("Title", "1.2.3", "Author", "Publisher data", localization, chain, windows);
        await consent.ViewTrustDetailsCommand.ExecuteAsync(null);
        var inspector = asDialog ? dialog! : page.TrustChain;
        inspector.ShowCloseButton.Should().Be(asDialog);
        AssertPack(inspector, localization);
        inspector.Rows[0].Primary.Should().Be(chain.BuildVersion);
        inspector.Rows[0].Fingerprint.Should().Be("abcd 1234");
        inspector.Rows[1].Primary.Should().Be(chain.Publisher.Label);
        inspector.Rows[1].Id.Should().Be(chain.Publisher.Id);
        inspector.Rows[1].Fingerprint.Should().Be(chain.Publisher.KeyFingerprint);
        inspector.Rows[2].Primary.Should().Be(chain.Intermediate.Label);
        inspector.Rows[2].Id.Should().Be(chain.Intermediate.Id);
        inspector.Rows[2].Fingerprint.Should().Be(chain.Intermediate.KeyFingerprint);
        inspector.Rows[3].Fingerprint.Should().Be(chain.Root.KeyFingerprint);
    }

    [AvaloniaTheory]
    [InlineData("de")]
    [InlineData("pseudo")]
    public void Open_options_rebuilds_translated_rows_on_language_switch(string language)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        using var page = new AddinOptionsPageViewModel(Substitute.For<IAddinRegistry>(),
            new AddinInfo("/a", "Title", "Description", "1", "Author") { Chain = Chain() }, localization: localization);
        var before = page.TrustChain;
        var changes = new List<string?>();
        page.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        localization.Apply(language);
        changes.Should().Contain(nameof(page.TrustChain));
        page.TrustChain.Rows.Select(row => row.Heading).Should().NotEqual(before.Rows.Select(row => row.Heading));
        AssertPack(page.TrustChain, localization);
    }

    [AvaloniaTheory]
    [InlineData("en")]
    [InlineData("de")]
    public void Branding_preserves_the_upstream_root_and_comparison_instructions(string language)
    {
        var localization = new LocalizationService();
        localization.Apply(language);
        var strings = new Dictionary<string, string>
        {
            ["Str.Dialog.TrustChain.Root"] = localization.GetString("Str.Dialog.TrustChain.Root"),
            ["Str.Dialog.TrustChain.CompareHint"] = localization.GetString("Str.Dialog.TrustChain.CompareHint"),
            ["Str.Test.Brand"] = "HexIDE",
        };
        BrandText.Apply(strings, "Acme Studio");
        strings["Str.Test.Brand"].Should().Be("Acme Studio");
        foreach (var key in new[] { "Str.Dialog.TrustChain.Root", "Str.Dialog.TrustChain.CompareHint" })
            strings[key].Should().Be(localization.GetString(key)).And.Contain("HexIDE").And.NotContain("Acme Studio");
        strings["Str.Dialog.TrustChain.CompareHint"].Should().Contain("TRUST.md");
    }

    private static void AssertPack(TrustChainViewModel inspector, LocalizationService localization)
    {
        string Text(string name)
        {
            var key = "Str.Dialog.TrustChain." + name;
            var value = localization.GetString(key);
            value.Should().NotBe(key);
            return value;
        }
        inspector.Title.Should().Be(Text("DialogTitle")).And.NotBe(localization.GetStringFrom("en", "Str.Dialog.TrustChain.DialogTitle"));
        inspector.Rows.Select(row => row.Heading).Should().Equal(new[] { Text("Build"), Text("Publisher"), Text("Marketplace"), Text("Root") });
        inspector.Rows[3].Primary.Should().Be(Text("TrustAnchor"));
        inspector.CompareHint.Should().Be(Text("CompareHint"));
    }
}
