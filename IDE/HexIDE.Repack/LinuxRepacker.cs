using System.Text;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>Repacks a published stock Linux build (#3413).</summary>
internal static class LinuxRepacker
{
    internal const string StockExecutable = "HexIDE";

    public static RepackResult Run(string inputDirectory, string brandFile, string outputDirectory)
        => Repacker.Run(new LinuxTarget(), inputDirectory, brandFile, outputDirectory);
}

/// <summary>
/// A Linux executable holds no icon, version details or signature, so its identity is its name and the files that
/// describe it to the desktop. The executable's bytes don't change, and nothing here depends on its processor.
/// </summary>
internal sealed class LinuxTarget : RepackTarget
{
    private static ReadOnlySpan<byte> ElfMagic => [0x7F, (byte)'E', (byte)'L', (byte)'F'];

    public override string StockExecutable => LinuxRepacker.StockExecutable;

    public override string ExecutableName(DistributionIdentity brand) => brand.Id;

    public override void CheckStockExecutable(string path)
    {
        Span<byte> start = stackalloc byte[ElfMagic.Length];
        using (var file = File.OpenRead(path))
        {
            if (file.ReadAtLeast(start, start.Length, throwOnEndOfStream: false) < start.Length || !start.SequenceEqual(ElfMagic))
                throw new RepackException($"{path} is not a Linux executable.");
        }

        Payload.RequireExecutable(path, "Linux");
    }

    public override bool FinishExecutable(string executable, string stockExecutable, DistributionIdentity brand)
    {
        Payload.VerifySame(stockExecutable, executable, Path.GetFileName(executable));
        return false;
    }

    public override void WriteInstallerInputs(string outputDirectory, DistributionIdentity brand, string executableName, string stockExecutable)
        => DesktopEntry.Write(Path.Combine(outputDirectory, DesktopEntry.ShareDirectory), brand, executableName);

    public override IEnumerable<string> Describe(RepackResult result)
    {
        yield return $"  app/{result.ExecutableName}: HexIDE's executable under {result.ProductName}'s name.";
        yield return $"  {DesktopEntry.ShareDirectory}/applications/{result.ExecutableName}.desktop: its menu entry, which starts {result.ExecutableName} from PATH.";
        yield return $"  {DesktopEntry.ShareDirectory}/icons/hicolor: its icon, made from your logo.";
        yield return "  The helper programs beside it keep HexIDE's names, and every other file is as published.";
        yield return "";
        yield return "This output is not signed, and a Linux executable has no signature to remove. Sign your archive or package before you ship it.";
    }
}

/// <summary>
/// The files that put a product in a Linux desktop's menu, laid out as they are under <c>/usr/share</c> or
/// <c>~/.local/share</c>, so that installing them is copying this tree there.
/// </summary>
internal static class DesktopEntry
{
    public const string ShareDirectory = "share";

    public static void Write(string share, DistributionIdentity brand, string executableName)
    {
        var applications = Path.Combine(share, "applications");
        Directory.CreateDirectory(applications);
        File.WriteAllText(Path.Combine(applications, brand.Id + ".desktop"), Text(brand, executableName), new UTF8Encoding(false));

        foreach (var (size, png) in IconResources.Render(IconResources.LogoOf(brand)))
        {
            var apps = Path.Combine(share, "icons", "hicolor", $"{size}x{size}", "apps");
            Directory.CreateDirectory(apps);
            File.WriteAllBytes(Path.Combine(apps, brand.Id + ".png"), png);
        }
    }

    /// <summary>
    /// The entry itself. <c>Exec</c> is a bare name because where the product is installed is the packager's choice,
    /// and the specification looks a bare name up on PATH. <c>StartupWMClass</c> is what lets the desktop match the
    /// running window to this entry, so it has to be the value the process declares.
    /// </summary>
    internal static string Text(DistributionIdentity brand, string executableName) => string.Join('\n',
        "[Desktop Entry]",
        "Type=Application",
        $"Name={Escape(brand.Name)}",
        $"Exec={executableName}",
        $"Icon={brand.Id}",
        "Terminal=false",
        "Categories=Development;IDE;",
        $"StartupWMClass={brand.WindowClass}",
        "");

    // In a desktop entry's value a backslash starts an escape. The name can't hold the line breaks and tabs that are
    // the format's other reserved characters, because the IDE's own check refuses them.
    private static string Escape(string value) => value.Replace("\\", "\\\\");
}
