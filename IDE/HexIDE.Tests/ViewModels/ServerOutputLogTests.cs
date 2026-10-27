using HexIDE.Conversations;

namespace HexIDE.Tests.ViewModels;

public class ServerOutputLogTests
{
    [Fact]
    public void ServerOutputKeepsRecentOutputPerServerWithinABound()
    {
        var output = new ServerOutputLog();
        output.Append("quiet", "stderr", "keep me");
        output.Append("noisy", "stderr", new string('x', ServerOutputLog.MaxCharacters * 2));
        output.Append("noisy", "stderr", "latest");
        output.Text("noisy").Length.Should().Be(ServerOutputLog.MaxCharacters);
        output.Text("noisy").Should().StartWith("…");
        output.Text("noisy").Should().EndWith("latest" + Environment.NewLine);
        output.Text("quiet").Should().Contain("keep me");
    }
}
