using System.Diagnostics;
using System.Text;
using HexIDE.AddinPacker;

namespace HexIDE.Tests.Addins;

/// <summary>
/// The packer's keyless <c>verify-revocations</c>, which the release workflow runs against hexide.io's list (#4537). A list
/// signed by an ephemeral root verifies; any change to its bytes, a line-ending conversion included, doesn't.
/// </summary>
public sealed class RevocationVerificationTests : IDisposable
{
    // LF only, so that a CRLF copy differs from what was signed.
    private const string List =
        "{\n  \"schema\": 1,\n  \"issuedUtc\": \"2026-02-01T00:00:00Z\",\n  \"revoked\": {\n    \"manifestHashes\": [],\n"
        + "    \"publisherIds\": [\"org.example.revoked\"],\n    \"intermediateIds\": []\n  }\n}\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-revverify-" + Guid.NewGuid().ToString("N"));
    private string Trust => Path.Combine(_root, "trust");
    private string Signed => Path.Combine(_root, "signed");
    private string SignedJson => Path.Combine(Signed, "revocations.json");
    private string SignedSig => Path.Combine(Signed, "revocations.sig");

    public RevocationVerificationTests()
    {
        PackageSigner.GenerateDevTrust(Trust);
        var input = Path.Combine(_root, "input.json");
        File.WriteAllBytes(input, Encoding.UTF8.GetBytes(List));
        PackageSigner.SignRevocations(input, Path.Combine(Trust, "root.key"), Signed);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private static (int ExitCode, string Message) Packer(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(typeof(PackageSigner).Assembly.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result + stderr);
    }

    private (int ExitCode, string Message) Verify(string? json = null, string? root = null) =>
        Packer("verify-revocations", "--json", json ?? SignedJson, "--sig", SignedSig,
            "--root", root ?? Path.Combine(Trust, "hexide-root.pub"));

    [Fact]
    public void A_list_signed_by_the_root_verifies()
    {
        var result = Verify();

        result.ExitCode.Should().Be(0, result.Message);
        result.Message.Should().Contain("Verified").And.Contain("2026-02-01T00:00:00Z");
    }

    [Fact]
    public void A_list_with_one_byte_changed_does_not_verify()
    {
        var bytes = File.ReadAllBytes(SignedJson);
        var at = Array.IndexOf(bytes, (byte)'2');   // the first digit of issuedUtc's year
        bytes[at] = (byte)'3';
        var changed = Path.Combine(_root, "changed.json");
        File.WriteAllBytes(changed, bytes);

        var result = Verify(json: changed);

        result.ExitCode.Should().Be(2, result.Message);
        result.Message.Should().Contain("does NOT verify");
    }

    [Fact]
    public void A_crlf_copy_of_the_list_does_not_verify()
    {
        var crlf = File.ReadAllText(SignedJson).ReplaceLineEndings("\r\n");
        crlf.Should().NotBe(List, "the copy must differ from the signed bytes for this test to mean anything");
        var copy = Path.Combine(_root, "crlf.json");
        File.WriteAllBytes(copy, Encoding.UTF8.GetBytes(crlf));

        var result = Verify(json: copy);

        result.ExitCode.Should().Be(2, result.Message);
        result.Message.Should().Contain("does NOT verify");
    }

    [Fact]
    public void A_list_checked_against_another_root_does_not_verify()
    {
        var other = Path.Combine(_root, "other");
        PackageSigner.GenerateDevTrust(other);

        var result = Verify(root: Path.Combine(other, "hexide-root.pub"));

        result.ExitCode.Should().Be(2, result.Message);
    }

    [Fact]
    public void A_missing_option_is_a_usage_error_not_a_verdict()
    {
        var result = Packer("verify-revocations", "--json", SignedJson, "--sig", SignedSig);

        result.ExitCode.Should().Be(1, result.Message);
        result.Message.Should().Contain("--root");
    }

    // ── A list HexIDE would ignore, wholly or in part (#5315) ────

    private static string WithKeys(string issued, params string[] keys) =>
        "{\"schema\":1,\"issuedUtc\":\"" + issued + "\",\"revoked\":{\"manifestHashes\":[],\"publisherIds\":[],\"intermediateIds\":[],"
        + "\"keyFingerprints\":[" + string.Join(",", keys.Select(key => $"\"{key}\"")) + "]}}";

    private static readonly string Grouped = string.Join(" ", Enumerable.Repeat("A1B2", 16));

    [Fact]
    public void A_list_naming_a_key_signs_and_verifies_with_the_fingerprint_grouped_or_not()
    {
        var input = Path.Combine(_root, "keys.json");
        File.WriteAllText(input, WithKeys("2026-02-01T00:00:00Z", Grouped, Grouped.Replace(" ", "").ToLowerInvariant()));
        PackageSigner.SignRevocations(input, Path.Combine(Trust, "root.key"), Path.Combine(_root, "keys"));

        var result = Packer("verify-revocations", "--json", Path.Combine(_root, "keys", "revocations.json"),
            "--sig", Path.Combine(_root, "keys", "revocations.sig"), "--root", Path.Combine(Trust, "hexide-root.pub"));

        result.ExitCode.Should().Be(0, result.Message);
    }

    // Checked before the root key is read, so the key file named here needn't exist.
    [Theory]
    [InlineData("a fingerprint one digit short", "isn't a key fingerprint")]
    [InlineData("an issue time that isn't one", "isn't a time")]
    [InlineData("JSON that isn't a list", "isn't a revocation list")]
    [InlineData("a set that is null", "is null")]
    public void Sign_revocations_refuses_a_list_HexIDE_would_ignore(string problem, string reason)
    {
        var json = problem switch
        {
            "a fingerprint one digit short" => WithKeys("2026-02-01T00:00:00Z", Grouped[..^1]),
            "an issue time that isn't one" => WithKeys("next Tuesday"),
            "JSON that isn't a list" => "[1, 2, 3]",
            _ => "{\"schema\":1,\"issuedUtc\":\"2026-02-01T00:00:00Z\",\"revoked\":{\"manifestHashes\":null}}",
        };
        var input = Path.Combine(_root, "bad.json");
        File.WriteAllText(input, json);

        var result = Packer("sign-revocations", "--in", input, "--root-key", Path.Combine(_root, "no-such.key"),
            "--out", Path.Combine(_root, "bad"));

        result.ExitCode.Should().Be(1, result.Message);
        result.Message.Should().Contain(reason).And.Contain("is not signed");
        Directory.Exists(Path.Combine(_root, "bad")).Should().BeFalse("nothing is written for a list that isn't signed");
    }

    // A list someone signed by other means is judged the same way when it is checked.
    [Fact]
    public void Verify_revocations_fails_a_signed_list_whose_key_fingerprint_matches_nothing()
    {
        var bytes = Encoding.UTF8.GetBytes(WithKeys("2026-02-01T00:00:00Z", "A1B2"));
        var json = Path.Combine(_root, "short.json");
        File.WriteAllBytes(json, bytes);
        using (var root = PackageCrypto.ImportPrivateKey(File.ReadAllText(Path.Combine(Trust, "root.key")).Trim()))
            File.WriteAllText(Path.Combine(_root, "short.sig"), PackageCrypto.Sign(root, bytes));

        var result = Packer("verify-revocations", "--json", json, "--sig", Path.Combine(_root, "short.sig"),
            "--root", Path.Combine(Trust, "hexide-root.pub"));

        result.ExitCode.Should().Be(2, result.Message);
        result.Message.Should().Contain("would ignore").And.Contain("isn't a key fingerprint");
    }
}
