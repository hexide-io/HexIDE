using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using HexIDE.IDE;

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
/// A macOS build's identity is in its bundle. The executable's bytes don't change, so the ad-hoc signature each program
/// was published with stays valid, and Apple Silicon needs one to run the program at all. The bundle isn't sealed, so
/// there is no signature over the files that do change.
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

        // Rewriting Info.plist would break a signature over the whole bundle, and HexIDE publishes none.
        var contents = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        if (Directory.Exists(Path.Combine(contents, "_CodeSignature")))
            throw new RepackException("This bundle is signed as a whole, and rewriting it would invalidate that signature. This tool repacks the unsealed bundles HexIDE publishes.");
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
            CheckSignature(Path.Combine(bundle, MacRepacker.ProgramDirectory, executableName));
        }
    }

    // Asked about a bundle's main executable, codesign answers for the bundle, and this one isn't sealed. What Apple
    // Silicon checks when it starts the program is the signature inside the file, so a copy elsewhere is what to ask about.
    private static void CheckSignature(string executable)
    {
        var scratch = Directory.CreateTempSubdirectory("hexide-repack-");
        try
        {
            var copy = Path.Combine(scratch.FullName, Path.GetFileName(executable));
            File.Copy(executable, copy);
            RunCheck("/usr/bin/codesign", ["--verify", copy],
                $"macOS doesn't accept the signature on {Path.GetFileName(executable)}, and Apple Silicon wouldn't run it.");
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static void RunCheck(string tool, string[] arguments, string failure)
    {
        var start = new ProcessStartInfo(tool, arguments) { RedirectStandardError = true, RedirectStandardOutput = true };
        using var process = Process.Start(start) ?? throw new RepackException($"Couldn't start {tool}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill();
            throw new RepackException($"{tool} didn't finish. {failure}");
        }

        if (process.ExitCode != 0)
            throw new RepackException($"{failure} {errors.GetAwaiter().GetResult()}{output.GetAwaiter().GetResult()}".TrimEnd());
    }

    public override IEnumerable<string> Describe(RepackResult result)
    {
        yield return $"  {result.ProductName}.app: its name, bundle identifier and icon are now {result.ProductName}'s.";
        yield return $"  Contents/MacOS/{result.ExecutableName}: HexIDE's executable under its new name.";
        yield return "  The helper programs beside it keep HexIDE's names, and every other file is as published.";
        yield return "";
        yield return "This output has no signing identity. Its programs keep stock's ad-hoc signatures, which identify nobody. Sign it with your own Developer ID and notarize it before you ship it.";
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

/// <summary>The bundle's icon, made from the brand's logo.</summary>
internal static class MacIcon
{
    private const int HeaderSize = 8;

    // The entries stock's own icon has, all PNG, in its order.
    internal static readonly (string Type, int Size)[] Entries =
    [
        ("icp4", 16), ("icp5", 32), ("ic11", 32), ("ic12", 64), ("ic07", 128),
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
            icon.Write(Encoding.ASCII.GetBytes(type));
            BinaryPrimitives.WriteInt32BigEndian(length, HeaderSize + png.Length);
            icon.Write(length);
            icon.Write(png);
        }

        var result = icon.ToArray();
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), result.Length);
        return result;
    }
}
