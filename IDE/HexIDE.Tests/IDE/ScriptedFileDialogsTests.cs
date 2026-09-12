using HexIDE.IDE;

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
}
