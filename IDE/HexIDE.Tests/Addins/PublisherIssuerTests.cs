using System.Diagnostics;
using System.Text;
using HexIDE.AddinPacker;

namespace HexIDE.Tests.Addins;

/// <summary>
/// issue-publisher turns an approved request into a signed record on the machine holding the intermediate key (#5322).
/// Signing the wrong thing puts someone's code in-process with full trust under a name users believe, so each way it could
/// be led to sign what wasn't approved, or to issue what someone else holds, is refused here, and nothing is written then.
/// </summary>
public sealed class PublisherIssuerTests : IDisposable
{
    private readonly string _work = Directory.CreateTempSubdirectory("publisher-issuer-").FullName;
    private DateTimeOffset _listIssued = DateTimeOffset.UtcNow.AddDays(-1);

    public PublisherIssuerTests()
    {
        // A throwaway chain: root.key, hexide-root.pub, intermediate.json/.sig and intermediate.key.
        PackageSigner.GenerateDevTrust(In("chain"));
        Directory.CreateDirectory(In("ledger"));
    }

    public void Dispose() => Directory.Delete(_work, true);

    private string In(params string[] parts) => Path.Combine([_work, .. parts]);

    private PublisherRequestCheck Request(string dir, string id = "acme.tools", string? previousKey = null,
        string gitHub = "acme-dev", string url = "https://acme.example/k", string displayName = "Acme Tools",
        string[]? namespaces = null) =>
        PublisherRequests.Create(id, displayName, namespaces ?? ["acme."], gitHub, url, In(dir + "-keys"), null,
            previousKey, In(dir));

