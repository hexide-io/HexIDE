using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Fetches the pinned third-party Markdown language server the foreign-backend tests need, into a
/// gitignored cache inside the repository.
///
/// <para>
/// <b>Why fetched rather than committed.</b> The binary is ~18 MB per platform, ~6.5 MB as its published
/// archive. This repository's entire history is under 6 MB, so vendoring Windows and Linux would more than
/// triple it, with the majority being a third-party test fixture — and every version bump would add the
/// same again, permanently, because binaries do not delta and git history is not editable in practice. A
/// pinned URL plus the publisher's own checksum buys the same determinism for a one-line diff.
/// </para>
///
/// <para>
/// <b>The checksum is not decoration.</b> This downloads an executable and then runs it. The digests below
/// are the ones the publisher ships beside each asset; a mismatch aborts rather than falling back, because
/// the one failure mode worth refusing outright is executing something other than what was pinned.
/// </para>
///
/// <para>
/// Static musl builds are chosen for Linux deliberately — <c>ldd</c> reports "not a dynamic executable", so
/// the same file runs on any distribution and on the CI image without a glibc question. Verified under WSL
/// against the same tree the Windows tests use.
/// </para>
/// </summary>
internal static class ForeignServerAcquisition
{
    private const string Version = "0.2.64";

    /// <summary>
    /// One published archive. <paramref name="Sha256"/> is the publisher's digest for that exact asset,
    /// compared case-insensitively because they are not published in a consistent case.
    /// </summary>
    private sealed record Asset(string Rid, string FileName, string Sha256);

    // From https://github.com/rvben/rumdl/releases/tag/v0.2.64 — each asset's own .sha256 file.
    private static readonly Asset[] Assets =
    [
        new("win-x64", $"rumdl-v{Version}-x86_64-pc-windows-msvc.zip",
            "ADF3EC6D49C3308D080A01B75E82FBF8D1AEFED00CAA80D4E7E63C6DAF67231C"),
        new("linux-x64", $"rumdl-v{Version}-x86_64-unknown-linux-musl.tar.gz",
            "f08ac2f6b0e512f2fc53e33f8d3168471bf3ba9f0e41be978c495ef371987fac"),
        new("linux-arm64", $"rumdl-v{Version}-aarch64-unknown-linux-musl.tar.gz",
            "1f5cfe7963ce2c0cfe03168bf7e848bcdacc264f3c5348fba34a41c3316b68a1"),
        new("osx-x64", $"rumdl-v{Version}-x86_64-apple-darwin.tar.gz",
            "6e0b97487425f66702e801bf5dbb1293d5b52977d856887ca66fbc269949c74e"),
        new("osx-arm64", $"rumdl-v{Version}-aarch64-apple-darwin.tar.gz",
            "0bc09741ad3e4caccbe88c97601e6b093391d476323fd3f44cb5c57157fa209e"),
    ];

    private const string ReleaseUrlFormat =
        "https://github.com/rvben/rumdl/releases/download/v{0}/{1}";

    /// <summary>Set to a falsy value to keep a machine off the network; the tests then skip as before.</summary>
    public const string OptOutVariable = "HEXIDE_FOREIGN_LSP_DOWNLOAD";

    private static readonly Lock Gate = new();
    private static string? cached;
    private static bool attempted;

    /// <summary>
    /// The cached executable, downloading it once if needed, or null when it cannot be obtained.
    ///
    /// <para>
    /// Attempted at most once per process and guarded, because xunit runs classes in parallel and two
    /// collections racing to write one file is a flake that would look like a corrupt download.
    /// </para>
    /// </summary>
    public static string? EnsureAvailable()
    {
        lock (Gate)
        {
            if (attempted) return cached;
            attempted = true;
            cached = Acquire();
            return cached;
        }
    }

    private static string? Acquire()
    {
        if (AssetForThisPlatform() is not { } asset) return null;

        var exeName = OperatingSystem.IsWindows() ? "rumdl.exe" : "rumdl";
        var directory = Path.Combine(CacheRoot(), Version, asset.Rid);
        var executable = Path.Combine(directory, exeName);

        // The common case: already fetched by an earlier run, or restored from the CI cache.
        if (File.Exists(executable)) return executable;

        if (Environment.GetEnvironmentVariable(OptOutVariable) is "0" or "false" or "off") return null;

        try
        {
            Directory.CreateDirectory(directory);
            var archive = DownloadVerified(asset, directory);
            Extract(archive, directory);
            File.Delete(archive);

            if (!File.Exists(executable)) return null;
            MakeExecutable(executable);
            return executable;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or TaskCanceledException)
        {
            // Offline, or a transient failure. The caller skips visibly and says why; it must never
            // silently pass, which is the whole point of ForeignServerFactAttribute.
            return null;
        }
    }

    private static Asset? AssetForThisPlatform()
    {
        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
        var rid =
            OperatingSystem.IsWindows() ? "win" :
            OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : null;
        if (rid is null) return null;

        var suffix = arch switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (suffix is null) return null;

        return Array.Find(Assets, a => a.Rid == $"{rid}-{suffix}");
    }

    private static string DownloadVerified(Asset asset, string directory)
    {
        var url = string.Format(ReleaseUrlFormat, Version, asset.FileName);
        var path = Path.Combine(directory, asset.FileName);

        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                   .GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            using var target = File.Create(path);
            response.Content.CopyToAsync(target).GetAwaiter().GetResult();
        }

        using (var stream = File.OpenRead(path))
        {
            var actual = Convert.ToHexString(SHA256.HashData(stream));
            if (!actual.Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                // Thrown, not swallowed into a skip. A skip says "not available"; this says "something
                // other than what was pinned arrived", and the tests are about to EXECUTE it.
                throw new InvalidOperationException(
                    $"Checksum mismatch for {asset.FileName}: expected {asset.Sha256}, got {actual}. "
                  + "Refusing to run it.");
            }
        }

        return path;
    }

    private static void Extract(string archive, string directory)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archive, directory, overwriteFiles: true);
            return;
        }

        using var file = File.OpenRead(archive);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        TarFile.ExtractToDirectory(gzip, directory, overwriteFiles: true);
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;

        // Tar carries the mode, but only if the extractor applied it — and a zip does not carry one at all.
        // Setting it unconditionally on Unix is cheaper than discovering the difference as "permission
        // denied" from a process start.
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
          | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
          | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    /// <summary>
    /// Where downloads live: inside the repository, so one fetch serves every suite and both platforms —
    /// the Windows and WSL runs share a working tree, and this is gitignored.
    /// </summary>
    private static string CacheRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        // Falling back to the temp directory rather than failing: an unusual layout should cost a slower
        // cache, not the tests.
        return dir is null
            ? Path.Combine(Path.GetTempPath(), "hexide-foreign-lsp")
            : Path.Combine(dir.FullName, "IDE", "HexIDE.Tests", "tools", "foreign-lsp");
    }
}
