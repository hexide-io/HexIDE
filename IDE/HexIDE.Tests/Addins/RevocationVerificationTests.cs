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
}
