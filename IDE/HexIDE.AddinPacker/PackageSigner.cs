using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using HexIDE.Addins;

namespace HexIDE.AddinPacker;

public sealed class PackRequest
{
    public required string SrcDir { get; init; }
    public required string OutDir { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Description { get; init; } = "";
    public required string Version { get; init; }
    public string Author { get; init; } = "";
    public required string Entry { get; init; }
    /// <summary>Directory holding firstparty.key, publisher.json/.sig and intermediate.json/.sig.</summary>
    public required string TrustDir { get; init; }
    /// <summary>Package-relative names of private-dependency files to bundle alongside the entry.</summary>
    public List<string> Include { get; init; } = [];
    /// <summary>Optional package-relative path of a PNG logo. Copied + hashed like an <see cref="Include"/>
    /// file and recorded as the manifest's <c>logoPath</c> so it is signature-covered.</summary>
    public string? Logo { get; init; }
    public string? MinHexideVersion { get; init; }
    public List<string> Capabilities { get; init; } = [];
}

/// <summary>
/// Produces a signed add-in package folder. Mirror image of <c>HexIDE.Addins.PackageVerifier</c>:
/// it hashes every bundled file, writes <c>addin.json</c>, signs the exact manifest bytes with the
/// publisher key, and copies the publisher + intermediate chain artifacts so the package verifies
/// fully offline.
/// </summary>
public static class PackageSigner
{
    /// <summary>Generates a self-contained dev trust chain (root → intermediate → first-party
    /// publisher) into <paramref name="outDir"/>. One-time bootstrap; the outputs are committed.</summary>
    public static void GenerateDevTrust(string outDir)
    {
        Directory.CreateDirectory(outDir);

        var outputs = new Dictionary<string, byte[]>();

        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var intermediate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firstParty = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        var interRec = new IntermediateRecord
        {
            IntermediateId = "hexide-marketplace-dev",
            DisplayName = "HexIDE Marketplace (DEV)",
            PublicKey = PackageCrypto.ExportPublicKey(intermediate),
        };
        var interBytes = JsonSerializer.SerializeToUtf8Bytes(interRec, AddinJsonContext.Default.IntermediateRecord);
        outputs["intermediate.json"] = interBytes;
        outputs["intermediate.sig"] = Encoding.UTF8.GetBytes(PackageCrypto.Sign(root, interBytes));

        var pubRec = new PublisherRecord
        {
            PublisherId = "com.hexide.firstparty",
            DisplayName = "HexIDE (First-Party)",
            PublicKey = PackageCrypto.ExportPublicKey(firstParty),
            IntermediateId = "hexide-marketplace-dev",
        };
        var pubBytes = JsonSerializer.SerializeToUtf8Bytes(pubRec, AddinJsonContext.Default.PublisherRecord);
        outputs["publisher.json"] = pubBytes;
        outputs["publisher.sig"] = Encoding.UTF8.GetBytes(PackageCrypto.Sign(intermediate, pubBytes));

        outputs["hexide-root.pub"] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPublicKey(root));
        // The first-party publisher PUBLIC key is pinned (baked into the IDE) so first-party status is
        // bound to possession of the first-party private key, not just the publisherId string.
        outputs["hexide-firstparty.pub"] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPublicKey(firstParty));
        outputs["firstparty.key"] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(firstParty));
        // Dev-only private keys retained so the chain can be regenerated/rotated. NEVER ship these.
        outputs["root.key"] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(root));
        outputs["intermediate.key"] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(intermediate));

        WriteNewFiles(outDir, outputs);
        Console.WriteLine($"Generated dev trust chain in {outDir}");
    }

    /// <summary>Generates a release chain with explicit identities and separate private-key outputs.</summary>
    public static void GenerateReleaseTrust(ReleaseTrustRequest r)
    {
        if (!PublisherId.IsValid(r.IntermediateId) || !PublisherId.IsValid(r.FirstPartyId))
            throw new ArgumentException("invalid publisher or intermediate id");
        ValidateDisplayName(r.IntermediateName);
        ValidateDisplayName(r.FirstPartyName);

        var directories = new[] { r.RootOut, r.IntermediateOut, r.FirstPartyOut, r.PublicOut }
            .Select(path => ResolveOutputDirectory(path)).ToArray();
        var prefixes = directories.Select(path => Path.EndsInDirectorySeparator(path)
            ? path : path + Path.DirectorySeparatorChar).ToArray();
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var i = 0; i < directories.Length; i++)
            for (var j = i + 1; j < directories.Length; j++)
                if (string.Equals(directories[i], directories[j], comparison) ||
                    directories[i].StartsWith(prefixes[j], comparison) ||
                    directories[j].StartsWith(prefixes[i], comparison))
                    throw new ArgumentException("output directories must not overlap");

        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var intermediate = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var firstParty = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootPublic = PackageCrypto.ExportPublicKey(root);
        var intermediatePublic = PackageCrypto.ExportPublicKey(intermediate);
        var firstPartyPublic = PackageCrypto.ExportPublicKey(firstParty);
        var interBytes = JsonSerializer.SerializeToUtf8Bytes(new IntermediateRecord
        {
            IntermediateId = r.IntermediateId,
            DisplayName = r.IntermediateName,
            PublicKey = intermediatePublic,
        }, AddinJsonContext.Default.IntermediateRecord);
        var pubBytes = JsonSerializer.SerializeToUtf8Bytes(new PublisherRecord
        {
            PublisherId = r.FirstPartyId,
            DisplayName = r.FirstPartyName,
            PublicKey = firstPartyPublic,
            IntermediateId = r.IntermediateId,
        }, AddinJsonContext.Default.PublisherRecord);

        var outputs = new Dictionary<string, byte[]>
        {
            [Path.Combine(directories[0], "root.key")] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(root)),
            [Path.Combine(directories[1], "intermediate.key")] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(intermediate)),
            [Path.Combine(directories[2], "firstparty.key")] = Encoding.UTF8.GetBytes(PackageCrypto.ExportPrivateKey(firstParty)),
            [Path.Combine(directories[3], "hexide-root.pub")] = Encoding.UTF8.GetBytes(rootPublic),
            [Path.Combine(directories[3], "hexide-firstparty.pub")] = Encoding.UTF8.GetBytes(firstPartyPublic),
            [Path.Combine(directories[3], "intermediate.json")] = interBytes,
            [Path.Combine(directories[3], "intermediate.sig")] = Encoding.UTF8.GetBytes(PackageCrypto.Sign(root, interBytes)),
            [Path.Combine(directories[3], "publisher.json")] = pubBytes,
            [Path.Combine(directories[3], "publisher.sig")] = Encoding.UTF8.GetBytes(PackageCrypto.Sign(intermediate, pubBytes)),
        };
        foreach (var directory in directories) Directory.CreateDirectory(directory);
        // Absolute paths let the existing reservation/cleanup cover all four directories together.
        WriteNewFiles("", outputs);
        Console.WriteLine($"Root: {PackageCrypto.KeyFingerprint(rootPublic)}");
        Console.WriteLine($"Intermediate: {PackageCrypto.KeyFingerprint(intermediatePublic)}");
        Console.WriteLine($"Publisher: {PackageCrypto.KeyFingerprint(firstPartyPublic)}");
    }

    private static string ResolveOutputDirectory(string path, int links = 0)
    {
        if (links > 40) throw new IOException("too many symbolic links in output directory");
        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = new DirectoryInfo(Path.Combine(current, part));
            current = directory.LinkTarget is null ? directory.FullName
                : ResolveOutputDirectory(directory.ResolveLinkTarget(true)!.FullName, links + 1);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>Signs a revocation list with the HexIDE root key: copies the input JSON verbatim to
    /// <c>revocations.json</c> and writes the detached root signature to <c>revocations.sig</c>.</summary>
    public static void SignRevocations(string inJsonPath, string rootKeyPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var outputs = new Dictionary<string, byte[]>();
        var jsonBytes = File.ReadAllBytes(inJsonPath);
        var keyB64 = File.ReadAllText(rootKeyPath).Trim();
        using var key = PackageCrypto.ImportPrivateKey(keyB64);
        outputs["revocations.json"] = jsonBytes;
        outputs["revocations.sig"] = Encoding.UTF8.GetBytes(PackageCrypto.Sign(key, jsonBytes));
        WriteNewFiles(outDir, outputs);
        Console.WriteLine($"Signed revocation list -> {Path.Combine(outDir, "revocations.json")}");
    }

    /// <summary>Issues a publisher's public key using an offline intermediate signing key.</summary>
    public static void SignPublisher(string rootPublicKeyPath, string intermediateKeyPath, string intermediateDir,
        string publisherId, string displayName, string publicKeyPath, string outDir)
    {
        if (!PublisherId.IsValid(publisherId))
            throw new ArgumentException("invalid publisher id (expected 3–64 lowercase letters/digits joined by single dots or hyphens)");
        ValidateDisplayName(displayName);

        var interBytes = File.ReadAllBytes(Path.Combine(intermediateDir, "intermediate.json"));
        var interSig = File.ReadAllText(Path.Combine(intermediateDir, "intermediate.sig")).Trim();
        using var root = ReadKey(rootPublicKeyPath, privateKey: false, "root public key");
        if (!PackageCrypto.Verify(root, interBytes, interSig))
            throw new ArgumentException("intermediate signature does not verify against the supplied root");
        var inter = JsonSerializer.Deserialize(interBytes, AddinJsonContext.Default.IntermediateRecord)
                    ?? throw new ArgumentException("invalid intermediate record");
        if (!PublisherId.IsValid(inter.IntermediateId))
            throw new ArgumentException("invalid intermediate id");
        using var interPublic = PackageCrypto.ImportPublicKey(inter.PublicKey);
        RequireP256(interPublic, "intermediate public key");
        using var key = ReadKey(intermediateKeyPath, privateKey: true, "intermediate private key");
        if (PackageCrypto.ExportPublicKey(key) != PackageCrypto.ExportPublicKey(interPublic))
            throw new ArgumentException("intermediate private key does not match the intermediate record");
        using var publisher = ReadKey(publicKeyPath, privateKey: false, "publisher public key");
        var record = new PublisherRecord
        {
            PublisherId = publisherId,
            DisplayName = displayName,
            PublicKey = PackageCrypto.ExportPublicKey(publisher),
            IntermediateId = inter.IntermediateId,
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(record, AddinJsonContext.Default.PublisherRecord);
        var signature = PackageCrypto.Sign(key, bytes);
        Directory.CreateDirectory(outDir);
        WriteNewFiles(outDir, new Dictionary<string, byte[]>
        {
            ["publisher.json"] = bytes,
            ["publisher.sig"] = Encoding.UTF8.GetBytes(signature),
        });
        if (!PackageCrypto.Verify(root, File.ReadAllBytes(Path.Combine(intermediateDir, "intermediate.json")),
                File.ReadAllText(Path.Combine(intermediateDir, "intermediate.sig")).Trim())
            || !PackageCrypto.Verify(interPublic, File.ReadAllBytes(Path.Combine(outDir, "publisher.json")),
                File.ReadAllText(Path.Combine(outDir, "publisher.sig")).Trim()))
            throw new InvalidOperationException("written publisher chain verification failed");
        Console.WriteLine("Verified publisher record against the intermediate and supplied root public key");
    }

    private static void ValidateDisplayName(string displayName)
    {
        // The trust inspector shows this name to users, so a bidi override or other invisible format character
        // (U+202E, U+200B, U+FEFF, ...) could make one publisher's name read as another's. Refuse them with the controls.
        if (string.IsNullOrWhiteSpace(displayName) || displayName != displayName.Trim()
            || displayName.Length > 128 || displayName.Any(c => char.IsControl(c)
                || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator))
            throw new ArgumentException("invalid display name (expected 1–128 trimmed characters without control or invisible format characters)");
    }

    private static ECDsa ReadKey(string path, bool privateKey, string label)
    {
        try
        {
            var text = File.ReadAllText(path).Trim();
            var bytes = Convert.FromBase64String(text);
            var key = ECDsa.Create();
            try
            {
                int consumed;
                if (privateKey) key.ImportPkcs8PrivateKey(bytes, out consumed);
                else key.ImportSubjectPublicKeyInfo(bytes, out consumed);
                if (consumed != bytes.Length) throw new ArgumentException("trailing key data");
                RequireP256(key, label);
                return key;
            }
            catch { key.Dispose(); throw; }
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            // Key parser diagnostics must never echo key material.
            throw new ArgumentException($"invalid {label} (expected base64 ECDSA P-256 {(privateKey ? "PKCS#8" : "SPKI")})");
        }
    }

    private static void RequireP256(ECDsa key, string label)
    {
        if (key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new ArgumentException($"invalid {label}: expected ECDSA P-256");
    }

    private static void WriteNewFiles(string directory, Dictionary<string, byte[]> outputs)
    {
        var streams = new Dictionary<string, FileStream>();
        var completed = false;
        try
        {
            try
            {
                // Reserve every destination before writing, so a collision cannot replace part of a chain.
                foreach (var name in outputs.Keys)
                    streams.Add(name, new FileStream(Path.Combine(directory, name), FileMode.CreateNew, FileAccess.Write));
                foreach (var (name, bytes) in outputs)
                {
                    streams[name].Write(bytes);
                    streams[name].Flush(flushToDisk: true);
                }
            }
            finally
            {
                foreach (var stream in streams.Values)
                    stream.Dispose();
            }
            completed = true;
        }
        finally
        {
            if (!completed)
                foreach (var name in streams.Keys)
                {
                    // Preserve the original failure if the filesystem also refuses cleanup.
                    try { File.Delete(Path.Combine(directory, name)); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
        }
    }

    public static void Pack(PackRequest r)
    {
        if (r.MinHexideVersion is { } minimum)
            _ = System.Version.Parse(minimum);

        var capabilities = JsonSerializer.SerializeToElement(r.Capabilities.Order(StringComparer.Ordinal).ToArray());
        if (!AddinCapabilities.IsValid(capabilities))
            throw new ArgumentException("invalid capabilities declaration", nameof(r));

        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(r.OutDir));
        RequireOwnedOutput(output, r.TrustDir);
        var staging = output + "." + Guid.NewGuid().ToString("N") + ".staging";
        var previous = output + "." + Guid.NewGuid().ToString("N") + ".previous";
        Directory.CreateDirectory(staging);
        try
        {
            var count = WritePackage(r, staging, capabilities);
            // Check again after staging: a user may have edited the old package while we built.
            RequireOwnedOutput(output, r.TrustDir);
            var replacing = Directory.Exists(output);
            var previousPackage = Path.Combine(previous, "package");
            if (replacing)
            {
                Directory.CreateDirectory(previous);
                Directory.Move(output, previousPackage);
            }
            try { Directory.Move(staging, output); }
            catch
            {
                if (replacing)
                {
                    Directory.Move(previousPackage, output);
                    Directory.Delete(previous);
                }
                throw;
            }
            // Installation has committed. A cleanup failure must not turn a successful pack into
            // a reported failure with the old output gone. The container has no root manifest, so
            // any leftovers cannot be discovered as a second add-in by the IDE.
            if (replacing)
            {
                try { Directory.Delete(previous, recursive: true); }
                catch (IOException) { Console.Error.WriteLine(previous); }
                catch (UnauthorizedAccessException) { Console.Error.WriteLine(previous); }
            }
            Console.WriteLine($"Packed '{r.Id}' -> {r.OutDir} ({count} file(s))");
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static void RequireOwnedOutput(string output, string trust)
    {
        if (new DirectoryInfo(output).LinkTarget is not null)
            throw OutputRefusal(output, LinkedOutput);
        if (File.Exists(output)) throw OutputRefusal(output, UnownedOutput);
        if (!Directory.Exists(output)) return;

        var linked = false;
        try
        {
            var inventory = new HashSet<string>(StringComparer.Ordinal);
            void Scan(string directory)
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    {
                        linked = true;
                        throw new IOException(output);
                    }
                    inventory.Add(Path.GetRelativePath(output, path).Replace('\\', '/'));
                    if (Directory.Exists(path)) Scan(path);
                }
            }
            Scan(output);
            if (inventory.Count == 0) return;
            var manifestBytes = File.ReadAllBytes(Path.Combine(output, "addin.json"));
            using var key = PackageCrypto.ImportPrivateKey(File.ReadAllText(Path.Combine(trust, "firstparty.key")).Trim());
            if (!PackageCrypto.Verify(key, manifestBytes, File.ReadAllText(Path.Combine(output, "addin.sig"))))
                throw new IOException(output);
            var manifest = JsonSerializer.Deserialize(manifestBytes, AddinJsonContext.Default.AddinManifest);
            if (manifest?.Files is null) throw new IOException(output);
            var expected = new HashSet<string>(StringComparer.Ordinal) { "addin.json", "addin.sig" };
            foreach (var name in new[] { "publisher.json", "publisher.sig", "intermediate.json", "intermediate.sig" })
            {
                expected.Add(name);
                if (!File.ReadAllBytes(Path.Combine(output, name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(trust, name))))
                    throw new IOException(output);
            }
            foreach (var file in manifest.Files)
            {
                var name = file.Path.Replace('\\', '/');
                if (Path.IsPathRooted(name) || name.Split('/').Any(part => part is "" or "." or ".."))
                    throw new IOException(output);
                expected.Add(name);
                for (var slash = name.LastIndexOf('/'); slash >= 0; slash = name.LastIndexOf('/', slash - 1))
                    expected.Add(name[..slash]);
                if (PackageCrypto.Sha256Hex(File.ReadAllBytes(Path.Combine(output, name))) != file.Sha256)
                    throw new IOException(output);
            }
            if (!inventory.SetEquals(expected)) throw new IOException(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException)
        {
            // Never echo signing material from parser failures.
            throw OutputRefusal(output, linked ? LinkedOutput : UnownedOutput);
        }
    }

    private const string LinkedOutput = "is or contains a link";
    private const string UnownedOutput = "is not unchanged output of this tool";

    private static IOException OutputRefusal(string output, string reason) =>
        new($"Output '{output}' {reason}. Nothing was changed. Choose another folder or move this one aside.");

    private static int WritePackage(PackRequest r, string destination, JsonElement capabilities)
    {
        var files = new List<AddinFileHash>();

        void CopyAndHash(string relName)
        {
            relName = relName.Replace('\\', '/');
            if (Path.IsPathRooted(relName) || relName.Contains(':') ||
                relName.Split('/').Any(part => part is "" or "." or ".."))
                throw new IOException(relName);
            var dst = Path.Combine(destination, relName);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(Path.Combine(r.SrcDir, relName), dst, overwrite: true);
            files.Add(new AddinFileHash
            {
                Path = relName.Replace('\\', '/'),
                Sha256 = PackageCrypto.Sha256Hex(File.ReadAllBytes(dst)),
            });
        }

        CopyAndHash(r.Entry);
        foreach (var inc in r.Include)
            CopyAndHash(inc);

        // The logo is just another hashed payload file. Copy it unless it was already included, so it is
        // listed in files[] exactly once and covered by the manifest signature + completeness check.
        var logoRel = string.IsNullOrEmpty(r.Logo) ? null : r.Logo.Replace('\\', '/');
        if (logoRel is not null && !files.Exists(f => string.Equals(f.Path, logoRel, StringComparison.OrdinalIgnoreCase)))
            CopyAndHash(r.Logo!);

        var pubBytes = File.ReadAllBytes(Path.Combine(r.TrustDir, "publisher.json"));
        var pub = JsonSerializer.Deserialize(pubBytes, AddinJsonContext.Default.PublisherRecord)
                  ?? throw new InvalidOperationException("publisher.json in trust dir is invalid");

        var manifest = new AddinManifest
        {
            Schema = AddinManifest.CurrentSchema,
            Capabilities = capabilities,
            MinHexideVersion = r.MinHexideVersion,
            Id = r.Id,
            Title = r.Title,
            Description = r.Description,
            Version = r.Version,
            Author = r.Author,
            PublisherId = pub.PublisherId,
            Entry = r.Entry.Replace('\\', '/'),
            LogoPath = logoRel,
            Files = files.ToArray(),
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, AddinJsonContext.Default.AddinManifest);
        File.WriteAllBytes(Path.Combine(destination, "addin.json"), manifestBytes);

        var keyB64 = File.ReadAllText(Path.Combine(r.TrustDir, "firstparty.key")).Trim();
        using var key = PackageCrypto.ImportPrivateKey(keyB64);
        File.WriteAllText(Path.Combine(destination, "addin.sig"), PackageCrypto.Sign(key, manifestBytes));

        foreach (var f in new[] { "publisher.json", "publisher.sig", "intermediate.json", "intermediate.sig" })
            File.Copy(Path.Combine(r.TrustDir, f), Path.Combine(destination, f), overwrite: true);

        return files.Count;
    }
}
