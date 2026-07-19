using HexIDE.Forms.ViewModels;

namespace HexIDE.Tests.ViewModels;

public class LanguageRevertGateViewModelTests
{
    private static LanguageRevertGateViewModel Create(int seconds = 3) =>
        new("Confirm", "Keep this language?", "Keep", "Revert", "Reverting in {0}s", seconds);

    [Fact]
    public void Countdown_StartsAtGivenSeconds()
    {
        var sut = Create(15);
        sut.SecondsRemaining.Should().Be(15);
        sut.CountdownText.Should().Be("Reverting in 15s");
    }

    [Fact]
    public void Tick_DecrementsAndUpdatesText()
    {
        var sut = Create(3);

        sut.Tick();

        sut.SecondsRemaining.Should().Be(2);
        sut.CountdownText.Should().Be("Reverting in 2s");
    }

    [Fact]
    public void Keep_ClosesWithTrue()
    {
        var sut = Create();
        bool? result = null;
        sut.CloseRequested += r => result = r;

        sut.KeepCommand.Execute(null);

        result.Should().BeTrue();
    }

    [Fact]
    public void Revert_ClosesWithFalse()
    {
        var sut = Create();
        bool? result = null;
        sut.CloseRequested += r => result = r;

        sut.RevertCommand.Execute(null);

        result.Should().BeFalse();
    }

    [Fact]
    public void Countdown_AutoRevertsAtZero()
    {
        var sut = Create(2);
        bool? result = null;
        sut.CloseRequested += r => result = r;

        sut.Tick();   // 2 -> 1
        result.Should().BeNull();
        sut.Tick();   // 1 -> 0 -> revert

        result.Should().BeFalse();
    }

    [Fact]
    public void Close_IsIdempotent_AndStopsLaterTicks()
    {
        var sut = Create(5);
        var raised = 0;
        sut.CloseRequested += _ => raised++;

        sut.KeepCommand.Execute(null);
        sut.Tick();   // already closed — must not re-fire
        sut.RevertCommand.Execute(null);

        raised.Should().Be(1);
    }
}
