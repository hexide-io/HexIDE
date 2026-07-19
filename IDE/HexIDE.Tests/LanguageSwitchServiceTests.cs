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
}