    private string Revocations(params string[] revokedIds)
    {
        var name = "revocations-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(In(name + ".json"), "{\"schema\":1,\"issuedUtc\":\""
            + _listIssued.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)
            + "\",\"revoked\":{\"manifestHashes\":[],\"publisherIds\":[" + string.Join(",", revokedIds.Select(id => $"\"{id}\""))
            + "],\"intermediateIds\":[]}}");
        PackageSigner.SignRevocations(In(name + ".json"), In("chain", "root.key"), In(name));
        return In(name);
    }

    private IssueOutcome Issue(string requestDir, string? sha = null, string? typed = null, string? revocations = null,
        string outName = "out")
    {
        var check = PublisherRequests.Verify(In(requestDir), null, null);
        var request = check.Request ?? PublisherRequests.Verify(In(requestDir), new RequestCheckContext()).Request;
        return PublisherIssuer.Issue(new IssueRequest
        {
            RequestDir = In(requestDir),
            ApprovedSha256 = sha ?? check.Sha256 ?? new string('0', 64),
            RootPublicKeyPath = In("chain", "hexide-root.pub"),
            IntermediateKeyPath = In("chain", "intermediate.key"),
            IntermediateDir = In("chain"),
            LedgerDir = In("ledger"),
            OutDir = In(outName),
            RevocationsJson = revocations is null ? null : Path.Combine(revocations, "revocations.json"),
            RevocationsSig = revocations is null ? null : Path.Combine(revocations, "revocations.sig"),
        }, () => typed ?? request?.PublisherId, TextWriter.Null);
    }

    private void NothingWritten(string outName = "out", string id = "acme.tools")
    {
        Directory.Exists(In(outName)).Should().BeFalse("nothing is written when issuance is refused");
        Directory.Exists(In("ledger", id)).Should().BeFalse();
    }

    // ── Issuing ──────────────────────────────────────────────────

    [Fact]
    public void An_approved_request_is_issued_to_the_publisher_and_the_ledger()
    {
        Request("request");

        Issue("request").ExitCode.Should().Be(0);

        Directory.GetFiles(In("out")).Select(Path.GetFileName).Should().BeEquivalentTo(["publisher.json", "publisher.sig",
            "intermediate.json", "intermediate.sig", "hexide-root.pub", "request.json", "request.sig", "receipt.md"]);
        Directory.GetFiles(In("ledger", "acme.tools")).Select(Path.GetFileName).Should().BeEquivalentTo(["publisher.json",
            "publisher.sig", "intermediate.json", "intermediate.sig", "request.json", "request.sig", "receipt.md"]);
        File.ReadAllText(In("out", "hexide-root.pub")).Should().Be(File.ReadAllText(In("chain", "hexide-root.pub")).Trim());

        // The ledger it wrote is one the next check accepts as genuine, chain and stored request included.
        Request("next", id: "globex", namespaces: ["globex."]);
        PublisherRequests.Verify(In("next"), new RequestCheckContext
        {
            LedgerDir = In("ledger"), RootPublicKey = File.ReadAllText(In("chain", "hexide-root.pub")).Trim(),
        }).Verified.Should().BeTrue();
    }

    // The folder the publisher is sent is their pack --trust folder, which since #5316 must hold the root that signed its
    // intermediate. The request files and receipt.md beside the chain files don't get in the way.
    [Fact]
    public void The_issued_folder_packs_an_add_in_that_verifies_under_the_root()
    {
        Request("request");
        Issue("request").ExitCode.Should().Be(0);
        Directory.CreateDirectory(In("src"));
        File.WriteAllBytes(In("src", "probe.dll"), [1, 2, 3]);

        PackageSigner.Pack(new PackRequest
        {
            SrcDir = In("src"), OutDir = In("package"), Id = "acme.probe", Title = "Probe", Version = "1.0.0",
            Entry = "probe.dll", TrustDir = In("out"), KeyFile = In("request-keys", "acme.tools.key"),
        });

        var result = PackageVerification.Verify(In("package"), File.ReadAllText(In("chain", "hexide-root.pub")).Trim());
        result.IsVerified.Should().BeTrue(result.Error);
        result.PublisherId.Should().Be("acme.tools");
        result.PublisherNamespaces.Should().Equal("acme.");
        File.ReadAllBytes(In("package", PackageVerification.PackageRootFile))
            .Should().Equal(File.ReadAllBytes(In("out", "hexide-root.pub")));
    }

    [Fact]
    public void A_request_whose_hash_isn_t_the_approved_one_is_refused()
    {
        Request("request");

        var outcome = Issue("request", sha: new string('a', 64));

        outcome.ExitCode.Should().Be(2);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain("not the one approved");
        NothingWritten();
    }

    [Theory]
    [InlineData("acme.tool")]
    [InlineData(" acme.tools")]
    [InlineData("ACME.TOOLS")]
    [InlineData("")]
    public void Nothing_is_signed_unless_the_publisher_id_is_typed_back_exactly(string typed)
    {
        Request("request");

        Issue("request", typed: typed).ExitCode.Should().Be(2);

        NothingWritten();
    }

    [Fact]
    public void A_typed_id_with_its_line_ending_is_accepted()
    {
        Request("request");

        Issue("request", typed: "acme.tools\r").ExitCode.Should().Be(0);
    }

    [Fact]
    public void An_id_the_ledger_already_holds_is_never_issued_again()
    {
        Request("first");
        Issue("first").ExitCode.Should().Be(0);
        Request("second", namespaces: ["other."]);

        Issue("second", outName: "out2").ExitCode.Should().Be(2);

        Directory.Exists(In("out2")).Should().BeFalse();
    }

    [Theory]
    [InlineData("record")]
    [InlineData("stored request")]
    [InlineData("retired gap")]
    public void A_ledger_that_doesn_t_verify_refuses_rather_than_being_trusted(string fault)
    {
        Request("first");
        Issue("first").ExitCode.Should().Be(0);
        var entry = In("ledger", "acme.tools");
        switch (fault)
        {
            case "record":
                File.WriteAllText(Path.Combine(entry, "publisher.json"),
                    File.ReadAllText(Path.Combine(entry, "publisher.json")).Replace("Acme Tools", "Acme Toolz"));
                break;
            case "stored request":
                File.WriteAllText(Path.Combine(entry, "request.json"),
                    File.ReadAllText(Path.Combine(entry, "request.json")).Replace("acme-dev", "acme-dew"));
                break;
            default:
                Directory.CreateDirectory(Path.Combine(entry, "retired", "2"));
                break;
        }
        Request("second", id: "globex", namespaces: ["globex."]);

        var outcome = Issue("second", outName: "out2");

        outcome.ExitCode.Should().Be(2);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain(fault switch
        {
            "record" => "didn't sign",
            "stored request" => "isn't the one its record was issued from",
            _ => "numbered 1, 2, 3",
        });
        Directory.Exists(In("out2")).Should().BeFalse();
    }

    // ── Rotation ─────────────────────────────────────────────────

    private string IssueFirst()
    {
        Request("first");
        Issue("first").ExitCode.Should().Be(0);
        return In("first-keys", "acme.tools.key");
    }

    [Fact]
    public void A_rotation_retires_the_old_record_and_its_key_stays_held()
    {
        var old = IssueFirst();
        var firstRecord = File.ReadAllBytes(In("ledger", "acme.tools", "publisher.json"));
        Request("rotation", previousKey: old);

        Issue("rotation", revocations: Revocations(), outName: "out2").ExitCode.Should().Be(0);

        File.ReadAllBytes(In("ledger", "acme.tools", "retired", "1", "publisher.json")).Should().Equal(firstRecord);
        File.Exists(In("ledger", "acme.tools", "request.prev.sig")).Should().BeTrue();
        // The retired key is held: nobody can have it certified under another id.
        Directory.CreateDirectory(In("thief"));
        PublisherRequests.Create("thief.tools", "Thief", ["thief."], "thief", "https://thief.example/k", null, old, null,
            In("thief-request"));
        PublisherRequests.Verify(In("thief-request"), new RequestCheckContext
        {
            LedgerDir = In("ledger"), RootPublicKey = File.ReadAllText(In("chain", "hexide-root.pub")).Trim(),
        }).Refusals.Should().ContainSingle().Which.Should().Contain("retired");
    }

    [Fact]
    public void A_rotation_without_the_revocation_list_is_refused()
    {
        Request("rotation", previousKey: IssueFirst());

        var outcome = Issue("rotation", outName: "out2");

        outcome.ExitCode.Should().Be(2);
        Directory.Exists(In("out2")).Should().BeFalse();
    }

    [Fact]
    public void A_rotation_checked_against_a_revocation_list_over_a_week_old_is_left_to_a_person()
    {
        Request("rotation", previousKey: IssueFirst());
        _listIssued = DateTimeOffset.UtcNow.AddDays(-8);

        var outcome = Issue("rotation", revocations: Revocations(), outName: "out2");

        outcome.ExitCode.Should().Be(3);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain("more than 7 days old");
        Directory.Exists(In("out2")).Should().BeFalse();
    }

    [Theory]
    [InlineData("github")]
    [InlineData("url")]
    public void A_rotation_from_another_handle_or_address_is_left_to_a_person(string changed)
    {
        var old = IssueFirst();
        Request("rotation", previousKey: old, gitHub: changed == "github" ? "someone-else" : "acme-dev",
            url: changed == "url" ? "https://elsewhere.example/k" : "https://acme.example/k");

        Issue("rotation", revocations: Revocations(), outName: "out2").ExitCode.Should().Be(3);

        Directory.Exists(In("out2")).Should().BeFalse();
    }

    [Fact]
    public void A_rotation_of_an_id_issued_by_hand_without_a_stored_request_is_left_to_a_person()
    {
        var old = IssueFirst();
        File.Delete(In("ledger", "acme.tools", "request.json"));
        File.Delete(In("ledger", "acme.tools", "request.sig"));
        Request("rotation", previousKey: old);

        Issue("rotation", revocations: Revocations(), outName: "out2").ExitCode.Should().Be(3);
    }

    [Fact]
    public void A_rotation_away_from_a_revoked_publisher_is_refused()
    {
        Request("rotation", previousKey: IssueFirst());

        var outcome = Issue("rotation", revocations: Revocations("acme.tools"), outName: "out2");

        outcome.ExitCode.Should().Be(2);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain("revoked publisher");
    }

    // ── What is written, and in what order (#5678) ───────────────

    // The ledger is written before --out, so a record that reached the publisher is always one whose id and key are held.
    [Fact]
    public void When_out_can_t_be_written_the_ledger_already_holds_the_record()
    {
        Request("request");
        // A folder where publisher.json would go: --out passes every check, then can't be written.
        Directory.CreateDirectory(In("out", "publisher.json"));

        var failure = FluentActions.Invoking(() => Issue("request")).Should().Throw<IOException>().Which;

        failure.Message.Should().Contain("added to the ledger as acme.tools").And.Contain(In("ledger", "acme.tools"));
        File.Exists(In("out", "publisher.sig")).Should().BeFalse("nothing else is left in --out");
        Request("again");
        PublisherRequests.Verify(In("again"), new RequestCheckContext
        {
            LedgerDir = In("ledger"), RootPublicKey = File.ReadAllText(In("chain", "hexide-root.pub")).Trim(),
        }).Refusals.Should().ContainSingle().Which.Should().Contain("already has a publisher with this id");
    }

    // The ledger is committed and --out goes to the publisher: a folder in the ledger would read as an entry of its own.
    [Theory]
    [InlineData("ledger")]
    [InlineData("ledger/handover")]
    [InlineData("ledger/acme.tools")]
    public void An_out_folder_in_the_ledger_is_refused_before_anything_is_signed(string outName)
    {
        Request("request");

        var outcome = Issue("request", outName: Path.Combine(outName.Split('/')));

        outcome.ExitCode.Should().Be(2);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain("inside --ledger");
        Directory.EnumerateFileSystemEntries(In("ledger")).Should().BeEmpty("nothing is written to the ledger or to --out");
    }

    // A revocation list that can't be checked refuses, as verify-request's does, rather than throwing. These refused before
    // #5678 too, because PackageCrypto.Verify answers false for them; the theory pins the outcome, not the catch.
    [Theory]
    [InlineData("not base64!")]
    [InlineData("")]
    [InlineData("AQID")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")]
    public void A_revocation_list_whose_signature_is_malformed_is_refused(string signature)
    {
        Request("request");
        var list = Revocations();
        File.WriteAllText(Path.Combine(list, "revocations.sig"), signature);

        var outcome = Issue("request", revocations: list);

        outcome.ExitCode.Should().Be(2);
        outcome.Reasons.Should().ContainSingle().Which.Should().Contain("revocation list doesn't verify");
        NothingWritten();
    }

    // ── The receipt ──────────────────────────────────────────────

    [Fact]
    public void Request_text_in_the_receipt_can_t_render_as_a_link_or_HTML()
    {
        Request("request", displayName: "Acme ``` <img src=x> [x](https://evil.example)");

        Issue("request").ExitCode.Should().Be(0);

        var receipt = File.ReadAllText(In("out", "receipt.md"));
        var fenceStart = receipt.IndexOf("````text\n", StringComparison.Ordinal);
        fenceStart.Should().BePositive("a fence longer than the name's own run of backticks");
        var fenceEnd = receipt.IndexOf("\n````\n", fenceStart, StringComparison.Ordinal);
        var inside = receipt[fenceStart..fenceEnd];
        inside.Should().Contain("<img src=x>").And.Contain("[x](https://evil.example)");
        receipt.Remove(fenceStart, fenceEnd - fenceStart).Should().NotContain("<img").And.NotContain("evil.example");
        receipt.Should().Contain(PackageCrypto.KeyFingerprint(File.ReadAllText(In("chain", "hexide-root.pub")).Trim()));
    }

    // ── The command line ─────────────────────────────────────────

    [Fact]
    public void Issue_publisher_refuses_input_that_isn_t_a_terminal()
    {
        Request("request");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = _work,
        };
        start.ArgumentList.Add(typeof(PackerCli).Assembly.Location);
        foreach (var arg in new[] { "issue-publisher", "--request", In("request"), "--approved-sha256", new string('0', 64),
                     "--root-public-key", In("chain", "hexide-root.pub"), "--intermediate-key", In("chain", "intermediate.key"),
                     "--intermediate", In("chain"), "--ledger", In("ledger"), "--out", In("out") })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardInput.Write("acme.tools\n");
        process.StandardInput.Close();
        var error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(2);
        error.Should().Contain("input isn't a terminal");
        NothingWritten();
    }
}
