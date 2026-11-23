using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;

namespace HexIDE.Tests.ViewModels;

// Measured rows: docs/vb6-fidelity-oracle/the-design-time-font-dialog-its-script-list-its-sizes-and-the-charset-it-writes.md.
public class FontDialogScriptTests
{
    private const FontCodePageCoverage Measured = FontCodePageCoverage.Latin1 | FontCodePageCoverage.Hebrew
        | FontCodePageCoverage.Arabic | FontCodePageCoverage.Greek | FontCodePageCoverage.Turkish
        | FontCodePageCoverage.WindowsBaltic | FontCodePageCoverage.Latin2EasternEurope
        | FontCodePageCoverage.Cyrillic | FontCodePageCoverage.Vietnamese;

    [AvaloniaFact]
    public void Measured_scripts_write_their_charsets_in_order()
    {
        var vm = Create("Arial", 0);
        vm.ScriptKeys.Should().Equal(new[] { "Western", "Hebrew", "Arabic", "Greek", "Turkish", "Baltic",
            "CentralEuropean", "Cyrillic", "Vietnamese" }.Select(Key));
        var expected = new[] { 0, 177, 178, 161, 162, 186, 238, 204, 163 };
        for (var i = 0; i < expected.Length; i++)
        {
            vm.SelectedScriptIndex = i;
            Accept(vm).Charset.Should().Be(expected[i]);
        }
    }

    [AvaloniaFact]
    public void Symbol_alone_writes_two_and_mixed_coverage_places_it_last()
    {
        var vm = new FontDialogViewModel(Initial("Wingdings", 2), _ => FontCodePageCoverage.Symbol);
        vm.ScriptKeys.Should().Equal(Key("Symbol"));
        vm.SelectedScriptIndex.Should().Be(0);
        Accept(vm).Charset.Should().Be(2);
        FontDialogViewModel.GetScriptKeys("Mixed", Measured | FontCodePageCoverage.Symbol)
            .Last().Should().Be(Key("Symbol"));
    }

    [AvaloniaFact]
    public void Bitmap_font_has_only_Western_and_its_measured_sizes()
    {
        var vm = Create("MS Sans Serif", 0);
        vm.ScriptKeys.Should().Equal(Key("Western"));
        vm.FontSizes.Should().Equal(8, 10, 12, 14, 15, 17, 18, 23, 24, 30);
    }

    [AvaloniaTheory]
    [InlineData(161, "Greek")]
    [InlineData(204, "Cyrillic")]
    public void Opening_selects_saved_script(int charset, string name)
    {
        var vm = Create("Arial", charset);
        vm.SelectedScriptIndex.Should().BeGreaterThanOrEqualTo(0);
        vm.ScriptKeys[vm.SelectedScriptIndex].Should().Be(Key(name));
        Accept(vm).Charset.Should().Be(charset);
    }

    [AvaloniaFact]
    public void Changing_family_keeps_Greek_then_falls_back_to_Western()
    {
        var vm = Create("Arial", 161);
        vm.SelectedFamily = "Times New Roman";
        vm.ScriptKeys[vm.SelectedScriptIndex].Should().Be(Key("Greek"));
        Accept(vm).Charset.Should().Be(161);
        vm.SelectedFamily = "MS Sans Serif";
        vm.ScriptKeys[vm.SelectedScriptIndex].Should().Be(Key("Western"));
        Accept(vm).Charset.Should().Be(0);
        vm.FontSizes.Should().Equal(8, 10, 12, 14, 15, 17, 18, 23, 24, 30);
        vm.SelectedFamily = "Arial";
        vm.FontSizes.Should().Contain(9).And.NotContain(15);
    }

    [AvaloniaFact]
    public void Western_and_TrueType_sizes_are_regression_guards()
    {
        var vm = Create("Arial", 0);
        vm.SelectedScriptIndex.Should().Be(0);
        Accept(vm).Charset.Should().Be(0);
        vm.FontSizes.Should().Equal(8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72);
    }

    [AvaloniaFact]
    public void Missing_coverage_preserves_unknown_saved_charset_and_fractional_size()
    {
        var vm = new FontDialogViewModel(Initial("Unknown", 161) with { Size = 8.25 }, _ => FontCodePageCoverage.None);
        vm.ScriptKeys.Should().Equal(Key("Western"));
        vm.SelectedScriptIndex.Should().Be(-1);
        Accept(vm).Charset.Should().BeNull(); // PropertyFontBox preserves the saved charset on a null choice.
        vm.FontSizes.Should().Contain(8.25);
        vm.SelectedFamily = "MS Sans Serif";
        vm.SelectedScriptIndex.Should().Be(0);
        vm.FontSizes.Should().Contain(8.25).And.NotContain(9);
    }

    [AvaloniaTheory]
    [InlineData("Arial", 161, "Greek")]
    [InlineData("Wingdings", 2, "Symbol")]
    public void Installed_fonts_expose_the_measured_script(string family, int charset, string script)
    {
        if (!FontManager.Current.SystemFonts.Any(font => font.Name == family))
            throw Xunit.Sdk.SkipException.ForSkip(family + " is not installed; flag mapping is tested separately.");
        var vm = new FontDialogViewModel(Initial(family, charset));
        vm.SelectedScriptIndex.Should().BeGreaterThanOrEqualTo(0);
        vm.ScriptKeys[vm.SelectedScriptIndex].Should().Be(Key(script));
        Accept(vm).Charset.Should().Be(charset);
    }

    private static string Key(string name) => "Str.Dialog.Font.Script." + name;
    private static FontDialogResult Initial(string family, int charset) =>
        new(new FontFamily(family), FontStyle.Normal, FontWeight.Normal, 8, false, false, charset);
    private static FontDialogViewModel Create(string family, int charset) => new(Initial(family, charset), _ => Measured);
    private static FontDialogResult Accept(FontDialogViewModel vm)
    {
        FontDialogResult? result = null;
        vm.AcceptRequested += Capture;
        try { vm.Accept().Should().BeNull(); }
        finally { vm.AcceptRequested -= Capture; }
        result.Should().NotBeNull();
        return result!;
        void Capture(FontDialogResult? answer) => result = answer;
    }
}
