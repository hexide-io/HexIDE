using System;
using System.IO;
using HexIDE.Addins;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Unit-tests the first-load consent store: decisions are keyed by manifest hash, persist across
/// reloads, and a new hash (an updated build) has no matching decision so the user is re-prompted.
/// </summary>
public sealed class ConsentStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "hexide_consent_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void Unknown_hash_has_no_decision()
    {
        new ConsentStore(_path).GetDecision("hash-never-seen").Should().BeNull();
    }

    [Fact]
    public void Records_and_persists_an_allow()
    {
        new ConsentStore(_path).Record("com.x", "pub_x", "hashA", ConsentDecisions.Allow);

        // A fresh store reading the same file sees the persisted decision.
        new ConsentStore(_path).GetDecision("hashA").Should().Be(ConsentDecisions.Allow);
    }

    [Fact]
    public void Records_a_block()
    {
        var store = new ConsentStore(_path);
        store.Record("com.x", "pub_x", "hashB", ConsentDecisions.Block);

        store.GetDecision("hashB").Should().Be(ConsentDecisions.Block);
    }

    [Fact]
    public void A_new_manifest_hash_is_not_matched_so_an_update_reprompts()
    {
        var store = new ConsentStore(_path);
        store.Record("com.x", "pub_x", "v1hash", ConsentDecisions.Allow);

        store.GetDecision("v1hash").Should().Be(ConsentDecisions.Allow);
        store.GetDecision("v2hash").Should().BeNull();   // updated build → no record → re-prompt
    }

    [Fact]
    public void Revoke_drops_the_decision_and_persists()
    {
        var store = new ConsentStore(_path);
        store.Record("com.x", "pub_x", "hashC", ConsentDecisions.Allow);

        store.Revoke("hashC");

        store.GetDecision("hashC").Should().BeNull();
        new ConsentStore(_path).GetDecision("hashC").Should().BeNull();   // persisted removal
    }

    [Fact]
    public void Corrupt_file_fails_safe_to_empty()
    {
        File.WriteAllText(_path, "{ this is not valid json ");

        // No throw; behaves as if nothing was ever consented (everything re-prompts).
        new ConsentStore(_path).GetDecision("anything").Should().BeNull();
    }

    [Fact]
    public void Unknown_schema_is_ignored()
    {
        File.WriteAllText(_path,
            "{ \"schema\": 99, \"consents\": [ { \"manifestHash\": \"h\", \"decision\": \"allow\" } ] }");

        new ConsentStore(_path).GetDecision("h").Should().BeNull();
    }
}
