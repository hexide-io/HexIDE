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
    private RevocationList _active;

    public RevocationStore() : this(ReadEmbeddedText("hexide-root.pub"), DefaultCacheDir()) { }

    public RevocationStore(string? rootPub, string cacheDir)
    {
        _rootPub = rootPub;
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
            using var cts = new CancellationTokenSource(timeout);
            var (jsonBytes, sig) = Task.Run(async () =>
            {
                using var http = new HttpClient { Timeout = timeout };
                var j = await http.GetByteArrayAsync(url, cts.Token);
                var s = (await http.GetStringAsync(url + ".sig", cts.Token)).Trim();
                return (j, s);
            }, cts.Token).GetAwaiter().GetResult();

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
                File.WriteAllBytes(_cacheJsonPath, jsonBytes);
                File.WriteAllText(_cacheSigPath, sig);
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
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "HexIDE");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
