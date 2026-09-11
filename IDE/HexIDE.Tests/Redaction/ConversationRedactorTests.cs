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

    [Fact]
    public void ALoopbackEndpointKeepsItsHost()
    {
        // Hiding loopback would make every local server look remote, which is a material fact about how it
        // was reached rather than a detail.
        var redactor = Redactor();

        redactor.Endpoint("ws://localhost:5123/lsp").Should().StartWith("ws://localhost:5123");
        redactor.Endpoint("ws://127.0.0.1:5123/lsp").Should().StartWith("ws://127.0.0.1:5123");
    }

    [Fact]
    public void AVb6UriIsLeftAlone()
    {
        // A recorded limit rather than an oversight: the component name is the only handle an author has on
        // which document a message concerned, and a capture where every module is the same word cannot be
        // read against the project it came from.
        const string Uri = "vb6://module/Module1";

        Redactor().Uri(Uri).Should().Be(Uri);
        Redactor().Body($"{{\"uri\":\"{Uri}\"}}").Should().Contain(Uri);
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
