using System.IO.Pipes;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// The pipe-name budget, pinned against the platform rather than against the arithmetic that produced it.
///
/// <para>
/// hexide-io/HexIDE#694: a <c>pipeName</c> that works on Windows and Linux can be impossible on macOS,
/// because .NET builds a Unix named pipe out of a domain socket at <c>$TMPDIR/CoreFxPipe_&lt;name&gt;</c> and
/// <c>sun_path</c> is 104 bytes. macOS's per-session temp directory spends 60 of those before the name
/// starts. Six tests in two files threw before asserting anything, the first time this suite ran on a Mac.
/// </para>
/// <para>
/// <b>The load-bearing test here is <see cref="APipeOfExactlyTheStatedLengthCanBeCreated"/>.</b> The other
/// two would pass against any number this class chose to return. That one fails if the budget is one too
/// generous — which is exactly the mistake available, since the framework rejects a path of 104 with a
/// message saying 104 is allowed, and the difference is only observable on the platform nobody develops on.
/// </para>
/// </summary>
public class PipeNameLimitTests
{
    [Fact]
    public void APipeOfExactlyTheStatedLengthCanBeCreated()
    {
        // Not "a long name fails", which would prove only that some limit exists somewhere. This asserts
        // the stated budget is REACHABLE: build the longest name the limit allows and make the platform
        // accept it. An over-generous Max throws here, on the platform whose Max is wrong, which is the
        // only place the error was ever going to show.
        var name = new string('a', PipeNameLimit.Max);

        var create = () =>
        {
            using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        };

        create.Should().NotThrow(
            $"PipeNameLimit.Max is {PipeNameLimit.Max} on this host, so a name of that length must work; "
          + "if this throws, the budget is too generous and every pipe near the limit fails at connect time");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANameWithinTheBudgetIsAccepted(int delta)
    {
        PipeNameLimit.IsTooLong(new string('a', PipeNameLimit.Max + delta)).Should().BeFalse();
    }

    [Fact]
    public void ANameOneOverTheBudgetIsRefused()
    {
        PipeNameLimit.IsTooLong(new string('a', PipeNameLimit.Max + 1)).Should().BeTrue();
    }

    [Fact]
    public void TheRefusalNamesTheBudgetAndTheNamesOwnLength()
    {
        // A refusal that says only "too long" leaves the reader to guess by how much, on a limit that
        // differs per machine. Both numbers are the point.
        var name = new string('a', PipeNameLimit.Max + 5);

        var refusal = PipeNameLimit.Refusal(name);

        refusal.Should().Contain(name.Length.ToString(), "the reader needs to know what they supplied");
        refusal.Should().Contain(PipeNameLimit.Max.ToString(), "and what this host allows");
        refusal.Should().Contain("pipeName", "named as the field it is, not as an internal concept");
    }

    [Fact]
    public void TheTransportRefusesAnOverLongNameOnConstruction()
    {
        // Before a registration exists, rather than at connect time with one already standing.
        var tooLong = new string('a', PipeNameLimit.Max + 1);

        var construct = () => new NamedPipeLspTransport(
            tooLong, NamedPipeRole.Listen, Substitute.For<ILogger<NamedPipeLspTransport>>());

        construct.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("pipeName");
    }
}
