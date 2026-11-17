using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.IDE;

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

    [Fact]
    public void InterruptedRefreshPreservesPreviouslyCachedListOnRestart()
    {
        using var root = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = PackageCrypto.ExportPublicKey(root);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new RevocationList
        {
            IssuedUtc = "2090-01-01T00:00:00Z",
            Revoked = new RevokedSets { ManifestHashes = ["previous"] },
        }, RevocationJsonContext.Default.RevocationList);
        var store = new RevocationStore(pub, _dir, (_, _) => (bytes, PackageCrypto.Sign(root, bytes)));
        store.RefreshFromUrl("https://example.invalid/list", TimeSpan.FromSeconds(3));
        new RevocationStore(pub, _dir).IsRevoked("previous", null, null, false).Should().Be("build revoked");

        // Refuse the old second-file write; the old implementation overwrites its JSON first.
        var sigPath = Path.Combine(_dir, "revocations-cache.sig");
        File.WriteAllText(sigPath, PackageCrypto.Sign(root, bytes));
        File.SetAttributes(sigPath, FileAttributes.ReadOnly);
        bytes = JsonSerializer.SerializeToUtf8Bytes(new RevocationList
        {
            IssuedUtc = "2091-01-01T00:00:00Z",
            Revoked = new RevokedSets { ManifestHashes = ["next"] },
        }, RevocationJsonContext.Default.RevocationList);
        InterruptedStream? interrupted = null;
        AtomicFileWriter.OpenStream.Value = path => interrupted = new InterruptedStream(path);
        try { store.RefreshFromUrl("https://example.invalid/list", TimeSpan.FromSeconds(3)); }
        finally
        {
            AtomicFileWriter.OpenStream.Value = null;
            File.SetAttributes(sigPath, FileAttributes.Normal);
        }

        store.IsRevoked("next", null, null, false).Should().Be("build revoked", "cache failure remains fail-open in memory");
        new RevocationStore(pub, _dir).IsRevoked("previous", null, null, false).Should().Be("build revoked");
        interrupted.Should().NotBeNull();
        interrupted!.WasInterrupted.Should().BeTrue();
    }

    private sealed class InterruptedStream(string path)
        : FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)
    {
        private bool _jsonWritten;
        public bool WasInterrupted { get; private set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            base.Write(buffer, offset, count);
            _jsonWritten = true;
        }

        public override void WriteByte(byte value)
        {
            if (_jsonWritten)
            {
                WasInterrupted = true;
                throw new IOException("Interrupted between list and signature");
            }
            base.WriteByte(value);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_jsonWritten)
            {
                WasInterrupted = true;
                throw new IOException("Interrupted between list and signature");
            }
            base.Write(buffer);
        }
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
