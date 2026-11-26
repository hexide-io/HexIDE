using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HexIDE.Addins;

namespace HexIDE.AddinPacker;

/// <summary>What the maintainer gives <c>issue-publisher</c>.</summary>
public sealed record IssueRequest
{
    public required string RequestDir { get; init; }
    public required string ApprovedSha256 { get; init; }
    public required string RootPublicKeyPath { get; init; }
    public required string IntermediateKeyPath { get; init; }
    public required string IntermediateDir { get; init; }
    public required string LedgerDir { get; init; }
    public required string OutDir { get; init; }
    public string? RevocationsJson { get; init; }
    public string? RevocationsSig { get; init; }
}

/// <summary>How <c>issue-publisher</c> ended: 0 issued, 2 refused, 3 left for a person to check, with the reasons.</summary>
public sealed record IssueOutcome(int ExitCode, IReadOnlyList<string> Reasons);

/// <summary>
/// Turns an approved publisher request into a signed record (#5322), on the machine that holds the intermediate key. The
/// request is hostile input there, so every check verify-request makes runs again, against the ledger, the root and the
/// revocation list, and only a check with nothing refused and nothing left unchecked is signed. The SHA-256 the
/// maintainer approved must be the request's, the publisher id must be typed back, and the record goes to the publisher
/// and into the ledger together.
/// </summary>
public static class PublisherIssuer
{
    private static readonly string[] ChainFiles = ["publisher.json", "publisher.sig", "intermediate.json", "intermediate.sig"];

