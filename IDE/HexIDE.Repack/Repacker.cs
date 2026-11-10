using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.IDE;

namespace HexIDE.Repack;

/// <summary>Why a repack stopped. The message is written for the person running the tool.</summary>
internal sealed class RepackException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a finished repack produced.</summary>
internal sealed record RepackResult(string ProductName, string ExecutableName, bool RemovedSignature);

/// <summary>What differs between one platform's build and another's. <see cref="Repacker"/> holds what doesn't.</summary>
internal abstract class RepackTarget
{
    /// <summary>Where a published stock build's executable is, relative to the build's root.</summary>
    public abstract string StockExecutable { get; }

    /// <summary>The folder holding the programs and the files the IDE reads beside them, relative to the build's root.</summary>
    public virtual string ProgramDirectory => "";

    /// <summary>What the copied build is called in the output.</summary>
    public virtual string PayloadName(DistributionIdentity brand) => "app";

    /// <summary>The executable's name once it is the brand's.</summary>
    public abstract string ExecutableName(DistributionIdentity brand);

    /// <summary>Refuses a brand this platform can't carry.</summary>
    public virtual void CheckBrand(DistributionIdentity brand)
    {
    }

    /// <summary>Refuses a stock executable this platform's repack can't use.</summary>
    public abstract void CheckStockExecutable(string path);

    /// <summary>Makes the copied executable the brand's and checks it. True if a signature had to be removed.</summary>
    public abstract bool FinishExecutable(string executable, string stockExecutable, DistributionIdentity brand);

    /// <summary>Writes what installing the product needs, beside the build rather than inside it.</summary>
    public abstract void WriteInstallerInputs(string outputDirectory, DistributionIdentity brand, string executableName, string stockExecutable);

    /// <summary>What to tell the person who ran the tool about a finished repack.</summary>
    public abstract IEnumerable<string> Describe(RepackResult result);

    /// <summary>
    /// Chooses by what the build holds, because the same machine can hold either kind. Each kind has to be repacked on
    /// its own platform, for the reason its message gives.
    /// </summary>
    public static RepackTarget For(string inputDirectory)
    {
        if (!Directory.Exists(inputDirectory))
            throw new RepackException($"{inputDirectory} does not exist.");

        if (File.Exists(Path.Combine(inputDirectory, WindowsRepacker.StockExecutable)))
        {
            if (!OperatingSystem.IsWindows())
                throw new RepackException("This is a Windows build, and it has to be repacked on Windows, because the tool rewrites the executable through Windows' own resource functions.");
            return new WindowsTarget();
        }

        if (File.Exists(Path.Combine(inputDirectory, LinuxRepacker.StockExecutable)))
        {
            if (!OperatingSystem.IsLinux())
                throw new RepackException("This is a Linux build, and it has to be repacked on Linux, because only there does a copy keep the execute permission its programs need.");
            return new LinuxTarget();
        }

        if (File.Exists(Path.Combine(inputDirectory, MacRepacker.StockExecutablePath)))
        {
            if (!OperatingSystem.IsMacOS())
                throw new RepackException("This is a macOS build, and it has to be repacked on macOS, because the tool checks the result with macOS's own signature and property-list tools.");
            return new MacTarget();
        }

        throw new RepackException($"{inputDirectory} has no {WindowsRepacker.StockExecutable}, {LinuxRepacker.StockExecutable} or {MacRepacker.StockExecutablePath}. Point --input at a published HexIDE build: an unpacked release archive, or HexIDE.app on macOS.");
    }
}

/// <summary>
/// Turns a published stock build into another product, driven by the same <c>brand.json</c> the IDE reads at startup
/// (#3413).
/// </summary>
/// <remarks>
/// <para>
/// <b>The result is built beside the destination and moved into place only when it is complete,</b> so a run that
/// fails leaves nothing that could be mistaken for a finished build. The input is never written to.
/// </para>
/// <para>
/// <b>This tool has no signing path and no trust path.</b> The adopter signs the result. Nothing here reads or writes
/// a key or a trust anchor.
/// </para>
/// </remarks>
internal static class Repacker
{
    private const string ManagedEntryPoint = "HexIDE.Desktop.dll";

    private static readonly string[] Notices = ["LICENSE", "THIRD-PARTY-NOTICES.md"];

