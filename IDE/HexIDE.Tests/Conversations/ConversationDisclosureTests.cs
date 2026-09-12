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
        for (var i = 0; i < 6; i++) Sync("textDocument/didChange", "vb6://Form1", $"Private Sub A{i}");

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Should().ContainSingle();
        disclosure.Documents[0].Document.Should().Be("Form1");
        disclosure.Documents[0].Copies.Should().Be(6);
        disclosure.DocumentSummary().Should().Contain("6 copies of Form1");
    }

    [Fact]
    public async Task TwoDocumentsAreNamedSeparatelyAndOrderedByWeight()
    {
        // Ordered by bytes rather than alphabetically: the reader is deciding whether to send this, and the
        // largest thing in it is the one that decides.
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "vb6://Small", "x");
        Sync("textDocument/didOpen", "vb6://Large", new string('y', 2000));

        var disclosure = await ConversationDisclosure.OfAsync(_capture);

        disclosure.Documents.Select(d => d.Document).Should().Equal(["Large", "Small"]);
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
    public async Task AMessageThatCarriesNoDocumentTextIsNotCountedAsSource()
    {
        // A request that names a document is not a copy of it. Counting hovers as source would inflate the
        // one number the reader is trying to weigh.
        _capture.Arm("vb6", true);
        Sync("textDocument/hover", "vb6://Form1", "");

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
        Sync("textDocument/didOpen", "vb6://Form1", "Private Sub A");
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
            $$$"""{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"vb6://Big"},"text":"{{{text}}}"}}""");
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
        Sync("textDocument/didOpen", "vb6://Form1", "Private Sub A");

        var summary = (await ConversationDisclosure.OfAsync(_capture)).DocumentSummary();

        summary.Should().Be("Form1");
    }

    [Fact]
    public async Task SeveralDocumentsAreEachSizedBecauseNoTotalCanSayWhichIsLarge()
    {
        _capture.Arm("vb6", true);
        Sync("textDocument/didOpen", "vb6://Form1", "a");
        Sync("textDocument/didOpen", "vb6://Module1", new string('b', 500));

        var summary = (await ConversationDisclosure.OfAsync(_capture)).DocumentSummary();

        summary.Should().Contain("Module1 (").And.Contain("Form1 (");
    }

    [Fact]
    public void ByteCountsAreRenderedTheWayAPersonReadsThem()
    {
        ConversationDisclosure.Bytes(512).Should().Be("512 B");
        ConversationDisclosure.Bytes(2048).Should().Be("2.0 KB");
        ConversationDisclosure.Bytes(3 * 1024 * 1024).Should().Be("3.0 MB");
    }
}
