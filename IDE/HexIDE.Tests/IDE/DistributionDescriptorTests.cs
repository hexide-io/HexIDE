using System.IO;
using System.Text;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;

namespace HexIDE.Tests.IDE;

/// <summary>The installation descriptor that makes a build another product (#2042).</summary>
public sealed class DistributionDescriptorTests
{
    private const string Minimal = """{"schemaVersion":1,"id":"acme-ide","name":"Acme IDE"}""";

    private static byte[] Png(uint width = 64, uint height = 64)
    {
        var bytes = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        bytes[11] = 13;
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static DistributionIdentity Parse(string json, Func<string, byte[]>? assets = null) =>
        DistributionDescriptor.Parse(Encoding.UTF8.GetBytes(json), "brand.json", assets ?? (_ => throw new FileNotFoundException()));

    private static string Temporary()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-brand-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void A_folder_with_no_descriptor_is_stock_HexIDE()
    {
        var directory = Temporary();
        try
        {
            DistributionDescriptor.Resolve(directory).Should().BeSameAs(DistributionIdentity.Stock);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_descriptor_beside_the_executable_is_read()
    {
        var directory = Temporary();
        try
        {
            File.WriteAllText(Path.Combine(directory, DistributionDescriptor.FileName), Minimal);

            var identity = DistributionDescriptor.Resolve(directory);

            identity.Id.Should().Be("acme-ide");
            identity.Name.Should().Be("Acme IDE");
            identity.IsStock.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_broken_descriptor_is_refused_and_names_the_file()
    {
        var directory = Temporary();
        try
        {
            File.WriteAllText(Path.Combine(directory, DistributionDescriptor.FileName), "{");

            var read = () => DistributionDescriptor.Resolve(directory);

            read.Should().Throw<DistributionDescriptorException>().WithMessage($"*{DistributionDescriptor.FileName}*");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_logo_outside_the_installation_is_refused()
    {
        var directory = Temporary();
        try
        {
            File.WriteAllText(Path.Combine(directory, DistributionDescriptor.FileName),
                """{"schemaVersion":1,"id":"acme-ide","name":"Acme IDE","logo":"../logo.png"}""");

            var read = () => DistributionDescriptor.Resolve(directory);

            read.Should().Throw<DistributionDescriptorException>().WithMessage("*logo.png*");
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Everything_a_descriptor_may_say_is_captured()
    {
        var identity = Parse("""
            {
              "schemaVersion": 1, "id": "acme", "name": "Acme", "tagline": "For widgets", "copyright": "(c) Acme",
              "credits": ["Thanks to Bob"], "logo": "art/logo.png",
              "links": { "home": "https://acme.example/", "support": "https://acme.example/help",
                         "repository": "https://example.com/acme/ide" }
            }
            """, reference => reference == "art/logo.png" ? Png() : throw new FileNotFoundException());

        identity.Tagline.Should().Be("For widgets");
        identity.Copyright.Should().Be("(c) Acme");
        identity.AdditionalCredits.Should().Equal("Thanks to Bob");
        identity.HomeUrl.Should().Be("https://acme.example/");
        identity.SupportUrl.Should().Be("https://acme.example/help");
        identity.RepositoryUrl.Should().Be("https://example.com/acme/ide");
        identity.OpenLogo().Should().NotBeNull();
    }

    [Theory]
    [InlineData("""{"schemaVersion":2,"id":"a","name":"A"}""", "schemaVersion")]
    [InlineData("""{"id":"a","name":"A"}""", "schemaVersion")]
    [InlineData("""{"schemaVersion":1,"name":"A"}""", "'id'")]
    [InlineData("""{"schemaVersion":1,"id":"a"}""", "'name'")]
    [InlineData("""{"schemaVersion":1,"id":"hexide","name":"A"}""", "share HexIDE")]
    [InlineData("""{"schemaVersion":1,"id":"HexIDE","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"con","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"lpt1","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"..","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"a/b","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"a-","name":"A"}""", "folder name")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A","id":"b"}""", "repeats")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A_B"}""", "'name'")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"{0}"}""", "'name'")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":" A"}""", "'name'")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A\u202EB"}""", "format")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A","links":{"home":"http://a.example/"}}""", "https")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A","links":{"home":"https://u:p@a.example/"}}""", "https")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A","links":{"docs":"https://a.example/"}}""", "docs")]
    [InlineData("""{"schemaVersion":1,"id":"a","name":"A","logo":"logo.svg"}""", ".png")]
    public void An_unusable_descriptor_is_refused(string json, string reason)
    {
        var parse = () => Parse(json);

        parse.Should().Throw<DistributionDescriptorException>().WithMessage($"*{reason}*");
    }

    // There is no member for any of these, which is the whole of the protection, so each must stay an error.
    [Theory]
    [InlineData("userDataDirectory")]
    [InlineData("trustRoot")]
    [InlineData("onStart")]
    [InlineData("replaceCredits")]
    [InlineData("hideCredits")]
    public void A_descriptor_cannot_ask_for_what_it_is_not_allowed(string member)
    {
        var parse = () => Parse($$"""{"schemaVersion":1,"id":"a","name":"A","{{member}}":"x"}""");

        parse.Should().Throw<DistributionDescriptorException>().WithMessage($"*{member}*");
    }

    [Fact]
    public void A_file_that_is_not_a_PNG_is_refused_as_a_logo()
    {
        var parse = () => Parse("""{"schemaVersion":1,"id":"a","name":"A","logo":"l.png"}""", _ => new byte[64]);

        parse.Should().Throw<DistributionDescriptorException>().WithMessage("*not a PNG*");
    }

    [Theory]
    [InlineData(0u, 10u)]
    [InlineData(10u, 0u)]
    [InlineData(5000u, 10u)]
    [InlineData(10u, 5000u)]
    public void A_logo_of_an_unreasonable_size_is_refused(uint width, uint height)
    {
        var parse = () => Parse("""{"schemaVersion":1,"id":"a","name":"A","logo":"l.png"}""", _ => Png(width, height));

        parse.Should().Throw<DistributionDescriptorException>().WithMessage("*pixels*");
    }

    [Fact]
    public void State_follows_the_id_and_not_the_name()
    {
        var before = new DistributionIdentity("acme", "Acme");
        var renamed = new DistributionIdentity("acme", "Acme Studio");

        renamed.StateDirectoryName.Should().Be(before.StateDirectoryName).And.Be("acme");
        DistributionIdentity.Stock.StateDirectoryName.Should().Be("HexIDE");
    }

    [Fact]
    public void The_identity_cannot_change_once_something_has_read_it()
    {
        var holder = new DistributionHolder();
        _ = holder.Current;

        var establish = () => holder.Establish(new DistributionIdentity("acme", "Acme"));

        establish.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void The_identity_is_established_once()
    {
        var holder = new DistributionHolder();
        holder.Establish(new DistributionIdentity("acme", "Acme"));

        var again = () => holder.Establish(new DistributionIdentity("other", "Other"));

        again.Should().Throw<InvalidOperationException>();
        holder.Current.Id.Should().Be("acme");
    }

    [Fact]
    public void Recovery_files_live_under_the_distributions_own_folder()
    {
        // A real path on whichever host runs this: the method refuses one that is not rooted, as a drive letter is not on Linux.
        var local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "local"));

        RecoveryPath.For(null, windows: true, local, null, "/home/u", "acme")
            .Should().Be(Path.Combine(local, "acme", "Recovery"));
        RecoveryPath.For(null, windows: false, "", "/state", "/home/u", "acme")
            .Should().Be(Path.Combine("/state", "acme", "Recovery"));
    }

    [Fact]
    public void Stock_recovery_files_stay_where_they_have_always_been() =>
        RecoveryPath.For(null, windows: false, "", "/state", "/home/u")
            .Should().Be(Path.Combine("/state", "HexIDE", "Recovery"));

    [Fact]
    public void The_bundled_server_is_told_where_a_brand_logs()
    {
        var acme = new DistributionIdentity("acme", "Acme");

        LanguageServerDefaults.BundledArguments("", acme).Should().Be($"--log-root \"{acme.LogsDirectory}\"");
        LanguageServerDefaults.BundledArguments("--x", acme).Should().StartWith("--x --log-root ");
        acme.LogsDirectory.Should().Contain("acme").And.NotContain("HexIDE");
    }

    [Fact]
    public void Stock_starts_the_bundled_server_exactly_as_before() =>
        LanguageServerDefaults.BundledArguments("--x", DistributionIdentity.Stock).Should().Be("--x");

    [Fact]
    public void Help_names_the_brand_and_drops_HexIDEs_mark()
    {
        var help = ServerOptions.HelpText(new ConsoleTarget(false, 120),
            new DistributionIdentity("acme", "Acme Studio", "For widgets"));

        help.Should().Contain("Acme Studio - For widgets").And.Contain("An argument Acme Studio does not");
        help.Should().NotContain("open, cross-platform IDE").And.NotContain("docs/command-line.md");
        help.Should().NotContain("_-----------_");
    }

    [Fact]
    public void Stock_help_is_unchanged()
    {
        var help = ServerOptions.HelpText(new ConsoleTarget(false, 120), DistributionIdentity.Stock);

        help.Should().Contain("HexIDE - an open, cross-platform IDE").And.Contain("HexIDE.Desktop [options]");
        help.Should().Contain("_-----------_").And.Contain("docs/command-line.md");
    }

    [Fact]
    public void The_product_name_replaces_HexIDE_in_the_IDEs_own_strings()
    {
        var strings = new Dictionary<string, string> { ["Str.Menu.Help.About"] = "_About HexIDE...", ["Other"] = "Nothing here" };

        BrandText.Apply(strings, "Acme");

        strings["Str.Menu.Help.About"].Should().Be("_About Acme...");
        strings["Other"].Should().Be("Nothing here");
    }

    [Theory]
    [InlineData("Build HexIDE.Standalone first.", "Build HexIDE.Standalone first.")]
    [InlineData("Close HexIDE. Then retry.", "Close Acme. Then retry.")]
    [InlineData("Ends with HexIDE.", "Ends with Acme.")]
    [InlineData("HexIDE's safety", "Acme's safety")]
    public void A_file_name_keeps_its_name_and_a_sentence_does_not(string before, string after)
    {
        var strings = new Dictionary<string, string> { ["Key"] = before };

        BrandText.Apply(strings, "Acme");

        strings["Key"].Should().Be(after);
    }

    [Fact]
    public void A_product_name_is_substituted_literally()
    {
        var strings = new Dictionary<string, string> { ["Key"] = "About HexIDE" };

        BrandText.Apply(strings, "Cash $1 Studio");

        strings["Key"].Should().Be("About Cash $1 Studio");
    }

    [Theory]
    [InlineData("Str.Dialog.About.Credits.Thanks")]
    [InlineData("Str.Dialog.About.SigningKeys.RootKey")]
    [InlineData("Str.Dialog.About.SigningKeys.FirstPartyKey")]
    [InlineData("Str.Addins.Incompatible")]
    public void Statements_about_HexIDE_itself_keep_saying_so(string key)
    {
        var strings = new Dictionary<string, string> { [key] = "HexIDE made this" };

        BrandText.Apply(strings, "Acme");

        strings[key].Should().Be("HexIDE made this");
    }

    private static IUserTranslationsService Overrides(string packId, string key, string value)
    {
        var service = Substitute.For<IUserTranslationsService>();
        service.GetOverrides(Arg.Any<string>()).Returns(new Dictionary<string, string>());
        service.GetOverrides(packId).Returns(new Dictionary<string, string> { [key] = value });
        return service;
    }

    [Theory]
    [InlineData("A brand's own thanks")]
    [InlineData(" ")]
    public void A_translation_override_cannot_replace_HexIDEs_credits(string attempt)
    {
        var shipped = new LocalizationService().GetStringFrom("en", BrandText.UpstreamCreditsKey);

        var service = new LocalizationService(Overrides("en", BrandText.UpstreamCreditsKey, attempt));

        service.GetStringFrom("en", BrandText.UpstreamCreditsKey).Should().Be(shipped).And.Contain("Claude").And.Contain("Codex");
    }

    [Fact]
    public void Other_translation_overrides_still_apply()
    {
        var service = new LocalizationService(Overrides("en", "Str.Menu.Help", "Assistance"));

        service.GetStringFrom("en", "Str.Menu.Help").Should().Be("Assistance");
    }

    [Fact]
    public void A_shipped_translation_of_the_credits_is_kept()
    {
        var english = new LocalizationService().GetStringFrom("en", BrandText.UpstreamCreditsKey);

        new LocalizationService().GetStringFrom("de", BrandText.UpstreamCreditsKey).Should().NotBe(english);
    }
}