    public static RepackResult Run(RepackTarget target, string inputDirectory, string brandFile, string outputDirectory)
    {
        var input = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inputDirectory));
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputDirectory));
        var brandPath = Path.GetFullPath(brandFile);

        try
        {
            var brand = ReadBrand(target, brandPath);
            var executableName = target.ExecutableName(brand);
            var stockExecutable = CheckInput(target, input, output, executableName);
            return Build(target, input, stockExecutable, brandPath, brand, executableName, output);
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

    private static DistributionIdentity ReadBrand(RepackTarget target, string brandPath)
    {
        if (!string.Equals(Path.GetFileName(brandPath), DistributionDescriptor.FileName, StringComparison.OrdinalIgnoreCase))
            throw new RepackException($"--brand must name a file called {DistributionDescriptor.FileName}, because that is the only name the IDE reads.");
        if (!File.Exists(brandPath))
            throw new RepackException($"{brandPath} does not exist.");

        var brand = DistributionDescriptor.Resolve(Path.GetDirectoryName(brandPath)!);
        target.CheckBrand(brand);

        if (brand.OpenLogo() is not { } logo)
            throw new RepackException($"{brandPath} names no 'logo', so the product would keep HexIDE's icon under {brand.Name}'s name. Add a logo.");
        logo.Dispose();

        return brand;
    }

    private static string CheckInput(RepackTarget target, string input, string output, string executableName)
    {
        if (!Directory.Exists(input))
            throw new RepackException($"{input} does not exist.");

        var comparison = Payload.PathComparison;
        if (string.Equals(input, output, comparison)
            || output.StartsWith(input + Path.DirectorySeparatorChar, comparison)
            || input.StartsWith(output + Path.DirectorySeparatorChar, comparison))
            throw new RepackException("--output must be separate from --input. This tool writes a new directory and never rewrites a build in place.");
        if (Directory.Exists(output) || File.Exists(output))
            throw new RepackException($"{output} already exists. This tool only writes a new directory.");

        var programs = Path.Combine(input, target.ProgramDirectory);
        var stock = Path.Combine(input, target.StockExecutable);
        if (!File.Exists(stock) || !File.Exists(Path.Combine(programs, ManagedEntryPoint)))
            throw new RepackException($"{input} has no {target.StockExecutable} and {ManagedEntryPoint}. Point --input at a published HexIDE build, such as an unpacked release archive.");
        if (File.Exists(Path.Combine(programs, DistributionDescriptor.FileName)))
            throw new RepackException($"{input} already has a {DistributionDescriptor.FileName}, so it isn't a stock build.");

        // A folder counts: on Linux an id of 'standalone' would need the name of the runner's own folder.
        if (Path.Exists(Path.Combine(programs, executableName)))
            throw new RepackException($"The build already has something called {executableName}, which is the name this brand's 'id' gives its executable. Choose another id.");

        foreach (var notice in Notices)
        {
            if (!File.Exists(Path.Combine(programs, notice)))
                throw new RepackException($"{input} has no {notice}. It has to travel with every copy, so this build can't be repacked.");
        }

        target.CheckStockExecutable(stock);
        return stock;
    }

    private static RepackResult Build(RepackTarget target, string input, string stockExecutable, string brandPath,
        DistributionIdentity brand, string executableName, string output)
    {
        var staging = $"{output}.partial-{Guid.NewGuid():N}";
        try
        {
            var app = Path.Combine(staging, target.PayloadName(brand));
            var programs = Path.Combine(app, target.ProgramDirectory);
            Payload.Copy(input, app, renameFrom: target.StockExecutable, renameTo: Path.Combine(target.ProgramDirectory, executableName));
            CopyBrandFiles(brandPath, brand, programs);

            // Checked again where it will really run: the same reader, against the layout the IDE will see.
            DistributionDescriptor.Resolve(programs);

            var removedSignature = target.FinishExecutable(Path.Combine(programs, executableName), stockExecutable, brand);

            Payload.VerifyUnchanged(input, app, except: target.StockExecutable);
            target.WriteInstallerInputs(staging, brand, executableName, stockExecutable);

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

/// <summary>The stock build's files, copied and then checked.</summary>
internal static class Payload
{
    private static readonly EnumerationOptions Everything = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    /// <summary>How this machine's file system compares names.</summary>
    public static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>A file's Unix permissions. Windows has none, so every file there compares equal.</summary>
    public static UnixFileMode ModeOf(string path) => OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(path);

    public static void Copy(string source, string destination, string? renameFrom = null, string? renameTo = null)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in new DirectoryInfo(source).EnumerateFileSystemInfos("*", Everything))
        {
            // A published build has no links, and following one would copy something from outside it.
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new RepackException($"{entry.FullName} is a link. A build to repack has to be plain files.");

            var relative = Path.GetRelativePath(source, entry.FullName);
            if (string.Equals(relative, renameFrom, PathComparison))
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

    /// <summary>
    /// Everything but the executable is required to be what was published, byte for byte. So are its permissions: a
    /// helper program that loses its execute bit doesn't fail, it quietly stops being used.
    /// </summary>
    public static void VerifyUnchanged(string source, string destination, string except)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", Everything))
        {
            var relative = Path.GetRelativePath(source, file);
            if (!string.Equals(relative, except, PathComparison))
                VerifySame(file, Path.Combine(destination, relative), relative);
        }
    }

    /// <summary>Refuses an executable whose permissions didn't survive unpacking.</summary>
    public static void RequireExecutable(string path, string platform)
    {
        if ((ModeOf(path) & UnixFileMode.UserExecute) == 0)
            throw new RepackException($"{path} isn't marked as executable, so this build lost its permissions when it was unpacked. Unpack the release archive on {platform}, with tar.");
    }

    /// <summary>Requires a copy to hold the same bytes and permissions as its original.</summary>
    public static void VerifySame(string original, string copy, string description)
    {
        if (!HashOf(original).SequenceEqual(HashOf(copy)))
            throw new RepackException($"{description} differs from the stock build's copy after repacking.");
        if (ModeOf(original) != ModeOf(copy))
            throw new RepackException($"{description} doesn't have the stock build's permissions after repacking.");
    }

    private static byte[] HashOf(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
