using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HexIDE.Addins;

namespace HexIDE.AddinPacker;

/// <summary>
/// A publisher's signed request for a record (#5321): <c>request.json</c>, signed with the key it asks the maintainer to
/// certify, so the request itself proves the sender holds that key. A rotation is also signed with the key it replaces.
/// </summary>
/// <remarks>
/// The file is one canonical line of ASCII, exactly as <see cref="PublisherRequestJsonContext"/> writes it, so it survives
/// being pasted into an issue form and its SHA-256, which is what the maintainer approves, names one request only. Every
/// signature covers <see cref="SignaturePrefix"/> followed by those bytes, so a request signature can never stand in for a
/// manifest's or a record's, nor theirs for a request's.
/// </remarks>
public sealed record PublisherRequest
{
    [JsonPropertyName("schema")] public required int Schema { get; init; }
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("publisherId")] public required string PublisherId { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("namespaces")] public required string[] Namespaces { get; init; }
    [JsonPropertyName("publicKey")] public required string PublicKey { get; init; }

    [JsonPropertyName("previousPublicKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PreviousPublicKey { get; init; }

    [JsonPropertyName("github")] public required string GitHub { get; init; }
    [JsonPropertyName("fingerprintUrl")] public required string FingerprintUrl { get; init; }
    [JsonPropertyName("createdUtc")] public required string CreatedUtc { get; init; }
}

[JsonSourceGenerationOptions(UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(PublisherRequest))]
internal partial class PublisherRequestJsonContext : JsonSerializerContext;

/// <summary>What <see cref="PublisherRequests.Verify(string, RequestCheckContext)"/> found. A request passes only with no
/// refusals and nothing left unchecked.</summary>
public sealed record PublisherRequestCheck(IReadOnlyList<string> Refusals, IReadOnlyList<string> NotChecked,
    PublisherRequest? Request, string? Sha256)
{
    public bool Verified => Refusals.Count == 0;

    /// <summary>Nothing refused, but something only a person can settle: a rotation checked without the ledger or the
    /// revocation list, one whose ledger entry has no stored request or a changed handle or address, or a revocation list
    /// over a week old. Automation must treat this as a failure (exit 3), and issue-publisher signs nothing.</summary>
    public bool NeedsManualCheck => Verified && NotChecked.Count > 0;

    /// <summary>The verified request's exact bytes and signatures, for issue-publisher to store beside the record without
    /// reading the files again.</summary>
    internal byte[]? RequestBytes { get; init; }
    internal byte[]? Signature { get; init; }
    internal byte[]? PreviousSignature { get; init; }
}

/// <summary>What a request is checked against, beyond itself.</summary>
public sealed record RequestCheckContext
{
    /// <summary>The issued records, <c>&lt;id&gt;/</c> each, with <c>retired/&lt;n&gt;/</c> for rotated-out ones.</summary>
    public string? LedgerDir { get; init; }

    /// <summary>The account that submitted the request, compared with its own ignoring case.</summary>
    public string? ExpectedGitHub { get; init; }

    /// <summary>The root's public key, as base64 SPKI. With it, every ledger record's chain and stored request is verified.</summary>
    public string? RootPublicKey { get; init; }

    /// <summary>A revocation list already verified against <see cref="RootPublicKey"/>.</summary>
    public RevocationList? Revocations { get; init; }

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;
}

public static partial class PublisherRequests
{
    public const string RequestFile = "request.json";
    public const string SignatureFile = "request.sig";
    public const string PreviousSignatureFile = "request.prev.sig";
    public const string NewKind = "new";
    public const string RotationKind = "rotation";

    /// <summary>Signed before the request's bytes, so the signature is a request signature and nothing else.</summary>
    public const string SignaturePrefix = "hexide-publisher-request-v1\n";

    private const int MaxRequestBytes = 16 * 1024;
    private const int MaxSignatureBytes = 512;
    private const int MaxUrlLength = 2048;

    // GitHub's own rule: 1 to 39 letters, digits or single hyphens, not starting or ending with one.
    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9]|-(?=[A-Za-z0-9])){0,38}\z", RegexOptions.CultureInvariant)]
    private static partial Regex GitHubHandle();

    /// <summary>The exact bytes a request is written as, and must be read as.</summary>
    public static byte[] Canonical(PublisherRequest request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, PublisherRequestJsonContext.Default.PublisherRequest);

    /// <summary>What every signature on a request covers.</summary>
    public static byte[] SignedBytes(byte[] requestBytes) => [.. Encoding.ASCII.GetBytes(SignaturePrefix), .. requestBytes];

    /// <summary>
    /// Makes a request and writes it to <paramref name="outDir"/>: with <paramref name="newKeyDir"/> it makes the key too,
    /// as <c>&lt;publisher-id&gt;.key</c>, written before anything else; with <paramref name="keyFile"/> it uses that key.
    /// A <paramref name="previousKeyFile"/> makes it a rotation, signed with the old key as well. Every field is checked as
    /// <c>verify-request</c> checks it before anything is written, and the written request is verified afterwards.
    /// </summary>
    public static PublisherRequestCheck Create(string publisherId, string displayName, IReadOnlyList<string>? namespaces,
        string gitHub, string fingerprintUrl, string? newKeyDir, string? keyFile, string? previousKeyFile, string outDir)
    {
        if ((newKeyDir is null) == (keyFile is null))
            throw new ArgumentException("request-publisher takes exactly one of --new-key (make a key) and --key (use yours)");

        using var previous = previousKeyFile is null ? null : PackageSigner.ReadPrivateKey(previousKeyFile, "previous private key");
        using var created = newKeyDir is null ? null : ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var existing = keyFile is null ? null : PackageSigner.ReadPrivateKey(keyFile, "publisher private key");
        var key = created ?? existing!;

        var request = new PublisherRequest
        {
            Schema = 1,
            Kind = previous is null ? NewKind : RotationKind,
            PublisherId = publisherId,
            DisplayName = displayName,
            Namespaces = [.. (namespaces ?? []).Distinct(StringComparer.Ordinal)],
            PublicKey = PackageCrypto.ExportPublicKey(key),
            PreviousPublicKey = previous is null ? null : PackageCrypto.ExportPublicKey(previous),
            GitHub = gitHub,
            FingerprintUrl = fingerprintUrl,
            CreatedUtc = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (FieldRefusals(request) is [var first, ..])
            throw new ArgumentException(first);

        var output = PackageSigner.ResolveOutputDirectory(outDir);
        if (newKeyDir is not null)
        {
            var keyDir = PackageSigner.ResolveOutputDirectory(newKeyDir);
            if (IsSameOrUnder(keyDir, output))
                throw new ArgumentException("--new-key must not be --out or a folder inside it: the request is meant to be "
                    + "published, and the key must never go with it");
            Directory.CreateDirectory(keyDir);
            WritePrivateKey(Path.Combine(keyDir, publisherId + ".key"), PackageCrypto.ExportPrivateKey(created!));
        }

        var bytes = Canonical(request);
        var signed = SignedBytes(bytes);
        var files = new Dictionary<string, byte[]>
        {
            [RequestFile] = bytes,
            [SignatureFile] = Encoding.ASCII.GetBytes(PackageCrypto.Sign(key, signed)),
        };
        if (previous is not null)
            files[PreviousSignatureFile] = Encoding.ASCII.GetBytes(PackageCrypto.Sign(previous, signed));
        Directory.CreateDirectory(output);
        PackageSigner.WriteNewFiles(output, files);

        var check = Verify(output, ledgerDir: null, expectedGitHub: null);
        if (!check.Verified)
            throw new InvalidOperationException("the written request does not verify: " + check.Refusals[0]);
        return check;
    }

    /// <summary>
    /// Checks a request folder as hostile input: the request's form, every field, its signatures and, with
    /// <paramref name="ledgerDir"/> (a folder of issued records, <c>&lt;id&gt;/publisher.json</c>), that no other publisher
    /// holds its id, key or namespaces. Never throws for anything the request holds; what is wrong comes back as refusals.
    /// </summary>
    public static PublisherRequestCheck Verify(string requestDir, string? ledgerDir, string? expectedGitHub) =>
        Verify(requestDir, new RequestCheckContext { LedgerDir = ledgerDir, ExpectedGitHub = expectedGitHub });

    /// <summary>
    /// Checks a request folder as hostile input, against what <paramref name="context"/> supplies: the ledger (that no
    /// other publisher holds its id, key or namespaces, and that a rotation replaces the ledger's key for its id and comes
    /// from whoever asked for it), the root (that every ledger record is genuine) and the revocation list. Never throws for
    /// anything the request or the ledger holds; what is wrong comes back as refusals, and what only a person can settle
    /// as <see cref="PublisherRequestCheck.NotChecked"/>.
    /// </summary>
    public static PublisherRequestCheck Verify(string requestDir, RequestCheckContext context)
    {
        var refusals = new List<string>();
        var notChecked = new List<string>();
        PublisherRequestCheck Refused(params string[] reasons) => new([.. refusals, .. reasons], notChecked, null, null);

        if (ReadBounded(requestDir, RequestFile, MaxRequestBytes, out var bytes) is { } unreadable)
            return Refused(unreadable);
        if (ReadBounded(requestDir, SignatureFile, MaxSignatureBytes, out var signatureBytes) is { } noSignature)
            return Refused(noSignature);
        var hasPreviousSignature = File.Exists(Path.Combine(requestDir, PreviousSignatureFile))
            || Directory.Exists(Path.Combine(requestDir, PreviousSignatureFile));
        byte[]? previousSignatureBytes = null;
        if (hasPreviousSignature
            && ReadBounded(requestDir, PreviousSignatureFile, MaxSignatureBytes, out previousSignatureBytes) is { } badPrevious)
            return Refused(badPrevious);

        PublisherRequest? request;
        try
        {
            request = JsonSerializer.Deserialize(bytes, PublisherRequestJsonContext.Default.PublisherRequest);
        }
        catch (JsonException)
        {
            request = null;
        }
        if (request is null)
            return Refused($"{RequestFile} is not a publisher request: a field is missing, unknown or of the wrong type");

        // The canonical form, byte for byte: one ASCII line in the serializer's order, with no BOM, whitespace or repeated
        // field. Anything else could hash differently from what was approved while meaning the same, or the reverse.
        if (FieldRefusals(request) is { Count: > 0 } fieldRefusals)
            return Refused([.. fieldRefusals]);
        if (!bytes.AsSpan().SequenceEqual(Canonical(request)))
            return Refused($"{RequestFile} is not in canonical form: it must be exactly as request-publisher writes it, one "
                + "ASCII line with no added whitespace, reordered or repeated fields, or byte-order mark");

        var signed = SignedBytes(bytes);
        using (var key = PackageCrypto.ImportPublicKey(request.PublicKey))
            if (!PackageCrypto.Verify(key, signed, Encoding.ASCII.GetString(signatureBytes).Trim()))
                refusals.Add($"{SignatureFile} is not a signature of this request by its publicKey");
        if (request.Kind == RotationKind)
        {
            if (previousSignatureBytes is null)
                refusals.Add($"a rotation needs {PreviousSignatureFile}, its signature by previousPublicKey");
            else
                using (var previous = PackageCrypto.ImportPublicKey(request.PreviousPublicKey!))
                    if (!PackageCrypto.Verify(previous, signed, Encoding.ASCII.GetString(previousSignatureBytes).Trim()))
                        refusals.Add($"{PreviousSignatureFile} is not a signature of this request by its previousPublicKey");
        }
        else if (hasPreviousSignature)
        {
            refusals.Add($"a new publisher's request has no {PreviousSignatureFile}");
        }

        if (context.ExpectedGitHub is { } expected && !string.Equals(expected, request.GitHub, StringComparison.OrdinalIgnoreCase))
            refusals.Add("the request's github handle is not the account that submitted it");

        if (context.LedgerDir is null)
        {
            if (request.Kind == RotationKind)
                notChecked.Add("whether previousPublicKey is the key the ledger holds for this id: no --ledger was given, so "
                    + "only the request says the id is its to rotate");
        }
        else
        {
            refusals.AddRange(LedgerRefusals(request, context, notChecked));
        }
        if (context.Revocations is { } list)
            RevocationRefusals(request, list, context.Now, refusals, notChecked);
        else if (request.Kind == RotationKind)
            notChecked.Add("whether this publisher id is revoked: no revocation list was given");

        var verified = refusals.Count == 0;
        return new PublisherRequestCheck(refusals, verified ? notChecked : [], verified ? request : null,
            verified ? PackageCrypto.Sha256Hex(bytes) : null)
        {
            RequestBytes = verified ? bytes : null,
            Signature = verified ? signatureBytes : null,
            PreviousSignature = verified ? previousSignatureBytes : null,
        };
    }

    /// <summary>verify-request from its command line: the root and the revocation list read and verified here, so a bad one
    /// is a refusal (2) like anything else the check finds.</summary>
    public static int VerifyCommand(string requestDir, string? ledgerDir, string? gitHub, string? rootPath,
        string? revocationsJson, string? revocationsSig)
    {
        if ((revocationsJson is null) != (revocationsSig is null) || (revocationsJson is not null && rootPath is null))
            throw new ArgumentException("--revocations and --revocations-sig go together, and need --root to verify them");
        string? root = null;
        RevocationList? revocations = null;
        try
        {
            if (rootPath is not null)
            {
                using var key = PackageCrypto.ImportPublicKey(File.ReadAllText(rootPath).Trim());
                root = PackageCrypto.ExportPublicKey(key);
            }
            if (revocationsJson is not null)
            {
                var bytes = File.ReadAllBytes(revocationsJson);
                using var key = PackageCrypto.ImportPublicKey(root!);
                revocations = PackageCrypto.Verify(key, bytes, File.ReadAllText(revocationsSig!).Trim())
                    ? JsonSerializer.Deserialize(bytes, RevocationJsonContext.Default.RevocationList) : null;
                if (revocations is null)
                    return Report(new PublisherRequestCheck(["the revocation list doesn't verify against the root, or doesn't parse"],
                        [], null, null));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException
            or CryptographicException or JsonException)
        {
            return Report(new PublisherRequestCheck(["the root or the revocation list can't be read"], [], null, null));
        }
        return Report(Verify(requestDir, new RequestCheckContext
        {
            LedgerDir = ledgerDir, ExpectedGitHub = gitHub, RootPublicKey = root, Revocations = revocations,
        }));
    }

    /// <summary>Prints what verify-request found and returns its exit code: 0 verified, 2 refused, 3 verified but needing a
    /// person's check (<see cref="PublisherRequestCheck.NeedsManualCheck"/>). Every line starts with the packer's own words, never with text from the request.</summary>
    public static int Report(PublisherRequestCheck check)
    {
        if (!check.Verified)
        {
            foreach (var reason in check.Refusals)
                Console.Error.WriteLine("Refused: " + reason);
            return 2;
        }
        var r = check.Request!;
        Console.WriteLine($"Verified: a {r.Kind} publisher request, signed by the key it asks to certify");
        Console.WriteLine("  Publisher id:    " + r.PublisherId);
        Console.WriteLine("  Display name:    " + r.DisplayName);
        Console.WriteLine("  Namespaces:      " + (r.Namespaces.Length == 0 ? "(none)" : string.Join(", ", r.Namespaces)));
        Console.WriteLine("  GitHub:          " + r.GitHub);
        Console.WriteLine("  Fingerprint URL: " + r.FingerprintUrl);
        Console.WriteLine("  Key fingerprint: " + PackageCrypto.KeyFingerprint(r.PublicKey));
        if (r.PreviousPublicKey is not null)
            Console.WriteLine("  Replaces key:    " + PackageCrypto.KeyFingerprint(r.PreviousPublicKey));
        Console.WriteLine("  Request SHA-256: " + check.Sha256);
        foreach (var gap in check.NotChecked)
            Console.WriteLine("Not checked: " + gap);
        return check.NeedsManualCheck ? 3 : 0;
    }

    /// <summary>Tells the publisher what request-publisher wrote and what to do with it.</summary>
    public static void ReportCreated(PublisherRequestCheck check, string outDir, string? newKeyDir)
    {
        var r = check.Request!;
        if (newKeyDir is not null)
            Console.WriteLine($"Wrote your private key: {Path.Combine(newKeyDir, r.PublisherId + ".key")}. Keep it to yourself, "
                + "and keep a backup: nobody can recover it.");
        Console.WriteLine($"Wrote {RequestFile} and {SignatureFile}" + (r.PreviousPublicKey is null ? "" : $" and {PreviousSignatureFile}")
            + $" to {outDir}");
        Console.WriteLine("  Key fingerprint: " + PackageCrypto.KeyFingerprint(r.PublicKey));
        Console.WriteLine("  Request SHA-256: " + check.Sha256);
        Console.WriteLine($"Publish the key fingerprint at {r.FingerprintUrl}, then submit the request files as TRUST.md describes.");
    }

    // Each field as sign-publisher checks it, and the request's own: the same rules, from the same code.
    private static List<string> FieldRefusals(PublisherRequest r)
    {
        var refusals = new List<string>();
        if (r.Schema != 1)
            refusals.Add("schema must be 1");
        if (r.Kind is not (NewKind or RotationKind))
            refusals.Add($"kind must be '{NewKind}' or '{RotationKind}'");
        if (!Addins.PublisherId.IsValid(r.PublisherId))
            refusals.Add("invalid publisher id (expected 3-64 lowercase letters/digits joined by single dots or hyphens)");
        if (r.DisplayName is null || !PackageSigner.IsValidDisplayName(r.DisplayName))
            refusals.Add(PackageSigner.InvalidDisplayName);
        if (r.Namespaces is null || r.Namespaces.Any(ns => ns is null || !PublisherNamespaces.IsUsable(ns)))
            refusals.Add("each namespace must be a name followed by '.', such as 'acme.', and not "
                + $"'{PublisherNamespaces.Reserved}' or under it");
        else if (r.Namespaces.Distinct(StringComparer.OrdinalIgnoreCase).Count() != r.Namespaces.Length)
            refusals.Add("a namespace is listed twice");
        if (!IsCanonicalKey(r.PublicKey))
            refusals.Add("publicKey must be a P-256 public key, as base64 SubjectPublicKeyInfo");
        if (r.Kind == RotationKind)
        {
            if (!IsCanonicalKey(r.PreviousPublicKey))
                refusals.Add("a rotation's previousPublicKey must be a P-256 public key, as base64 SubjectPublicKeyInfo");
            else if (r.PreviousPublicKey == r.PublicKey)
                refusals.Add("a rotation's new key must differ from previousPublicKey");
        }
        else if (r.PreviousPublicKey is not null)
        {
            refusals.Add("only a rotation has a previousPublicKey");
        }
        if (r.GitHub is null || !GitHubHandle().IsMatch(r.GitHub))
            refusals.Add("github must be a GitHub handle: 1-39 letters, digits or single hyphens");
        if (!IsHttpsUrl(r.FingerprintUrl))
            refusals.Add("fingerprintUrl must be an https address, with no user name or password, where you publish the "
                + "key's fingerprint");
        if (r.CreatedUtc is null || !DateTimeOffset.TryParseExact(r.CreatedUtc, "yyyy-MM-ddTHH:mm:ssZ",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _))
            refusals.Add("createdUtc must be a UTC time such as 2026-11-25T21:00:00Z");
        return refusals;
    }

    // ── The ledger ───────────────────────────────────────────────
    // publishers/<id>/ holds the live record (four chain files, the request it was issued from, a receipt), and retired/<n>/
    // the records a rotation replaced, n = 1, 2, 3 and so on. Every key and id in it is held forever (#5322).

    private sealed record LedgerRecord(PublisherRecord Record, string Key, PublisherRequest? Request);

    private sealed record LedgerEntry(string Id, LedgerRecord Live, IReadOnlyList<LedgerRecord> Retired)
    {
        public IEnumerable<LedgerRecord> All => [Live, .. Retired];
    }

    // Uniqueness against the issued records. A request never names a path here: every entry is enumerated, and one that
    // can't be read, or with a root, doesn't verify, refuses, since what it holds can't be ruled out.
    private static List<string> LedgerRefusals(PublisherRequest r, RequestCheckContext context, List<string> notChecked)
    {
        var refusals = new List<string>();
        var entries = new List<LedgerEntry>();
        try
        {
            foreach (var folder in Directory.EnumerateDirectories(context.LedgerDir!))
                if (ReadEntry(folder, context.RootPublicKey, refusals) is { } entry)
                    entries.Add(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusals.Add("the ledger folder can't be read");
            return refusals;
        }
        if (refusals.Count > 0)
            return refusals;
        if (entries.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            return ["the ledger has two entries for one publisher id"];

        var same = entries.Where(e => string.Equals(e.Id, r.PublisherId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (r.Kind == NewKind && same.Count > 0)
            refusals.Add("the ledger already has a publisher with this id; an id is never issued twice, and a new key for "
                + "it is a rotation");
        if (r.Kind == RotationKind)
        {
            if (same.Count != 1)
                refusals.Add("a rotation needs a ledger entry for this id");
            else if (same[0].Live.Key != r.PreviousPublicKey)
                refusals.Add("previousPublicKey is not the key the ledger holds for this id");
            else if (same[0].Live.Request is not { } issuedFrom)
                notChecked.Add("who asked for this id: its ledger entry has no stored request, so the rotation must be "
                    + "checked by hand");
            else if (!string.Equals(issuedFrom.GitHub, r.GitHub, StringComparison.OrdinalIgnoreCase)
                     || !string.Equals(issuedFrom.FingerprintUrl, r.FingerprintUrl, StringComparison.Ordinal))
                notChecked.Add("whether the same publisher is asking: the github handle or fingerprintUrl differs from the "
                    + "request this id was issued from, so the rotation must be checked by hand");
        }
        if (entries.SelectMany(e => e.All).Any(record => record.Key == r.PublicKey))
            refusals.Add("the ledger already holds this key, now or retired; one key never serves two records, because "
                + "revoking one id couldn't separate them");
        foreach (var other in entries.Where(e => !same.Contains(e)))
            foreach (var held in other.All.SelectMany(record => record.Record.Namespaces ?? []))
                if (r.Namespaces.FirstOrDefault(ns => Overlaps(ns, held)) is { } clash)
                    refusals.Add($"the namespace '{clash}' overlaps one another publisher holds");
        return [.. refusals.Distinct(StringComparer.Ordinal)];
    }

    private static LedgerEntry? ReadEntry(string folder, string? rootPublicKey, List<string> refusals)
    {
        var name = Printable(Path.GetFileName(folder));
        if (new DirectoryInfo(folder).LinkTarget is not null)
        {
            refusals.Add($"the ledger entry '{name}' is a link");
            return null;
        }
        if (ReadRecord(folder, rootPublicKey, $"the ledger entry '{name}'", refusals) is not { } live)
            return null;

        var retired = new List<LedgerRecord>();
        var retiredDir = Path.Combine(folder, "retired");
        if (Directory.Exists(retiredDir) || File.Exists(retiredDir))
        {
            if (new DirectoryInfo(retiredDir).LinkTarget is not null || File.Exists(retiredDir))
            {
                refusals.Add($"the ledger entry '{name}' has a retired that isn't a folder of its own");
                return null;
            }
            // retired/1, retired/2 and so on, with no gap, no duplicate and nothing else.
            var numbered = Directory.EnumerateFileSystemEntries(retiredDir).Select(Path.GetFileName).ToList();
            var expected = Enumerable.Range(1, numbered.Count).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (!numbered.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                refusals.Add($"the ledger entry '{name}' has retired records that aren't numbered 1, 2, 3 and so on");
                return null;
            }
            for (var n = 1; n <= numbered.Count; n++)
            {
                var path = Path.Combine(retiredDir, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (!Directory.Exists(path) || new DirectoryInfo(path).LinkTarget is not null)
                {
                    refusals.Add($"the ledger entry '{name}' has a retired record {n} that isn't a folder of its own");
                    return null;
                }
                if (ReadRecord(path, rootPublicKey, $"retired record {n} of the ledger entry '{name}'", refusals) is not { } old)
                    return null;
                if (!string.Equals(old.Record.PublisherId, live.Record.PublisherId, StringComparison.Ordinal))
                {
                    refusals.Add($"retired record {n} of the ledger entry '{name}' is for another publisher id");
                    return null;
                }
                retired.Add(old);
            }
        }
        return new LedgerEntry(live.Record.PublisherId, live, retired);
    }

    // One issued record: publisher.json, and with a root its whole chain, and the request it was issued from when stored.
    // A stored request counts only if it is the very request: canonical, signed under the record's key, with the record's
    // id and, byte for byte, the record's key.
    private static LedgerRecord? ReadRecord(string folder, string? rootPublicKey, string label, List<string> refusals)
    {
        LedgerRecord? Refuse(string reason)
        {
            refusals.Add($"{label} {reason}");
            return null;
        }
        if (ReadBounded(folder, "publisher.json", MaxRequestBytes, out var recordBytes) is not null)
            return Refuse("has no readable publisher.json of its own");
        PublisherRecord? record;
        try { record = JsonSerializer.Deserialize(recordBytes, AddinJsonContext.Default.PublisherRecord); }
        catch (JsonException) { record = null; }
        if (record is null || CanonicalKey(record.PublicKey) is not { } key || !Addins.PublisherId.IsValid(record.PublisherId))
            return Refuse("doesn't parse as a publisher record");

        if (rootPublicKey is not null && ChainRefusal(folder, rootPublicKey, record, recordBytes) is { } broken)
            return Refuse(broken);

        PublisherRequest? issuedFrom = null;
        if (File.Exists(Path.Combine(folder, RequestFile)) || File.Exists(Path.Combine(folder, SignatureFile)))
        {
            if (ReadBounded(folder, RequestFile, MaxRequestBytes, out var requestBytes) is { } noRequest)
                return Refuse("has a stored request that can't be read: " + noRequest);
            if (ReadBounded(folder, SignatureFile, MaxSignatureBytes, out var signature) is { } noSignature)
                return Refuse("has a stored request that can't be read: " + noSignature);
            try { issuedFrom = JsonSerializer.Deserialize(requestBytes, PublisherRequestJsonContext.Default.PublisherRequest); }
            catch (JsonException) { issuedFrom = null; }
            using var recordKey = PackageCrypto.ImportPublicKey(key);
            if (issuedFrom is null || FieldRefusals(issuedFrom).Count > 0
                || !requestBytes.AsSpan().SequenceEqual(Canonical(issuedFrom))
                || !string.Equals(issuedFrom.PublisherId, record.PublisherId, StringComparison.Ordinal)
                || !string.Equals(issuedFrom.PublicKey, record.PublicKey, StringComparison.Ordinal)
                || !PackageCrypto.Verify(recordKey, SignedBytes(requestBytes), Encoding.ASCII.GetString(signature).Trim()))
                return Refuse("has a stored request that isn't the one its record was issued from");
        }
        return new LedgerRecord(record, key, issuedFrom);
    }

    // The record's chain under the root: intermediate.json signed by the root, publisher.json by that intermediate, naming it.
    private static string? ChainRefusal(string folder, string rootPublicKey, PublisherRecord record, byte[] recordBytes)
    {
        if (ReadBounded(folder, "publisher.sig", MaxSignatureBytes, out var recordSignature) is not null
            || ReadBounded(folder, "intermediate.json", MaxRequestBytes, out var interBytes) is not null
            || ReadBounded(folder, "intermediate.sig", MaxSignatureBytes, out var interSignature) is not null)
            return "is missing a chain file (publisher.sig, intermediate.json or intermediate.sig)";
        try
        {
            using var root = PackageCrypto.ImportPublicKey(rootPublicKey);
            if (!PackageCrypto.Verify(root, interBytes, Encoding.ASCII.GetString(interSignature).Trim()))
                return "has an intermediate the root didn't sign";
            var inter = JsonSerializer.Deserialize(interBytes, AddinJsonContext.Default.IntermediateRecord);
            if (inter is null || CanonicalKey(inter.PublicKey) is null)
                return "has an intermediate.json that doesn't parse";
            using var interKey = PackageCrypto.ImportPublicKey(inter.PublicKey);
            if (!PackageCrypto.Verify(interKey, recordBytes, Encoding.ASCII.GetString(recordSignature).Trim()))
                return "has a publisher record its intermediate didn't sign";
            if (!string.Equals(record.IntermediateId, inter.IntermediateId, StringComparison.Ordinal))
                return "has a publisher record that names another intermediate";
            return null;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
        {
            return "has a chain that doesn't parse";
        }
    }

    // ── The revocation list ──────────────────────────────────────

    /// <summary>A revocation list issued longer ago than this may miss a recent revocation, so a check that relies on it
    /// is left to a person (#5322).</summary>
    public static readonly TimeSpan RevocationListMaxAge = TimeSpan.FromDays(7);

    private static void RevocationRefusals(PublisherRequest r, RevocationList list, DateTimeOffset now,
        List<string> refusals, List<string> notChecked)
    {
        if (!DateTimeOffset.TryParse(list.IssuedUtc, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var issued))
        {
            refusals.Add("the revocation list has no issue time it can be judged by");
            return;
        }
        if (now - issued > RevocationListMaxAge)
            notChecked.Add($"whether a revocation since {issued.UtcDateTime:yyyy-MM-dd} applies: the revocation list is more "
                + $"than {RevocationListMaxAge.TotalDays:0} days old, so confirm by hand that it is the current one");
        // A rotation away from a revoked publisher is never automatic. The list can't name a key yet, so a request whose new
        // key is revoked can't be caught here until #5315 adds key fingerprints to it.
        if (r.Kind == RotationKind && list.Revoked.PublisherIds.Contains(r.PublisherId, StringComparer.OrdinalIgnoreCase))
            refusals.Add("the revocation list names this publisher id; a rotation away from a revoked publisher is "
                + "handled by hand");
    }

    // One namespace inside another, ignoring case: either publisher's add-ins could then claim the deeper prefix.
    private static bool Overlaps(string a, string b) =>
        a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

    private static string? ReadBounded(string dir, string name, int limit, out byte[] bytes)
    {
        bytes = [];
        var path = Path.Combine(dir, name);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return $"{name} is missing";
            if (info.LinkTarget is not null)
                return $"{name} is a link; a request's files must be the files themselves";
            if (info.Length > limit)
                return $"{name} is larger than {limit} bytes";
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[limit + 1];
            var read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            if (read > limit)
                return $"{name} is larger than {limit} bytes";
            bytes = buffer[..read];
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"{name} can't be read";
        }
    }

    private static bool IsCanonicalKey(string? key) => key is not null && CanonicalKey(key) == key;

    private static string? CanonicalKey(string? key)
    {
        if (key is null)
            return null;
        try
        {
            var bytes = Convert.FromBase64String(key);
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(bytes, out var consumed);
            if (consumed != bytes.Length
                || ecdsa.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
                return null;
            return PackageCrypto.ExportPublicKey(ecdsa);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }

    private static bool IsHttpsUrl(string? url) =>
        url is { Length: > 0 and <= MaxUrlLength } && url.All(char.IsAscii) && !url.Any(char.IsControl)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo) && !string.IsNullOrEmpty(uri.Host);

    internal static bool IsSameOrUnder(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." || !(relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative));
    }

    // A private key on its own, never over another file, and readable only by its owner where the system says so.
    private static void WritePrivateKey(string path, string key)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(Encoding.ASCII.GetBytes(key));
        stream.Flush(flushToDisk: true);
    }

    // A ledger folder's name, shown in a refusal without control characters, so it can't forge a line of output.
    private static string Printable(string text) =>
        new([.. text.Take(80).Select(c => char.IsControl(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '?' : c)]);
}
