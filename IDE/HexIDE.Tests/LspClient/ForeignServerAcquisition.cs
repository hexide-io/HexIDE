using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// How the digest pinning a download was arrived at.
///
/// <para>
/// Recorded per server because it is not the same for all of them, and the difference is the difference
/// between provenance and trust-on-first-use. It should not be smoothed over by a manifest that lists
/// only the hex.
/// </para>
/// </summary>
internal enum DigestProvenance
{
    /// <summary>
    /// Taken from a checksum file the publisher released beside the asset. Attests that the bytes are
    /// the ones the publisher intended.
    /// </summary>
    Publisher,

    /// <summary>
    /// Computed here from a download, because the publisher releases none.
    ///
    /// <para>
    /// This pins <em>what was tested against</em>: it still catches a corrupted transfer, a replaced
    /// asset, or an unannounced rebuild under the same tag. It does <b>not</b> attest provenance — if the
    /// release were already compromised when it was first fetched, this records the compromise faithfully.
    /// Trust on first use, and worth naming as such rather than letting a hex string imply more.
    /// </para>
    /// </summary>
    ComputedHere,
}

/// <summary>One published archive for one platform.</summary>
/// <param name="Rid">Which platform this is for, in .NET runtime-identifier shape.</param>
/// <param name="FileName">The asset name, appended to the release URL.</param>
/// <param name="Sha256">Compared case-insensitively — publishers are not consistent about case.</param>
internal sealed record ForeignAsset(string Rid, string FileName, string Sha256);

/// <summary>
/// A language server the foreign-backend tests can drive, and how to obtain it.
/// </summary>
/// <param name="Key">Short name; also the cache directory and the environment-variable suffix.</param>
/// <param name="ExecutableName">The file inside the archive, without a platform extension.</param>
/// <param name="ReleaseUrlFormat">Format string taking the version and the asset name.</param>
internal sealed record ForeignServerSource(
    string Key,
    string Version,
    string ExecutableName,
    string ReleaseUrlFormat,
    DigestProvenance Provenance,
    ForeignAsset[] Assets);

/// <summary>
/// Fetches the pinned third-party language servers the foreign-backend tests need, into a gitignored
/// cache inside the repository.
///
/// <para>
/// <b>Why fetched rather than committed.</b> Each is several megabytes per platform against a repository
/// whose whole history is a fraction of that, and every version bump would add the same again,
/// permanently, because binaries do not delta. A pinned URL plus a checksum buys the same determinism for
/// a one-line diff. For one of these servers it is also a licensing requirement rather than a preference:
/// see <c>docs/foreign-language-servers.md</c>.
/// </para>
///
/// <para>
/// <b>The checksum is not decoration.</b> This downloads an executable and then runs it. A mismatch
/// aborts rather than falling back, because the one failure worth refusing outright is executing
/// something other than what was pinned.
/// </para>
///
/// <para>
/// <b>On adding a third.</b> The value of these tests is independence — a client and server written by
/// one hand agree with each other rather than with the specification — and the first two capture most of
/// it. A further server should earn its place by exercising a protocol <em>shape</em> neither of these
/// does, not by being another server. The shapes currently unexercised by anything real are the
/// <c>pipe</c> and <c>websocket</c> transports, and a server that defers its analysis to save.
/// </para>
/// </summary>
internal static class ForeignServerAcquisition
{
    /// <summary>Set to a falsy value to keep a machine off the network; the tests then skip, visibly.</summary>
    public const string OptOutVariable = "HEXIDE_FOREIGN_LSP_DOWNLOAD";

    /// <summary>
    /// A Markdown linter. Publishes a checksum beside every asset, so its digests are attested.
    /// </summary>
    public static readonly ForeignServerSource Markdown = new(
        Key: "markdown",
        Version: "0.2.64",
        ExecutableName: "rumdl",
        ReleaseUrlFormat: "https://github.com/rvben/rumdl/releases/download/v{0}/{1}",
        Provenance: DigestProvenance.Publisher,
        Assets:
        [
            new("win-x64", "rumdl-v0.2.64-x86_64-pc-windows-msvc.zip",
                "ADF3EC6D49C3308D080A01B75E82FBF8D1AEFED00CAA80D4E7E63C6DAF67231C"),
            new("linux-x64", "rumdl-v0.2.64-x86_64-unknown-linux-musl.tar.gz",
                "f08ac2f6b0e512f2fc53e33f8d3168471bf3ba9f0e41be978c495ef371987fac"),
            new("linux-arm64", "rumdl-v0.2.64-aarch64-unknown-linux-musl.tar.gz",
                "1f5cfe7963ce2c0cfe03168bf7e848bcdacc264f3c5348fba34a41c3316b68a1"),
            new("osx-x64", "rumdl-v0.2.64-x86_64-apple-darwin.tar.gz",
                "6e0b97487425f66702e801bf5dbb1293d5b52977d856887ca66fbc269949c74e"),
            new("osx-arm64", "rumdl-v0.2.64-aarch64-apple-darwin.tar.gz",
                "0bc09741ad3e4caccbe88c97601e6b093391d476323fd3f44cb5c57157fa209e"),
        ]);

