using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Which lines a caller naming them by number may mark: the rule behind set_breakpoints, set_bookmarks,
/// run_to_cursor and set_next_statement (hexide-io/HexIDE#273 task 3.12).
/// </summary>
public class MarkLineRulesTests
{
    // A nine-line header (designer block, then the attribute run) and seven lines of code, the last of them
    // the empty line after the final line break: 16 lines in the code window. The code section alone, which is
    // what the range used to be counted in, starts at the attribute run and so has 9.
    private const string FormText =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n" +
        "' last\r\n";

    // The model splits a form after its designer block, so the attribute run is the code's first lines.
    private static readonly CodeWindowText Form =
        new(FormText, FormText.IndexOf("Attribute VB_Name", StringComparison.Ordinal));

    private static string? Breakpoints(params int[] lines) =>
        MarkLineRules.Refusal(Form, "P/frmOrders", lines, [], first: 1, "breakpoint");

    [Fact]
    public void LinesOfCodeAreAccepted() =>
        Breakpoints(10, 12, 13).Should().BeNull();

    [Fact]
    public void ALinePastTheCodeSectionButInTheFileIsAccepted()
    {
        // The defect this task closes on the branch: the range was the code section's, so a breakpoint on a
        // form's last lines, shown and settable in the code window, was refused as "not a line".
        Breakpoints(15, 16).Should().BeNull();
    }

    [Fact]
    public void ALineTheFileDoesNotHaveIsRefusedAndTheReplySaysHowLinesAreCounted()
    {
        var refusal = Breakpoints(12, 17);

        refusal.Should().Contain("17 is not a line of P/frmOrders, which has 16 lines")
            .And.Contain("numbered 1..16")
            .And.Contain("from the top of the file")
            .And.EndWith("Nothing was changed.");
    }

    [Fact]
    public void AHeaderLineIsRefusedAndTheReplySaysWhereTheCodeStarts()
    {
        var refusal = Breakpoints(3, 12);

        refusal.Should().StartWith("3 is in a read-only part of P/frmOrders")
            .And.Contain("its header (1..9)")
            .And.Contain("its code starts at 10")
            .And.EndWith("Nothing was changed.");
    }

    [Fact]
    public void ARunToCursorTargetIsToldExecutionCannotStopThere()
    {
        // run_to_cursor and set_next_statement ask for a "line"; "no line can go there" would say nothing.
        MarkLineRules.Refusal(Form, "P/frmOrders", [3], [], first: 1, "line")
            .Should().Contain("so execution can never stop there").And.NotContain("no line");
    }

    [Fact]
    public void ALineOfTheAttributeRunIsRefusedLikeTheDesignerBlock() =>
        Breakpoints(9).Should().StartWith("9 is in a read-only part");

    [Fact]
    public void ARefusalListsEveryOffendingLineOnce() =>
        Breakpoints(4, 2, 4).Should().StartWith("2, 4 are in a read-only part");

    [Fact]
    public void AReadOnlyLineAlreadyMarkedIsLetThrough()
    {
        // An edit or a reload can leave one there, since a mark does not follow its line (debugger-vb6-divergences
        // D24). A caller that reads a document's marks and writes the same set back must not be refused for it.
        MarkLineRules.Refusal(Form, "P/frmOrders", [3, 12], held: [3], first: 1, "breakpoint").Should().BeNull();
    }

    [Fact]
    public void ADocumentWithNoHeaderDoesNotTalkAboutOne()
    {
        var text = new CodeWindowText(
            "Public Function Total() As Currency\r\nAttribute Total.VB_Description = \"x\"\r\nEnd Function\r\n", 0);

        var refusal = MarkLineRules.Refusal(text, "P/Order", [2], [], first: 1, "breakpoint");

        refusal.Should().Contain("the Attribute lines that describe a member")
            .And.NotContain("header").And.NotContain("code starts");
    }

    [Fact]
    public void TheSidecarsRangeIsTheWholeFile()
    {
        var (kept, dropped) = MarkLineRules.Within(Form, [0, 1, 3, 16, 17], first: 1);

        kept.Should().Equal([1, 3, 16], "a header line is kept: an unmigrated sidecar counts from the code");
        dropped.Should().Equal([0, 17]);
    }

    // ── bookmarks, stored from 0, counted from 1 at the tools' edge (#571) ──────────────────────────────────────

    [Fact]
    public void A_bookmark_is_shown_one_higher_than_stored_and_stored_one_lower_than_sent()
    {
        MarkLineRules.BookmarksShown([0, 9, 12]).Should().Equal(1, 10, 13);
        MarkLineRules.BookmarksStored([1, 10, 13]).Should().Equal(0, 9, 12);
    }

    [Fact]
    public void A_bookmark_takes_the_line_number_a_breakpoint_takes()
    {
        // Line 10 is the first line of code. Counting from 0, set_bookmarks took 9, the last Attribute line, and
        // was refused; or it took 10 and bookmarked the blank line below the code a breakpoint at 10 marks.
        string? Bookmarks(params int[] lines) =>
            MarkLineRules.Refusal(Form, "P/frmOrders", lines, [], first: 1, "bookmark");

        Bookmarks(10).Should().BeNull().And.Be(Breakpoints(10));
        Bookmarks(9).Should().Contain("its code starts at 10");
        Bookmarks(0).Should().Contain("bookmarks are numbered 1..16").And.NotContain("counting from 0");
        MarkLineRules.BookmarksStored([10]).Should().Equal([9], "the store's 9 is the gutter's 10");
    }


    [Fact]
    public void A_bookmark_held_on_the_header_can_be_written_back_as_read()
    {
        // A sidecar from before the header was protected can hold one there: stored 2, the gutter's 3. Reading the
        // set and writing it back is never refused; compared in two numberings, the store's 2 is not the caller's 3,
        // and the 3 was refused as a new bookmark on the header.
        var held = new[] { 2 };
        var read = MarkLineRules.BookmarksShown(held);

        var (refusal, stored) = MarkLineRules.BookmarkRequest(Form, "P/frmOrders", read, held);
        refusal.Should().BeNull();
        stored.Should().Equal(2);
    }

    [Fact]
    public void A_bookmark_request_is_judged_and_stored_in_the_two_numberings()
    {
        var (refusal, stored) = MarkLineRules.BookmarkRequest(Form, "P/frmOrders", [10, 13], []);
        refusal.Should().BeNull();
        stored.Should().Equal(9, 12);

        MarkLineRules.BookmarkRequest(Form, "P/frmOrders", [3], []).Refusal.Should().Contain("its code starts at 10");
        MarkLineRules.BookmarkRequest(Form, "P/frmOrders", [17], []).Refusal.Should().Contain("numbered 1..16");
        MarkLineRules.BookmarkRequest(Form, "P/frmOrders", [17], []).ToStore.Should().BeEmpty();
    }
}
