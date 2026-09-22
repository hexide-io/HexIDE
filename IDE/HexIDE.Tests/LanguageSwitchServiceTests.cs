using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests;

public class LanguageSwitchServiceTests
{
    private readonly ILocalizationService _loc = Substitute.For<ILocalizationService>();
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();

    public LanguageSwitchServiceTests()
    {
        _loc.ActiveLanguage.Returns("en");
        // Non-null so the gate VM's countdown String.Format doesn't choke (keys have no placeholder here).
        _loc.GetStringFrom(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(1));
    }

    private LanguageSwitchService CreateSut() => new(_loc, _windows);

    [Fact]
    public async Task SameLanguage_IsNoOp_NoGate()
    {
        var kept = await CreateSut().SwitchWithGateAsync("en");

        kept.Should().BeTrue();
        await _windows.DidNotReceive().ShowDialog(Arg.Any<IDialog>());
        _loc.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public async Task Kept_AppliesNewLanguage_AndDoesNotRevert()
    {
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);

        var kept = await CreateSut().SwitchWithGateAsync("pseudo");

        kept.Should().BeTrue();
        _loc.Received().Apply("pseudo");
        _loc.DidNotReceive().Apply("en");
    }

    [Fact]
    public async Task Reverted_AppliesThenRestoresPrevious()
    {
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(false);

        var kept = await CreateSut().SwitchWithGateAsync("pseudo");

        kept.Should().BeFalse();
        Received.InOrder(() =>
        {
            _loc.Apply("pseudo");
            _loc.Apply("en");
        });
    }

    [Fact]
    public async Task Switch_ShowsTheGateDialog()
    {
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(true);

        await CreateSut().SwitchWithGateAsync("pseudo");

        await _windows.Received(1).ShowDialog(Arg.Any<LanguageRevertGateViewModel>());
    }

    // One gate at a time (#588). Each gate reverts to the language active when it opened, so a second gate on
    // top of the first would revert to the first's unconfirmed language.

    [Fact]
    public async Task ASecondSwitch_WhileAGateIsOpen_IsRefusedAndChangesNothing()
    {
        var firstGate = new TaskCompletionSource<bool>();
        // A second gate, were one opened, would answer at once, so a missing guard fails here rather than hangs.
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(firstGate.Task, Task.FromResult(true));
        var sut = CreateSut();

        var first = sut.SwitchWithGateAsync("fr-CA");
        sut.PendingLanguage.Should().Be("fr-CA");
        _loc.ClearReceivedCalls();

        var second = await sut.SwitchWithGateAsync("de");

        second.Should().BeFalse();
        _loc.DidNotReceive().Apply(Arg.Any<string>());
        await _windows.Received(1).ShowDialog(Arg.Any<IDialog>());
        sut.PendingLanguage.Should().Be("fr-CA", "the open gate is still the first one");

        firstGate.SetResult(false);
        (await first).Should().BeFalse();
        _loc.Received(1).Apply("en");
        sut.PendingLanguage.Should().BeNull();
    }

    [Fact]
    public async Task OnceTheGateCloses_TheNextSwitchOpensItsOwn()
    {
        var firstGate = new TaskCompletionSource<bool>();
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(firstGate.Task, Task.FromResult(true));
        var sut = CreateSut();

        var first = sut.SwitchWithGateAsync("fr-CA");
        firstGate.SetResult(false);
        await first;

        (await sut.SwitchWithGateAsync("de")).Should().BeTrue();
        _loc.Received().Apply("de");
        sut.PendingLanguage.Should().BeNull();
    }

    [Fact]
    public async Task AGateThatThrows_DoesNotLeaveTheNextSwitchRefused()
    {
        _windows.ShowDialog(Arg.Any<IDialog>()).Returns(
            Task.FromException<bool>(new InvalidOperationException("no window")), Task.FromResult(true));
        var sut = CreateSut();

        var act = () => sut.SwitchWithGateAsync("fr-CA");
        await act.Should().ThrowAsync<InvalidOperationException>();

        sut.PendingLanguage.Should().BeNull();
        (await sut.SwitchWithGateAsync("de")).Should().BeTrue();
    }
}
