using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A pipe entry can start its own server.
///
/// <para>
/// <see cref="NamedPipeLspTransport"/> has always accepted a <see cref="NamedPipeLaunch"/>, and
/// <c>NamedPipeLspTransportTests</c> has always exercised it — but nothing ever passed one, so from
/// configuration the pipe arm could only ever CONNECT. A server reached that way had to be started by hand,
/// outside the IDE, before the IDE would find it: a capability that existed, was tested, and was
/// unreachable by any user.
/// </para>
///
/// <para>
/// These drive the REAL factory rather than constructing a transport directly, because the gap was never in
/// the transport. Everything below the factory already worked.
/// </para>
/// </summary>
public class PipeLaunchWiringTests
{
    private static LanguageServerRegistrationFactory Factory() =>
        new(Substitute.For<ILoggerFactory>().WithNullLoggers());

    private static LanguageServerEntry PipeEntry(string? command = null, string? arguments = null) =>
        new()
        {
            Id = "probe",
            Extensions = [".bas"],
            LanguageId = "vba",
            Transport = "pipe",
            PipeName = "hexide-test-" + Guid.NewGuid().ToString("N")[..8],
            Command = command,
            Arguments = arguments,
        };

    [Fact]
    public async Task APipeEntryWithACommandStartsTheServerItself()
    {
        // The whole point. The executable does not exist, so the launch fails immediately and says so —
        // which is exactly what distinguishes "we tried to start something" from the old behaviour, where
        // the transport dialled a pipe nobody was listening on and waited out the connect timeout.
        var registration = Factory()
            .Create([PipeEntry(command: "hexide-definitely-not-installed.exe", arguments: "-n {pipe}")])
            .Should().ContainSingle().Subject;

        await using var client = registration.CreateClient();
        await client.StartAsync(TestContext.Current.CancellationToken);

        var attempt = ((VBLspClient)client).LastAttempt;
        attempt.Should().NotBeNull();

        var stop = attempt!.Steps.Single(s => s.Outcome == LanguageConnectionStepOutcome.StoppedHere);
        stop.Detail.Should().Contain("hexide-definitely-not-installed.exe",
            "a launch was attempted, so the failure is about starting a process — not about waiting for a "
          + "pipe that nobody was ever going to create");
    }

    [Fact]
    public async Task APipeEntryWithNoCommandStillOnlyConnects()
    {
        // Unchanged behaviour, pinned. An entry that names no command connects to something already
        // running, and must not acquire a launch by accident.
        var registration = Factory().Create([PipeEntry()])
            .Should().ContainSingle().Subject;

        await using var client = registration.CreateClient();

        // Not started here: connecting to a pipe nobody owns waits out the transport's timeout, and this
        // test is about what was WIRED, not about how long a doomed dial takes.
        registration.Transport.Should().Be(LanguageConnectionTransport.Pipe);
        registration.Endpoint.Should().Contain("(connect)");
    }

    [Fact]
    public void TheEndpointNamesThePipeAndTheRoleWhicheverWayItWasConfigured()
    {
        // Connecting to a pipe and owning one fail in opposite ways, and "nothing dialled in" reads
        // identically to "nothing was listening" without the role beside the name.
        var listening = PipeEntry();
        listening.PipeRole = "listen";

        var registration = Factory().Create([listening]).Should().ContainSingle().Subject;

        registration.Endpoint.Should().Contain("(listen)");
    }
}

internal static class NullLoggerFactoryExtensions
{
    /// <summary>A logger factory whose loggers do nothing, for tests that care about wiring rather than output.</summary>
    public static ILoggerFactory WithNullLoggers(this ILoggerFactory factory)
    {
        factory.CreateLogger(Arg.Any<string>()).Returns(Substitute.For<ILogger>());
        return factory;
    }
}
