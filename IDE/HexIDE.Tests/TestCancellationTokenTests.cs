namespace HexIDE.Tests;

/// <summary>
/// Checks that <c>TestContext.Current.CancellationToken</c> is a real token, because 188 call sites now
/// pass it.
///
/// <para>
/// Adopting it (hexide-io/HexIDE#300) is only worth anything if the token can actually be cancelled. Were
/// it <c>CancellationToken.None</c> in this configuration, every one of those call sites would be
/// decoration — the code would look responsive to cancellation and behave exactly as it did before, and
/// nothing would say so. That is the shape of failure this suite keeps finding, so it is worth one
/// assertion rather than an assumption.
/// </para>
/// </summary>
public class TestCancellationTokenTests
{
    [Fact]
    public void TheAmbientTokenCanActuallyBeCancelled()
    {
        TestContext.Current.CancellationToken.CanBeCanceled.Should().BeTrue(
            "a token that cannot be cancelled makes every call site passing it a no-op, and the whole "
          + "point of xUnit1051 is that a cancelled run stops waiting");
    }

    [Fact]
    public void TheAmbientTokenIsNotAlreadyCancelledDuringAPassingTest()
    {
        TestContext.Current.CancellationToken.IsCancellationRequested.Should().BeFalse(
            "if this were true the tokens now threaded through the suite would abort work mid-test");
    }
}
