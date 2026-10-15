using HexIDE.Redaction;

namespace HexIDE.Tests.Redaction;

/// <summary>
/// The four surfaces a capture carries something about the machine through.
/// </summary>
/// <remarks>
/// <b>Two kinds of assertion, and both are needed.</b> That nothing real survives is the obvious one. That
/// the structure does survive is the one a redactor fails quietly: a component that replaced every path
/// with one placeholder would pass every leak test here and make the captures useless, at which point
/// somebody turns it off and the leak test stops protecting anybody.
/// </remarks>
public class ConversationRedactorTests
{
    private static ConversationRedactor Redactor(int seed = 11, bool pseudonymise = true) =>
        new(new Pseudonymiser(new Random(seed)), pseudonymise);

    // ── That nothing real survives ───────────────────────────────────────────

    [Fact]
    public void NothingFromTheMachineSurvivesARedactedBody()
    {
        // A realistic didOpen, with a real shape of path. Every part of it that names a person or a
        // project must be gone from the output — checked by searching the whole result, not by inspecting
        // the field this test happened to think of.
        const string Body = """
            {"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":
            {"uri":"file:///C:/Users/quintana/Projects/InvoiceLedger/Form1.frm","languageId":"vb6",
            "version":1,"text":"Option Explicit"}}}
            """;

        var redacted = Redactor().Body(Body);

        redacted.Should().NotContain("quintana");
        redacted.Should().NotContain("Projects");
        redacted.Should().NotContain("InvoiceLedger");
        redacted.Should().NotContain("Users");
    }

    [Fact]
    public void AWorkspaceFolderAndARootUriAgreeWithTheDocumentsInThem()
    {
        // One mapping across every surface, so a reader can see that the root, the folder and the file are
        // the same place. Three independent mappings would make a capture unreadable while leaking nothing,
        // which is the failure mode that gets a redactor switched off.
        var redactor = Redactor();

        var root = redactor.Uri("file:///C:/Users/quintana/Projects/InvoiceLedger");
        var document = redactor.Uri("file:///C:/Users/quintana/Projects/InvoiceLedger/Form1.frm");

        document.Should().StartWith(root + "/");
    }

    [Fact]
    public void ALaunchArgumentCarryingAValueDoesNotSurvive()
    {
        var redacted = Redactor().LaunchArguments(
            ["--stdio", "--token=sk-live-9f2b", "-v", @"D:\work\quintana\config.json", "/nologo"]);

        redacted.Should().Contain("--stdio").And.Contain("-v").And.Contain("/nologo",
            "a bare switch is protocol and says nothing about the machine");
        redacted.Should().NotContain("--token=sk-live-9f2b");
        string.Join(" ", redacted).Should().NotContain("sk-live").And.NotContain("quintana");
    }

    [Fact]
    public void ARemoteEndpointLosesItsHost()
    {
        var redacted = Redactor().Endpoint("ws://build-07.corp.internal:9000/lsp");

        redacted.Should().NotContain("build-07").And.NotContain("corp.internal").And.NotContain("lsp");
        redacted.Should().StartWith("ws://").And.Contain(":9000",
            "the scheme and the port are protocol details worth keeping");
    }

    [Fact]
    public void APipeNameDoesNotSurvive() =>
        Redactor().PipeName("hexide-quintana-invoiceledger").Should().NotContain("quintana");

    // ── That the structure does ──────────────────────────────────────────────

    [Fact]
    public void ADriveLetterKeepsItsCaseSoAMismatchStaysVisible()
    {
        // The defect this whole design is shaped around: a client sending one drive-letter case and a
        // server echoing another was invisible for as long as it existed. After redaction the two must
        // still differ, and differ in the same place they differed before.
        var redactor = Redactor();

        var sent = redactor.Uri("file:///C:/Users/quintana/Form1.frm");
        var echoed = redactor.Uri("file:///c:/Users/quintana/Form1.frm");

        sent.Should().NotBe(echoed, "folding these together is what made the original bug invisible");
        sent.Should().Contain("/C:/");
        echoed.Should().Contain("/c:/");
        sent.Replace("/C:/", "/*/").Should().Be(echoed.Replace("/c:/", "/*/"),
            "and the difference must be ONLY the drive, or the redaction has invented a second one");
    }

    [Fact]
    public void AnExtensionSurvives()
    {
        // Routing is by extension here, by specification, so .cls against .frm is a diagnosis rather than
        // a detail — and an extension is nobody's name.
        var redactor = Redactor();

        redactor.Uri("file:///C:/x/Form1.frm").Should().EndWith(".frm");
        redactor.Uri("file:///C:/x/Class1.cls").Should().EndWith(".cls");
    }

