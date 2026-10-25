using Avalonia.Input;

namespace HexIDE.Tests;

public class ApplicationCommandsTests
{
    [Fact]
    public void ToggleFoldIsUnboundOnMacOS()
    {
        ApplicationCommands.GetToggleFoldKeyGestures(isMacOS: true).Should().BeEmpty();
        if (OperatingSystem.IsMacOS())
            ApplicationCommands.ToggleFoldCommand.Gestures.Should().BeEmpty();
    }

    [Fact]
    public void ToggleFoldUsesThePlatformCommandKeyElsewhere()
    {
        if (OperatingSystem.IsMacOS())
            return;

        var gesture = ApplicationCommands.GetToggleFoldKeyGestures(isMacOS: false).Should().ContainSingle().Subject;
        gesture.Key.Should().Be(Key.M);
        gesture.KeyModifiers.Should().Be(ApplicationCommands.SaveCommand.Gestures[0].KeyModifiers);
        ApplicationCommands.ToggleFoldCommand.Gestures.Should().Equal(gesture);
    }
}
