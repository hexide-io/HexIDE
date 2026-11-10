using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using HexIDE.IDE;
using HexIDE.Repack;
using SkiaSharp;

namespace HexIDE.Tests.Repack;

/// <summary>For a test of what macOS's own tools say about a repacked bundle.</summary>
public sealed class MacOnlyFactAttribute : FactAttribute
{
    public MacOnlyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsMacOS())
            Skip = "macOS-only: asks macOS's own tools about the result.";
    }
}

/// <summary>For a test that needs files to have Unix permissions, which Linux and macOS both give it.</summary>
public sealed class UnixOnlyFactAttribute : FactAttribute
{
    public UnixOnlyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (OperatingSystem.IsWindows())
            Skip = "Needs Unix file permissions.";
    }
}

/// <summary>
/// The macOS repack (#3413). The checks made through macOS's own tools run on macOS. The rest of a run needs only Unix
/// permissions, so it is pinned on Linux as well, with a stand-in for the executable.
/// </summary>
public sealed class MacRepackerTests : IDisposable
{
    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode PlainFile = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private const string StockPlist = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>CFBundleName</key><string>HexIDE</string>
          <key>CFBundleDisplayName</key><string>HexIDE</string>
          <key>CFBundleIdentifier</key><string>io.hexide.HexIDE</string>
          <key>CFBundleExecutable</key><string>HexIDE</string>
          <key>CFBundleIconFile</key><string>hexide</string>
          <key>CFBundlePackageType</key><string>APPL</string>
          <key>CFBundleShortVersionString</key><string>0.6.0</string>
          <key>CFBundleVersion</key><string>0.6.0</string>
          <key>LSMinimumSystemVersion</key><string>13.0</string>
          <key>NSHighResolutionCapable</key><true/>
        </dict>
        </plist>
        """;

    // On macOS the check is real, so the stand-in has to carry a real signature: the test host's own launcher has one.
    private static byte[] MachO => OperatingSystem.IsMacOS()
        ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "HexIDE.Tests"))
        : [0xCF, 0xFA, 0xED, 0xFE, 7, 0, 0, 1, 9, 9, 9];

    private readonly string _root = Directory.CreateTempSubdirectory("hexide-repack-mac-").FullName;
    private string Input => Path.Combine(_root, "HexIDE.app");
    private string Programs => Path.Combine(Input, "Contents", "MacOS");
    private string Brand => Path.Combine(_root, "brand");
    private string Output => Path.Combine(_root, "out");
    private string BrandFile => Path.Combine(Brand, "brand.json");
    private string Bundle => Path.Combine(Output, "Acme IDE.app");
    private string RepackedExecutable => Path.Combine(Bundle, "Contents", "MacOS", "acme-ide");

    public MacRepackerTests()
    {
        Directory.CreateDirectory(Path.Combine(Programs, "standalone"));
        Directory.CreateDirectory(Path.Combine(Input, "Contents", "Resources"));
        Directory.CreateDirectory(Path.Combine(Brand, "branding"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void WriteFile(string path, byte[] content, UnixFileMode mode)
    {
        File.WriteAllBytes(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }

    private void WriteStock(byte[]? executable = null, UnixFileMode executableMode = Executable)
    {
        WriteFile(Path.Combine(Programs, MacRepacker.StockExecutable), executable ?? MachO, executableMode);
        WriteFile(Path.Combine(Programs, "HexIDE.VbLspServer"), [1, 2, 3], Executable);
        WriteFile(Path.Combine(Programs, "standalone", "HexIDE.Standalone"), [4, 5, 6], Executable);
        WriteFile(Path.Combine(Programs, "HexIDE.Desktop.dll"), "managed"u8.ToArray(), PlainFile);
        WriteFile(Path.Combine(Programs, "LICENSE"), "licence text"u8.ToArray(), PlainFile);
        WriteFile(Path.Combine(Programs, "THIRD-PARTY-NOTICES.md"), "notices text"u8.ToArray(), PlainFile);
        WriteFile(Path.Combine(Input, "Contents", "Info.plist"), Encoding.UTF8.GetBytes(StockPlist), PlainFile);
        WriteFile(Path.Combine(Input, "Contents", "Resources", "hexide.icns"), "stock icon"u8.ToArray(), PlainFile);
    }

    private void WriteBrand(string name = "Acme IDE", bool logo = true)
    {
        var members = new List<string> { "\"schemaVersion\": 1", "\"id\": \"acme-ide\"", $"\"name\": \"{name}\"" };
        if (logo)
            members.Add("\"logo\": \"branding/logo.png\"");

        File.WriteAllText(BrandFile, "{" + string.Join(",", members) + "}");
        File.WriteAllBytes(Path.Combine(Brand, "branding", "logo.png"), Png(64, 32));
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.OrangeRed);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private void ShouldRefuse(string reason)
    {
        var run = () => MacRepacker.Run(Input, BrandFile, Output);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(reason);
        Directory.Exists(Output).Should().BeFalse("a refused run writes nothing");
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty("a refused run leaves nothing half-built");
    }

    private static DistributionIdentity Acme => new("acme-ide", "Acme IDE");

    private static string Rewritten(DistributionIdentity brand)
        => Encoding.UTF8.GetString(MacBundle.RewriteInfoPlist(Encoding.UTF8.GetBytes(StockPlist), brand, brand.Id));

    private static string ValueOf(string plist, string key)
    {
        var marker = $"<key>{key}</key><string>";
        var start = plist.IndexOf(marker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{key} should be there");
        start += marker.Length;
        return plist[start..plist.IndexOf("</string>", start, StringComparison.Ordinal)];
    }

    private static string RunTool(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(tool, arguments) { RedirectStandardError = true, RedirectStandardOutput = true };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.Should().Be(0, output);
        return output;
    }

    // --- Which platform's repack runs ---

    [MacOnlyFact]
    public void A_mac_build_is_repacked_as_one()
    {
        WriteStock();

        RepackTarget.For(Input).Should().BeOfType<MacTarget>();
    }

    [LinuxOnlyFact]
    public void A_mac_build_is_refused_on_linux()
    {
        WriteStock();

        var choose = () => RepackTarget.For(Input);

        choose.Should().Throw<RepackException>().Which.Message.Should().Contain("has to be repacked on macOS");
    }

    [WindowsOnlyFact]
    public void A_mac_build_is_refused_on_windows()
    {
        WriteStock();

        var choose = () => RepackTarget.For(Input);

        choose.Should().Throw<RepackException>().Which.Message.Should().Contain("has to be repacked on macOS");
    }

    // --- What gets refused ---

    [UnixOnlyFact]
    public void A_file_that_is_not_a_mac_executable_is_refused()
    {
        WriteStock(executable: [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1]);
        WriteBrand();

        ShouldRefuse("is not a macOS executable");
    }

    [UnixOnlyFact]
    public void A_build_that_lost_its_execute_permission_is_refused()
    {
        WriteStock(executableMode: PlainFile);
        WriteBrand();

        ShouldRefuse("isn't marked as executable");
    }

    [UnixOnlyFact]
    public void A_build_without_its_licence_is_refused()
    {
        WriteStock();
        WriteBrand();
        File.Delete(Path.Combine(Programs, "LICENSE"));

        ShouldRefuse("LICENSE");
    }

    [UnixOnlyFact]
    public void A_build_that_is_already_branded_is_refused()
    {
        WriteStock();
        WriteBrand();
        File.Copy(BrandFile, Path.Combine(Programs, "brand.json"));

        ShouldRefuse("isn't a stock build");
    }

    [UnixOnlyFact]
    public void A_brand_with_no_logo_is_refused()
    {
        WriteStock();
        WriteBrand(logo: false);

        ShouldRefuse("names no 'logo'");
    }

    [UnixOnlyFact]
    public void A_property_list_with_a_key_missing_is_refused()
    {
        WriteStock();
        WriteBrand();
        File.WriteAllText(Path.Combine(Input, "Contents", "Info.plist"),
            StockPlist.Replace("<key>CFBundleExecutable</key><string>HexIDE</string>", ""));

        ShouldRefuse("has no CFBundleExecutable");
    }

    [Theory]
    [InlineData("Acme/IDE", "'/'")]
    [InlineData("Acme: IDE", "':'")]
    [InlineData(".Acme", "full stop")]
    public void A_name_that_cannot_be_a_bundles_name_is_refused(string name, string reason)
    {
        var check = () => MacNames.Check(name);

        check.Should().Throw<RepackException>().Which.Message.Should().Contain(reason);
    }

    [UnixOnlyFact]
    public void A_run_with_a_name_that_cannot_be_a_bundles_name_is_refused()
    {
        WriteStock();
        WriteBrand(name: "Acme: IDE");

        ShouldRefuse("':'");
    }

    [Fact]
    public void A_name_Windows_would_refuse_is_fine_for_a_bundle()
    {
        var check = () => MacNames.Check("Acme? IDE");

        check.Should().NotThrow();
    }

    // --- What a finished run holds ---

    [UnixOnlyFact]
    public void The_bundle_takes_the_name_and_the_executable_the_id()
    {
        WriteStock();
        WriteBrand();

        var result = MacRepacker.Run(Input, BrandFile, Output);

        result.ExecutableName.Should().Be("acme-ide");
        result.RemovedSignature.Should().BeFalse("a macOS executable's bytes aren't touched");
        File.ReadAllBytes(RepackedExecutable).Should().Equal(MachO);
        Payload.ModeOf(RepackedExecutable).Should().Be(Executable);
        File.Exists(Path.Combine(Bundle, "Contents", "MacOS", MacRepacker.StockExecutable)).Should().BeFalse();
    }

    [UnixOnlyFact]
    public void The_property_list_describes_the_brand_and_stocks_icon_is_gone()
    {
        WriteStock();
        WriteBrand();

        MacRepacker.Run(Input, BrandFile, Output);

        var plist = File.ReadAllText(Path.Combine(Bundle, "Contents", "Info.plist"));
        ValueOf(plist, "CFBundleName").Should().Be("Acme IDE");
        ValueOf(plist, "CFBundleExecutable").Should().Be("acme-ide");
        ValueOf(plist, "CFBundleIconFile").Should().Be("acme-ide");
        File.Exists(Path.Combine(Bundle, "Contents", "Resources", "acme-ide.icns")).Should().BeTrue();
        File.Exists(Path.Combine(Bundle, "Contents", "Resources", "hexide.icns")).Should().BeFalse();
    }

    [UnixOnlyFact]
    public void Every_other_file_is_as_published_and_the_helpers_keep_their_names_and_permissions()
    {
        WriteStock();
        WriteBrand();

        MacRepacker.Run(Input, BrandFile, Output);

        var programs = Path.Combine(Bundle, "Contents", "MacOS");
        File.ReadAllText(Path.Combine(programs, "LICENSE")).Should().Be("licence text");
        File.ReadAllText(Path.Combine(programs, "THIRD-PARTY-NOTICES.md")).Should().Be("notices text");
        Payload.ModeOf(Path.Combine(programs, "HexIDE.VbLspServer")).Should().Be(Executable);
        Payload.ModeOf(Path.Combine(programs, "standalone", "HexIDE.Standalone")).Should().Be(Executable);
        File.ReadAllBytes(Path.Combine(Input, "Contents", "Resources", "hexide.icns")).Should().Equal("stock icon"u8.ToArray(), "the input is never written to");
    }

    [UnixOnlyFact]
    public void The_brands_files_land_beside_the_programs_where_the_IDE_looks()
    {
        WriteStock();
        WriteBrand();

        MacRepacker.Run(Input, BrandFile, Output);

        var identity = DistributionDescriptor.Resolve(Path.Combine(Bundle, "Contents", "MacOS"));
        identity.Id.Should().Be("acme-ide");
        identity.OpenLogo().Should().NotBeNull();
    }

    // --- The bundle's identity and icon ---

    [Fact]
    public void The_bundle_identifier_comes_from_the_id_and_cannot_be_stocks()
    {
        var identifier = MacBundle.Identifier(Acme);

        identifier.Should().Be("io.hexide.distribution.acme-ide");
        identifier.Should().NotBe("io.hexide.HexIDE");
        identifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.').Should().BeTrue("that is all a bundle identifier may hold");
    }

    [Fact]
    public void Only_the_five_identity_values_change_in_the_property_list()
    {
        var plist = Rewritten(Acme);

        ValueOf(plist, "CFBundleName").Should().Be("Acme IDE");
        ValueOf(plist, "CFBundleDisplayName").Should().Be("Acme IDE");
        ValueOf(plist, "CFBundleIdentifier").Should().Be("io.hexide.distribution.acme-ide");
        ValueOf(plist, "CFBundleExecutable").Should().Be("acme-ide");
        ValueOf(plist, "CFBundleIconFile").Should().Be("acme-ide");
        ValueOf(plist, "CFBundleShortVersionString").Should().Be("0.6.0");
        ValueOf(plist, "LSMinimumSystemVersion").Should().Be("13.0");
        plist.Should().Contain("<key>NSHighResolutionCapable</key><true />", "XML writes an empty element with this space");
        plist.Should().Contain("""<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">""");
    }

    [Fact]
    public void A_name_with_characters_XML_reserves_is_escaped()
    {
        var plist = Rewritten(new DistributionIdentity("acme-ide", "Acme & <Co>"));

        ValueOf(plist, "CFBundleName").Should().Be("Acme &amp; &lt;Co&gt;");
    }

    [Fact]
    public void The_icon_has_every_entry_stocks_has_at_its_size()
    {
        var icon = MacIcon.FromLogo(Png(64, 32));

        Encoding.ASCII.GetString(icon, 0, 4).Should().Be("icns");
        BinaryPrimitives.ReadInt32BigEndian(icon.AsSpan(4)).Should().Be(icon.Length);

        var found = new List<(string Type, int Size)>();
        for (var at = 8; at < icon.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(icon.AsSpan(at + 4));
            using var frame = SKBitmap.Decode(icon.AsSpan(at + 8, length - 8));
            frame.Width.Should().Be(frame.Height, "a logo that isn't square sits in a transparent square");
            found.Add((Encoding.ASCII.GetString(icon, at, 4), frame.Width));
            at += length;
        }

        found.Should().Equal(MacIcon.Entries);
    }

    // --- What macOS itself says ---

    [MacOnlyFact]
    public void macOS_accepts_the_property_list_signature_and_icon()
    {
        WriteStock();
        WriteBrand();

        MacRepacker.Run(Input, BrandFile, Output);

        RunTool("/usr/bin/plutil", "-lint", Path.Combine(Bundle, "Contents", "Info.plist"));
        RunTool("/usr/bin/codesign", "--verify", RepackedExecutable);
        var iconset = Path.Combine(_root, "acme.iconset");
        RunTool("/usr/bin/iconutil", "-c", "iconset", "-o", iconset, Path.Combine(Bundle, "Contents", "Resources", "acme-ide.icns"));
        File.Exists(Path.Combine(iconset, "icon_512x512@2x.png")).Should().BeTrue();
    }

    [MacOnlyFact]
    public void A_signature_macOS_rejects_stops_the_run_and_leaves_nothing()
    {
        var damaged = MachO;
        damaged[damaged.Length / 2] ^= 0xFF;
        WriteStock(executable: damaged);
        WriteBrand();

        ShouldRefuse("doesn't accept the signature");
    }
}
