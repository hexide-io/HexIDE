using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>Why a repack stopped. The message is written for the person running the tool.</summary>
internal sealed class RepackException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a finished repack produced.</summary>
internal sealed record RepackResult(string ProductName, string ExecutableName, bool RemovedSignature);

/// <summary>
/// Turns a published stock Windows build into another product, driven by the same <c>brand.json</c> the IDE reads at
/// startup (#3413).
/// </summary>
/// <remarks>
/// <para>
/// <b>The result is built beside the destination and moved into place only when it is complete,</b> so a run that
/// fails leaves nothing that could be mistaken for a finished build. The input is never written to.
/// </para>
/// <para>
/// <b>This tool has no signing path and no trust path.</b> The rewritten executable's old signature can't be valid, so
/// it is removed and the adopter signs the result. Nothing here reads or writes a key or a trust anchor.
/// </para>
/// </remarks>
internal static class WindowsRepacker
{
    internal const string StockExecutable = "HexIDE.exe";
    private const string ManagedEntryPoint = "HexIDE.Desktop.dll";

    private static readonly string[] Notices = ["LICENSE", "THIRD-PARTY-NOTICES.md"];

    public static RepackResult Run(string inputDirectory, string brandFile, string outputDirectory)
    {
        var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputDirectory));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var brandPath = Path.GetFullPath(brandFile);

        try
        {
            var brand = ReadBrand(brandPath);
            var executableName = brand.Id + ".exe";
            var stockExecutable = CheckInput(input, output, executableName);
            return Build(input, stockExecutable, brandPath, brand, executableName, output);
        }
        catch (DistributionDescriptorException ex)
        {
            throw new RepackException(ex.Message, ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new RepackException($"The repack failed and wrote nothing to {output}. {ex.Message}", ex);
        }
    }

    private static DistributionIdentity ReadBrand(string brandPath)
    {
        if (!string.Equals(Path.GetFileName(brandPath), DistributionDescriptor.FileName, StringComparison.OrdinalIgnoreCase))
            throw new RepackException($"--brand must name a file called {DistributionDescriptor.FileName}, because that is the only name the IDE reads.");
        if (!File.Exists(brandPath))
            throw new RepackException($"{brandPath} does not exist.");

        var brand = DistributionDescriptor.Resolve(Path.GetDirectoryName(brandPath)!);
        WindowsNames.Check(brand.Name);

        if (brand.OpenLogo() is not { } logo)
            throw new RepackException($"{brandPath} names no 'logo', so the executable would keep HexIDE's icon under {brand.Name}'s name. Add a logo.");
        logo.Dispose();

        return brand;
    }

    private static string CheckInput(string input, string output, string executableName)
    {
        if (!Directory.Exists(input))
            throw new RepackException($"{input} does not exist.");

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(input, output, comparison)
            || output.StartsWith(input + Path.DirectorySeparatorChar, comparison)
            || input.StartsWith(output + Path.DirectorySeparatorChar, comparison))
            throw new RepackException("--output must be separate from --input. This tool writes a new directory and never rewrites a build in place.");
        if (Directory.Exists(output) || File.Exists(output))
            throw new RepackException($"{output} already exists. This tool only writes a new directory.");

        var stock = Path.Combine(input, StockExecutable);
        if (!File.Exists(stock) || !File.Exists(Path.Combine(input, ManagedEntryPoint)))
            throw new RepackException($"{input} has no {StockExecutable} and {ManagedEntryPoint}. Point --input at a published HexIDE build, such as an unpacked release archive.");
        if (File.Exists(Path.Combine(input, DistributionDescriptor.FileName)))
            throw new RepackException($"{input} already has a {DistributionDescriptor.FileName}, so it isn't a stock build.");
        if (File.Exists(Path.Combine(input, executableName)))
            throw new RepackException($"The build already has a file called {executableName}, which is the name this brand's 'id' gives its executable. Choose another id.");

        foreach (var notice in Notices)
        {
            if (!File.Exists(Path.Combine(input, notice)))
                throw new RepackException($"{input} has no {notice}. It has to travel with every copy, so this build can't be repacked.");
        }

        var machine = ReadMachine(stock);
        if (machine != Machine.Amd64)
            throw new RepackException($"{StockExecutable} is built for {machine}. This version repacks x64 builds only, which is what HexIDE's own installer targets.");

        return stock;
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

    private static RepackResult Build(string input, string stockExecutable, string brandPath, DistributionIdentity brand,
        string executableName, string output)
    {
        var staging = $"{output}.partial-{Guid.NewGuid():N}";
        try
        {
            var app = Path.Combine(staging, "app");
            Payload.Copy(input, app, renameFrom: StockExecutable, renameTo: executableName);
            CopyBrandFiles(brandPath, brand, app);

            // Checked again where it will really run: the same reader, against the layout the IDE will see.
            DistributionDescriptor.Resolve(app);

            var executable = Path.Combine(app, executableName);
            var stockVersion = FileVersionInfo.GetVersionInfo(stockExecutable);
            var removedSignature = Rewrite(executable, brand, executableName, stockVersion);

            Payload.VerifyUnchanged(input, app, except: StockExecutable);
            InstallerInputs.Write(Path.Combine(staging, "installer"), brand, executableName, stockVersion);

            Directory.Move(staging, output);
            return new RepackResult(brand.Name, executableName, removedSignature);
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    private static void CopyBrandFiles(string brandPath, DistributionIdentity brand, string app)
    {
        var brandDirectory = Path.GetDirectoryName(brandPath)!;
        var descriptor = File.ReadAllBytes(brandPath);
        File.WriteAllBytes(Path.Combine(app, DistributionDescriptor.FileName), descriptor);

        // The logo's reference isn't kept on the identity, and has already been contained and read by the validator.
        using var document = JsonDocument.Parse(descriptor);
        Payload.CopyNewFile(brandDirectory, document.RootElement.GetProperty("logo").GetString()!, app);

        if (brand.DefaultProfile is { } profile && profile.Contains('/'))
            Payload.CopyNewDirectory(brandDirectory, profile, app);
    }

    private static bool Rewrite(string executable, DistributionIdentity brand, string executableName, FileVersionInfo stockVersion)
    {
        using var logo = new MemoryStream();
        using (var source = brand.OpenLogo()!)
            source.CopyTo(logo);

        var version = VersionDetails.ForBrand(brand, executableName, stockVersion);
        var replacements = IconResources.FromLogo(logo.ToArray()).Append(VersionResource.Build(version)).ToList();

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

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Couldn't remove the unfinished directory {directory}: {ex.Message}");
        }
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

/// <summary>The stock build's files, copied and then checked.</summary>
internal static class Payload
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    public static void Copy(string source, string destination, string? renameFrom = null, string? renameTo = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", Everything))
        {
            // A published build has no links, and following one would copy something from outside it.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new RepackException($"{entry.FullName} is a link. A build to repack has to be plain files.");

            var relative = Path.GetRelativePath(source, entry.FullName);
            if (string.Equals(relative, renameFrom, StringComparison.OrdinalIgnoreCase))
                relative = renameTo!;

            var target = Path.Combine(destination, relative);
            if (entry is DirectoryInfo)
                Directory.CreateDirectory(target);
            else
                File.Copy(entry.FullName, target);
        }
    }

    /// <summary>Copies a file a brand added. It may not land on a file of the build, which stays as published.</summary>
    public static void CopyNewFile(string sourceDirectory, string reference, string destinationDirectory)
    {
        var target = Path.Combine(destinationDirectory, reference);
        if (File.Exists(target))
            throw new RepackException($"The brand's '{reference}' would replace a file of the stock build. Move it to a folder of its own.");

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(Path.Combine(sourceDirectory, reference), target);
    }

    /// <summary>Copies a folder a brand added, on the same terms as <see cref="CopyNewFile"/>.</summary>
    public static void CopyNewDirectory(string sourceDirectory, string reference, string destinationDirectory)
    {
        var source = Path.Combine(sourceDirectory, reference);
        var target = Path.Combine(destinationDirectory, reference);
        if (!Directory.Exists(source))
            throw new RepackException($"The brand's 'defaultProfile' names {reference}, which isn't a folder beside its brand.json.");
        if (Directory.Exists(target))
            throw new RepackException($"The brand's '{reference}' would land on a folder of the stock build. Move it to a folder of its own.");

        Copy(source, target);
    }

    /// <summary>Everything but the executable is required to be byte for byte what was published.</summary>
    public static void VerifyUnchanged(string source, string destination, string except)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", Everything))
        {
            var relative = Path.GetRelativePath(source, file);
            if (!string.Equals(relative, except, StringComparison.OrdinalIgnoreCase)
                && !HashOf(file).SequenceEqual(HashOf(Path.Combine(destination, relative))))
                throw new RepackException($"{relative} differs from the stock build's copy after repacking.");
        }
    }

    private static byte[] HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
