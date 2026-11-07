using HexIDE.IDE;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Server;

/// <summary>
/// The replies of the tools that run, step, break and stop a program say what it printed with Debug.Print since its run
/// began or a reply last reported it (#2531). A caller had to find the Immediate window's editor and inspect it.
/// </summary>
public class ProgramPrintedTests
{
    [Fact]
    public void What_was_printed_is_taken_once_with_its_line_breaks_as_the_Immediate_window_shows_them()
    {
        var printed = new ProgramPrinted();
        printed.Append("Hello\r\n");
        printed.Append("second; ");

        printed.Take().Should().Be("Hello\nsecond; ");
        printed.Take().Should().BeEmpty("a reply that reported it has taken it");
    }

    [Fact]
    public void A_run_that_begins_starts_afresh_and_one_that_ends_keeps_its_output_for_the_stop_reply()
    {
        var printed = new ProgramPrinted();
        var runner = Substitute.For<IProjectRunnerService>();
        printed.FollowRuns(runner);
        printed.Append("an earlier run's line\r\n");

        runner.RunStarting += Raise.Event<Action>();
        printed.Append("this run's line\r\n");
        runner.IsRunning.Returns(true);
        runner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(runner, new PropertyChangedEventArgs("IsRunning"));
        runner.IsRunning.Returns(false);
        runner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(runner, new PropertyChangedEventArgs("IsRunning"));

        printed.Take().Should().Be("this run's line\n");
    }

    [Fact]
    public void Nothing_printed_is_said_so_rather_than_left_out()
    {
        var (field, note) = ProgramPrinted.Report("");

        field.Should().BeNull();
        note.Should().Be(" Debug.Print wrote nothing since the run began, or since the last reply that reported its output.");
    }

    [Fact]
    public void What_was_printed_is_counted_in_lines_and_given_whole()
    {
        ProgramPrinted.Report("Hello\n").Should().Be(("Hello\n",
            " Debug.Print wrote 1 line since the run began, or since the last reply that reported its output; they are in 'printed'."));
        ProgramPrinted.Report("a\nb\nopen").Note.Should().Contain("wrote 3 lines", "a line left open still counts");
    }

    [Fact]
    public void Output_past_the_limit_keeps_its_end_and_says_how_much_was_left_out()
    {
        var taken = new string('x', 100) + new string('y', ProgramPrinted.Limit);

        var (field, note) = ProgramPrinted.Report(taken);

        field.Should().Be("…" + new string('y', ProgramPrinted.Limit), "the end is what the program wrote most recently");
        note.Should().Contain($"'printed' holds the last {ProgramPrinted.Limit} of their {taken.Length} characters")
            .And.Contain("the Immediate window holds them all");
    }
}
