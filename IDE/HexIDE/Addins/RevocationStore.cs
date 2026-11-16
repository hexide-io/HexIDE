using HexIDE.IDE;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Addins;
using Serilog;

namespace HexIDE.Addins;

/// <summary>
/// Holds the active add-in revocation list — the freshest <i>validly root-signed</i> of: the bundled
/// floor (embedded), the last-known-good cache (<c>%AppData%</c>), and (when configured) a freshly
/// fetched list. Verification of every candidate is against the baked-in root. Fail-open: any problem
/// (no key, bad signature, offline, timeout, corrupt cache) leaves the previous list in place, never
/// blocking startup. Fail-closed only on a <i>known</i> revocation in a valid list.
/// </summary>
internal sealed class RevocationStore
{
    private readonly string? _rootPub;
    private readonly string _cacheJsonPath;
    private readonly string _cacheSigPath;
    private readonly string _cachePath;
    private readonly Func<string, TimeSpan, (byte[] Json, string Signature)> _fetch;
    private RevocationList _active;

    public RevocationStore() : this(ReadEmbeddedText("hexide-root.pub"), DefaultCacheDir()) { }

    public RevocationStore(string? rootPub, string cacheDir,
        Func<string, TimeSpan, (byte[] Json, string Signature)>? fetch = null)
    {
        _rootPub = rootPub;
        _fetch = fetch ?? Fetch;
        _cachePath = Path.Combine(cacheDir, "revocations-cache.signed");
        _cacheJsonPath = Path.Combine(cacheDir, "revocations-cache.json");
        _cacheSigPath = Path.Combine(cacheDir, "revocations-cache.sig");
        _active = PickFreshest(LoadBundled(), LoadCached()) ?? new RevocationList();
    }

    public string? IsRevoked(string? manifestHash, string? publisherId, string? intermediateId, bool isFirstParty) =>
        RevocationCheck.Reason(_active, manifestHash, publisherId, intermediateId, isFirstParty);

    /// <summary>Blocking fetch (off the UI thread) with a timeout. Fail-open: any failure keeps the
    /// current (cached/bundled) list. A valid, newer list updates the cache and becomes active.</summary>
    public void RefreshFromUrl(string url, TimeSpan timeout)
    {
        if (_rootPub is null)
            return;
        try
        {
            var (jsonBytes, sig) = _fetch(url, timeout);

            var fetched = ParseIfValid(jsonBytes, sig);
            if (fetched is null)
            {
                Log.Warning("RevocationStore: fetched list failed root verification — ignored");
                return;
            }
            if (!IsNewer(fetched, _active))
            {
                Log.Debug("RevocationStore: fetched list is not newer than the active one — ignored");
                return;
            }

            try
            {
                // Keep the exact signed bytes and signature in one atomic replacement.
                AtomicFileWriter.Write(_cachePath, stream =>
                {
                    using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                    writer.Write(jsonBytes.Length);
                    writer.Write(jsonBytes);
                    writer.Write(sig);
                });
            }
            catch (Exception ex) { Log.Warning(ex, "RevocationStore: failed to cache fetched list"); }

            _active = fetched;
            Log.Information("RevocationStore: applied fetched revocation list (issued {Issued})", fetched.IssuedUtc);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RevocationStore: fetch from {Url} failed — using cached/bundled list", url);
        }
    }

    private static (byte[] Json, string Signature) Fetch(string url, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        return Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = timeout };
            var json = await http.GetByteArrayAsync(url, cts.Token);
            var sig = (await http.GetStringAsync(url + ".sig", cts.Token)).Trim();
            return (json, sig);
        }, cts.Token).GetAwaiter().GetResult();
    }

    // ── candidate loading + selection ────────────────────────────

    private RevocationList? LoadBundled()
    {
        var json = ReadEmbedded("revocations.json");
        var sig = ReadEmbeddedText("revocations.sig");
        return json is null || sig is null ? null : ParseIfValid(json, sig);
    }

    private RevocationList? LoadCached()
    {
        try
        {
            if (File.Exists(_cachePath))
            {
                using var stream = File.OpenRead(_cachePath);
                using var reader = new BinaryReader(stream, Encoding.UTF8);
                var length = reader.ReadInt32();
                if (length < 0 || length > stream.Length - stream.Position)
                    return null;
                return ParseIfValid(reader.ReadBytes(length), reader.ReadString());
            }
            // Read pre-4022 caches until the first successful refresh replaces them.
            if (!File.Exists(_cacheJsonPath) || !File.Exists(_cacheSigPath))
                return null;
            return ParseIfValid(File.ReadAllBytes(_cacheJsonPath), File.ReadAllText(_cacheSigPath).Trim());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RevocationStore: failed to read cached list");
            return null;
        }
    }

    private RevocationList? ParseIfValid(byte[] jsonBytes, string sig)
    {
        if (_rootPub is null)
            return null;
        try
        {
            using var root = PackageCrypto.ImportPublicKey(_rootPub);
            if (!PackageCrypto.Verify(root, jsonBytes, sig))
                return null;
            return JsonSerializer.Deserialize(jsonBytes, RevocationJsonContext.Default.RevocationList);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RevocationStore: revocation list parse/verify error");
            return null;
        }
    }

    private static RevocationList? PickFreshest(RevocationList? a, RevocationList? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return IsNewer(a, b) ? a : b;
    }

    private static bool IsNewer(RevocationList candidate, RevocationList current) =>
        ParseTime(candidate.IssuedUtc) > ParseTime(current.IssuedUtc);

    private static DateTimeOffset ParseTime(string s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t : DateTimeOffset.MinValue;

    // ── embedded resources + paths ───────────────────────────────

    private static byte[]? ReadEmbedded(string fileNameSuffix)
    {
        var asm = typeof(RevocationStore).Assembly;
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase));
        if (name is null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? ReadEmbeddedText(string fileNameSuffix)
    {
        var bytes = ReadEmbedded(fileNameSuffix);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes).Trim();
    }

    private static string DefaultCacheDir()
    {
        var dir = UserDataPath.Directory;
        Directory.CreateDirectory(dir);
        return dir;
    }
}
