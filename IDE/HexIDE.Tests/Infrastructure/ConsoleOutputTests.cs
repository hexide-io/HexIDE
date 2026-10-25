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
}