    /// <summary>
    /// A LaTeX server, and the reason for a second one.
    ///
    /// <para>
    /// It is by a different author under a different licence, which is the point: the first foreign server
    /// established that HexIDE can talk to something it did not write, and a second establishes that it
    /// was not accidentally shaped around that one server's habits. It also claims <c>.cls</c>, which is a
    /// VB6 class module and a LaTeX class file both — so the collision this project reasons about becomes
    /// something the suite can actually exercise rather than argue from.
    /// </para>
    ///
    /// <para>
    /// Its digests are <b>computed here</b>, because it publishes none. The Linux build is the musl one,
    /// which runs on any distribution rather than only where its glibc matches.
    /// </para>
    /// </summary>
    public static readonly ForeignServerSource Latex = new(
        Key: "latex",
        Version: "5.26.0",
        ExecutableName: "texlab",
        ReleaseUrlFormat: "https://github.com/latex-lsp/texlab/releases/download/v{0}/{1}",
        Provenance: DigestProvenance.ComputedHere,
        Assets:
        [
            new("win-x64", "texlab-x86_64-windows.zip",
                "cb028d44c3d2b85d36a2ed52d41a0ff43a341b1f04c500c56c4524c4eb72b316"),
            new("linux-x64", "texlab-x86_64-alpine.tar.gz",
                "66ed15ef745076a2d50594a13badbb9e8d54dd4eda2e2bbcf2bf9f8d97d27896"),
            new("linux-arm64", "texlab-aarch64-linux.tar.gz",
                "a85cdfcd22454b8d8550f4b0f0620c45ab51760f302fac7a12bc18a890f70f8c"),
            new("osx-x64", "texlab-x86_64-macos.tar.gz",
                "6091611f756b28e1a57612b130c196df4b0bb6e22dde5cf5d890578513397daf"),
            new("osx-arm64", "texlab-aarch64-macos.tar.gz",
                "af7972ffd230711ba04ada9b69cc32ce9111d9196ba69538062872faefdbee56"),
        ]);

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, string?> Attempted = [];

    /// <summary>
    /// The cached executable for one server, downloading it once if needed, or null when it cannot be
    /// obtained.
    ///
    /// <para>
    /// Attempted at most once per server per process and guarded, because xunit runs classes in parallel
    /// and two collections racing to write one file is a flake that would look like a corrupt download.
    /// </para>
    /// </summary>
    public static string? EnsureAvailable(ForeignServerSource server)
    {
        lock (Gate)
        {
            if (Attempted.TryGetValue(server.Key, out var already)) return already;
            var path = Acquire(server);
            Attempted[server.Key] = path;
            return path;
        }
    }

    private static string? Acquire(ForeignServerSource server)
    {
        if (AssetForThisPlatform(server) is not { } asset) return null;

        var exeName = OperatingSystem.IsWindows() ? server.ExecutableName + ".exe" : server.ExecutableName;
        var directory = Path.Combine(CacheRoot(), server.Key, server.Version, asset.Rid);
        var executable = Path.Combine(directory, exeName);

        // The common case: already fetched by an earlier run, or restored from the CI cache.
        if (File.Exists(executable)) return executable;

        if (Environment.GetEnvironmentVariable(OptOutVariable) is "0" or "false" or "off") return null;

        try
        {
            Directory.CreateDirectory(directory);
            var archive = DownloadVerified(server, asset, directory);
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

    private static ForeignAsset? AssetForThisPlatform(ForeignServerSource server)
    {
        var os =
            OperatingSystem.IsWindows() ? "win" :
            OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : null;
        if (os is null) return null;

        var arch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (arch is null) return null;

        return Array.Find(server.Assets, a => a.Rid == $"{os}-{arch}");
    }

    private static string DownloadVerified(
        ForeignServerSource server, ForeignAsset asset, string directory)
    {
        var url = string.Format(server.ReleaseUrlFormat, server.Version, asset.FileName);
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
    /// Where downloads live: under the repository's <c>artifacts/</c>, so one fetch serves every suite and
    /// both platforms — the Windows and WSL runs share a working tree.
    ///
    /// <para>
    /// <b>Deliberately not inside a source directory.</b> This used to sit at
    /// <c>IDE/HexIDE.Tests/tools/</c>, which on disk is <c>Tools/</c> — an existing directory holding
    /// tracked test source — and was ignored by a lowercase rule that matched only because git on Windows
    /// is case-insensitive. Downloaded binaries do not belong among source files under any casing, and
    /// <c>artifacts/</c> is already ignored and is what it means.
    /// </para>
    /// </summary>
    internal static string CacheRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        // Falling back to the temp directory rather than failing: an unusual layout should cost a slower
        // cache, not the tests.
        return dir is null
            ? Path.Combine(Path.GetTempPath(), "hexide-foreign-lsp")
            : Path.Combine(dir.FullName, "artifacts", "foreign-lsp");
    }
}
