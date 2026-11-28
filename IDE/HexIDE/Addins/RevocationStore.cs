using HexIDE.IDE;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
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
    private readonly Func<string, TimeSpan, string?, FetchedList?> _fetch;
    // Read on the UI thread and by language-server launches while the hourly check swaps it from the background (#5577).
    private volatile RevocationList _active;
    // The ETag of the last list whose signature verified, in memory only. Kept from a pair that failed verification, it
    // would answer every later check with 304 and the valid pair would never arrive (#5577).
    private string? _validator;
    // What the checks found, for Options. One value swapped whole, since the hourly check writes it off the UI thread.
    private RevocationCheckState _checks = new(null, null, false);

    /// <summary>When a check last reached a list that verified, or was told the list hadn't changed, and when one last
    /// failed (#5577). Both are null until the first check of this session.</summary>
    internal RevocationCheckState Checks => Volatile.Read(ref _checks);

    /// <summary>When the list in use was issued, as it says, or empty for a list that names no time.</summary>
    internal string ActiveIssuedUtc => _active.IssuedUtc;

    internal Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    public RevocationStore() : this(ReadEmbeddedText("hexide-root.pub"), DefaultCacheDir()) { }

    public RevocationStore(string? rootPub, string cacheDir,
        Func<string, TimeSpan, (byte[] Json, string Signature)>? fetch = null)
        : this(rootPub, cacheDir, fetch is null ? null : (url, timeout, _) =>
        {
            var (json, signature) = fetch(url, timeout);
            return new FetchedList(json, signature, null);
        })
    {
    }

    /// <summary>For a fetch that can answer "not modified": it is given the validator of the last list that verified, and
    /// returns null when the list hasn't changed since.</summary>
    internal RevocationStore(string? rootPub, string cacheDir, Func<string, TimeSpan, string?, FetchedList?>? conditionalFetch)
    {
        _rootPub = rootPub;
        _fetch = conditionalFetch ?? Fetch;
        _cachePath = Path.Combine(cacheDir, "revocations-cache.signed");
        _cacheJsonPath = Path.Combine(cacheDir, "revocations-cache.json");
        _cacheSigPath = Path.Combine(cacheDir, "revocations-cache.sig");
        _active = PickFreshest(LoadBundled(), LoadCached()) ?? new RevocationList();
    }

    /// <summary>Why the active list refuses this package, or null. The key fingerprints have no default, so every caller says
    /// which keys it checked (#5315); pass null only where no key is known.</summary>
    public string? IsRevoked(string? manifestHash, string? publisherId, string? intermediateId, bool isFirstParty,
        string? publisherKeyFingerprint, string? intermediateKeyFingerprint) =>
        RevocationCheck.Reason(_active, manifestHash, publisherId, intermediateId, isFirstParty,
            publisherKeyFingerprint, intermediateKeyFingerprint);

    /// <summary>Blocking fetch (off the UI thread) with a timeout. Fail-open: any failure keeps the
    /// current (cached/bundled) list. A valid, newer list updates the cache and becomes active, and the result says
    /// so, so the hourly check knows to look at what is already loaded (#5577).</summary>
    public RevocationRefresh RefreshFromUrl(string url, TimeSpan timeout)
    {
        var result = Refresh(url, timeout);
        var now = Clock();
        var last = Volatile.Read(ref _checks);
        Volatile.Write(ref _checks, result == RevocationRefresh.Failed
            ? last with { LastFailed = now, LastAttemptFailed = true }
            : last with { LastSucceeded = now, LastAttemptFailed = false });
        return result;
    }

    private RevocationRefresh Refresh(string url, TimeSpan timeout)
    {
        if (_rootPub is null)
            return RevocationRefresh.Failed;
        try
        {
            if (_fetch(url, timeout, _validator) is not { } response)
            {
                Log.Debug("RevocationStore: revocation list not modified since the last check");
                return RevocationRefresh.Unchanged;
            }
            var (jsonBytes, sig, validator) = response;

            var fetched = ParseIfValid(jsonBytes, sig);
            if (fetched is null)
            {
                Log.Warning("RevocationStore: fetched list failed root verification — ignored");
                return RevocationRefresh.Failed;
            }
            _validator = validator;
            if (!IsNewer(fetched, _active))
            {
                Log.Debug("RevocationStore: fetched list is not newer than the active one — ignored");
                return RevocationRefresh.Unchanged;
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
            return RevocationRefresh.Applied;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "RevocationStore: fetch from {Url} failed — using cached/bundled list", url);
            return RevocationRefresh.Failed;
        }
    }

    // The list is asked for conditionally, with the validator of the last list that verified; its signature always in full,
    // since a signature on its own says nothing about which list it signs.
    private static FetchedList? Fetch(string url, TimeSpan timeout, string? validator)
    {
        using var cts = new CancellationTokenSource(timeout);
        return Task.Run(async () =>
        {
            using var http = new HttpClient { Timeout = timeout };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (validator is not null && EntityTagHeaderValue.TryParse(validator, out var tag))
                request.Headers.IfNoneMatch.Add(tag);
            using var response = await http.SendAsync(request, cts.Token);
            if (response.StatusCode == HttpStatusCode.NotModified)
                return null;
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var sig = (await http.GetStringAsync(url + ".sig", cts.Token)).Trim();
            return new FetchedList(json, sig, response.Headers.ETag?.ToString());
        }, cts.Token).GetAwaiter().GetResult();
    }

    // ── candidate loading + selection ────────────────────────────

    internal RevocationList? LoadBundled()
    {
        var json = ReadEmbedded("revocations.json");
        var sig = ReadEmbeddedText("revocations.sig");
        if (json is null || sig is null)
            return null;
        var list = ParseIfValid(json, sig);
        // A fetched list can fail for reasons outside the build. This one is the build's own file, so its failing is a
        // defect, and it leaves revocation resting on the network (#4534, #4537).
        if (list is null && _rootPub is not null)
            Log.Error("RevocationStore: this build's own revocation list was refused: it fails root verification or "
                + "doesn't parse, so revocation rests on the cached and fetched lists alone");
        return list;
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

/// <summary>What a revocation fetch did (#5577).</summary>
internal enum RevocationRefresh
{
    /// <summary>A valid list newer than the active one arrived and is now active.</summary>
    Applied,
    /// <summary>The list was reached and is no newer than the active one, or wasn't modified since the last check.</summary>
    Unchanged,
    /// <summary>The list couldn't be fetched or didn't verify; the active list stays.</summary>
    Failed,
}

/// <summary>A fetched list, its signature, and the validator (an ETag) to send next time.</summary>
internal sealed record FetchedList(byte[] Json, string Signature, string? Validator);

/// <summary>When a revocation check last worked and last failed, and whether the latest attempt failed (#5577). A check
/// works when it reaches a list that verifies, or hears the list hasn't changed since the last one that did.</summary>
internal sealed record RevocationCheckState(DateTimeOffset? LastSucceeded, DateTimeOffset? LastFailed, bool LastAttemptFailed);
