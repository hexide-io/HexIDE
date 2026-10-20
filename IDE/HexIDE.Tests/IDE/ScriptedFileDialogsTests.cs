using HexIDE.IDE;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.IDE;

/// <summary>
/// The seam that lets automation drive a flow ending in a native file dialog.
/// </summary>
/// <remarks>
/// <b>Single-shot is the property that matters, and it is the one worth a test.</b> A sticky answer would
/// silently redirect the next unrelated save — a script that arms a path and then takes a different branch
/// would leave a live override behind it, and the damage would show up somewhere else entirely. Everything
/// else here is a queue.
/// </remarks>
public class ScriptedFileDialogsTests
{
    public ScriptedFileDialogsTests() => ScriptedFileDialogs.Clear();

    [Fact]
    public void NothingArmedMeansTheRealDialogIsShown()
    {
        ScriptedFileDialogs.TryTake(out _).Should().BeFalse(
            "the seam must be inert until something arms it, or a shipped-looking build behaves oddly");
    }

    [Fact]
    public void AnArmedAnswerIsSpentByOneDialogAndNoMore()
    {
        ScriptedFileDialogs.AnswerNextWith(@"C:\x\y.jsonl");

        ScriptedFileDialogs.TryTake(out var first).Should().BeTrue();
        first.Should().Be(@"C:\x\y.jsonl");

        ScriptedFileDialogs.TryTake(out _).Should().BeFalse(
            "a standing override would redirect a later save nobody armed it for");
    }

    [Fact]
    public void AnEmptyPathAnswersAsACancelledDialog()
    {
        // A distinct path through every save flow, and the one least likely to have been exercised by
        // hand — so it has to be expressible.
        ScriptedFileDialogs.AnswerNextWith("   ");

        ScriptedFileDialogs.TryTake(out var answer).Should().BeTrue();
        answer.Should().BeNull();
    }

    [Fact]
    public void AnswersAreSpentInTheOrderTheyWereArmed()
    {
        ScriptedFileDialogs.AnswerNextWith("first");
        ScriptedFileDialogs.AnswerNextWith("second");

        ScriptedFileDialogs.Pending.Should().Be(2);
        ScriptedFileDialogs.TryTake(out var a);
        ScriptedFileDialogs.TryTake(out var b);

        a.Should().Be("first");
        b.Should().Be("second");
    }

    [Fact]
    public void ClearingSaysHowMuchWasStillArmed()
    {
        // How a script finds out that a step it believed opened a picker did not. Silence there is how a
        // leftover answer reaches an unrelated dialog.
        ScriptedFileDialogs.AnswerNextWith("one");
        ScriptedFileDialogs.AnswerNextWith("two");

        ScriptedFileDialogs.Clear().Should().Be(2);
        ScriptedFileDialogs.Pending.Should().Be(0);
    }

    [Fact]
    public void ADocumentWithNoFileAndNothingArmedWouldShowThePicker()
    {
        ScriptedFileDialogs.WouldShowPicker(null).Should().BeTrue(
            "that save opens a modal native picker, which stops the automation server answering (#514)");
    }

    [Fact]
    public void ADocumentWithAFileNeverShowsThePicker()
    {
        ScriptedFileDialogs.WouldShowPicker(@"C:\p\Form1.frm").Should().BeFalse();
    }

    [Fact]
    public void AnArmedAnswerMakesTheSaveSafeToAttemptWithoutSpendingIt()
    {
        // An armed cancel counts, because answering the picker as cancelled is how a caller keeps a
        // document without a file while testing that state.
        ScriptedFileDialogs.AnswerNextWith(null);

        ScriptedFileDialogs.WouldShowPicker(null).Should().BeFalse();
        ScriptedFileDialogs.Pending.Should().Be(1, "asking must not spend the answer the save is about to take");
    }

    // ── Whether a save was the armed cancel the tools offer (#755) ─────────────────────────────────────────

    [Fact]
    public void A_save_of_a_document_without_a_file_that_takes_the_armed_cancel_was_cancelled()
    {
        ScriptedFileDialogs.AnswerNextWith(null);
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);
        watch.Cancelled.Should().BeFalse("nothing has taken the answer yet");

