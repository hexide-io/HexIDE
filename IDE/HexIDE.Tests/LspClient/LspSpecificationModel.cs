using System.Security.Cryptography;
using System.Text.Json;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The Language Server Protocol's own machine-readable model of itself.
///
/// <para>
/// The specification generates <c>metaModel.json</c> alongside its prose: every request and notification,
/// each with the direction it travels. It is the canonical list, which makes it the only honest thing to
/// measure our coverage against — a hand-written denominator is just this repository's memory of the
/// protocol, and that is precisely what drifted.
/// </para>
///
/// <para>
/// <b>Licence.</b> The specification repository is <b>CC BY 4.0</b> — attribution-only, with no copyleft,
/// so it cannot affect this tree's MIT licence. It is also never redistributed: like the foreign servers,
/// it is fetched at test time into the gitignored <c>artifacts/</c> and no release artefact contains it.
/// That is a weaker claim than the one already accepted for texlab, which is GPL-3.0 and obtained the same
/// way; see <c>docs/foreign-language-servers.md</c> for the full reasoning.
/// </para>
///
/// <para>
/// <b>Pinned to a commit, not a branch.</b> The URL names the commit that last touched the file, so the
/// bytes cannot change under us — a branch URL would make the digest below a periodic false alarm rather
/// than a guarantee. The digest was computed here rather than taken from a publisher's checksum file: the
/// specification publishes none, so this pins <em>what was tested against</em> and does not attest
/// provenance. Same distinction, and same honesty about it, as <c>DigestProvenance</c> draws for texlab.
/// </para>
/// </summary>
internal static class LspSpecificationModel
{
    /// <summary>The protocol version this model describes, for use in assertion messages.</summary>
    public const string Version = "3.17.0";

    private const string Commit = "f20ba0702f2bbfe538226a5255739fc118744407";

    private const string Url =
        "https://raw.githubusercontent.com/microsoft/language-server-protocol/"
      + Commit + "/_specifications/lsp/3.17/metaModel/metaModel.json";

    private const string Sha256Hex =
        "c207890d9e0f54d8f9e462a924670f563423ec9af90a0a369645ff55384c8d6d";

    /// <summary>Which way a message travels. The specification's own vocabulary, not ours.</summary>
    internal enum Direction { ClientToServer, ServerToClient, Both }

    internal sealed record Message(string Method, Direction Direction, bool IsRequest);

    private static readonly Lock Gate = new();
    private static IReadOnlyList<Message>? _cached;
    private static bool _attempted;

    /// <summary>
    /// Every message the protocol defines, or null when the model could not be obtained.
    ///
    /// <para>
    /// Attempted once per process and guarded: xunit runs classes in parallel, and two of them racing to
    /// write one file is a flake that would present as a corrupt download.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Message>? All()
    {
        lock (Gate)
        {
            if (_attempted) return _cached;
            _attempted = true;
            try { _cached = Load(); }
            catch (Exception) { _cached = null; }
            return _cached;
        }
    }

    private static IReadOnlyList<Message>? Load()
    {
        var path = EnsureAvailable();
        if (path is null) return null;

        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;

        var messages = new List<Message>();
        Collect(root, "requests", isRequest: true, messages);
        Collect(root, "notifications", isRequest: false, messages);
        return messages;
    }

    private static void Collect(
        JsonElement root, string property, bool isRequest, List<Message> into)
    {
        if (!root.TryGetProperty(property, out var array)) return;

        foreach (var entry in array.EnumerateArray())
        {
            if (!entry.TryGetProperty("method", out var method)) continue;
            if (method.GetString() is not { Length: > 0 } name) continue;

            var direction = entry.TryGetProperty("messageDirection", out var d)
                ? d.GetString() switch
                {
                    "clientToServer" => Direction.ClientToServer,
                    "serverToClient" => Direction.ServerToClient,
                    _ => Direction.Both,
                }
                : Direction.Both;

            into.Add(new Message(name, direction, isRequest));
        }
    }

    /// <summary>The cached model file, downloading it once if needed, or null when unobtainable.</summary>
    private static string? EnsureAvailable()
    {
        if (Environment.GetEnvironmentVariable(PathVariable) is { Length: > 0 } configured)
            return File.Exists(configured) ? configured : null;

        var cached = Path.Combine(CacheRoot(), Commit, "metaModel.json");
        if (File.Exists(cached) && DigestOf(cached).Equals(Sha256Hex, StringComparison.OrdinalIgnoreCase))
            return cached;

        if (Environment.GetEnvironmentVariable(ForeignServerAcquisition.OptOutVariable) is "0" or "false")
            return null;

        return Download(cached);
    }

    private static string? Download(string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".partial";

        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
            using (var source = http.GetStreamAsync(Url).GetAwaiter().GetResult())
            using (var file = File.Create(temporary))
            {
                source.CopyTo(file);
            }

            var actual = DigestOf(temporary);
            if (!actual.Equals(Sha256Hex, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporary);
                throw new InvalidOperationException(
                    $"Checksum mismatch for the LSP {Version} metaModel: expected {Sha256Hex}, got {actual}. "
                  + "The URL names a commit, so its bytes cannot legitimately change — treat this as a "
                  + "corrupted transfer or a substituted response, not as a version bump.");
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch (Exception) when (!File.Exists(destination))
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { /* best effort */ }
            return null;
        }
    }

    private static string DigestOf(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    /// <summary>Point this at your own copy to work offline, or to try an unreleased model.</summary>
    public const string PathVariable = "HEXIDE_LSP_METAMODEL";

    private static string CacheRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        return dir is null
            ? Path.Combine(Path.GetTempPath(), "hexide-lsp-metamodel")
            : Path.Combine(dir.FullName, "artifacts", "lsp-metamodel");
    }
}
