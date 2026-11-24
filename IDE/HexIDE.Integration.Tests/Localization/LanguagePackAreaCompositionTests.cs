using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Localization;

/// <summary>
/// An area registered in the running IDE's registry reaches every reader the real composition builds (#5394). A reader
/// built over a registry of its own would miss it in silence, which is what this guards against.
/// </summary>
public sealed class LanguagePackAreaCompositionTests
{
    private const string Key = "Str.Fixture.Composition.Count";

    [AvaloniaFact]
    public async Task Each_reader_the_composition_builds_sees_an_area_registered_in_the_running_IDEs_registry()
    {
        await using var owned = new RealComposition();
        using var registration = LanguagePackAreas.Current.Register("fixture.composition", "Composition fixture",
            new Uri("avares://HexIDE.Integration.Tests/Localization/Areas/Composition/"), _ => true);

        var localization = owned.Composition.LocalizationService;
        localization.GetString(Key).Should().Be("{0} things");
        localization.GetStringFrom("de", Key).Should().Be("{0} Dinge");

        owned.Composition.UserTranslationsService.PreservesPlaceholders(Key, "Dinge").Should().BeFalse(
            "the user tier checks an override against the area's English, which has {0}");

        owned.Composition.TranslationEditorViewModel.Rows.Select(row => row.Key).Should().Contain(Key);
    }
}
