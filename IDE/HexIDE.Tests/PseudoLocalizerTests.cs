using HexIDE.Localization;

namespace HexIDE.Tests;

public class PseudoLocalizerTests
{
    private static string Pseudo(string value, bool rtl = false)
        => PseudoLocalizer.Transform(new Dictionary<string, string> { ["k"] = value }, rtl)["k"];

    [Fact]
    public void Transform_PreservesAllKeys()
    {
        var input = new Dictionary<string, string> { ["a"] = "One", ["b"] = "Two" };

        var result = PseudoLocalizer.Transform(input, rtl: false);

        result.Keys.Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void PreservesMnemonicAccessKey()
    {
        // The letter after '_' must stay ASCII so Alt+N still works.
        Pseudo("_New").Should().Contain("_N");
    }

    [Fact]
    public void PreservesFormatPlaceholder()
    {
        Pseudo("Save {0}").Should().Contain("{0}");
    }

    [Fact]
    public void PreservesAmpersand()
    {
        Pseudo("Cut & Paste").Should().Contain("&");
    }

    [Fact]
    public void WrapsInBrackets_ForLtr()
    {
        Pseudo("File").Should().StartWith("⟦").And.EndWith("⟧");
    }

    [Fact]
    public void ExpandsLength_ToStressTruncation()
    {
        Pseudo("File").Length.Should().BeGreaterThan("File".Length);
    }

    [Fact]
    public void AccentsLetters()
    {
        // "Open" → O→Ó, e→é, n→ñ
        Pseudo("Open").Should().Contain("Ó");
    }

    [Fact]
    public void IsKlingonFlavoured()
    {
        // The padding draws from deterministic Klingon filler. "Settings" (8 chars) lands the
        // filler cursor on index 0 → "Qapla'". Qapla'!
        Pseudo("Settings").Should().Contain("Qapla'");
    }

    [Fact]
    public void Rtl_WrapsWithDirectionMarks()
    {
        Pseudo("File", rtl: true).Should().StartWith("‫").And.EndWith("‬");
    }
}
