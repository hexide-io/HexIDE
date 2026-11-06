using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Tests.ViewModels;

public class AboutDialogViewModelTests
{
    [AvaloniaFact]
    public void DefaultAbout_ThanksHumanContributorsClaudeAndCodex()
    {
        var vm = new AboutDialogViewModel(new AboutDialogOptions(), new LocalizationService());

        vm.Credits.Should().ContainSingle();
        vm.Credits[0].Should().Contain("HexIDE").And.Contain("contributors")
            .And.Contain("Claude").And.Contain("Anthropic").And.Contain("Codex").And.Contain("OpenAI");
    }

    [AvaloniaFact]
    public void WhiteLabelAbout_AppendsBrandCreditsWithoutReplacingHexIDEThanks()
    {
        var additions = new[] { "Example brand contributors", "Example sponsors" };
        var options = new AboutDialogOptions
        {
            Title = "Example IDE", SubTitle = "Example edition", Copyright = "Example copyright",
            AdditionalCredits = additions
        };
        var localization = new LocalizationService();
        var original = new AboutDialogViewModel(new AboutDialogOptions(), localization);
        var branded = new AboutDialogViewModel(options, localization);

        branded.Title.Should().Be("Example IDE");
        branded.Credits.Should().Equal(original.Credits.Concat(additions));
        branded.Credits[0].Should().Contain("HexIDE").And.Contain("Claude").And.Contain("Codex");
        additions[0] = "Changed after opening";
        branded.Credits.Should().Contain("Example brand contributors");
        branded.Credits.Should().BeAssignableTo<System.Collections.Generic.IList<string>>()
            .Which.IsReadOnly.Should().BeTrue();
    }
}
