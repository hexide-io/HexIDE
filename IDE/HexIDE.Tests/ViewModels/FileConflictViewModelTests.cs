using HexIDE.Forms.ViewModels;

namespace HexIDE.Tests.ViewModels;

public class FileConflictViewModelTests
{
    private readonly FileConflictViewModel _sut = new();

    [Fact]
    public void Title_ShouldBeHexIDE() => _sut.Title.Should().Be("HexIDE");

    [Fact]
    public void CanResize_ShouldBeFalse() => _sut.CanResize.Should().BeFalse();

    [Fact]
    public void ReloadChosen_DefaultsToFalse() => _sut.ReloadChosen.Should().BeFalse();

    [Fact]
    public void ReloadAll_SetsReloadChosenTrue_AndFiresCloseRequestedTrue()
    {
        bool? received = null;
        _sut.CloseRequested += value => received = value;

        _sut.ReloadAll();

        _sut.ReloadChosen.Should().BeTrue();
        received.Should().BeTrue();
    }

    [Fact]
    public void KeepAll_SetsReloadChosenFalse_AndFiresCloseRequestedTrue()
    {
        bool? received = null;
        _sut.CloseRequested += value => received = value;

        _sut.KeepAll();

        _sut.ReloadChosen.Should().BeFalse();
        received.Should().BeTrue();
    }

    [Fact]
    public void Add_AppendsNamedFile()
    {
        _sut.Add("Form1");
        _sut.Add("Module1");

        _sut.Files.Should().HaveCount(2);
        _sut.Files[0].Name.Should().Be("Form1");
        _sut.Files[1].ToString().Should().Be("Module1");
    }
}
