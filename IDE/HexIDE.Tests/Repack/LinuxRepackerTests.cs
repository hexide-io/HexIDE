using System.Runtime.CompilerServices;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Repack;
using SkiaSharp;

namespace HexIDE.Tests.Repack;

/// <summary>For a test of a Linux build's permissions, which only exist where the test runs on Linux.</summary>
public sealed class LinuxOnlyFactAttribute : FactAttribute
{
    public LinuxOnlyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsLinux())
            Skip = "Linux-only: a Linux build has to be repacked on Linux, and only there do its files have execute permissions.";
    }
}

/// <summary>
/// The Linux repack (#3413). The refusals it shares with Windows are in <see cref="WindowsRepackerTests"/>, and this
/// class pins what is Linux's own: execute permissions, the menu entry, and the window class that ties them together.
/// </summary>
public sealed class LinuxRepackerTests : IDisposable
{
    private const UnixFileMode Executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    private const UnixFileMode PlainFile = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    private static readonly byte[] Elf = [0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0, 9, 9, 9];

    private readonly string _root = Directory.CreateTempSubdirectory("hexide-repack-linux-").FullName;
    private string Input => Path.Combine(_root, "stock");
    private string Brand => Path.Combine(_root, "brand");
    private string Output => Path.Combine(_root, "out");
    private string BrandFile => Path.Combine(Brand, "brand.json");
    private string RepackedExecutable => Path.Combine(Output, "app", "acme-ide");
    private string MenuEntry => Path.Combine(Output, "share", "applications", "acme-ide.desktop");

