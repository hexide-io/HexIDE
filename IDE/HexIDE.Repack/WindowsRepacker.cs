using System.Diagnostics;
using System.Reflection.PortableExecutable;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>Repacks a published stock Windows build (#3413).</summary>
internal static class WindowsRepacker
{
    internal const string StockExecutable = "HexIDE.exe";

    public static RepackResult Run(string inputDirectory, string brandFile, string outputDirectory)
        => Repacker.Run(new WindowsTarget(), inputDirectory, brandFile, outputDirectory);
}

/// <summary>
/// A Windows build's identity is inside its executable, so the file is rewritten. Its old signature can't be valid
/// afterwards, so it is removed.
/// </summary>
internal sealed class WindowsTarget : RepackTarget
{
    public override string StockExecutable => WindowsRepacker.StockExecutable;

    public override string ExecutableName(DistributionIdentity brand) => brand.Id + ".exe";

    public override void CheckBrand(DistributionIdentity brand) => WindowsNames.Check(brand.Name);

    public override void CheckStockExecutable(string path)
    {
        var machine = ReadMachine(path);
        if (machine != Machine.Amd64)
            throw new RepackException($"{StockExecutable} is built for {machine}. This version repacks x64 builds only, which is what HexIDE's own installer targets.");
    }

    private static Machine ReadMachine(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            using var image = new PEReader(file);
            return image.PEHeaders.CoffHeader.Machine;
        }
        catch (BadImageFormatException ex)
        {
            throw new RepackException($"{path} is not a Windows executable.", ex);
        }
    }

    public override bool FinishExecutable(string executable, string stockExecutable, DistributionIdentity brand)
    {
        var version = VersionDetails.ForBrand(brand, Path.GetFileName(executable), FileVersionInfo.GetVersionInfo(stockExecutable));
        var replacements = IconResources.FromLogo(IconResources.LogoOf(brand)).Append(VersionResource.Build(version)).ToList();

        try
        {
            var removedSignature = PeImage.RemoveSignature(executable);
            var replaced = PeImage.ReadResources(executable)
                .Where(r => r.Type is PeImage.TypeIcon or PeImage.TypeGroupIcon or PeImage.TypeVersion)
                .ToList();
            NativeResources.Replace(executable, replaced, replacements);
            Verify(executable, replacements, version);
            return removedSignature;
        }
        catch (Exception ex) when (ex is BadImageFormatException or ArgumentException or IndexOutOfRangeException)
        {
            throw new RepackException($"{executable} is not a Windows executable this tool can rewrite. {ex.Message}", ex);
        }
    }

    // Read back from the file itself, and through Windows' own reader for the version details, so that what is
    // reported is what a person looking at the executable will see.
    private static void Verify(string executable, List<NativeResource> written, VersionDetails expected)
    {
        if (PeImage.HasSignature(executable))
            throw new RepackException($"{executable} still has a certificate table after rewriting.");

        var present = PeImage.ReadResources(executable);
        foreach (var type in new[] { PeImage.TypeIcon, PeImage.TypeGroupIcon })
        {
            var wanted = written.Where(r => r.Type == type).OrderBy(r => r.Name).ToList();
            var found = present.Where(r => r.Type == type).OrderBy(r => r.Name).ToList();
            if (wanted.Count != found.Count || !wanted.Zip(found).All(p => p.First.Name == p.Second.Name && p.First.Data.SequenceEqual(p.Second.Data)))
                throw new RepackException($"{executable} doesn't hold exactly the icon this tool wrote.");
        }

        var actual = FileVersionInfo.GetVersionInfo(executable);
        if (actual.ProductName != expected.ProductName || actual.FileDescription != expected.ProductName
            || actual.CompanyName != expected.ProductName || actual.LegalCopyright != expected.Copyright
            || actual.OriginalFilename != expected.ExecutableName || actual.ProductVersion != expected.ProductVersion)
            throw new RepackException($"Windows doesn't read the version details this tool wrote into {executable}.");
    }

    public override void WriteInstallerInputs(string outputDirectory, DistributionIdentity brand, string executableName, string stockExecutable)
        => InstallerInputs.Write(Path.Combine(outputDirectory, "installer"), brand, executableName, FileVersionInfo.GetVersionInfo(stockExecutable));

    public override IEnumerable<string> Describe(RepackResult result)
    {
        yield return $"  app\\{result.ExecutableName}: its name, icon and version details are now {result.ProductName}'s.";
        yield return $"  installer\\{InstallerInputs.ScriptName}: compile this with Inno Setup's ISCC to make the setup program.";
        yield return "  The helper programs beside it keep HexIDE's names, and every other file is as published.";
        yield return "";
        if (result.RemovedSignature)
            yield return $"Removed the signature from {result.ExecutableName}, because rewriting the file invalidated it.";
        yield return $"This output is not signed. Sign {result.ExecutableName} and the setup program before you ship them.";
    }
}

/// <summary>A brand's name becomes a folder and a shortcut, which the IDE's own check on names doesn't cover.</summary>
internal static class WindowsNames
{
    private static readonly string[] Reserved =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    public static void Check(string name)
    {
        var bad = name.FirstOrDefault(c => "<>:\"/\\|?*".Contains(c));
        if (bad != default)
            throw new RepackException($"The name '{name}' contains '{bad}', and it has to work as a Windows folder and shortcut name.");
        if (name.EndsWith('.'))
            throw new RepackException($"The name '{name}' ends with a full stop, which Windows removes from folder and shortcut names.");
        if (Reserved.Contains(name.Split('.')[0], StringComparer.OrdinalIgnoreCase))
            throw new RepackException($"The name '{name}' is one Windows reserves, so it can't be a folder or shortcut name.");
    }
}