    public static IssueOutcome Issue(IssueRequest r, Func<string?> readLine, TextWriter output)
    {
        static IssueOutcome Refused(params string[] reasons) => new(2, reasons);

        if (r.ApprovedSha256.Length != 64 || !r.ApprovedSha256.All(Uri.IsHexDigit))
            return Refused("--approved-sha256 must be the 64-digit SHA-256 verify-request printed");
        if ((r.RevocationsJson is null) != (r.RevocationsSig is null))
            return Refused("--revocations and --revocations-sig go together");

        string rootPublicKey;
        try
        {
            using var root = PackageCrypto.ImportPublicKey(File.ReadAllText(r.RootPublicKeyPath).Trim());
            rootPublicKey = PackageCrypto.ExportPublicKey(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or CryptographicException)
        {
            return Refused("the root public key can't be read as base64 ECDSA P-256 SPKI");
        }

        RevocationList? revocations = null;
        if (r.RevocationsJson is not null)
        {
            if (LoadRevocations(r.RevocationsJson, r.RevocationsSig!, rootPublicKey) is not { } list)
                return Refused("the revocation list doesn't verify against the root, or doesn't parse");
            revocations = list;
        }

        var check = PublisherRequests.Verify(r.RequestDir, new RequestCheckContext
        {
            LedgerDir = r.LedgerDir,
            RootPublicKey = rootPublicKey,
            Revocations = revocations,
        });
        if (!check.Verified)
            return Refused([.. check.Refusals]);
        var request = check.Request!;
        if (request.Kind == PublisherRequests.RotationKind && revocations is null)
            return Refused("a rotation needs the revocation list: give --revocations and --revocations-sig");
        if (check.NeedsManualCheck)
            return new IssueOutcome(3, [.. check.NotChecked]);
        if (!string.Equals(check.Sha256, r.ApprovedSha256, StringComparison.OrdinalIgnoreCase))
            return Refused($"the request's SHA-256 is {check.Sha256}, not the one approved; nothing that wasn't approved is signed");

        PackageSigner.SignedRecord signed;
        try
        {
            signed = PackageSigner.SignRecord(r.RootPublicKeyPath, r.IntermediateKeyPath, r.IntermediateDir,
                request.PublisherId, request.DisplayName, () => request.PublicKey,
                request.Namespaces.Length == 0 ? null : request.Namespaces);
        }
        catch (ArgumentException ex)
        {
            return Refused(ex.Message);
        }
        if (revocations?.Revoked.IntermediateIds.Contains(signed.IntermediateRecord.IntermediateId, StringComparer.OrdinalIgnoreCase) == true)
            return Refused("the revocation list names this intermediate; nothing is issued under it");

        // The one path built from the request's id. The id's own rule allows only letters, digits, dots and hyphens in
        // parts, so it can't climb out, and this checks that it didn't.
        var ledger = Path.GetFullPath(r.LedgerDir);
        var entry = Path.GetFullPath(Path.Combine(ledger, request.PublisherId));
        if (!string.Equals(Path.GetDirectoryName(entry), Path.TrimEndingDirectorySeparator(ledger), StringComparison.Ordinal))
            return Refused("the publisher id doesn't name a folder directly inside the ledger");
        var rotation = request.Kind == PublisherRequests.RotationKind;
        var retiredSlot = rotation ? Path.Combine(entry, "retired", NextRetired(entry)) : null;
        if (rotation ? Directory.Exists(retiredSlot) : Directory.Exists(entry) || File.Exists(entry))
            return Refused("the ledger already has what this would write");
        var outFolder = PackageSigner.ResolveOutputDirectory(r.OutDir);

        var receipt = Receipt(request, check.Sha256!, signed, DateTimeOffset.UtcNow);
        var files = new Dictionary<string, byte[]>
        {
            ["publisher.json"] = signed.Record,
            ["publisher.sig"] = Encoding.ASCII.GetBytes(signed.Signature),
            ["intermediate.json"] = signed.Intermediate,
            ["intermediate.sig"] = Encoding.ASCII.GetBytes(signed.IntermediateSignature),
            [PublisherRequests.RequestFile] = check.RequestBytes!,
            [PublisherRequests.SignatureFile] = check.Signature!,
            ["receipt.md"] = Encoding.UTF8.GetBytes(receipt),
        };
        if (check.PreviousSignature is { } previousSignature)
            files[PublisherRequests.PreviousSignatureFile] = previousSignature;
        var existing = files.Keys.Append("hexide-root.pub").Where(name => File.Exists(Path.Combine(outFolder, name))).ToArray();
        if (existing.Length > 0)
            return Refused($"--out already holds {string.Join(", ", existing)}");

        // Shown before signing, and signed only once the id is typed back.
        output.WriteLine($"About to issue a {request.Kind} publisher record:");
        output.WriteLine("  Publisher id:    " + request.PublisherId);
        output.WriteLine("  Display name:    " + request.DisplayName);
        output.WriteLine("  Namespaces:      " + (request.Namespaces.Length == 0 ? "(none)" : string.Join(", ", request.Namespaces)));
        output.WriteLine("  GitHub:          " + request.GitHub);
        output.WriteLine("  Key fingerprint: " + PackageCrypto.KeyFingerprint(request.PublicKey));
        if (request.PreviousPublicKey is not null)
            output.WriteLine("  Replaces key:    " + PackageCrypto.KeyFingerprint(request.PreviousPublicKey));
        output.WriteLine("  Intermediate:    " + signed.IntermediateRecord.IntermediateId);
        output.WriteLine("  Root:            " + PackageCrypto.KeyFingerprint(rootPublicKey));
        output.Write("Type the publisher id to sign this record, or anything else to stop: ");
        output.Flush();
        var typed = readLine()?.TrimEnd('\r', '\n');
        if (!string.Equals(typed, request.PublisherId, StringComparison.Ordinal))
            return Refused("the id typed wasn't the publisher id; nothing was signed or written");

        Directory.CreateDirectory(outFolder);
        PackageSigner.WriteNewFiles(outFolder, new Dictionary<string, byte[]>(files)
        {
            ["hexide-root.pub"] = Encoding.ASCII.GetBytes(rootPublicKey),
        });
        PackageSigner.VerifyWrittenChain(signed, r.IntermediateDir, outFolder);

        if (rotation)
            Retire(entry, retiredSlot!, files);
        else
        {
            Directory.CreateDirectory(entry);
            PackageSigner.WriteNewFiles(entry, files);
        }
        PackageSigner.VerifyWrittenChain(signed, r.IntermediateDir, entry);

        output.WriteLine($"Issued: wrote the record to {outFolder} and added it to the ledger as {request.PublisherId}.");
        output.WriteLine("Send the publisher the four chain files and hexide-root.pub, and commit the ledger.");
        return new IssueOutcome(0, []);
    }

    private static RevocationList? LoadRevocations(string jsonPath, string sigPath, string rootPublicKey)
    {
        try
        {
            var bytes = File.ReadAllBytes(jsonPath);
            using var root = PackageCrypto.ImportPublicKey(rootPublicKey);
            if (!PackageCrypto.Verify(root, bytes, File.ReadAllText(sigPath).Trim()))
                return null;
            return JsonSerializer.Deserialize(bytes, RevocationJsonContext.Default.RevocationList);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // retired/<n> for the next n, after the ones verify-request has already checked run 1, 2, 3 without a gap.
    private static string NextRetired(string entry)
    {
        var retired = Path.Combine(entry, "retired");
        var count = Directory.Exists(retired) ? Directory.EnumerateFileSystemEntries(retired).Count() : 0;
        return (count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // The live record moves to retired/<n>, written new beside it first, then the new record replaces each live file.
    private static void Retire(string entry, string slot, Dictionary<string, byte[]> files)
    {
        var live = Directory.EnumerateFiles(entry).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes,
            StringComparer.Ordinal);
        Directory.CreateDirectory(slot);
        PackageSigner.WriteNewFiles(slot, live);
        foreach (var (name, bytes) in files)
        {
            var temporary = Path.Combine(entry, name + ".new");
            PackageSigner.WriteNewFiles(entry, new Dictionary<string, byte[]> { [name + ".new"] = bytes });
            File.Move(temporary, Path.Combine(entry, name), overwrite: true);
        }
        foreach (var stale in live.Keys.Where(name => !files.ContainsKey(name)))
            File.Delete(Path.Combine(entry, stale));
    }

    // GitHub renders the receipt, so what came from the request sits in a fenced block that can't become a link or HTML.
    private static string Receipt(PublisherRequest request, string sha256, PackageSigner.SignedRecord signed, DateTimeOffset now)
    {
        var said = string.Join("\n", new[]
        {
            "Display name:    " + request.DisplayName,
            "Namespaces:      " + (request.Namespaces.Length == 0 ? "(none)" : string.Join(", ", request.Namespaces)),
            "GitHub:          " + request.GitHub,
            "Fingerprint URL: " + request.FingerprintUrl,
        });
        var longestRun = said.Split(c => c != '`').Max(run => run.Length);
        var fence = new string('`', Math.Max(3, longestRun + 1));
        var lines = new List<string>
        {
            $"# Publisher record: {request.PublisherId}",
            "",
            $"Issued {now.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ} by issue-publisher, from the approved request with SHA-256",
            $"`{sha256}`.",
            "",
            $"- **Kind:** {request.Kind}",
            $"- **Publisher key fingerprint:** `{PackageCrypto.KeyFingerprint(request.PublicKey)}`",
        };
        if (request.PreviousPublicKey is not null)
            lines.Add($"- **Replaces key:** `{PackageCrypto.KeyFingerprint(request.PreviousPublicKey)}`");
        lines.AddRange(
        [
            $"- **Intermediate:** {signed.IntermediateRecord.IntermediateId}, key fingerprint " +
                $"`{PackageCrypto.KeyFingerprint(signed.IntermediateRecord.PublicKey)}`",
            $"- **Root key fingerprint:** `{PackageCrypto.KeyFingerprint(signed.RootPublicKey)}`",
            "",
            "What the request said:",
            "",
            fence + "text",
            said,
            fence,
            "",
            "HexIDE trusts this record when the root above is the one it carries. TRUST.md gives the fingerprint of HexIDE's",
            "root; a build that carries a different root reports packages signed under this record as signed under a root it",
            "doesn't recognise.",
            "",
        ]);
        return string.Join("\n", lines);
    }
}

internal static class StringSplitExtensions
{
    public static IEnumerable<string> Split(this string text, Func<char, bool> separator)
    {
        var start = 0;
        for (var i = 0; i <= text.Length; i++)
            if (i == text.Length || separator(text[i]))
            {
                yield return text[start..i];
                start = i + 1;
            }
    }
}
