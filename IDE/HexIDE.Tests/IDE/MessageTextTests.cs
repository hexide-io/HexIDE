using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

/// <summary>
/// A run's message and HexIDE's own sentence after it stay two sentences, whatever the message ends with. The case
/// that found it: run_project answered "...fieldLength as type not implemented Nothing is running."
/// </summary>
public class MessageTextTests
{
    private const string Ours = "Nothing is running.";

    [Theory]
    [InlineData("HexIDE does not implement this yet:\n\nfieldLength as type not implemented")]   // no full stop
    [InlineData("Compile error:\n\nSyntax error.")]
    [InlineData("Form 'Form1' failed: Width must be positive. \n")]                               // trailing space
    public void The_sentence_after_a_message_starts_a_paragraph_of_its_own(string message)
    {
        var joined = MessageText.ThenSay(message, Ours);

        joined.Should().Be(message.TrimEnd() + "\n\n" + Ours);
        joined.Should().StartWith(message.TrimEnd(), "the message is kept as it was worded");
    }

    [Fact]
    public void A_failed_starts_reply_keeps_the_error_and_says_nothing_is_running_in_a_paragraph_of_its_own()
    {
        // The reply run_project, the step tools and run_to_cursor give, as sent.
        MessageText.StartFailedReply("HexIDE does not implement this yet:\n\nfieldLength as type not implemented")
            .Should().Be("HexIDE does not implement this yet:\n\nfieldLength as type not implemented\n\n"
                         + "Nothing is running. The IDE showed this in a runtime-error dialog, which is still open, and "
                         + "get_last_runtime_error returns it.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n")]
    public void An_empty_message_leaves_the_sentence_alone(string message)
    {
        MessageText.ThenSay(message, Ours).Should().Be(Ours);
    }
}
