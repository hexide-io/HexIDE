using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class ColorPaletteToolViewModelTests
{
    [AvaloniaFact]
    public void Title_IsLocalizedAndUpdatesWhenLanguageChanges()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var sut = new ColorPaletteToolViewModel(Substitute.For<IDocumentDockService>(), localization);

        sut.Title.Should().Be("Color Palette");

        localization.Apply("pl");

        sut.Title.Should().Be("Paleta kolorów");
    }
}