        ScriptedFileDialogs.TryTake(out _);   // the save's picker

        watch.Cancelled.Should().BeTrue();
    }

    [Fact]
    public void A_save_refused_before_its_picker_was_not_cancelled()
    {
        // A save can stop before it asks for a path, for a document HexIDE cannot reproduce. It takes no answer,
        // so the armed cancel is still waiting, and the save failed for another reason.
        ScriptedFileDialogs.AnswerNextWith(null);
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);

        watch.Cancelled.Should().BeFalse();
        ScriptedFileDialogs.Pending.Should().Be(1, "watching must not spend the answer either");
    }

    [Fact]
    public void A_save_answered_with_an_armed_path_was_not_cancelled()
    {
        ScriptedFileDialogs.AnswerNextWith(@"C:\x\Form1.frm");
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);

        ScriptedFileDialogs.TryTake(out _);

        watch.Cancelled.Should().BeFalse("an armed path is a save, and a failure after it is a real failure");
    }

    [Fact]
    public void A_document_with_a_file_was_not_cancelled_even_if_a_cancel_was_armed_and_taken()
    {
        // It saves without a picker. A cancel something else takes in the meantime is not its outcome.
        ScriptedFileDialogs.AnswerNextWith(null);
        var watch = ScriptedFileDialogs.WatchForCancel(@"C:\x\Form1.frm");

        ScriptedFileDialogs.TryTake(out _);

        watch.Cancelled.Should().BeFalse();
    }

    [Fact]
    public void With_nothing_armed_a_save_was_not_cancelled()
    {
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);

        ScriptedFileDialogs.TryTake(out _).Should().BeFalse();

        watch.Cancelled.Should().BeFalse();
    }

    // ── An answer a save left armed (#810) ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_save_that_needed_no_picker_says_the_answer_armed_for_it_is_still_pending()
    {
        ScriptedFileDialogs.AnswerNextWith(null);
        var watch = ScriptedFileDialogs.WatchForCancel(@"C:\x\Form2.frm");

        watch.StillArmed.Should().Be(" The armed file-dialog answer is still pending: this save needed no picker, because "
            + "the document already has a file. The next file dialog to open will take it, whatever it is for; "
            + "clear_file_dialog_answers discards it.");
        ScriptedFileDialogs.Pending.Should().Be(1, "saying so must not spend it");
    }

    [Fact]
    public void A_save_that_stopped_before_its_picker_says_why_the_answer_is_still_pending()
    {
        ScriptedFileDialogs.AnswerNextWith(@"C:\x\Form2.frm");
        ScriptedFileDialogs.AnswerNextWith(null);
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);

        watch.StillArmed.Should().StartWith(" All 2 armed file-dialog answers are still pending: this save stopped "
            + "before its picker opened.").And.EndWith("will take the first, whatever it is for; "
            + "clear_file_dialog_answers discards them.");
    }

    [Fact]
    public void A_save_that_took_an_answer_says_nothing_of_the_ones_armed_after_it()
    {
        // Two armed for two saves: the first save took its own, and the second answer is the next save's.
        ScriptedFileDialogs.AnswerNextWith(@"C:\x\Form2.frm");
        ScriptedFileDialogs.AnswerNextWith(@"C:\x\Form3.frm");
        var watch = ScriptedFileDialogs.WatchForCancel(absolutePath: null);

        ScriptedFileDialogs.TryTake(out _);

        watch.StillArmed.Should().BeEmpty();
    }

    [Fact]
    public void With_nothing_armed_a_save_says_nothing_of_answers()
    {
        ScriptedFileDialogs.WatchForCancel(@"C:\x\Form2.frm").StillArmed.Should().BeEmpty();
    }

    [Fact]
    public void Every_tool_that_saves_through_an_armed_answer_says_when_it_left_one()
    {
        var saving = ToolSource.Tools.Where(t => t.Body.Contains("WatchForCancel")).ToList();

        saving.Select(t => t.Name).Should().BeEquivalentTo(["set_file_content", "set_control_property", "add_control"]);
        saving.Where(t => !t.Body.Contains("StillArmed")).Select(t => t.Name).Should().BeEmpty();
    }
}
