using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HexIDE.Dap;

namespace HexIDE.Tests.DebugAdapter;

internal sealed record ForeignAdapterAsset(string Url, string Sha256, string EntryPoint);
internal sealed record ForeignAdapterResolution(DapProcessOptions? Options, string? Unavailable, bool ChecksumMismatch = false, bool NoBuildForPlatform = false);

internal static class ForeignAdapterAcquisition
{
    internal const string DebugpyVersion = "1.8.22";
    internal const string NetcoredbgVersion = "3.2.0-1092";
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, ForeignAdapterResolution> Resolved = [];

    // Publisher-attested: PyPI's version JSON and GitHub's release asset digests, respectively.
    internal static ForeignAdapterAsset Asset(string adapter, string rid) => adapter == "debugpy"
        ? new("https://files.pythonhosted.org/packages/56/3d/c7dc9f35bc9e22cdb53679f844bee40fd5cff871862f60595869a433400d/debugpy-1.8.22-py2.py3-none-any.whl",
            "a9e9d3550e15ca479c59333e90845029190531f0cacfedab3b815a57bd913947", "debugpy/adapter/__main__.py")
        : rid switch
        {
            "linux-x64" => Netcoredbg("netcoredbg-linux-amd64.tar.gz", "080eb3b2d2152465f599d3b33d1ee6e747794e11cc0a3773ec689f5e5f2c5afa", "netcoredbg/netcoredbg"),
            "linux-arm64" => Netcoredbg("netcoredbg-linux-arm64.tar.gz", "065ff49badec8a695dbea2de6ab6a330c774a191e426a217ab8cc05250627ccb", "netcoredbg/netcoredbg"),
            "win-x64" => Netcoredbg("netcoredbg-win64.zip", "3c410a45fa502415203a94fcb88654af65bf8e3dac158a5527a722e7a6b9274a", "netcoredbg/netcoredbg.exe"),
            "osx-arm64" => Netcoredbg("netcoredbg-osx-arm64.zip", "f4fa33b3ff874910cc184b4bb3b9c56d0abdf5c6521cee0b144d7c6e4a6e59ea", "netcoredbg/netcoredbg"),
            _ => throw new PlatformNotSupportedException($"No pinned netcoredbg asset for {rid}."),
        };

    private static ForeignAdapterAsset Netcoredbg(string file, string digest, string entry) =>
        new($"https://github.com/Samsung/netcoredbg/releases/download/{NetcoredbgVersion}/{file}", digest, entry);

    internal static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !(Directory.Exists(Path.Combine(directory.FullName, "IDE"))
                                      && Directory.Exists(Path.Combine(directory.FullName, "LspServer"))))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate the repository.");
    }

    internal static ForeignAdapterResolution Resolve(string adapter)
    {
        lock (Gate)
        {
            if (Resolved.TryGetValue(adapter, out var result)) return result;
            try { result = Acquire(adapter); }
            catch (InvalidDataException error) { result = new(null, error.Message, true); }
            catch (Exception error)
            { result = new(null, $"{adapter}: {error.GetType().Name}: {error.Message}"); }
            return Resolved[adapter] = result;
        }
    }

    private static ForeignAdapterResolution Acquire(string adapter)
    {
        if (adapter == "netcoredbg" && OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            return new(null, "netcoredbg publishes no Windows Arm build, and its x64 build crashes with an access violation launching an Arm program (#3548).",
                NoBuildForPlatform: true);
        var python = adapter == "debugpy" ? FindPython() : null;
        if (adapter == "debugpy" && python is null) return new(null, "debugpy requires Python 3 on PATH (CI installs 3.14).");
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var arch = RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        var asset = Asset(adapter, $"{os}-{arch}");
        var version = adapter == "debugpy" ? DebugpyVersion : NetcoredbgVersion;
        var directory = Path.Combine(RepositoryRoot(), "artifacts", "foreign-dap", adapter, version, $"{os}-{arch}");
        var archive = Path.Combine(directory, Path.GetFileName(new Uri(asset.Url).AbsolutePath));
        if (!File.Exists(archive))
        {
            if (Environment.GetEnvironmentVariable("HEXIDE_FOREIGN_LSP_DOWNLOAD") is "0" or "false" or "off")
                return new(null, $"{adapter} {version} is not cached; HEXIDE_FOREIGN_LSP_DOWNLOAD disables acquisition.");
            Directory.CreateDirectory(directory);
            using var http = new HttpClient();
            DownloadVerified(http, asset.Url, asset.Sha256, archive);
        }
        // Recheck even a cached archive before extraction/execution. An interrupted extraction is never reused.
        Verify(archive, asset.Sha256);
        var extracted = Path.Combine(directory, "extracted");
        if (Directory.Exists(extracted)) Directory.Delete(extracted, recursive: true);
        Directory.CreateDirectory(extracted);
        if (archive.EndsWith(".tar.gz", StringComparison.Ordinal))
        {
            using var file = File.OpenRead(archive);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, extracted, overwriteFiles: true);
        }
        else ZipFile.ExtractToDirectory(archive, extracted);
        var entry = Path.Combine(extracted, asset.EntryPoint.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(entry)) throw new IOException($"Pinned {adapter} archive lacks {asset.EntryPoint}.");
        if (adapter == "debugpy")
            return new(new(python!)
            {
                Arguments = [entry],
                Environment = new Dictionary<string, string?>
                {
                    ["PYTHONPATH"] = extracted, ["PYTHONNOUSERSITE"] = "1", ["PYTHONDONTWRITEBYTECODE"] = "1",
                    ["PYDEVD_DISABLE_FILE_VALIDATION"] = "1",
                },
                ShutdownTimeout = TimeSpan.FromSeconds(10),
            }, null);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(entry, File.GetUnixFileMode(entry) | UnixFileMode.UserExecute);
        return new(new(entry) { Arguments = ["--interpreter=vscode"], ShutdownTimeout = TimeSpan.FromSeconds(10) }, null);
    }

    internal static void DownloadVerified(HttpClient http, string url, string sha256, string archive)
    {
        var partial = archive + ".partial";
        try
        {
            DownloadRetry.ToFile(http, url, partial, TimeSpan.FromSeconds(60));
            Verify(partial, sha256);
            File.Move(partial, archive, overwrite: true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    internal static void Verify(string file, string expected)
    {
        using var stream = File.OpenRead(file);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Checksum mismatch for {Path.GetFileName(file)}: expected {expected}, got {actual}. Refusing to run it.");
    }

    private static string? FindPython()
    {
        foreach (var name in OperatingSystem.IsWindows() ? new[] { "python.exe", "python3.exe" } : ["python3", "python"])
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory.Trim('"'), name);
            if (!File.Exists(candidate)) continue;
            try
            {
                using var process = Process.Start(new ProcessStartInfo(candidate, "--version")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
                if (process is null) continue;
                if (!process.WaitForExit(5000)) { process.Kill(entireProcessTree: true); continue; }
                if (process.ExitCode == 0 && process.StandardOutput.ReadToEnd().StartsWith("Python 3.", StringComparison.Ordinal)) return candidate;
            }
            catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException) { }
        }
        return null;
    }
}
