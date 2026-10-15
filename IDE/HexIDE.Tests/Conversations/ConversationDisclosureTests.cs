using System.Text;
using HexIDE.Conversations;

namespace HexIDE.Tests.Conversations;

/// <summary>
/// What an export is about to hand somebody, in terms they can weigh.
/// </summary>
/// <remarks>
/// <b>The number that matters is the copy count, and it is the one nobody expects.</b> Full document
/// synchronisation puts the whole file on the wire on every keystroke burst, so a minute of typing is
/// dozens of copies of the same source — and a dialog that reported "sixty messages" would have told the
/// owner of that source nothing at all about what they were attaching to a public issue.
/// </remarks>
public class ConversationDisclosureTests : IAsyncDisposable
{
    private readonly ConversationLog _capture = new();

    public async ValueTask DisposeAsync()
    {
        await _capture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void Sync(string method, string uri, string text, string connectionId = "vb6")
    {
        var body = Encoding.UTF8.GetBytes(
            $$$"""{"jsonrpc":"2.0","method":"{{{method}}}","params":{"textDocument":{"uri":"{{{uri}}}"},"text":"{{{text}}}"}}""");

        _capture.Record(connectionId, ConversationDirection.Sent, ConversationEntryKind.Notification,
            method, null, body.Length, _capture.ShouldKeepBody(connectionId) ? body : null);
    }

    [Fact]
    public async Task AnEmptyRecordDisclosesNothing()
    {
        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Messages.Should().Be(0);
        disclosure.CarriesDocuments.Should().BeFalse();
        disclosure.DocumentSummary().Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatedSynchronisationIsReportedAsCopiesOfOneDocument()
    {
        // The whole reason this exists. Six messages, one file — and it is the "six copies of Form1" that
        // tells somebody what they are about to send.
        _capture.Arm("vb6", true);
        for (var i = 0; i < 6; i++) Sync("textDocument/didChange", "untitled:Ledger/Form1.frm", $"Private Sub A{i}");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Should().ContainSingle();
        disclosure.Documents[0].Document.Should().Be("Form1.frm");
        disclosure.Documents[0].Copies.Should().Be(6);
        disclosure.DocumentSummary().Should().Contain("6 copies of Form1.frm");
    }

    [Fact]
    public async Task TwoDocumentsAreNamedSeparatelyAndOrderedByWeight()
    {
        // Ordered by bytes rather than alphabetically: the reader is deciding whether to send this, and the
        // largest thing in it is the one that decides.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Small.bas", "x");
        Sync("textDocument/didOpen", "untitled:Ledger/Large.bas", new string('y', 2000));

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Select(d => d.Document).Should().Equal(["Large.bas", "Small.bas"]);
        disclosure.DocumentBytes.Should().BeGreaterThan(2000);
    }

    [Fact]
    public async Task ACarriedFileIsNamedByItsFilenameRatherThanItsPath()
    {
        // The name the reader knows it by. The path itself is the redactor's business, not the
        // disclosure's — and a disclosure showing a pseudonym would be unreadable to the one person it is
        // addressed to.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "file:///C:/Repos/Ledger/README.md", "# Ledger");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Should().ContainSingle();
        disclosure.Documents[0].Document.Should().Be("README.md");
    }

    [Fact]
    public async Task TwoDocumentsCalledModule1AreTwoDocuments()
    {
        // #273 task 2.10. The count was keyed on the LAST SEGMENT of the URI, so two projects each holding
        // a Module1 -- or two directories, which is just as ordinary -- became one row whose copy count and
        // byte total belonged to neither of them. A disclosure exists to be weighed, and a number that is
        // the sum of two unrelated things is worse than no number: the reader sees one modest file and
        // sends two.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Module1.bas", "a");
        Sync("textDocument/didOpen", "untitled:Payroll/Module1.bas", new string('b', 400));

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Should().HaveCount(2);
        disclosure.Documents.Select(d => d.Uri).Should().BeEquivalentTo(
            ["untitled:Ledger/Module1.bas", "untitled:Payroll/Module1.bas"]);
    }

    [Fact]
    public async Task ACollidingNameIsWidenedUntilTheTwoRowsDiffer()
    {
        // The display half of the same change, and the problem it creates. Grouping by URI is correct and
        // would otherwise produce two rows both labelled Module1.bas with different numbers beside them --
        // which reads as a bug in the disclosure rather than as two files.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Module1.bas", "a");
        Sync("textDocument/didOpen", "untitled:Payroll/Module1.bas", new string('b', 400));

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Select(d => d.Document).Should().BeEquivalentTo(
            ["Ledger/Module1.bas", "Payroll/Module1.bas"]);
    }

    [Fact]
    public async Task ANameThatDoesNotCollideIsNotWidened()
    {
        // The other side of it: widening everything would put a directory in front of every filename, and
        // the person reading this already knows where their own files live.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Module1.bas", "a");
        Sync("textDocument/didOpen", "untitled:Payroll/Payslip.bas", "b");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Select(d => d.Document).Should().BeEquivalentTo(
            ["Module1.bas", "Payslip.bas"]);
    }

    [Fact]
    public async Task ADocumentIsShownByTheNameItsOwnerTypedNotByItsEncoding()
    {
        // An untitled: name is percent-encoded when it is minted, so a project with a space in it reaches
        // the wire escaped. The grouping key keeps the escaped form because that is what was sent; the
        // label does not, because this is the one surface addressed to the person who named the thing.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Bill%20of%20Fare/Order%20Form.frm", "a");
        Sync("textDocument/didOpen", "untitled:Payroll/Order%20Form.frm", "b");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Select(d => d.Document).Should().BeEquivalentTo(
            ["Bill of Fare/Order Form.frm", "Payroll/Order Form.frm"]);
        disclosure.Documents.Select(d => d.Uri).Should().AllSatisfy(u => u.Should().Contain("%20"));
    }

    [Fact]
    public async Task AMessageThatCarriesNoDocumentTextIsNotCountedAsSource()
    {
        // A request that names a document is not a copy of it. Counting hovers as source would inflate the
        // one number the reader is trying to weigh.
        _capture.Arm("vb6", true);
        Sync("textDocument/hover", "untitled:Ledger/Form1.frm", "");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Messages.Should().Be(1);
        disclosure.Documents.Should().BeEmpty();
        disclosure.CarriesDocuments.Should().BeFalse();
    }

    [Fact]
    public async Task AMessageWithNoRetainedBodyDisclosesNothingAndSaysSo()
    {
        // It still gets a line in the export, so the count has to be stated — otherwise the other numbers
        // read as the whole story.
        _capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didChange", null, 900, null);

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Messages.Should().Be(1);
        disclosure.MessagesWithNoBody.Should().Be(1);
        disclosure.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task ABodyThatCannotBeReadIsCountedAsUnattributedRatherThanGuessedAt()
    {
        // A disclosure that invents a filename is worse than one that admits it does not know which file
        // this was.
        _capture.Arm("vb6", true);
        var body = Encoding.UTF8.GetBytes("{this is not json");
        _capture.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didChange", null, body.Length, body);

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.UnattributedMessages.Should().Be(1);
        disclosure.Documents.Should().BeEmpty();
        disclosure.CarriesDocuments.Should().BeTrue("something of the reader's did go out, unidentified");
    }

    [Fact]
    public async Task OneConnectionCanBeDisclosedWithoutTheOther()
    {
        // The window exports what the filter shows, so the disclosure has to describe the same slice or it
        // is describing a different file from the one being written.
        _capture.Arm("vb6", true);
        _capture.Arm("latex", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Form1.frm", "Private Sub A");
        Sync("textDocument/didOpen", "file:///paper.tex", "\\\\documentclass", connectionId: "latex");

        var disclosure = await ConversationDisclosure.OfAsync(_capture, "latex");

        disclosure.Documents.Should().ContainSingle();
        disclosure.Documents[0].Document.Should().Be("paper.tex");
    }

    [Fact]
    public async Task ATruncatedBodyDisclosesWhatCrossedTheWireNotWhatWasKept()
    {
        // The disclosure is about what was sent. Reporting the retained size would understate it by
        // exactly the amount the frame cap discarded, which is the wrong direction for this number to err.
        await using var small = new ConversationLog(new CaptureLimits(FrameBytes: 1024));
        small.Arm("vb6", true);

        var text = new string('z', 4000);
        var body = Encoding.UTF8.GetBytes(
            $$$"""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"untitled:Ledger/Big.bas"},"text":"{{{text}}}"}}""");
        small.Record("vb6", ConversationDirection.Sent, ConversationEntryKind.Notification,
            "textDocument/didOpen", null, body.Length, body);

        var disclosure = await ConversationDisclosure.OfAsync(small);

        disclosure.Documents.Should().ContainSingle();
        disclosure.Documents[0].Bytes.Should().Be(body.Length);
    }

    [Fact]
    public async Task OneDocumentIsNotSizedTwice()
    {
        // The sentence around this list already gives the total. Repeating it beside the only entry reads
        // as two numbers that happen to agree, which is a reason to distrust both.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Form1.frm", "Private Sub A");

        var summary = (await ConversationDisclosure.OfAsync(_capture)).DocumentSummary();

        summary.Should().Be("Form1.frm");
    }

    [Fact]
    public async Task SeveralDocumentsAreEachSizedBecauseNoTotalCanSayWhichIsLarge()
    {
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "untitled:Ledger/Form1.frm", "a");
        Sync("textDocument/didOpen", "untitled:Ledger/Module1.bas", new string('b', 500));

        var summary = (await ConversationDisclosure.OfAsync(_capture)).DocumentSummary();

        summary.Should().Contain("Module1.bas (").And.Contain("Form1.frm (");
    }

    [Fact]
    public void ByteCountsAreRenderedTheWayAPersonReadsThem()
    {
        ConversationDisclosure.Bytes(512).Should().Be("512 B");
        ConversationDisclosure.Bytes(2048).Should().Be("2.0 KB");
        ConversationDisclosure.Bytes(3 * 1024 * 1024).Should().Be("3.0 MB");
    }
}
