using HexIDE.Forms.ViewModels.Options;
using HexIDE.IDE;
using HexIDE.Keymaps;

namespace HexIDE.Tests.ViewModels.Options;

public class KeymapPageViewModelTests
{
    private readonly ISettingsService _settings = Substitute.For<ISettingsService>();
    private readonly IKeymapService _keymap = Substitute.For<IKeymapService>();

    public KeymapPageViewModelTests()
    {
        _settings.ActiveKeymap.Returns("Default");
        _keymap.ActiveKeymap.Returns("Default");
        _keymap.AvailableKeymaps.Returns(["Default", "VB6"]);
    }

    private KeymapPageViewModel CreateSut() => new(_settings, _keymap);

    [Fact]
    public void Load_PullsActiveKeymap()
    {
        _settings.ActiveKeymap.Returns("VB6");
        CreateSut().SelectedKeymap.Should().Be("VB6");
    }

    [Fact]
    public void AvailableKeymaps_ComeFromService()
    {
        CreateSut().AvailableKeymaps.Should().Equal("Default", "VB6");
    }

    [Fact]
    public void ChangingSelection_DoesNotApply()
    {
        var sut = CreateSut();
        sut.SelectedKeymap = "VB6";
        _keymap.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public void Save_PersistsAndAppliesWhenChanged()
    {
        var sut = CreateSut();
        sut.SelectedKeymap = "VB6";

        sut.SaveToSettings();

        _settings.Received().ActiveKeymap = "VB6";
        _keymap.Received(1).Apply("VB6");
    }

    [Fact]
    public void Save_DoesNotReapplyWhenUnchanged()
    {
        var sut = CreateSut();   // selection already equals the active keymap

        sut.SaveToSettings();

        _settings.Received().ActiveKeymap = "Default";
        _keymap.DidNotReceive().Apply(Arg.Any<string>());
    }

    [Fact]
    public void Title_IsKeymap()
    {
        CreateSut().Title.Should().Be("Keymap");
    }
}
