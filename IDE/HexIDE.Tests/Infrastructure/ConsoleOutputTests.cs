using System.Text;
using HexIDE.Desktop;

namespace HexIDE.Tests.Infrastructure;

public class ConsoleOutputTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RedirectedOutputIsWrittenWithoutAttaching(bool parentHasConsole)
    {
        using var output = new MemoryStream();
        var attachCalled = false;
        ConsoleTarget? renderedFor = null;

        var written = ConsoleOutput.Write(target =>
        {
            renderedFor = target;
            return "command-line output";
        }, () => output, () =>
        {
            attachCalled = true;
            return parentHasConsole;
        });

        written.Should().BeTrue();
        attachCalled.Should().BeFalse("attaching can replace the inherited output handle");
        renderedFor.Should().Be(new ConsoleTarget(false, int.MaxValue));
        Encoding.UTF8.GetString(output.ToArray()).ReplaceLineEndings("\n")
            .Should().Be("command-line output\n\n");
    }

    // Console.OutputEncoding is Encoding.UTF8, preamble included, once anything in the process sets it so (the
    // standalone runner does), and the redirected write used to emit that preamble ahead of the text. Which test
    // ran first decided whether this file's other case saw a byte-order mark.
    [Fact]
    public void RedirectedOutputCarriesNoByteOrderMark()
    {
        using var output = new MemoryStream();

        ConsoleOutput.Write(_ => "command-line output", () => output, () => false, Encoding.UTF8);

        output.ToArray().Take(3).Should().NotEqual(Encoding.UTF8.GetPreamble());
        Encoding.UTF8.GetString(output.ToArray()).ReplaceLineEndings("\n").Should().Be("command-line output\n\n");
    }
}