    public LinuxRepackerTests()
    {
        Directory.CreateDirectory(Path.Combine(Input, "standalone"));
        Directory.CreateDirectory(Path.Combine(Brand, "branding"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteFile(string relative, byte[] content, UnixFileMode mode)
    {
        var path = Path.Combine(Input, relative);
        File.WriteAllBytes(path, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, mode);
    }

    private void WriteStock()
    {
        WriteFile(LinuxRepacker.StockExecutable, Elf, Executable);
        WriteFile("HexIDE.VbLspServer", Elf, Executable);
        WriteFile(Path.Combine("standalone", "HexIDE.Standalone"), Elf, Executable);
        WriteFile("HexIDE.Desktop.dll", "managed"u8.ToArray(), PlainFile);
        WriteFile("LICENSE", "licence text"u8.ToArray(), PlainFile);
        WriteFile("THIRD-PARTY-NOTICES.md", "notices text"u8.ToArray(), PlainFile);
    }

    private void WriteBrand(string id = "acme-ide", string name = "Acme IDE", bool logo = true)
    {
        var escaped = name.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var members = new List<string> { "\"schemaVersion\": 1", $"\"id\": \"{id}\"", $"\"name\": \"{escaped}\"" };
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

    [LinuxOnlyFact]
    public void Included_missing_folder_is_refused()
    {
        WriteStock();
        WriteBrand();
        var include = Path.Combine(_root, "missing");
        var run = () => Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, include);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(include);
        Directory.Exists(Output).Should().BeFalse();
    }

    [LinuxOnlyFact]
    public void Included_folder_below_a_link_is_allowed()
    {
        WriteStock();
        WriteBrand();
        var parent = Path.Combine(_root, "parent");
        var include = Path.Combine(parent, "include");
        Directory.CreateDirectory(include);
        File.WriteAllText(Path.Combine(include, "extra.txt"), "adopter");
        var link = Path.Combine(_root, "parent-link");
        Directory.CreateSymbolicLink(link, parent);
        Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, Path.Combine(link, "include"));
        File.ReadAllText(Path.Combine(Output, "app", "extra.txt")).Should().Be("adopter");
    }

    [LinuxOnlyFact]
    public void Included_output_ancestor_is_refused_before_staging()
    {
        WriteStock();
        WriteBrand();
        var run = () => Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, _root);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(_root);
        Directory.Exists(Output).Should().BeFalse();
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty();
    }

    [LinuxOnlyFact]
    public async Task Include_option_copies_files_and_reports_the_source_and_count()
    {
        WriteStock();
        WriteBrand();
        var include = Path.Combine(_root, "include");
        Directory.CreateDirectory(include);
        File.WriteAllText(Path.Combine(include, "notice.txt"), "adopter");
        var start = new System.Diagnostics.ProcessStartInfo("dotnet",
            [typeof(Repacker).Assembly.Location, "--input", Input, "--brand", BrandFile,
                "--output", Output, "--include", include])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        process.ExitCode.Should().Be(0, await errors);
        (await output).Should().Contain($"Included {1} file(s) from {include}");
        File.ReadAllText(Path.Combine(Output, "app", "notice.txt")).Should().Be("adopter");
    }

    private void ShouldRefuse(string reason)
    {
        var run = () => LinuxRepacker.Run(Input, BrandFile, Output);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(reason);
        Directory.Exists(Output).Should().BeFalse("a refused run writes nothing");
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty("a refused run leaves nothing half-built");
    }

    private static DistributionIdentity Acme => new("acme-ide", "Acme IDE");

    private static Dictionary<string, string> EntryValues(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("[Desktop Entry]");
        return lines.Skip(1).Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1]);
    }

    [LinuxOnlyFact]
    public void Included_files_reach_the_installable_payload_and_report()
    {
        WriteStock();
        WriteBrand();
        var include = Path.Combine(_root, "include");
        Directory.CreateDirectory(Path.Combine(include, "addons"));
        File.WriteAllText(Path.Combine(include, "addons", "notice.txt"), "adopter");

        var result = Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, include);

        File.ReadAllText(Path.Combine(Output, "app", "addons", "notice.txt")).Should().Be("adopter");
        result.IncludeDirectory.Should().Be(include);
        result.IncludedFiles.Should().Be(1);
        File.Exists(MenuEntry).Should().BeTrue();
    }

    public static bool CanInclude => OperatingSystem.IsLinux();

    [Theory(Skip = "Requires the target platform filesystem.", SkipUnless = nameof(CanInclude))]
    [InlineData("LICENSE")]
    [InlineData("HexIDE")]
    [InlineData("acme-ide")]
    [InlineData("brand.json")]
    [InlineData("branding/logo.png")]
    [InlineData("hexide-root.pub")]
    [InlineData("nested/HEXIDE-FIRSTPARTY.PUB")]
    [InlineData("revocations.json")]
    [InlineData("revocations.sig")]
    [InlineData("revocations-cache.json")]
    [InlineData("revocations-cache.sig")]
    public void Included_protected_files_are_refused_without_output(string relative)
    {
        WriteStock();
        WriteBrand();
        var include = Path.Combine(_root, "include");
        var file = Path.Combine(include, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "replacement");

        var run = () => Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, include);

        run.Should().Throw<RepackException>().Which.Message.Should().Contain(relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.Exists(Output).Should().BeFalse();
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty();
    }

    [LinuxOnlyFact]
    public void Included_links_are_refused_at_the_root_or_inside()
    {
        WriteStock();
        WriteBrand();
        var include = Path.Combine(_root, "include");
        Directory.CreateDirectory(include);
        File.CreateSymbolicLink(Path.Combine(include, "link"), BrandFile);
        var run = () => Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, include);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain("link");
        File.Delete(Path.Combine(include, "link"));
        var link = Path.Combine(_root, "linked");
        Directory.CreateSymbolicLink(link, include);
        run = () => Repacker.Run(new LinuxTarget(), Input, BrandFile, Output, link);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(link);
        Directory.Exists(Output).Should().BeFalse();
    }

    // --- Which platform's repack runs ---

    [LinuxOnlyFact]
    public void A_linux_build_is_repacked_as_one()
    {
        WriteStock();

        RepackTarget.For(Input).Should().BeOfType<LinuxTarget>();
    }

    [LinuxOnlyFact]
    public void A_windows_build_is_refused_on_linux()
    {
        WriteFile(WindowsRepacker.StockExecutable, [0x4D, 0x5A], PlainFile);

        var choose = () => RepackTarget.For(Input);

        choose.Should().Throw<RepackException>().Which.Message.Should().Contain("has to be repacked on Windows");
    }

    [WindowsOnlyFact]
    public void A_linux_build_is_refused_on_windows()
    {
        WriteFile(LinuxRepacker.StockExecutable, Elf, Executable);

        var choose = () => RepackTarget.For(Input);

        choose.Should().Throw<RepackException>().Which.Message.Should().Contain("has to be repacked on Linux");
    }

    [Fact]
    public void A_folder_with_neither_executable_is_refused()
    {
        var choose = () => RepackTarget.For(Input);

        choose.Should().Throw<RepackException>().Which.Message.Should().Contain("published HexIDE build");
    }

    // --- What gets refused ---

    [LinuxOnlyFact]
    public void A_file_that_is_not_a_linux_executable_is_refused()
    {
        WriteStock();
        WriteBrand();
        WriteFile(LinuxRepacker.StockExecutable, "#!/bin/sh\n"u8.ToArray(), Executable);

        ShouldRefuse("is not a Linux executable");
    }

    [LinuxOnlyFact]
    public void A_build_that_lost_its_execute_permission_is_refused()
    {
        WriteStock();
        WriteBrand();
        WriteFile(LinuxRepacker.StockExecutable, Elf, PlainFile);

        ShouldRefuse("isn't marked as executable");
    }

    [LinuxOnlyFact]
    public void A_build_without_its_licence_is_refused()
    {
        WriteStock();
        WriteBrand();
        File.Delete(Path.Combine(Input, "LICENSE"));

        ShouldRefuse("LICENSE");
    }

    [LinuxOnlyFact]
    public void A_brand_with_no_logo_is_refused_rather_than_having_no_icon()
    {
        WriteStock();
        WriteBrand(logo: false);

        ShouldRefuse("names no 'logo'");
    }

    [LinuxOnlyFact]
    public void An_id_that_is_the_name_of_a_folder_of_the_build_is_refused()
    {
        WriteStock();
        WriteBrand(id: "standalone");

        ShouldRefuse("Choose another id");
    }

    [LinuxOnlyFact]
    public void A_link_in_the_build_is_refused()
    {
        WriteStock();
        WriteBrand();
        File.CreateSymbolicLink(Path.Combine(Input, "outside"), "/etc/hostname");

        ShouldRefuse("is a link");
    }

    [LinuxOnlyFact]
    public void A_permission_lost_in_the_copy_stops_the_run()
    {
        WriteStock();
        var copy = Path.Combine(_root, "copy");
        Payload.Copy(Input, copy);
        Payload.VerifyUnchanged(Input, copy, except: "nothing");

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(copy, "HexIDE.VbLspServer"), PlainFile);
        var verify = () => Payload.VerifyUnchanged(Input, copy, except: "nothing");

        verify.Should().Throw<RepackException>().Which.Message.Should().Contain("permissions");
    }

    // --- What a finished run holds ---

    [LinuxOnlyFact]
    public void The_executable_takes_the_id_and_keeps_its_bytes_and_permission()
    {
        WriteStock();
        WriteBrand();

        var result = LinuxRepacker.Run(Input, BrandFile, Output);

        result.ExecutableName.Should().Be("acme-ide");
        result.RemovedSignature.Should().BeFalse("a Linux executable has no signature");
        File.ReadAllBytes(RepackedExecutable).Should().Equal(Elf);
        Payload.ModeOf(RepackedExecutable).Should().Be(Executable);
        File.Exists(Path.Combine(Output, "app", LinuxRepacker.StockExecutable)).Should().BeFalse();
    }

    [LinuxOnlyFact]
    public void Every_other_file_is_as_published_and_the_helpers_keep_their_names_and_permissions()
    {
        WriteStock();
        WriteBrand();

        LinuxRepacker.Run(Input, BrandFile, Output);

        var app = Path.Combine(Output, "app");
        File.ReadAllText(Path.Combine(app, "LICENSE")).Should().Be("licence text");
        File.ReadAllText(Path.Combine(app, "THIRD-PARTY-NOTICES.md")).Should().Be("notices text");
        Payload.ModeOf(Path.Combine(app, "HexIDE.VbLspServer")).Should().Be(Executable);
        Payload.ModeOf(Path.Combine(app, "standalone", "HexIDE.Standalone")).Should().Be(Executable);
        Payload.ModeOf(Path.Combine(app, "LICENSE")).Should().Be(PlainFile);
    }

    [LinuxOnlyFact]
    public void The_brands_files_land_where_the_IDE_will_look_for_them()
    {
        WriteStock();
        WriteBrand();

        LinuxRepacker.Run(Input, BrandFile, Output);

        var identity = DistributionDescriptor.Resolve(Path.Combine(Output, "app"));
        identity.Id.Should().Be("acme-ide");
        identity.OpenLogo().Should().NotBeNull();
    }

    [LinuxOnlyFact]
    public void The_menu_entry_and_icons_are_written_beside_the_build_and_not_inside_it()
    {
        WriteStock();
        WriteBrand();

        LinuxRepacker.Run(Input, BrandFile, Output);

        var values = EntryValues(File.ReadAllText(MenuEntry));
        values["Name"].Should().Be("Acme IDE");
        values["Exec"].Should().Be("acme-ide");
        values["Icon"].Should().Be("acme-ide");
        Directory.GetFiles(Path.Combine(Output, "app"), "*.desktop", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [LinuxOnlyFact]
    public void The_icon_is_the_logo_at_each_size_and_stays_square()
    {
        WriteStock();
        WriteBrand();

        LinuxRepacker.Run(Input, BrandFile, Output);

        foreach (var size in new[] { 16, 32, 48, 256 })
        {
            var icon = Path.Combine(Output, "share", "icons", "hicolor", $"{size}x{size}", "apps", "acme-ide.png");
            using var bitmap = SKBitmap.Decode(icon);
            bitmap.Width.Should().Be(size);
            bitmap.Height.Should().Be(size);
            bitmap.GetPixel(0, 0).Alpha.Should().Be(0, "a logo that isn't square sits in the middle of a transparent square");
            bitmap.GetPixel(size / 2, size / 2).Red.Should().BeGreaterThan(200, "the logo itself is orange-red");
        }
    }

    [LinuxOnlyFact]
    public void A_name_Windows_would_refuse_is_fine_on_linux()
    {
        WriteStock();
        WriteBrand(name: "Acme: IDE");

        LinuxRepacker.Run(Input, BrandFile, Output);

        EntryValues(File.ReadAllText(MenuEntry))["Name"].Should().Be("Acme: IDE");
    }

    // --- The menu entry's text, and the window class it shares with the process ---

    [Fact]
    public void A_backslash_in_the_name_is_escaped_as_the_format_needs()
    {
        var text = DesktopEntry.Text(new DistributionIdentity("acme-ide", @"Acme\IDE"), "acme-ide");

        EntryValues(text)["Name"].Should().Be(@"Acme\\IDE");
    }

    [Fact]
    public void The_entry_uses_only_line_feeds_and_ends_with_one()
    {
        var text = DesktopEntry.Text(Acme, "acme-ide");

        text.Should().NotContain("\r");
        text.Should().EndWith("\n");
    }

    [Fact]
    public void The_entry_names_the_window_class_the_process_declares()
    {
        var entry = EntryValues(DesktopEntry.Text(Acme, "acme-ide"));

        entry["StartupWMClass"].Should().Be(LinuxWindowIdentity.Options(Acme)!.WmClass);
    }

    [Fact]
    public void Stock_declares_no_window_class()
    {
        DistributionIdentity.Stock.WindowClass.Should().BeNull();
        LinuxWindowIdentity.Options(DistributionIdentity.Stock).Should().BeNull("stock's windows keep Avalonia's default class");
    }

    [Fact]
    public void A_brands_window_class_comes_from_its_id_and_stays_within_what_X11_carries()
    {
        var windowClass = new DistributionIdentity("acme-ide", "Acme IDÉ").WindowClass;

        windowClass.Should().Be("acme-ide");
        windowClass!.All(char.IsAscii).Should().BeTrue("Avalonia encodes the class as ASCII");
    }
}
