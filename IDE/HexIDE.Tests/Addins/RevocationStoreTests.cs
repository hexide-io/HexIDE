using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The revocation store: the bundled floor is empty (revokes nothing); a validly-root-signed cached
/// list takes effect; an invalidly-signed list is ignored (fail-open).
/// </summary>
public sealed class RevocationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hexide_revtest_" + Guid.NewGuid().ToString("N"));

    public RevocationStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Default_store_with_the_bundled_empty_floor_revokes_nothing()
    {
        new RevocationStore().IsRevoked("anyhash", "anypub", "anyinter", isFirstParty: false).Should().BeNull();
    }

    [Fact]
    public void A_validly_signed_cached_list_revokes_a_build()
    {
        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootPub = PackageCrypto.ExportPublicKey(root);
        WriteCachedList(root, "2026-02-01T00:00:00Z", hashes: ["badhash"]);

        var store = new RevocationStore(rootPub, _dir);   // bundled floor (dev-root) won't verify under this root

        store.IsRevoked("badhash", "p", "i", isFirstParty: false).Should().Be("build revoked");
        store.IsRevoked("otherhash", "p", "i", isFirstParty: false).Should().BeNull();
    }

    [Fact]
    public void A_cached_list_with_a_bad_signature_is_ignored_fail_open()
    {
        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var rootPub = PackageCrypto.ExportPublicKey(root);
        WriteCachedList(root, "2026-02-01T00:00:00Z", hashes: ["badhash"]);
        File.WriteAllText(Path.Combine(_dir, "revocations-cache.sig"), "not-a-valid-signature");

        var store = new RevocationStore(rootPub, _dir);

        store.IsRevoked("badhash", "p", "i", isFirstParty: false).Should().BeNull();
    }

    private void WriteCachedList(ECDsa root, string issuedUtc, string[] hashes)
    {
        var list = new RevocationList
        {
            IssuedUtc = issuedUtc,
            Revoked = new RevokedSets { ManifestHashes = hashes },
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(list, RevocationJsonContext.Default.RevocationList);
        File.WriteAllBytes(Path.Combine(_dir, "revocations-cache.json"), bytes);
        File.WriteAllText(Path.Combine(_dir, "revocations-cache.sig"), PackageCrypto.Sign(root, bytes));
    }
}
