using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HexIDE.IDE;
using SkiaSharp;

namespace HexIDE.Repack;

/// <summary>Repacks a published stock macOS build, which is the <c>HexIDE.app</c> bundle (#3413).</summary>
internal static class MacRepacker
{
    internal const string StockExecutable = "HexIDE";
    internal const string StockIcon = "hexide.icns";

    internal static readonly string ProgramDirectory = Path.Combine("Contents", "MacOS");
    internal static readonly string StockExecutablePath = Path.Combine(ProgramDirectory, StockExecutable);
    internal static readonly string InfoPlistPath = Path.Combine("Contents", "Info.plist");
    internal static readonly string ResourcesDirectory = Path.Combine("Contents", "Resources");

    public static RepackResult Run(string inputDirectory, string brandFile, string outputDirectory)
        => Repacker.Run(new MacTarget(), inputDirectory, brandFile, outputDirectory);
}

/// <summary>
/// A macOS repack rewrites the bundle's identity, then seals it again with an ad-hoc signature.
/// No signing identity or key is used.
/// </summary>
internal sealed class MacTarget : RepackTarget
{
    private static readonly byte[][] MachOMagic =
    [
        [0xCF, 0xFA, 0xED, 0xFE], // A 64-bit executable for one processor.
        [0xCA, 0xFE, 0xBA, 0xBE], // A universal executable.
    ];

    public override string StockExecutable => MacRepacker.StockExecutablePath;

    public override string ProgramDirectory => MacRepacker.ProgramDirectory;

    public override string PayloadName(DistributionIdentity brand) => brand.Name + ".app";

    public override string ExecutableName(DistributionIdentity brand) => brand.Id;

    public override void CheckBrand(DistributionIdentity brand) => MacNames.Check(brand.Name);

    public override void CheckStockExecutable(string path)
    {
        Span<byte> start = stackalloc byte[4];
        using (var file = File.OpenRead(path))
        {
            var read = file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false);
            var matches = false;
            foreach (var magic in MachOMagic)
                matches |= read == start.Length && start.SequenceEqual(magic);
            if (!matches)
                throw new RepackException($"{path} is not a macOS executable.");
        }

        Payload.RequireExecutable(path, "macOS");
    }

    public override bool FinishExecutable(string executable, string stockExecutable, DistributionIdentity brand)
    {
        Payload.VerifySame(stockExecutable, executable, Path.GetFileName(executable));
        return false;
    }

    public override void WriteInstallerInputs(string outputDirectory, DistributionIdentity brand, string executableName, string stockExecutable)
    {
        var bundle = Path.Combine(outputDirectory, PayloadName(brand));
        var plistPath = Path.Combine(bundle, MacRepacker.InfoPlistPath);
        if (!File.Exists(plistPath))
            throw new RepackException($"The build has no {MacRepacker.InfoPlistPath}.");

        File.WriteAllBytes(plistPath, MacBundle.RewriteInfoPlist(File.ReadAllBytes(plistPath), brand, executableName));

        var resources = Path.Combine(bundle, MacRepacker.ResourcesDirectory);
        Directory.CreateDirectory(resources);
        File.WriteAllBytes(Path.Combine(resources, brand.Id + ".icns"), MacIcon.FromLogo(IconResources.LogoOf(brand)));

        var stockIcon = Path.Combine(resources, MacRepacker.StockIcon);
        if (File.Exists(stockIcon))
            File.Delete(stockIcon);

        // What the tool reports has to be what macOS sees. A real macOS run always reaches these: other platforms are
        // refused before this, and get here only from tests of the rest of the repack.
        if (OperatingSystem.IsMacOS())
        {
            RunCheck("/usr/bin/plutil", ["-lint", plistPath], $"macOS doesn't accept the {MacRepacker.InfoPlistPath} this tool wrote.");

            // codesign seals a bundle only when every file in Contents/MacOS carries a signature of its own. A build
            // published by scripts/macos-bundle.sh has them already, but the brand's descriptor and logo and any
            // --include'd files are new, and a build published before #4495 has none on its data files. So anything
            // that doesn't verify is signed ad hoc first, as the bundle script does. The executable is left to the
            // bundle's own signature: codesign resolves its path to the bundle.
            var programs = Path.Combine(bundle, MacRepacker.ProgramDirectory);
            foreach (var file in Directory.EnumerateFiles(programs, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(programs, file);
                if (relative == executableName || Run("/usr/bin/codesign", ["--verify", file]).ExitCode == 0)
                    continue;
                RunCheck("/usr/bin/codesign", ["--force", "-s", "-", file], $"macOS couldn't sign {relative}.");
            }

            RunCheck("/usr/bin/codesign", ["--force", "-s", "-", bundle], "macOS couldn't re-seal the bundle.");
            RunCheck("/usr/bin/codesign", ["--verify", "--strict", "--deep", bundle], "macOS doesn't accept the bundle signature.");
        }
    }

    private static void RunCheck(string tool, string[] arguments, string failure)
    {
        var (exitCode, text) = Run(tool, arguments);
        if (exitCode is null)
            throw new RepackException($"{tool} didn't finish. {failure}");
        if (exitCode != 0)
            throw new RepackException($"{failure} {text}".TrimEnd());
    }

    // The exit code, or null when the tool didn't finish within a minute, with what it wrote.
    private static (int? ExitCode, string Text) Run(string tool, string[] arguments)
    {
        var start = new ProcessStartInfo(tool, arguments) { RedirectStandardError = true, RedirectStandardOutput = true };
        using var process = Process.Start(start) ?? throw new RepackException($"Couldn't start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill();
            return (null, "");
        }

        return (process.ExitCode, $"{errors.GetAwaiter().GetResult()}{output.GetAwaiter().GetResult()}");
    }

    public override IEnumerable<string> Describe(RepackResult result)
    {
        yield return $"  {result.ProductName}.app: its name, bundle identifier and icon are now {result.ProductName}'s.";
        yield return $"  Contents/MacOS/{result.ExecutableName}: HexIDE's executable under its new name.";
        yield return "  The helper programs beside it keep HexIDE's names, and the bundle has been re-sealed ad hoc.";
        yield return "";
        yield return "This output has no signing identity. Its bundle is re-sealed ad hoc, which identifies nobody. Sign it with your own Developer ID and notarize it before you ship it.";
    }
}

