using HexIDE.Runtime.Dialogs;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class ProgramButtonCaptionTests
{
    [Theory]
    [InlineData("&Yes", "_Yes")]
    [InlineData("Save && E&xit", "Save & E_xit")]
    [InlineData("OK", "OK")]
    [InlineData("A_B &C", "A__B _C")]
    [InlineData(null, "fallback")]
    public void Native_mnemonics_and_null_fallback(string? native, string expected) =>
        ProgramButtonCaptions.FromNative(native, "fallback").Should().Be(expected);
}
