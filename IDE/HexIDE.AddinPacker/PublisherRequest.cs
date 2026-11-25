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

/// <summary>What <see cref="PublisherRequests.Verify"/> found. A request passes only with no refusals; one checked without a
/// ledger also lists what that left unchecked.</summary>
public sealed record PublisherRequestCheck(IReadOnlyList<string> Refusals, IReadOnlyList<string> NotChecked,
    PublisherRequest? Request, string? Sha256)
{
    public bool Verified => Refusals.Count == 0;

    /// <summary>A rotation whose previous key nobody compared with the ledger: only its own word says the id is its to
    /// rotate. Automation must treat this as a failure (exit 3).</summary>
    public bool RotationUnchecked => Verified && NotChecked.Count > 0;
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
    public static PublisherRequestCheck Verify(string requestDir, string? ledgerDir, string? expectedGitHub)
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

        if (expectedGitHub is not null && !string.Equals(expectedGitHub, request.GitHub, StringComparison.OrdinalIgnoreCase))
            refusals.Add("the request's github handle is not the account that submitted it");

        if (ledgerDir is null)
        {
            if (request.Kind == RotationKind)
                notChecked.Add("whether previousPublicKey is the key the ledger holds for this id: no --ledger was given, so "
                    + "only the request says the id is its to rotate");
        }
        else
        {
            refusals.AddRange(LedgerRefusals(request, ledgerDir));
        }

        return new PublisherRequestCheck(refusals, notChecked, refusals.Count == 0 ? request : null,
            refusals.Count == 0 ? PackageCrypto.Sha256Hex(bytes) : null);
    }

    /// <summary>Prints what verify-request found and returns its exit code: 0 verified, 2 refused, 3 a rotation not
    /// checked against a ledger. Every line starts with the packer's own words, never with text from the request.</summary>
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
        return check.RotationUnchecked ? 3 : 0;
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

    // Uniqueness against the issued records. A request never names a path here: every entry is enumerated, and one that
    // can't be read refuses, since what it holds can't be ruled out.
    private static List<string> LedgerRefusals(PublisherRequest r, string ledgerDir)
    {
        var refusals = new List<string>();
        var records = new List<(PublisherRecord Record, string Key)>();
        try
        {
            foreach (var entry in Directory.EnumerateDirectories(ledgerDir))
            {
                var name = Path.GetFileName(entry);
                var path = Path.Combine(entry, "publisher.json");
                if (new DirectoryInfo(entry).LinkTarget is not null || !File.Exists(path) || new FileInfo(path).LinkTarget is not null)
                {
                    refusals.Add($"the ledger entry '{Printable(name)}' has no publisher.json of its own");
                    continue;
                }
                PublisherRecord? record = null;
                string? key = null;
                try
                {
                    record = JsonSerializer.Deserialize(File.ReadAllBytes(path), AddinJsonContext.Default.PublisherRecord);
                    key = record is null ? null : CanonicalKey(record.PublicKey);
                }
                catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
                {
                }
                if (record is null || key is null)
                    refusals.Add($"the ledger entry '{Printable(name)}' doesn't parse as a publisher record");
                else
                    records.Add((record, key));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            refusals.Add("the ledger folder can't be read");
            return refusals;
        }
        if (refusals.Count > 0)
            return refusals;

        var same = records.Where(e => string.Equals(e.Record.PublisherId, r.PublisherId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (r.Kind == NewKind && same.Count > 0)
            refusals.Add("the ledger already has a publisher with this id; a new key for it is a rotation");
        if (r.Kind == RotationKind)
        {
            if (same.Count != 1)
                refusals.Add("a rotation needs exactly one ledger record for this id");
            else if (same[0].Key != r.PreviousPublicKey)
                refusals.Add("previousPublicKey is not the key the ledger holds for this id");
        }
        if (records.Any(e => e.Key == r.PublicKey))
            refusals.Add("the ledger already has a publisher with this key; one key can't serve two records, because "
                + "revoking one id couldn't separate them");
        foreach (var other in records.Where(e => !same.Contains(e)))
            foreach (var held in other.Record.Namespaces ?? [])
                if (r.Namespaces.FirstOrDefault(ns => Overlaps(ns, held)) is { } clash)
                    refusals.Add($"the namespace '{clash}' overlaps one another publisher holds");
        return refusals;
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

    private static bool IsSameOrUnder(string path, string root)
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