/// <summary>A brand's name becomes the bundle's folder, which the IDE's own check on names doesn't cover.</summary>
internal static class MacNames
{
    public static void Check(string name)
    {
        var bad = name.FirstOrDefault(c => c is '/' or ':');
        if (bad != default)
            throw new RepackException($"The name '{name}' contains '{bad}', and it has to work as the name of a macOS bundle.");
        if (name.StartsWith('.'))
            throw new RepackException($"The name '{name}' starts with a full stop, which would hide the bundle in Finder.");
    }
}

/// <summary>The bundle's identity, in the property list macOS reads it from.</summary>
internal static class MacBundle
{
    private const string AppleDocType = "-//Apple//DTD PLIST 1.0//EN";
    private const string AppleDtd = "http://www.apple.com/DTDs/PropertyList-1.0.dtd";

    /// <summary>
    /// From the id, as the Windows taskbar identity is. A published product can't change this, because macOS and every
    /// user's preferences know the product by it. <c>id</c> is already only letters, digits and hyphens, which is
    /// what a bundle identifier allows.
    /// </summary>
    public static string Identifier(DistributionIdentity brand) => $"io.hexide.distribution.{brand.Id}";

    /// <summary>Stock's property list with this product's identity in it, and everything else left as it was.</summary>
    public static byte[] RewriteInfoPlist(byte[] stock, DistributionIdentity brand, string executableName)
    {
        var values = new Dictionary<string, string>
        {
            ["CFBundleName"] = brand.Name,
            ["CFBundleDisplayName"] = brand.Name,
            ["CFBundleIdentifier"] = Identifier(brand),
            ["CFBundleExecutable"] = executableName,
            ["CFBundleIconFile"] = brand.Id,
        };

        // The DTD is ignored so nothing is fetched or expanded from a file this tool was handed, and is written back
        // as Apple's own.
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(stock), settings);
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException ex)
        {
            throw new RepackException($"The build's {MacRepacker.InfoPlistPath} isn't a property list this tool can read. {ex.Message}", ex);
        }

        var dictionary = document.Root?.Element("dict")
            ?? throw new RepackException($"The build's {MacRepacker.InfoPlistPath} isn't a property list this tool can read.");

        foreach (var (key, value) in values)
        {
            var entry = dictionary.Elements("key").FirstOrDefault(k => k.Value == key)?.ElementsAfterSelf().FirstOrDefault()
                ?? throw new RepackException($"The build's {MacRepacker.InfoPlistPath} has no {key}.");
            entry.Value = value;
        }

        document.AddFirst(new XDocumentType("plist", AppleDocType, AppleDtd, null));
        var writerSettings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.None };
        using var output = new MemoryStream();
        using (var writer = XmlWriter.Create(output, writerSettings))
            document.Save(writer);
        return output.ToArray();
    }
}

/// <summary>
/// The bundle's icon, made from the brand's logo. To regenerate stock's icon, extract its ic10 PNG and pass it
/// to FromLogo, then write the result to IDE/HexIDE/Icons/AppIcon/hexide.icns.
/// </summary>
internal static class MacIcon
{
    private const int HeaderSize = 8;

    // The 16- and 32-pixel entries use Apple's ARGB format; the remaining entries are PNG.
    internal static readonly (string Type, int Size)[] Entries =
    [
        ("ic04", 16), ("ic05", 32), ("ic11", 32), ("ic12", 64), ("ic07", 128),
        ("ic13", 256), ("ic08", 256), ("ic14", 512), ("ic09", 512), ("ic10", 1024),
    ];

    public static byte[] FromLogo(byte[] logoPng)
    {
        var rendered = IconResources.Render(logoPng, Entries.Select(e => e.Size).Distinct().ToArray())
            .ToDictionary(r => r.Size, r => r.Png);

        using var icon = new MemoryStream();
        Span<byte> length = stackalloc byte[4];
        icon.Write("icns"u8);
        icon.Write(length); // The whole file's length, filled in below.

        foreach (var (type, size) in Entries)
        {
            var png = rendered[size];
            var data = type is "ic04" or "ic05" ? Argb(png, size) : png;
            icon.Write(Encoding.ASCII.GetBytes(type));
            BinaryPrimitives.WriteInt32BigEndian(length, HeaderSize + data.Length);
            icon.Write(length);
            icon.Write(data);
        }

        var result = icon.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), result.Length);
        return result;
    }
    private static byte[] Argb(byte[] png, int size)
    {
        using var bitmap = SKBitmap.Decode(png);
        using var output = new MemoryStream();
        output.Write("ARGB"u8);
        for (var channel = 0; channel < 4; channel++)
        {
            var pixels = new byte[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var color = bitmap.GetPixel(x, y);
                pixels[y * size + x] = channel switch
                {
                    0 => color.Alpha, 1 => color.Red, 2 => color.Green, _ => color.Blue,
                };
            }
            // Literal packets are valid for any pixels, up to 128 per packet.
            for (var start = 0; start < pixels.Length; start += 128)
            {
                var count = Math.Min(128, pixels.Length - start);
                output.WriteByte((byte)(count - 1));
                output.Write(pixels, start, count);
            }
        }
        return output.ToArray();
    }

}
