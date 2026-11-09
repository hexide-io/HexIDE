using Avalonia.Input;

namespace HexIDE.Tests;

public class ApplicationCommandsTests
{
    [Fact]
    public void ProjectComponentsUsesThePlatformCommandKeyAndT()
    {
        ApplicationCommands.ProjectComponentsCommand.Gestures.Should()
            .Equal(new KeyGesture(Key.T, ApplicationCommands.SaveCommand.Gestures[0].KeyModifiers));
    }

    [Fact]
    public void GoToDefinitionUsesShiftF2AndF12()
    {
        ApplicationCommands.GoToDefinitionCommand.Gestures.Should()
            .Equal(new KeyGesture(Key.F2, KeyModifiers.Shift), new KeyGesture(Key.F12));
    }

    [Fact]
    public void PreviousBookmarkHasNoDefaultGesture()
    {
        ApplicationCommands.PreviousBookmarkCommand.Gestures.Should().BeEmpty();
    }

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
