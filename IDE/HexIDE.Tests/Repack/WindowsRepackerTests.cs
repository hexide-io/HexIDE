using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Repack;
using HexIDE.Tests.Infrastructure;
using SkiaSharp;

namespace HexIDE.Tests.Repack;

/// <summary>
/// For a test that repacks a real executable. The test host's own launcher is what it uses, and the tool only takes
/// x64 builds, so an Arm runner has nothing it can repack.
/// </summary>
public sealed class WindowsX64OnlyFactAttribute : FactAttribute
{
    public WindowsX64OnlyFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Needs a Windows x64 test host, whose launcher stands in for a stock executable.";
    }
}

/// <summary>
/// The Windows repack tool (#3413). What Windows itself reports about the output is asserted on Windows, where the
/// rewrite can run. Everything that refuses a run does so before that, so it is pinned on every platform.
/// </summary>
public sealed class WindowsRepackerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("hexide-repack-").FullName;
    private string Input => Path.Combine(_root, "stock");
    private string Brand => Path.Combine(_root, "brand");
    private string Output => Path.Combine(_root, "out");
    private string BrandFile => Path.Combine(Brand, "brand.json");
    private string RepackedExecutable => Path.Combine(Output, "app", "acme-ide.exe");

    public WindowsRepackerTests()
    {
        Directory.CreateDirectory(Input);
        Directory.CreateDirectory(Path.Combine(Brand, "branding"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // A real Windows program with a resource table. The test host's own launcher is one, on the machine that runs these.
    private static string HostExecutable => Path.Combine(AppContext.BaseDirectory, "HexIDE.Tests.exe");

    private void WriteStock(bool realExecutable = true, byte[]? executable = null)
    {
        var target = Path.Combine(Input, WindowsRepacker.StockExecutable);
        if (executable != null)
            File.WriteAllBytes(target, executable);
        else if (realExecutable)
            File.Copy(HostExecutable, target);
        else
            File.WriteAllBytes(target, MinimalPe(certificateSize: 0));

        File.WriteAllText(Path.Combine(Input, "HexIDE.Desktop.dll"), "managed");
        File.WriteAllText(Path.Combine(Input, "LICENSE"), "licence text");
        File.WriteAllText(Path.Combine(Input, "THIRD-PARTY-NOTICES.md"), "notices text");
        Directory.CreateDirectory(Path.Combine(Input, "addins"));
        File.WriteAllText(Path.Combine(Input, "addins", "nested.dat"), "nested");
    }

    private void WriteBrand(string name = "Acme IDE", string? extra = null, bool logo = true, string? copyright = "© 2026 Acme Ltd.")
    {
        var members = new List<string> { "\"schemaVersion\": 1", "\"id\": \"acme-ide\"", $"\"name\": \"{name.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"" };
        if (copyright != null)
            members.Add($"\"copyright\": \"{copyright}\"");
        if (logo)
            members.Add("\"logo\": \"branding/logo.png\"");
        if (extra != null)
            members.Add(extra);

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
        var run = () => WindowsRepacker.Run(Input, BrandFile, Output);
        run.Should().Throw<RepackException>().Which.Message.Should().Contain(reason);
        Directory.Exists(Output).Should().BeFalse("a refused run writes nothing");
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty("a refused run leaves nothing half-built");
    }

    // --- What gets refused ---

    [Fact]
    public void A_build_without_its_licence_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        File.Delete(Path.Combine(Input, "LICENSE"));
        ShouldRefuse("LICENSE");
    }

    [Fact]
    public void A_build_without_its_third_party_notices_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        File.Delete(Path.Combine(Input, "THIRD-PARTY-NOTICES.md"));
        ShouldRefuse("THIRD-PARTY-NOTICES.md");
    }

    [Fact]
    public void A_build_that_is_already_branded_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        File.WriteAllText(Path.Combine(Input, "brand.json"), "{}");
        ShouldRefuse("isn't a stock build");
    }

    [Fact]
    public void A_folder_that_is_not_a_published_build_is_refused()
    {
        WriteBrand();
        ShouldRefuse(WindowsRepacker.StockExecutable);
    }

    [Fact]
    public void An_output_that_already_exists_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        Directory.CreateDirectory(Output);

        var run = () => WindowsRepacker.Run(Input, BrandFile, Output);

        run.Should().Throw<RepackException>().Which.Message.Should().Contain("already exists");
    }

    [Theory]
    [InlineData("stock")]
    [InlineData("stock/inside")]
    public void An_output_that_would_rewrite_the_input_is_refused(string relative)
    {
        WriteStock(realExecutable: false);
        WriteBrand();

        var run = () => WindowsRepacker.Run(Input, BrandFile, Path.Combine(_root, relative));

        run.Should().Throw<RepackException>().Which.Message.Should().Contain("never rewrites a build in place");
    }

    [Fact]
    public void A_brand_with_no_logo_is_refused_rather_than_keeping_HexIDEs_icon()
    {
        WriteStock(realExecutable: false);
        WriteBrand(logo: false);
        ShouldRefuse("names no 'logo'");
    }

    [Fact]
    public void A_descriptor_the_IDE_would_refuse_is_refused_here_too()
    {
        WriteStock(realExecutable: false);
        File.WriteAllText(BrandFile, """{"schemaVersion": 1, "id": "acme-ide", "name": "Acme", "keys": "no"}""");
        ShouldRefuse("does not know");
    }

    [Theory]
    [InlineData("Acme: IDE", ":")]
    [InlineData("Acme/IDE", "/")]
    [InlineData("Acme\\IDE", "\\")]
    [InlineData("Acme \"IDE\"", "\"")]
    [InlineData("Acme IDE.", "full stop")]
    [InlineData("CON", "reserves")]
    [InlineData("nul.ide", "reserves")]
    public void A_name_that_cannot_be_a_folder_or_shortcut_is_refused(string name, string reason)
    {
        WriteStock(realExecutable: false);
        WriteBrand(name: name);
        ShouldRefuse(reason);
    }

    [Fact]
    public void An_id_that_would_land_on_a_file_of_the_build_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        File.WriteAllText(Path.Combine(Input, "acme-ide.exe"), "already here");
        ShouldRefuse("Choose another id");
    }

    [Fact]
    public void A_brand_file_that_would_replace_a_file_of_the_build_is_refused()
    {
        WriteStock(realExecutable: false);
        WriteBrand();
        Directory.CreateDirectory(Path.Combine(Input, "branding"));
        File.WriteAllText(Path.Combine(Input, "branding", "logo.png"), "the build's own");
        ShouldRefuse("would replace a file of the stock build");
    }

    [Fact]
    public void A_build_for_another_processor_is_refused()
    {
        WriteStock(executable: MinimalPe(certificateSize: 0, machine: (ushort)Machine.Arm64));
        WriteBrand();
        ShouldRefuse("x64 builds only");
    }

    // --- The installer's identity ---

    [Fact]
    public void An_AppId_is_the_name_based_guid_of_the_distribution_id()
    {
        // Computed independently: Python's uuid.uuid5 with this namespace.
        InstallerInputs.AppId("acme-ide").Should().Be("{8870088B-EE64-5EFB-AD0A-70B00C739D66}");
        InstallerInputs.AppId("other-ide").Should().Be("{950B46D1-9EC7-5D9B-A7EF-0E267420A49B}");
    }

    [Fact]
    public void The_generated_script_defines_this_products_identity_ahead_of_the_shared_one()
    {
        var script = InstallerInputs.Script(new DistributionIdentity("acme-ide", "Acme IDE"), "acme-ide.exe", "0.5.0");

        script.Should().Contain("""#define AppId "{{8870088B-EE64-5EFB-AD0A-70B00C739D66}" """.TrimEnd());
        script.Should().Contain("""#define AppName "Acme IDE" """.TrimEnd());
        script.Should().Contain("""#define AppExe "acme-ide.exe" """.TrimEnd());
        script.Should().Contain("""#define AppVersion "0.5.0" """.TrimEnd());
        script.Should().Contain("""#define AppUserModelId "HexIDE.Distribution.acme-ide" """.TrimEnd());
        script.IndexOf("#include", StringComparison.Ordinal).Should().BeGreaterThan(script.IndexOf("#define SetupBaseName", StringComparison.Ordinal));
    }

    [Fact]
    public void Stock_keeps_its_installer_identity()
    {
        // The defaults in the one script: changing these would orphan every stock installation.
        var script = File.ReadAllText(Path.Combine(RepoTree.Root(), "installer", "HexIDE.iss")).ReplaceLineEndings("\n");

        script.Should().Contain("""#define AppId "{{B2630434-546D-47B0-A96E-EE613B99F71D}" """.TrimEnd());
        script.Should().Contain("""#define AppName "HexIDE" """.TrimEnd());
        script.Should().Contain("""#define AppExe "HexIDE.exe" """.TrimEnd());
        script.Should().Contain("""#define SetupOutputDir "..\IDE\bin" """.TrimEnd());
        script.Should().Contain("""#define SetupBaseName "hexide-windows-amd64-setup" """.TrimEnd());
        script.Should().Contain("#ifdef AppUserModelId", "stock's shortcut declares no taskbar identity");
    }

    // --- The taskbar's identity ---

    [Fact]
    public void Stock_declares_no_taskbar_identity()
    {
        DistributionIdentity.Stock.AppUserModelId.Should().BeNull();
    }

    [Fact]
    public void A_brands_taskbar_identity_comes_from_its_id_and_stays_within_what_Windows_allows()
    {
        var longest = new string('a', 64);
        var identity = new DistributionIdentity(longest, "Renamed At Will").AppUserModelId;

        identity.Should().Be("HexIDE.Distribution." + longest);
        identity!.Length.Should().BeLessThanOrEqualTo(128);
        identity.Should().NotContain(" ");
    }

    [WindowsOnlyFact]
    public void A_branded_process_declares_that_identity_to_Windows()
    {
        var brand = new DistributionIdentity("acme-ide", "Acme IDE");

        WindowsTaskbarIdentity.Apply(brand);

        ExplicitAppUserModelId().Should().Be(brand.AppUserModelId);
    }

    private static string? ExplicitAppUserModelId()
    {
        var result = GetCurrentProcessExplicitAppUserModelID(out var id);
        if (result != 0)
            return null;
        var text = Marshal.PtrToStringUni(id);
        Marshal.FreeCoTaskMem(id);
        return text;
    }

    [DllImport("shell32.dll")]
    private static extern int GetCurrentProcessExplicitAppUserModelID(out nint id);

    // --- The signature ---

    [Fact]
    public void A_signature_is_removed_with_its_directory_entry()
    {
        var path = Path.Combine(_root, "signed.exe");
        File.WriteAllBytes(path, MinimalPe(certificateSize: 16));
        PeImage.HasSignature(path).Should().BeTrue("the fixture has to be signed for this to prove anything");

        PeImage.RemoveSignature(path).Should().BeTrue();

        PeImage.HasSignature(path).Should().BeFalse();
        new FileInfo(path).Length.Should().Be(MinimalPeLength, "the certificate table is gone too, not just its directory entry");
    }

    [Fact]
    public void An_unsigned_executable_is_left_alone()
    {
        var path = Path.Combine(_root, "plain.exe");
        var bytes = MinimalPe(certificateSize: 0);
        File.WriteAllBytes(path, bytes);

        PeImage.RemoveSignature(path).Should().BeFalse();

        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public void Data_after_a_signature_stops_the_run_rather_than_being_cut_off()
    {
        var path = Path.Combine(_root, "trailing.exe");
        File.WriteAllBytes(path, [.. MinimalPe(certificateSize: 16), .. new byte[32]]);

        var remove = () => PeImage.RemoveSignature(path);

        remove.Should().Throw<RepackException>().Which.Message.Should().Contain("data after its signature");
    }

    // --- A whole run, on the platform that can do one ---

    [WindowsX64OnlyFact]
    public void A_repacked_executable_says_it_is_the_brand_when_Windows_reads_it()
    {
        WriteStock();
        WriteBrand();

        var result = WindowsRepacker.Run(Input, BrandFile, Output);

        result.ExecutableName.Should().Be("acme-ide.exe");
        var stock = FileVersionInfo.GetVersionInfo(Path.Combine(Input, WindowsRepacker.StockExecutable));
        var version = FileVersionInfo.GetVersionInfo(RepackedExecutable);
        version.ProductName.Should().Be("Acme IDE");
        version.FileDescription.Should().Be("Acme IDE");
        version.CompanyName.Should().Be("Acme IDE");
        version.LegalCopyright.Should().Be("© 2026 Acme Ltd.");
        version.OriginalFilename.Should().Be("acme-ide.exe");
        version.InternalName.Should().Be("acme-ide.exe");
        version.ProductVersion.Should().Be(stock.ProductVersion, "the numbers stay the build's");
        version.FileVersion.Should().Be(stock.FileVersion);
        version.FileMajorPart.Should().Be(stock.FileMajorPart);
        version.ProductBuildPart.Should().Be(stock.ProductBuildPart);
    }

    [WindowsX64OnlyFact]
    public void A_brand_with_no_copyright_keeps_stocks_rather_than_claiming_none()
    {
        WriteStock();
        WriteBrand(copyright: null);

        WindowsRepacker.Run(Input, BrandFile, Output);

        var stock = FileVersionInfo.GetVersionInfo(Path.Combine(Input, WindowsRepacker.StockExecutable));
        FileVersionInfo.GetVersionInfo(RepackedExecutable).LegalCopyright.Should().Be(stock.LegalCopyright);
    }

    [WindowsX64OnlyFact]
    public void The_icon_is_the_brands_with_nothing_left_of_stocks()
    {
        WriteStock();
        WriteBrand();

        // A frame the brand's icon has no number for, which only deleting it can remove.
        NativeResources.Replace(Path.Combine(Input, WindowsRepacker.StockExecutable), [],
            [new NativeResource(PeImage.TypeIcon, 9, 0, [1, 2, 3])]);

        WindowsRepacker.Run(Input, BrandFile, Output);

        var resources = PeImage.ReadResources(RepackedExecutable);
        var frames = resources.Where(r => r.Type == PeImage.TypeIcon).ToList();
        frames.Select(r => r.Name).Should().BeEquivalentTo([1, 2, 3, 4]);
        foreach (var frame in frames)
            SKBitmap.Decode(frame.Data).Should().NotBeNull("a frame is a PNG of the logo");

        var group = resources.Where(r => r.Type == PeImage.TypeGroupIcon).Should().ContainSingle().Subject;
        group.Name.Should().Be(32512);
        BinaryPrimitives.ReadUInt16LittleEndian(group.Data.AsSpan(4)).Should().Be(4);

        using var large = SKBitmap.Decode(frames.Last().Data);
        large.Width.Should().Be(256);
        large.Height.Should().Be(256);
        large.GetPixel(128, 8).Alpha.Should().Be(0, "a logo that isn't square sits in a transparent square");
        large.GetPixel(128, 128).Should().Be(SKColors.OrangeRed);
    }

    [WindowsX64OnlyFact]
    public void Every_file_but_the_executable_is_exactly_as_published()
    {
        WriteStock();
        WriteBrand();

        WindowsRepacker.Run(Input, BrandFile, Output);

        var app = Path.Combine(Output, "app");
        File.Exists(Path.Combine(app, WindowsRepacker.StockExecutable)).Should().BeFalse("it was renamed, not duplicated");
        File.ReadAllText(Path.Combine(app, "LICENSE")).Should().Be("licence text");
        File.ReadAllText(Path.Combine(app, "THIRD-PARTY-NOTICES.md")).Should().Be("notices text");
        File.ReadAllText(Path.Combine(app, "HexIDE.Desktop.dll")).Should().Be("managed");
        File.ReadAllText(Path.Combine(app, "addins", "nested.dat")).Should().Be("nested");
    }

    [WindowsX64OnlyFact]
    public void The_brands_files_land_where_the_IDE_will_look_for_them()
    {
        WriteStock();
        WriteBrand();

        WindowsRepacker.Run(Input, BrandFile, Output);

        var resolved = DistributionDescriptor.Resolve(Path.Combine(Output, "app"));
        resolved.Id.Should().Be("acme-ide");
        resolved.OpenLogo().Should().NotBeNull();
    }

    [WindowsX64OnlyFact]
    public void A_default_profile_folder_travels_with_the_brand()
    {
        WriteStock();
        WriteBrand(extra: "\"defaultProfile\": \"branding/profile\"");
        Directory.CreateDirectory(Path.Combine(Brand, "branding", "profile"));
        File.WriteAllText(Path.Combine(Brand, "branding", "profile", "profile.json"), "{}");

        WindowsRepacker.Run(Input, BrandFile, Output);

        File.Exists(Path.Combine(Output, "app", "branding", "profile", "profile.json")).Should().BeTrue();
    }

    [WindowsX64OnlyFact]
    public void The_installer_inputs_are_written_beside_the_build_and_not_inside_it()
    {
        WriteStock();
        WriteBrand();

        WindowsRepacker.Run(Input, BrandFile, Output);

        File.Exists(Path.Combine(Output, "installer", "brand.iss")).Should().BeTrue();
        File.ReadAllText(Path.Combine(Output, "installer", "HexIDE.iss")).Should().Contain("#ifndef AppId");
        Directory.Exists(Path.Combine(Output, "app", "installer")).Should().BeFalse();
    }

    [WindowsX64OnlyFact]
    public void A_signed_stock_executable_comes_out_unsigned_and_the_run_says_so()
    {
        var signed = AppendCertificate(File.ReadAllBytes(HostExecutable));
        WriteStock(executable: signed);
        WriteBrand();

        var result = WindowsRepacker.Run(Input, BrandFile, Output);

        result.RemovedSignature.Should().BeTrue();
        PeImage.HasSignature(RepackedExecutable).Should().BeFalse();
        PeImage.HasSignature(Path.Combine(Input, WindowsRepacker.StockExecutable)).Should().BeTrue("the input is never written to");
    }

    [WindowsOnlyFact]
    public void A_run_that_fails_partway_leaves_nothing_behind()
    {
        // Passes every check on the way in, and then Windows can't update it.
        WriteStock(realExecutable: false);
        WriteBrand();

        var run = () => WindowsRepacker.Run(Input, BrandFile, Output);

        run.Should().Throw<RepackException>();
        Directory.Exists(Output).Should().BeFalse();
        Directory.GetDirectories(_root, "out.partial-*").Should().BeEmpty();
    }

    // --- Fixtures ---

    private const int MinimalPeLength = 1024;
    private const int OptionalHeaderOffset = 0x80 + 4 + 20;
    private const int CertificateDirectoryOffset = OptionalHeaderOffset + 112 + 4 * 8;

    // The smallest PE32+ image these readers accept: headers and one section, with room to hang a certificate table on.
    private static byte[] MinimalPe(int certificateSize, ushort machine = (ushort)Machine.Amd64)
    {
        var image = new byte[MinimalPeLength + certificateSize];
        var span = image.AsSpan();
        "MZ"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[0x3C..], 0x80);
        "PE\0\0"u8.CopyTo(span[0x80..]);

        var coff = 0x84;
        BinaryPrimitives.WriteUInt16LittleEndian(span[coff..], machine);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 2)..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 16)..], 240);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(coff + 18)..], 0x22);

        var optional = OptionalHeaderOffset;
        BinaryPrimitives.WriteUInt16LittleEndian(span[optional..], 0x20B);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optional + 32)..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optional + 36)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optional + 56)..], 0x2000);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optional + 60)..], 0x200);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(optional + 68)..], 3);
        BinaryPrimitives.WriteInt32LittleEndian(span[(optional + 108)..], 16);

        var section = optional + 240;
        ".text"u8.CopyTo(span[section..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 8)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 12)..], 0x1000);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 16)..], 0x200);
        BinaryPrimitives.WriteInt32LittleEndian(span[(section + 20)..], 0x200);

        if (certificateSize > 0)
        {
            BinaryPrimitives.WriteInt32LittleEndian(span[CertificateDirectoryOffset..], MinimalPeLength);
            BinaryPrimitives.WriteInt32LittleEndian(span[(CertificateDirectoryOffset + 4)..], certificateSize);
        }

        return image;
    }

    // Hangs a certificate table on a real executable, which is all RemoveSignature looks at.
    private static byte[] AppendCertificate(byte[] executable)
    {
        const int size = 32;
        var padded = (executable.Length + 7) / 8 * 8;
        var result = new byte[padded + size];
        executable.CopyTo(result, 0);

        using var image = new PEReader(new MemoryStream(executable));
        var directories = image.PEHeaders.PEHeaderStartOffset + 112;
        var entry = result.AsSpan(directories + 4 * 8);
        BinaryPrimitives.WriteInt32LittleEndian(entry, padded);
        BinaryPrimitives.WriteInt32LittleEndian(entry[4..], size);
        return result;
    }
}
