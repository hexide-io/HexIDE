using HexIDE.Net;

namespace HexIDE.Tests.Server;

/// <summary>
/// The access check in front of the MCP dev server.
///
/// <para>
/// <b>The case that matters is <see cref="ARebindingAttackIsRefusedByItsHostHeader"/>, and every other
/// test here exists to stop that one being weakened by accident.</b> A guard that refuses everything
/// passes the attack test and breaks the dev loop; a guard that allows everything breaks the attack test
/// alone. Both halves have to be asserted or the pair is worth nothing.
/// </para>
/// </summary>
public class LoopbackOnlyTests
{
    private const int Port = 5123;

    // ── What must still get through ───────────────────────────────────────────

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]      // Host headers are not case-sensitive and clients do vary.
    [InlineData("127.0.0.1")]
    [InlineData("::1")]            // As a Host header carries it, unbracketed.
    [InlineData("[::1]")]          // As a URI carries it. Both spellings reach this code.
    [InlineData("127.0.0.5")]      // The whole of 127.0.0.0/8 is loopback, not just .1.
    public void ThisMachineIsAllowedByEverySpellingOfItsOwnName(string host)
    {
        LoopbackOnly.Allows(host, Port, Port, origin: null).Should().BeTrue();
    }

    [Fact]
    public void AnAutomationClientSendsNoOriginAndThatIsNormal()
    {
        // Only a browser sends one. Treating its absence as suspicious would refuse every real client.
        LoopbackOnly.Allows("localhost", Port, Port, origin: "").Should().BeTrue();
    }

    [Fact]
    public void ALocalToolThatDoesSendAnOriginIsStillAllowed()
    {
        LoopbackOnly.Allows("localhost", Port, Port, origin: $"http://localhost:{Port}").Should().BeTrue();
    }

    // ── The attack ────────────────────────────────────────────────────────────

    [Fact]
    public void ARebindingAttackIsRefusedByItsHostHeader()
    {
        // DNS rebinding is the one vector binding to loopback does not close. A page from evil.example is
        // same-origin with itself; the name is re-resolved to 127.0.0.1, so the browser sends the request
        // here with no preflight, believing nothing changed. The Host header still names the site the
        // browser was asked for, and that is the only thing the trick cannot forge.
        LoopbackOnly.Allows("evil.example", Port, Port, origin: null).Should().BeFalse();
    }

    [Fact]
    public void ARebindingAttackIsRefusedEvenWhenItLooksLikeALocalName()
    {
        // A hostname is not an address. "localhost.evil.example" resolves wherever its owner says.
        LoopbackOnly.Allows("localhost.evil.example", Port, Port, origin: null).Should().BeFalse();
    }

    [Fact]
    public void APageThatReachedUsWithTheRightHostIsStillRefusedOnItsOrigin()
    {
        // Defence in depth, and not hypothetical: a proxy or a client library may rewrite Host while
        // leaving Origin naming the page that actually made the call.
        LoopbackOnly.Allows("localhost", Port, Port, origin: "http://evil.example").Should().BeFalse();
    }

    [Fact]
    public void AnOpaqueOriginIsRefused()
    {
        // A sandboxed frame serialises its origin as the literal "null". That is exactly the caller that
        // should not be driving an IDE, and it is not a URI, so it fails without a special case.
        LoopbackOnly.Allows("localhost", Port, Port, origin: "null").Should().BeFalse();
    }

    // ── Addressed to something that is not this server ────────────────────────

    [Fact]
    public void TheRightMachineOnTheWrongPortIsNotThisServersTraffic()
    {
        LoopbackOnly.Allows("localhost", 9999, Port, origin: null).Should().BeFalse();
    }

    [Fact]
    public void AHostWithNoPortIsRefusedRatherThanGuessedAt()
    {
        LoopbackOnly.Allows("localhost", hostPort: null, Port, origin: null).Should().BeFalse();
    }

    [Fact]
    public void AnOriginOnADifferentPortIsRefused()
    {
        // Another local server is not this one, and same-machine is not same-application.
        LoopbackOnly.Allows("localhost", Port, Port, origin: "http://localhost:9999").Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("192.168.1.10")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    public void AnythingElseIsRefused(string? host)
    {
        LoopbackOnly.Allows(host, Port, Port, origin: null).Should().BeFalse();
    }
}
