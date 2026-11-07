using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Forms.ViewModels.Options;

namespace HexIDE.Tests;

public class BracketDepthTests
{
    [Theory]
    [InlineData("(())()", new[] { 0, 1, 1, 0, 0, 0 })]
    [InlineData(")(())(", new[] { -1, 0, 1, 1, 0, -1 })]
    [InlineData("( _\r\n())", new[] { 0, 1, 1, 0 })]
    [InlineData("\"(\" & \"a\"\"(\" ' (\r\n()", new[] { 0, 0 })]
    [InlineData("Rem (\r\n' )\r\n()", new[] { 0, 0 })]
    public void Scan_UsesCodeTokensAndPairs(string source, int[] depths)
    {
        var marks = BracketDepth.Scan(source);
        marks.Select(mark => mark.Depth).Should().Equal(depths);
        marks.Select(mark => source[mark.Offset]).Should().OnlyContain(c => c == '(' || c == ')');
    }
    [Fact]
    public void OptionLoadsSavesAndRestoresDefaults()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ColorBracketsByDepth.Returns(true);
        var page = new EditorGeneralPageViewModel(settings);
        page.ColorBracketsByDepth.Should().BeTrue();
        page.ColorBracketsByDepth = false;
        page.SaveToSettings();
        settings.Received().ColorBracketsByDepth = false;
        page.RestoreDefaults();
        page.ColorBracketsByDepth.Should().BeTrue();
    }
}
