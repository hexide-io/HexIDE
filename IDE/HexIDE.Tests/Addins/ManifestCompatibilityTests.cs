using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using System.Diagnostics;
using System.Text.Json;
using HexIDE.AddinPacker;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.Addins;

public sealed class ManifestCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-compatibility-" + Guid.NewGuid().ToString("N"));

    public ManifestCompatibilityTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Incompatibility_cannot_be_overridden_and_the_options_page_explains_it(bool developer, bool unsigned)
    {
        var package = Path.Combine(_root, "package");
        Directory.CreateDirectory(package);
        var verifier = Substitute.For<IPackageVerifier>();
        verifier.Verify(package).Returns(new PackageVerificationResult(PackageVerdict.Incompatible,
            new AddinManifest { Entry = "never-load.dll", Schema = 3, MinHexideVersion = "999.0" },
            null, null, "requires a newer host") { ManifestHash = "hash" });
        var settings = Substitute.For<ISettingsService>();
        settings.LoadUnsignedAddins.Returns(unsigned);
        var devMode = Substitute.For<IDeveloperModeService>();
        devMode.IsEnabled.Returns(developer);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Addins.Incompatible").Returns("Required schema {0}, HexIDE {1}; supported schema {2}, HexIDE {3}");
        var consent = new ConsentStore(Path.Combine(_root, "consent.json"));
        consent.Record("test", "", "hash", ConsentDecisions.Allow);
        using var registry = new AddinRegistry(verifier, settings, devMode, Substitute.For<IWindowManager>(), localization,
            consent, new RevocationStore(null, Path.Combine(_root, "revocations")), Path.Combine(_root, "state.json"), _root);

        registry.LoadAll(Substitute.For<IHexIdeHost>());

        registry.GetStatus(package).Should().Be(AddinStatus.Incompatible);
        var info = registry.GetAll().Should().ContainSingle().Which;
        var page = new AddinOptionsPageViewModel(registry, info, localization: localization);
        page.StatusText.Should().Be($"Required schema 3, HexIDE 999.0; supported schema 2, HexIDE {PackageVerification.RunningVersion.ToString(3)}");
        page.CanRevokeConsent.Should().BeFalse();
        page.ToggleEnabledCommand.Execute(null);
        page.Status.Should().Be(AddinStatus.Disabled);
        page.ToggleEnabledCommand.Execute(null);
        page.Status.Should().Be(AddinStatus.Incompatible);
        page.StatusText.Should().Be(info.CompatibilityError);
    }

    [AvaloniaFact]
    public void Compatibility_status_wraps_in_the_options_page()
    {
        const string reason = "This package requires a newer version of HexIDE. Required schema 2, minimum HexIDE 999.0; this build supports schema 1, HexIDE 0.2.0.";
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetStatus("package").Returns(AddinStatus.Incompatible);
        var info = new AddinInfo("package", "Test", "Test", "1.0", "Test") { CompatibilityError = reason };
        var view = new HexIDE.Forms.Views.Options.AddinOptionsPageView
        {
            DataContext = new AddinOptionsPageViewModel(registry, info),
        };
        var status = view.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == reason);
        status.TextWrapping.Should().Be(TextWrapping.Wrap);
        status.Measure(new Size(300, double.PositiveInfinity));
        status.DesiredSize.Height.Should().BeGreaterThan(status.FontSize * 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Packer_stamps_the_requested_minimum_and_current_schema(bool commandLine)
    {
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "test.dll"), [1]);
        var output = Path.Combine(_root, "out");
        if (commandLine)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { typeof(PackageSigner).Assembly.Location, "pack", "--src", src,
                         "--out", output, "--id", "test", "--title", "Test", "--version", "1.0",
                         "--entry", "test.dll", "--trust", trust, "--min-hexide-version", "0.1" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            PackageSigner.Pack(new PackRequest
            {
                SrcDir = src, OutDir = output, Id = "test", Title = "Test", Version = "1.0",
                Entry = "test.dll", TrustDir = trust, MinHexideVersion = "0.1",
            });
        }
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "addin.json")));
        json.RootElement.GetProperty("schema").GetInt32().Should().Be(AddinManifest.CurrentSchema);
        json.RootElement.GetProperty("minHexideVersion").GetString().Should().Be("0.1");
        PackageVerification.Verify(output, File.ReadAllText(Path.Combine(trust, "hexide-root.pub"))).IsVerified.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[1]")]
    [InlineData("[null]")]
    [InlineData("[\"project.read\",\"project.read\"]")]
    [InlineData("[\"Project.Read\"]")]
    [InlineData("[\"unknown\"]")]
    public void Schema_two_invalid_declarations_are_incompatible_before_trust(string? declaration)
    {
        var field = declaration is null ? "" : ",\"capabilities\":" + declaration;
        File.WriteAllText(Path.Combine(_root, "addin.json"), "{\"schema\":2,\"entry\":\"test.dll\"" + field + "}");
        var result = PackageVerification.Verify(_root, null);
        result.Verdict.Should().Be(PackageVerdict.Incompatible);
        result.Error.Should().Contain("capabilities");
        result.ManifestHash.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"project.read\",\"project.write\",\"program.run\",\"ui.contribute\",\"languageServer.contribute\"]")]
    public void Valid_schema_two_declarations_reach_trust_verification(string declaration)
    {
        File.WriteAllText(Path.Combine(_root, "addin.json"),
            "{\"schema\":2,\"entry\":\"test.dll\",\"capabilities\":" + declaration + "}");
        var result = PackageVerification.Verify(_root, null);
        result.Verdict.Should().Be(PackageVerdict.Untrusted);
        result.Error.Should().Be("no root public key available");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Packer_emits_schema_two_and_stable_explicit_declarations(bool commandLine)
    {
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "test.dll"), [1]);
        var output = Path.Combine(_root, "out");
        if (commandLine)
        {
            var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { typeof(PackageSigner).Assembly.Location, "pack", "--src", src,
                         "--out", output, "--id", "test", "--title", "Test", "--version", "1.0",
                         "--entry", "test.dll", "--trust", trust, "--capability", "ui.contribute",
                         "--capability", "project.read" })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            process.ExitCode.Should().Be(0, await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
        else
        {
            PackageSigner.Pack(new PackRequest
            {
                SrcDir = src, OutDir = output, Id = "test", Title = "Test", Version = "1.0",
                Entry = "test.dll", TrustDir = trust,
            });
        }
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "addin.json")));
        json.RootElement.GetProperty("schema").GetInt32().Should().Be(2);
        json.RootElement.GetProperty("capabilities").EnumerateArray().Select(c => c.GetString())
            .Should().Equal(commandLine ? new[] { "project.read", "ui.contribute" } : Array.Empty<string>());
        PackageVerification.Verify(output, File.ReadAllText(Path.Combine(trust, "hexide-root.pub"))).IsVerified.Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown", "project.read")]
    [InlineData("Project.Read", "project.read")]
    [InlineData("project.read", "project.read")]
    public async Task Invalid_packer_declarations_leave_existing_output_untouched(string first, string second)
    {
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        var src = Path.Combine(_root, "src");
        Directory.CreateDirectory(src);
        File.WriteAllBytes(Path.Combine(src, "test.dll"), [1]);
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        var sentinel = Path.Combine(output, "keep.txt");
        File.WriteAllText(sentinel, "keep");
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { typeof(PackageSigner).Assembly.Location, "pack", "--src", src,
                     "--out", output, "--id", "test", "--title", "Test", "--version", "1.0",
                     "--entry", "test.dll", "--trust", trust, "--capability", first, "--capability", second })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        process.ExitCode.Should().Be(1, await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        File.ReadAllText(sentinel).Should().Be("keep");
    }

    [Theory]
    [InlineData("HexIDE.AIChat.dll", new[] { "project.read", "project.write", "program.run", "ui.contribute" })]
    [InlineData("HexIDE.TestAddin.dll", new[] { "project.read", "ui.contribute" })]
    public void Bundled_pack_recipes_declare_current_host_use(string entry, string[] expected)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "openspec")))
            root = root.Parent;
        root.Should().NotBeNull();
        var project = System.Xml.Linq.XDocument.Load(Path.Combine(root!.FullName, "IDE", "HexIDE.Desktop", "HexIDE.Desktop.csproj"));
        var command = project.Descendants("Exec").Select(e => (string?)e.Attribute("Command"))
            .Single(c => c is not null && c.Contains("--entry " + entry, StringComparison.Ordinal))!;
        var arguments = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var capabilities = arguments.Select((value, index) => (value, index))
            .Where(a => a.value == "--capability").Select(a => arguments[a.index + 1]);
        capabilities.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Invalid_packer_minimum_is_rejected_before_touching_output()
    {
        var request = new PackRequest
        {
            SrcDir = _root, OutDir = Path.Combine(_root, "out"), Id = "test", Title = "Test", Version = "1.0",
            Entry = "test.dll", TrustDir = _root, MinHexideVersion = "invalid",
        };
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<ArgumentException>();
        Directory.Exists(request.OutDir).Should().BeFalse();
    }
}