    [Fact]
    public void ADotfileIsANameRatherThanAnExtension() =>
        Redactor().Uri("file:///C:/x/.gitignore").Should().NotContain(".gitignore");

    [Fact]
    public void TheDepthOfAPathSurvives()
    {
        var redactor = Redactor();

        var shallow = redactor.Uri("file:///C:/a/b.frm");
        var deep = redactor.Uri("file:///C:/a/b/c/d/e.frm");

        deep.Count(c => c == '/').Should().BeGreaterThan(shallow.Count(c => c == '/'),
            "how deep a path is can be the whole content of a bug report about path handling");
    }

    [Fact]
    public void ASeparatorComesBackTheWayItWentIn()
    {
        // A path written with backslashes returning with forward ones would be a change to the very thing
        // a reader is examining — and this repository has already paid for confusing the two.
        var redacted = Redactor().LocalPath(@"D:\work\quintana\Projects");

        redacted.Should().Contain(@"\").And.NotContain("/");
    }

    [Fact]
    public void AServersOwnNameSurvivesAndItsLocationDoesNot()
    {
        // Knowing the server was texlab is most of what makes a capture readable. Knowing which folder
        // somebody keeps it in is not.
        var redacted = Redactor().CommandPath(@"D:\work\quintana\tools\texlab.exe");

        redacted.Should().EndWith(@"\texlab.exe");
        redacted.Should().NotContain("quintana").And.NotContain("tools");
    }

    // ── Addresses, which is where this got things wrong ─────────────────────
    //
    // Every case below was measured against the merged implementation and every one of the first three
    // came back misdescribing what had been configured. That is a worse failure than leaking, because a
    // reader cannot tell a redaction artefact from a configuration mistake — they will go and look for a
    // `file:` URI nobody ever wrote.

    [Fact]
    public void AWindowsPipePathDoesNotBecomeAUri()
    {
        // Measured: this came back as `file://C:/hx-…/hx-….sock`. A Windows path parses as an absolute
        // URI, so rebuilding the answer from `Uri`'s parts invented a scheme and flipped the separators.
        var redacted = Redactor().Endpoint(@"C:\pipe\foo.sock");

        redacted.Should().NotContain("file:", "the configuration never said file:");
        redacted.Should().NotContain("/", "a path written with backslashes must come back with backslashes");
        redacted.Should().StartWith(@"C:\", "a drive letter is not somebody's name");
        redacted.Should().EndWith(".sock").And.NotContain("pipe").And.NotContain("foo");
    }

    [Fact]
    public void APortIsNeverInventedForAnAddressThatDidNotNameOne()
    {
        // Measured: `ws://host/lsp` came back as `ws://hx-…:80/hx-…`. Uri.Port answers with the scheme's
        // default when none was written, so the record stated a choice the user had not made.
        Redactor().Endpoint("ws://host/lsp").Should().NotContain(":80");
    }

    [Fact]
    public void AWildcardBindSurvivesBecauseItNamesNothing()
    {
        // Measured: both of these were pseudonymised. A wildcard is a material fact about how a server was
        // reached — it accepted a connection on every interface — and it belongs to nobody.
        var redactor = Redactor();

        redactor.Endpoint("ws://0.0.0.0:9000/").Should().Be("ws://0.0.0.0:9000/");
        redactor.Endpoint("ws://[::]:9000/").Should().Be("ws://[::]:9000/");
    }

    [Fact]
    public void AUnixSocketPathKeepsItsShape()
    {
        // A socket address is written as a path, not a URI. Collapsing it to one word loses the thing a
        // reader needs: that two servers were reached through the same runtime directory.
        var redacted = Redactor().Endpoint("/run/user/1000/foo.sock");

        redacted.Should().StartWith("/").And.EndWith(".sock");
        redacted.Count(c => c == '/').Should().Be(4, "the depth of the path is part of the diagnosis");
        redacted.Should().NotContain("run").And.NotContain("1000").And.NotContain("foo");
    }

    [Fact]
    public void AnIpv6LiteralKeepsItsBrackets()
    {
        // The brackets are punctuation the scheme requires, not part of the name, so a pseudonym goes
        // inside them. Pseudonymising them along with the host produces an address that will not parse.
        Redactor().Endpoint("ws://[2001:db8::1]:9000/lsp")
            .Should().MatchRegex(@"^ws://\[hx-[a-z-]+\]:9000/");
    }

    [Fact]
    public void CredentialsInAnAddressAreReplacedRatherThanDropped()
    {
        // They used to vanish, because Uri.Host does not carry them. Safe, and a lie: the record then
        // said an address had no credentials when it had.
        var redacted = Redactor().Endpoint("ws://someone:hunter2@build-07:9000/lsp");

        redacted.Should().NotContain("someone").And.NotContain("hunter2");
        redacted.Should().Contain("@", "that there were credentials is itself worth reporting");
    }

    [Fact]
    public void AQueryStringIsReplacedWholeBecauseATokenLivesThere()
    {
        var redacted = Redactor().Endpoint("wss://gate.example.com/lsp?token=sk-live-9f2b");

        redacted.Should().NotContain("sk-live").And.NotContain("token=");
        redacted.Should().StartWith("wss://").And.Contain("?");
    }

    [Fact]
    public void AnAddressForATransportThatDoesNotExistYetIsHandledAnyway()
    {
        // TCP is not a transport here today. The redactor is address-shaped rather than scheme-listed, so
        // it does not need editing when one arrives — asserted, because "it will probably work" is how a
        // redactor ships a leak on the first day of a new transport.
        var redactor = Redactor();

        redactor.Endpoint("tcp://127.0.0.1:6005").Should().Be("tcp://127.0.0.1:6005");
        redactor.Endpoint("tcp://build-07:6005").Should().StartWith("tcp://hx-").And.EndWith(":6005");
    }

    [Fact]
    public void ALoopbackEndpointKeepsItsHost()
    {
        // Hiding loopback would make every local server look remote, which is a material fact about how it
        // was reached rather than a detail.
        var redactor = Redactor();

        redactor.Endpoint("ws://localhost:5123/lsp").Should().StartWith("ws://localhost:5123");
        redactor.Endpoint("ws://127.0.0.1:5123/lsp").Should().StartWith("ws://127.0.0.1:5123");
    }

    // ── A document with no file yet ──────────────────────────────────

    [Fact]
    public void BothHalvesOfAnUntitledNameAreReplaced()
    {
        // The whole of task 2.10. A document with no file is named untitled:<Project>/<Name>, and since
        // #489 established that a document normally HAS no file until the project is saved, that is the
        // ordinary state of a new document rather than a corner of the format. Both segments are words the
        // developer typed, and neither was being replaced -- this scheme simply did not exist when the
        // redactor was written, and its predecessor was argued as deliberately exempt.
        var redacted = Redactor().Uri("untitled:InvoiceLedger/Form1.frm");

        redacted.Should().NotContain("InvoiceLedger").And.NotContain("Form1");
        redacted.Should().StartWith("untitled:");
    }

    [Fact]
    public void AnUntitledNameIsRedactedInsideABodyToo()
    {
        // The body path is textual and had only `file:` in its expression, so a didOpen for a document with
        // no file went out whole. That is the path every export actually takes.
        var redacted = Redactor().Body(
            """{"method":"textDocument/didOpen","params":{"textDocument":"""
          + """{"uri":"untitled:InvoiceLedger/Form1.frm","text":"Option Explicit"}}}""");

        redacted.Should().NotContain("InvoiceLedger").And.NotContain("Form1");
        redacted.Should().Contain("textDocument/didOpen", "the method is not a name belonging to anybody");
    }

    [Fact]
    public void AnUntitledNameKeepsItsShapeAndItsExtension()
    {
        // The other half of every rule in this file: what survives is what makes a capture readable. Two
        // segments in, two segments out, so a reader can still see that a message concerned a document OF a
        // project -- and `.frm` against `.cls` stays a diagnosis, because routing is by extension.
        var redacted = Redactor().Uri("untitled:InvoiceLedger/Form1.frm");

        // The "was replaced at all" clause first, and it is load-bearing rather than belt-and-braces:
        // without it every assertion below is satisfied by the untouched input, and this test would go on
        // passing with the feature deleted. Checked by deleting it.
        redacted.Should().NotContain("InvoiceLedger").And.NotContain("Form1");

        redacted.Should().EndWith(".frm");
        redacted["untitled:".Length..].Split('/').Should().HaveCount(2);
    }

    [Fact]
    public void TwoDocumentsOfOneProjectShareItsPseudonym()
    {
        // The structural claim, and the one a redactor fails quietly. Replacing each URI whole would pass
        // every leak assertion above and lose the fact that these two documents belong together, which for
        // an untitled: name is the only relationship the wire carries at all.
        var redactor = Redactor();

        var one = redactor.Uri("untitled:InvoiceLedger/Form1.frm");
        var two = redactor.Uri("untitled:InvoiceLedger/Module1.bas");

        var project = one.Split('/')[0];
        project.Should().Be(two.Split('/')[0], "one project is one pseudonym");

        // Again the clause that stops this passing on an untouched input, which it otherwise does: two
        // unredacted URIs share their project segment too, and differ from each other too.
        project.Should().NotBe("untitled:InvoiceLedger");
        one.Should().NotBe(two);
    }

    [Fact]
    public void TwoCapitalisationsOfOneUntitledNameStayTwoStrings()
    {
        // Deliberate, and the same rule the escaping comment states for paths. Two untitled: names are
        // compared case-insensitively by the client, so two spellings reaching the wire is a defect -- and
        // a redactor that folded them together would hide precisely the bug an export is being sent to
        // diagnose.
        var redactor = Redactor();

        var upper = redactor.Uri("untitled:Ledger/Form1.frm");
        var lower = redactor.Uri("untitled:ledger/Form1.frm");

        // Both actually replaced -- without this the assertion below holds for the untouched inputs, which
        // differ from each other by construction and would prove nothing.
        upper.Should().NotContain("edger");
        lower.Should().NotContain("edger");

        upper.Should().NotBe(lower);
    }

    [Fact]
    public void AUriWithAnUnknownSchemeIsLeftAlone()
    {
        // This test used to assert that a `vb6:` URI was left alone, which was a recorded limit of a scheme
        // HexIDE invented for itself: it carried a component name and nothing else, and replacing it made a
        // capture unreadable against the project it came from. That scheme is retired (#273) and the
        // exemption went with it, so the test is retargeted rather than deleted -- the rule it now states
        // is the one that remains true. A scheme this file does not understand may not hold a path at all,
        // so splitting it on slashes would produce a plausible URI meaning something else.
        const string Uri = "jdt://contents/rt.jar/java.lang/String.class";

        Redactor().Uri(Uri).Should().Be(Uri);
    }

    [Fact]
    public void EverythingAroundAUriInABodyIsUntouched()
    {
        var redacted = Redactor().Body(
            """{"method":"textDocument/didOpen","params":{"uri":"file:///C:/x/Form1.frm","version":7}}""");

        redacted.Should().Contain("textDocument/didOpen").And.Contain("\"version\":7");
        redacted.Should().StartWith("{").And.EndWith("}");
    }

    [Fact]
    public void ABodyThisClientCouldNotDecodeIsStillRedacted()
    {
        // Which is exactly when an export is worth having. A structural redactor would have to give up
        // here, and giving up means shipping the path.
        var redacted = Redactor().Body("""{"method":"x","params":{"uri":"file:///C:/quintana/a.frm",,,""");

        redacted.Should().NotContain("quintana");
    }

    [Fact]
    public void TheSameUriInsideABodyAndOnItsOwnGetTheSameName()
    {
        var redactor = Redactor();

        var alone = redactor.Uri("file:///C:/x/Form1.frm");
        var inBody = redactor.Body("""{"uri":"file:///C:/x/Form1.frm"}""");

        inBody.Should().Contain(alone);
    }

    // ── The opt-out ──────────────────────────────────────────────────────────

    [Fact]
    public void TurningItOffChangesNothingAtAll()
    {
        // Strictly non-default, for a developer who wants real values and accepts what that means. The
        // assertion worth having is that it is a true identity rather than a weaker redaction — a
        // half-redacted export is the one nobody can reason about.
        var redactor = Redactor(pseudonymise: false);

        const string Uri = "file:///C:/Users/quintana/Form1.frm";
        redactor.Uri(Uri).Should().Be(Uri);
        redactor.Body($"{{\"uri\":\"{Uri}\"}}").Should().Be($"{{\"uri\":\"{Uri}\"}}");
        redactor.LocalPath(@"D:\work\quintana").Should().Be(@"D:\work\quintana");
        redactor.CommandPath(@"C:\t\texlab.exe").Should().Be(@"C:\t\texlab.exe");
        redactor.PipeName("hexide-quintana").Should().Be("hexide-quintana");
        redactor.Endpoint("ws://build-07:9000/lsp").Should().Be("ws://build-07:9000/lsp");
        redactor.LaunchArguments(["--token=abc"]).Should().Equal(["--token=abc"]);

        redactor.IsPseudonymising.Should().BeFalse(
            "anything exported has to be able to say which of the two it is, or the reader cannot tell");
    }

    [Fact]
    public void ItIsOnWhenNobodySaidOtherwise() =>
        new ConversationRedactor(new Pseudonymiser()).IsPseudonymising.Should().BeTrue();
}
