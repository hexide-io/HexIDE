using System.Text;
using HexIDE.Conversations;
using HexIDE.Lsp;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// Turns a connection that did not reach the state a test wanted into a sentence.
/// </summary>
/// <remarks>
/// <b>Written because two CI failures explained nothing.</b> A foreign-server test asserted
/// <c>State.Should().Be(Running)</c> and got <c>Failed</c>, twice, on a runner where it could not be
/// reproduced locally on either platform — and the assertion reported only the two enum values. Every fact
/// needed to explain it was already on the object and was thrown away
/// (<a href="https://github.com/hexide-io/HexIDE/issues/390">#390</a>).
///
/// <para>
/// The connection carries a <see cref="LanguageConnectionAttempt"/>: the stage it reached, and a step per
/// stage with an outcome, a time and a detail. The client writes the answer straight into it — the
/// initialize timeout path records <c>"the server did not answer initialize within 00:00:30"</c> — so a
/// failing assertion could have said that from the first run rather than the fourth.
/// </para>
///
/// <para>
/// The capture is the second half. It records the handshake unconditionally whether or not anything is
/// armed, and it records process lifecycle, standard error and exit code beside the messages. A stdio
/// server must not write to stdout, so standard error is the only channel a crash stack can take, and
/// before this it reached nowhere a test could see.
/// </para>
///
/// <para>
/// <b>This is diagnosis, not assertion.</b> Nothing here changes what a test proves. It changes what a
/// test says when it fails, which for an intermittent that only happens on someone else's machine is the
/// difference between a fix and another re-run.
/// </para>
/// </remarks>
internal static class ConnectionDiagnostics
{
    /// <summary>
    /// Everything the connection and its capture can say about why it is not where it was expected.
    /// </summary>
    /// <param name="connection">The connection whose state disappointed a test.</param>
    /// <param name="capture">
    /// The record for this connection, if the test attached one. Optional so a test that has not been
    /// wired up still gets the attempt, which is most of the value.
    /// </param>
    public static string Explain(LanguageServerConnection connection, ConversationLog? capture = null)
    {
        var text = new StringBuilder();

        text.Append("connection '").Append(connection.Id).Append("' is ").Append(connection.State);

        if (connection.Attempt is { } attempt)
        {
            text.Append(", having reached ").Append(attempt.ReachedStage).Append('.');

            foreach (var step in attempt.Steps)
            {
                text.Append("\n  - ").Append(step.Stage).Append(' ').Append(step.Outcome);
                if (step.At is { } at) text.Append(" at ").Append($"{at.TotalMilliseconds:F0}ms");
                if (step.Detail is { } detail) text.Append(": ").Append(detail);
            }
        }
        else
        {
            text.Append(", and recorded no attempt at all, which is itself the finding");
        }

        if (capture is null) return text.ToString();

        // Drained first. An envelope that is still in the queue is exactly the one that would explain a
        // failure, and a snapshot taken without draining would be quietly short.
        capture.DrainAsync().GetAwaiter().GetResult();

        var envelopes = capture.Snapshot(connection.Id);
        if (envelopes.Count == 0)
        {
            text.Append("\n  the capture recorded nothing for this connection");
            return text.ToString();
        }

        text.Append("\n  the wire, as recorded:");
        foreach (var envelope in envelopes)
        {
            text.Append("\n  - ").Append(envelope.Direction).Append(' ').Append(envelope.Kind);
            if (envelope.Method is { } method) text.Append(' ').Append(method);
            if (envelope.Outcome != ConversationOutcome.None) text.Append(" [").Append(envelope.Outcome).Append(']');
            if (envelope.Elapsed is { } elapsed) text.Append($" after {elapsed.TotalMilliseconds:F0}ms");
            if (envelope.Detail is { } detail) text.Append(": ").Append(detail);
        }

        var (droppedEnvelopes, droppedBodies, refusedBodies) = capture.Losses(connection.Id);
        if (droppedEnvelopes + droppedBodies + refusedBodies > 0)
        {
            text.Append($"\n  (the capture discarded {droppedEnvelopes} envelopes and {droppedBodies} "
                      + $"bodies, and refused {refusedBodies}, so this is not the whole of it)");
        }

        return text.ToString();
    }
}
