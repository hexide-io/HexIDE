namespace HexIDE.Tests.Infrastructure;

public class TestingPlatformVersionTests
{
    [Fact]
    public void RunnerUsesMicrosoftTestingPlatformV2()
    {
        // 2.1 is the first release whose hang-dump activity signal is a pipe heartbeat. 1.x's named-mutex handoff never
        // reached the controller on macOS, so the timer counted the whole run there (#3874).
        typeof(Microsoft.Testing.Platform.Builder.TestApplication).Assembly.GetName().Version!
            .Should().BeGreaterThanOrEqualTo(new Version(2, 1));
    }

    [Fact]
    public void HangDumpMatchesMicrosoftTestingPlatform()
    {
        typeof(Microsoft.Testing.Extensions.HangDumpExtensions).Assembly.GetName().Version
            .Should().Be(typeof(Microsoft.Testing.Platform.Builder.TestApplication).Assembly.GetName().Version);
    }
}
