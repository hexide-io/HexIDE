using HexIDE.Conversations;

namespace HexIDE.Tests.Conversations;

/// <summary>The one rule list_lsp_messages' failuresOnly and the Protocol Inspector share (#945).</summary>
public class ConversationFailuresTests
{
    private static ConversationEnvelope Row(
        ConversationEntryKind kind, ConversationOutcome outcome = ConversationOutcome.None, bool fault = false,
        ConversationDirection direction = ConversationDirection.Local) =>
        new(1, "vb6", DateTimeOffset.UnixEpoch, direction, kind, null, null, 0, outcome, Fault: fault);

    [Theory]
    [InlineData(ConversationEntryKind.ErrorResponse, ConversationOutcome.None, false)]
    [InlineData(ConversationEntryKind.Request, ConversationOutcome.Failed, false)]
    [InlineData(ConversationEntryKind.Request, ConversationOutcome.Cancelled, false)]
    [InlineData(ConversationEntryKind.Request, ConversationOutcome.Abandoned, false)]
    [InlineData(ConversationEntryKind.NeverSent, ConversationOutcome.None, true)]
    [InlineData(ConversationEntryKind.Note, ConversationOutcome.None, true)]
    [InlineData(ConversationEntryKind.Lifecycle, ConversationOutcome.None, true)]
    public void Each_of_these_is_a_failure(ConversationEntryKind kind, ConversationOutcome outcome, bool fault)
    {
        Row(kind, outcome, fault).IsFailure().Should().BeTrue();
    }

    [Theory]
    [InlineData(ConversationEntryKind.Request, ConversationOutcome.None)]
    [InlineData(ConversationEntryKind.Request, ConversationOutcome.Answered)]
    [InlineData(ConversationEntryKind.Notification, ConversationOutcome.None)]
    [InlineData(ConversationEntryKind.Lifecycle, ConversationOutcome.None)]
    [InlineData(ConversationEntryKind.Note, ConversationOutcome.None)]
    [InlineData(ConversationEntryKind.StandardError, ConversationOutcome.None)]
    [InlineData(ConversationEntryKind.Unconsumed, ConversationOutcome.None)]
    public void Each_of_these_is_not(ConversationEntryKind kind, ConversationOutcome outcome)
    {
        Row(kind, outcome).IsFailure().Should().BeFalse();
    }

    [Theory]
    [InlineData(ConversationDirection.Sent)]
    [InlineData(ConversationDirection.Local)]
    public void A_request_withheld_for_want_of_a_capability_is_not_whichever_direction_it_is_filed_under(
        ConversationDirection direction)
    {
        // Two fixtures in the tree file it as Local, production as Sent: the direction decides nothing.
        Row(ConversationEntryKind.NeverSent, direction: direction).IsFailure().Should().BeFalse();
    }
}
