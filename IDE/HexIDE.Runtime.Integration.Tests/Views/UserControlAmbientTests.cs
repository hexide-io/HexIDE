using System.Globalization;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

public partial class UserControlKeyTests
{
    // vb6.exe SP6, design/F5/EXE measurements: https://github.com/hexide-io/HexIDE/issues/4149.
    [AvaloniaTheory]
    [InlineData("BackColor", "12648447")]
    [InlineData("ForeColor", "255")]
    [InlineData("Font.Name", "Arial")]
    [InlineData("Font.Size", "12")]
    [InlineData("Font.Bold", "True")]
    [InlineData("Font.Italic", "False")]
    [InlineData("DisplayName", "uc1")]
    [InlineData("LocaleID", "1033")]
    [InlineData("TextAlign", "0")]
    [InlineData("ScaleUnits", "Twip")]
    [InlineData("ShowGrabHandles", "False")]
    [InlineData("ShowHatching", "False")]
    [InlineData("SupportsMnemonics", "True")]
    [InlineData("RightToLeft", "False")]
    [InlineData("MessageReflect", "True")]
    [InlineData("DisplayAsDefault", "False")]
    [InlineData("UIDead", "False")]
    [InlineData("UserMode", "True")]
    public void Ambient_reads_the_measured_form_values_in_ReadProperties(string member, string expected)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-US"));
        using var run = new Run("", controlCode:
            $"Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print CStr(Ambient.{member})\nEnd Sub\n",
            formSettings: "BackColor = &H00C0FFFF&\nForeColor = &H000000FF&\nBeginProperty Font\nName = \"Arial\"\nSize = 12\nWeight = 700\nItalic = 0\nEndProperty\n");
        run.Lib.Printed.Should().Equal(new Vb6Value(expected));
    }
    // These are explicit HexIDE refusals, not invented VB6 results (#4149).
    [AvaloniaTheory]
    [InlineData("Palette", "en-US", "", false)]
    [InlineData("LocaleID", "en-GB", "", false)]
    [InlineData("RightToLeft", "en-US", "RightToLeft = -1\n", false)]
    [InlineData("UserMode", "en-US", "", true)]
    public void Ambient_refuses_unmeasured_cases(string member, string culture, string settings, bool frame)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo(culture));
        var act = () =>
        {
            using var run = new Run("", controlCode:
                $"Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print Ambient.{member}\nEnd Sub\n",
                formSettings: settings, blocks: frame ? Block("VB.Frame", "Frame1", 0, Block("P.Dial", "uc1", 1)) : null);
        };
        act.Should().Throw<System.NotImplementedException>().WithMessage($"Ambient.{member}*#4149*");
    }
}
