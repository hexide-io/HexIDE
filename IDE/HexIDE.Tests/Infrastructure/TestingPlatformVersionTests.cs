namespace HexIDE.Tests.Infrastructure;

public class TestingPlatformVersionTests
{
    [Fact]
    public void RunnerUsesMicrosoftTestingPlatformV2()
    {
        typeof(Microsoft.Testing.Platform.Builder.TestApplication).Assembly.GetName().Version!
            .Major.Should().Be(2);
    }
}
