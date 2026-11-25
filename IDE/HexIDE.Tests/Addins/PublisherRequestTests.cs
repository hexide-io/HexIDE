using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HexIDE.AddinPacker;
using HexIDE.AddinPacker.Interactive;

namespace HexIDE.Tests.Addins;

/// <summary>
/// A publisher's signed request for a record (#5321). The request reaches the machine holding the intermediate key, and
/// #5323's Action checks it as it arrives, so verify-request treats it as hostile: each way a request could be forged,
/// stretched or lifted is refused here.
/// </summary>
public sealed class PublisherRequestTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("publisher-request-").FullName;

    public void Dispose() => Directory.Delete(_work, true);

    private string In(params string[] parts) => Path.Combine([_work, .. parts]);

    private PublisherRequestCheck Request(string outName = "request", string? newKey = "keys", string? key = null,
        string? previousKey = null, string id = "acme.tools", string[]? namespaces = null, string displayName = "Acme Tools",
        string gitHub = "acme-dev", string url = "https://acme.example/hexide-key") =>
        PublisherRequests.Create(id, displayName, namespaces ?? ["acme."], gitHub, url,
            newKey is null ? null : In(newKey), key, previousKey, In(outName));

    private string KeyFile(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(In(name), PackageCrypto.ExportPrivateKey(key));
        return In(name);
    }

    private void Ledger(string id, string publicKey, params string[] namespaces)
    {
        Directory.CreateDirectory(In("ledger", id));
        File.WriteAllBytes(In("ledger", id, "publisher.json"), JsonSerializer.SerializeToUtf8Bytes(new PublisherRecord
        {
            PublisherId = id, DisplayName = id, PublicKey = publicKey, IntermediateId = "hexide-marketplace",
            Namespaces = namespaces.Length == 0 ? null : namespaces,
        }, AddinJsonContext.Default.PublisherRecord));
    }

    private static string PublicKeyOf(string privateKeyFile)
    {
        using var key = PackageCrypto.ImportPrivateKey(File.ReadAllText(privateKeyFile).Trim());
        return PackageCrypto.ExportPublicKey(key);
    }

    private static string OtherKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return PackageCrypto.ExportPublicKey(key);
    }

    // Rewrites request.json and signs the new bytes properly, so only the rule under test can refuse it.
    private void Resign(string dir, byte[] bytes, string keyFile, bool prefixed = true)
    {
        File.WriteAllBytes(Path.Combine(dir, "request.json"), bytes);
        using var key = PackageCrypto.ImportPrivateKey(File.ReadAllText(keyFile).Trim());
        File.WriteAllText(Path.Combine(dir, "request.sig"),
            PackageCrypto.Sign(key, prefixed ? PublisherRequests.SignedBytes(bytes) : bytes));
    }

    private static PublisherRequestCheck Verify(string dir, string? ledger = null, string? gitHub = null) =>
        PublisherRequests.Verify(dir, ledger, gitHub);

    // ── Making a request ─────────────────────────────────────────

    [Fact]
    public void A_new_key_and_its_request_are_written_and_verify()
    {
        var made = Request();

        made.Verified.Should().BeTrue();
        File.Exists(In("keys", "acme.tools.key")).Should().BeTrue();
        Directory.GetFiles(In("request")).Select(Path.GetFileName).Should().BeEquivalentTo(["request.json", "request.sig"]);
        made.Request!.PublicKey.Should().Be(PublicKeyOf(In("keys", "acme.tools.key")));
        made.Sha256.Should().Be(PackageCrypto.Sha256Hex(File.ReadAllBytes(In("request", "request.json"))));

        var check = Verify(In("request"));
        check.Verified.Should().BeTrue();
        check.RotationUnchecked.Should().BeFalse();
    }

    [Fact]
    public void The_request_is_one_line_of_ASCII_whatever_the_name_holds()
    {
        Request(displayName: "Acmé Tools 工具").Verified.Should().BeTrue();

        var bytes = File.ReadAllBytes(In("request", "request.json"));
        bytes.Should().OnlyContain(b => b >= 0x20 && b < 0x7F, "a request must survive being pasted into an issue form");
        Verify(In("request")).Request!.DisplayName.Should().Be("Acmé Tools 工具");
    }

    [Fact(Skip = "Unix file modes", SkipWhen = nameof(OnWindows))]
    public void A_new_private_key_is_readable_by_its_owner_only()
    {
        Request();

        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(In("keys", "acme.tools.key")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public void An_existing_key_can_be_used_instead_of_a_new_one()
    {
        var key = KeyFile("mine.key");

        Request(newKey: null, key: key).Request!.PublicKey.Should().Be(PublicKeyOf(key));
    }

    [Theory]
    [InlineData("request")]
    [InlineData("request/inner")]
    public void A_new_key_is_never_written_into_the_request_folder(string keyFolder)
    {
        FluentActions.Invoking(() => Request(newKey: keyFolder)).Should().Throw<ArgumentException>().WithMessage("*--new-key*--out*");

        Directory.Exists(In("request")).Should().BeFalse("nothing is written when the request is refused");
    }

    [Theory]
    [InlineData("Acme", "acme-dev", "https://acme.example/k", "publisher id")]
    [InlineData("acme.tools", "-acme", "https://acme.example/k", "GitHub handle")]
    [InlineData("acme.tools", "acme-dev", "http://acme.example/k", "https address")]
    [InlineData("acme.tools", "acme-dev", "https://user:pw@acme.example/k", "https address")]
    public void A_bad_field_is_refused_before_anything_is_written(string id, string gitHub, string url, string named)
    {
        FluentActions.Invoking(() => Request(id: id, gitHub: gitHub, url: url)).Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain(named);

        Directory.Exists(In("keys")).Should().BeFalse();
        Directory.Exists(In("request")).Should().BeFalse();
    }

    [Fact]
    public void A_namespace_hexide_keeps_is_refused()
    {
        FluentActions.Invoking(() => Request(namespaces: ["hexide.tools."])).Should().Throw<ArgumentException>();
    }

    // ── Checking a request ────────────────────────────────────────

    [Fact]
    public void A_request_edited_after_signing_is_refused()
    {
        Request();
        var path = In("request", "request.json");
        File.WriteAllText(path, File.ReadAllText(path).Replace("Acme Tools", "Acme Toolz"));

        Verify(In("request")).Refusals.Should().ContainSingle().Which.Should().Contain("request.sig is not a signature");
    }

    [Fact]
    public void A_request_signed_by_another_key_is_refused()
    {
        Request();
        Resign(In("request"), File.ReadAllBytes(In("request", "request.json")), KeyFile("other.key"));

        Verify(In("request")).Verified.Should().BeFalse();
    }

    [Fact]
    public void A_signature_over_the_bare_bytes_is_refused_because_it_could_be_a_manifest_signature()
    {
        Request();
        Resign(In("request"), File.ReadAllBytes(In("request", "request.json")), In("keys", "acme.tools.key"), prefixed: false);

        Verify(In("request")).Refusals.Should().ContainSingle().Which.Should().Contain("request.sig is not a signature");
    }

    [Theory]
    [InlineData("indented")]
    [InlineData("trailing newline")]
    [InlineData("byte-order mark")]
    [InlineData("repeated field")]
    [InlineData("reordered fields")]
    public void A_request_not_in_canonical_form_is_refused_though_its_signature_holds(string change)
    {
        Request();
        var json = File.ReadAllText(In("request", "request.json"));
        var bytes = change switch
        {
            "indented" => Encoding.ASCII.GetBytes(JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement,
                new JsonSerializerOptions { WriteIndented = true })),
            "trailing newline" => Encoding.ASCII.GetBytes(json + "\n"),
            "byte-order mark" => [0xEF, 0xBB, 0xBF, .. Encoding.ASCII.GetBytes(json)],
            "repeated field" => Encoding.ASCII.GetBytes(json.Replace("\"kind\":\"new\"", "\"kind\":\"rotation\",\"kind\":\"new\"")),
            _ => Encoding.ASCII.GetBytes(json.Replace("{\"schema\":1,\"kind\":\"new\",", "{\"kind\":\"new\",\"schema\":1,")),
        };
        bytes.Should().NotEqual(Encoding.ASCII.GetBytes(json), "the change under test must change the bytes");
        Resign(In("request"), bytes, In("keys", "acme.tools.key"));

        Verify(In("request")).Verified.Should().BeFalse();
    }

    [Fact]
    public void An_unknown_field_is_refused()
    {
        Request();
        var json = File.ReadAllText(In("request", "request.json"));
        Resign(In("request"), Encoding.ASCII.GetBytes(json.Replace("\"schema\":1,", "\"schema\":1,\"trusted\":true,")),
            In("keys", "acme.tools.key"));

        Verify(In("request")).Refusals.Should().ContainSingle().Which.Should().Contain("missing, unknown or of the wrong type");
    }

    [Fact]
    public void An_oversized_request_is_refused_without_being_parsed()
    {
        Directory.CreateDirectory(In("request"));
        File.WriteAllBytes(In("request", "request.json"), new byte[17 * 1024]);
        File.WriteAllText(In("request", "request.sig"), "x");

        Verify(In("request")).Refusals.Should().ContainSingle().Which.Should().Contain("larger than");
    }

    [Theory]
    [InlineData("acme-dev", true)]
    [InlineData("ACME-DEV", true)]
    [InlineData("someone-else", false)]
    public void The_submitting_account_must_be_the_request_s_own(string submitter, bool accepted)
    {
        Request();

        Verify(In("request"), gitHub: submitter).Verified.Should().Be(accepted);
    }

    // ── Against the ledger ───────────────────────────────────────

    [Fact]
    public void A_request_unique_in_the_ledger_verifies()
    {
        Ledger("globex", OtherKey(), "globex.");
        Request();

        Verify(In("request"), In("ledger")).Verified.Should().BeTrue();
    }

    [Theory]
    [InlineData("id")]
    [InlineData("key")]
    [InlineData("namespace")]
    [InlineData("namespace inside")]
    [InlineData("namespace in another case")]
    public void A_request_for_what_another_publisher_holds_is_refused(string clash)
    {
        Request();
        var key = Verify(In("request")).Request!.PublicKey;
        switch (clash)
        {
            case "id": Ledger("acme.tools", OtherKey()); break;
            case "key": Ledger("globex", key); break;
            case "namespace": Ledger("globex", OtherKey(), "acme."); break;
            case "namespace inside": Ledger("globex", OtherKey(), "acme.sub."); break;
            default: Ledger("globex", OtherKey(), "ACME."); break;
        }

        Verify(In("request"), In("ledger")).Verified.Should().BeFalse();
    }

    [Fact]
    public void A_ledger_entry_that_does_not_parse_refuses_rather_than_being_skipped()
    {
        Directory.CreateDirectory(In("ledger", "broken"));
        File.WriteAllText(In("ledger", "broken", "publisher.json"), "{");
        Request();

        Verify(In("request"), In("ledger")).Refusals.Should().ContainSingle().Which.Should().Contain("doesn't parse");
    }

    // ── Rotation ─────────────────────────────────────────────────

    [Fact]
    public void A_rotation_checked_without_a_ledger_says_so_and_is_not_a_pass()
    {
        var old = KeyFile("old.key");
        Request(previousKey: old).Verified.Should().BeTrue();

        var check = Verify(In("request"));

        check.Verified.Should().BeTrue();
        check.RotationUnchecked.Should().BeTrue();
        check.NotChecked.Should().ContainSingle().Which.Should().Contain("previousPublicKey");
        File.Exists(In("request", "request.prev.sig")).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void A_rotation_verifies_only_when_the_ledger_holds_its_previous_key_for_the_id(bool ledgerHoldsOldKey, bool accepted)
    {
        var old = KeyFile("old.key");
        Ledger("acme.tools", ledgerHoldsOldKey ? PublicKeyOf(old) : OtherKey(), "acme.");
        Request(previousKey: old);

        var check = Verify(In("request"), In("ledger"));

        check.Verified.Should().Be(accepted);
        check.RotationUnchecked.Should().BeFalse();
    }

    [Fact]
    public void An_old_key_s_signature_cannot_be_lifted_onto_another_request()
    {
        var old = KeyFile("old.key");
        Request("first", newKey: "keys1", previousKey: old);
        Request("second", newKey: "keys2", previousKey: old, displayName: "Acme Tools (new)");
        File.Copy(In("first", "request.prev.sig"), In("second", "request.prev.sig"), overwrite: true);

        Verify(In("second")).Refusals.Should().ContainSingle().Which.Should().Contain("request.prev.sig is not a signature");
    }

    [Fact]
    public void A_new_publisher_s_request_with_an_old_key_s_signature_is_refused()
    {
        Request();
        File.WriteAllText(In("request", "request.prev.sig"), "x");

        Verify(In("request")).Verified.Should().BeFalse();
    }

    // ── The command line ─────────────────────────────────────────

    [Fact]
    public void Verify_request_exits_0_when_verified_3_for_an_unchecked_rotation_and_2_when_refused()
    {
        Request("plain", newKey: "keys1");
        Request("rotation", newKey: "keys2", previousKey: KeyFile("old.key"));
        File.WriteAllText(In("plain", "request.sig"), "x");
        Request("good", newKey: "keys3");

        PackerCliRefusalTests.RunPacker(_work, "verify-request", "--request", In("good")).ExitCode.Should().Be(0);
        var rotation = PackerCliRefusalTests.RunPacker(_work, "verify-request", "--request", In("rotation"));
        rotation.ExitCode.Should().Be(3);
        rotation.Output.Should().Contain("Not checked: ");
        var refused = PackerCliRefusalTests.RunPacker(_work, "verify-request", "--request", In("plain"));
        refused.ExitCode.Should().Be(2);
        refused.Error.Should().StartWith("Refused: ");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Request_publisher_takes_exactly_one_of_new_key_and_key(bool newKey, bool key)
    {
        string[] args = ["request-publisher", "--publisher-id", "acme.tools", "--display-name", "Acme", "--github", "acme",
            "--fingerprint-url", "https://acme.example/k", "--out", In("request")];
        if (newKey) args = [.. args, "--new-key", In("keys")];
        if (key) args = [.. args, "--key", KeyFile("mine.key")];

        var run = PackerCliRefusalTests.RunPacker(_work, args);

        run.ExitCode.Should().Be(1);
        run.Error.Should().Contain("exactly one of --new-key");
        Directory.Exists(In("request")).Should().BeFalse();
        Directory.Exists(In("keys")).Should().BeFalse();
    }

    [Fact]
    public void A_repeated_option_is_refused_rather_than_one_copy_being_used()
    {
        var run = PackerCliRefusalTests.RunPacker(_work, "verify-request", "--request", In("a"), "--request", In("b"));

        run.ExitCode.Should().Be(1);
        run.Error.Should().Contain("--request exactly once");
    }

    // ── hexide-pack ──────────────────────────────────────────────

    [Fact]
    public void Hexide_pack_needs_a_key_file_unless_a_new_key_was_asked_for_and_refuses_one_when_it_was()
    {
        var key = PackerCommands.Find("request-publisher")!.Options.Single(o => o.Name == "key");
        var file = KeyFile("mine.key");

        AnswerChecks.NeededWhenSkipped(key, _ => null).Should().NotBeNull();
        AnswerChecks.NeededWhenSkipped(key, name => name == "new-key" ? In("keys") : null).Should().BeNull();
        AnswerChecks.Check(key, file, _ => null).Should().BeNull();
        AnswerChecks.Check(key, file, name => name == "new-key" ? In("keys") : null).Should().Contain("--new-key");
    }
}
