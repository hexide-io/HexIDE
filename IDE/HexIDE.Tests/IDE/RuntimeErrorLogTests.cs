using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

/// <summary>
/// The point of this class is that an error outlives its dialog. `stop_project` and `shutdown_ide` both
/// close open dialogs, so anything that exists only while the modal is up is gone by the time a caller
/// looks — and a run that silently did nothing then looks exactly like a run whose error was dismissed.
/// </summary>
public class RuntimeErrorLogTests
{
    [Fact]
    public void A_fresh_log_has_nothing()
    {
        new RuntimeErrorLog().Last.Should().BeNull();
    }

    [Fact]
    public void An_error_is_readable_after_it_is_recorded()
    {
        var log = new RuntimeErrorLog();

        log.Record("Division by zero\n\nat x / 0");

        log.Last!.Value.Message.Should().Be("Division by zero\n\nat x / 0");
    }

    [Fact]
    public void Clear_forgets_the_message_but_keeps_the_sequence()
    {
        // A run clears the message so a later read means "this run raised". The sequence survives so a
        // caller holding an older one can still tell that something happened in between — which comparing
        // message text cannot do, since a loop can raise the identical error twice.
        var log = new RuntimeErrorLog();
        log.Record("Overflow");
        var before = log.Last!.Value.Sequence;

        log.Clear();

        log.Last.Should().BeNull();
        log.Record("Overflow");
        log.Last!.Value.Sequence.Should().BeGreaterThan(before);
    }

    [Fact]
    public void The_same_error_twice_is_two_distinguishable_events()
    {
        var log = new RuntimeErrorLog();

        log.Record("Type mismatch");
        var first = log.Last!.Value.Sequence;
        log.Record("Type mismatch");

        log.Last!.Value.Sequence.Should().Be(first + 1,
            "identical text is why the sequence exists at all");
    }
}
