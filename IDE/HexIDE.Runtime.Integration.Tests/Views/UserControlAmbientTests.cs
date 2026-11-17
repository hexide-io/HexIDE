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
        using var languages = WindowsLanguages.Use(new(1033, 1033, 1033));
        using var run = new Run("", controlCode:
            $"Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print CStr(Ambient.{member})\nEnd Sub\n",
            formSettings: "BackColor = &H00C0FFFF&\nForeColor = &H000000FF&\nBeginProperty Font\nName = \"Arial\"\nSize = 12\nWeight = 700\nItalic = 0\nEndProperty\n");
        run.Lib.Printed.Should().Equal(new Vb6Value(expected));
    }
    // vb6.exe SP6, a built EXE: a control-array element's DisplayName is its name with its index (oracle PR #4271).
    [AvaloniaFact]
    public void Ambient_DisplayName_of_a_control_array_element_carries_its_index()
    {
        using var run = new Run("", controlCode:
            "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print Ambient.DisplayName\nEnd Sub\n",
            blocks: Block("P.Dial", "uc1", 0, extra: "Index = 0"));
        run.Lib.Printed.Should().Equal(new Vb6Value("uc1(0)"));
    }

    // A refusal or an error in a UserControl's Initialize, ReadProperties or Resize, which run while the form loads,
    // used to escape the start, and the IDE ended the run with nothing said (#4266).
    [AvaloniaTheory]
    [InlineData("Initialize")]
    [InlineData("ReadProperties(PropBag As PropertyBag)")]
    [InlineData("Resize")]
    public void A_refusal_while_the_form_loads_its_controls_is_reported(string handler)
    {
        using var run = new Run("", controlCode: $"Private Sub UserControl_{handler}\nDebug.Print Ambient.Palette\nEnd Sub\n");
        run.Faults.Should().ContainSingle().Which.Message.Should().Match("Ambient.Palette*");
    }

    // That the start goes on after it is HexIDE's rule for any event's run-time error (debugger-vb6-divergences D23),
    // not a VB6 measurement.
    [AvaloniaFact]
    public void A_run_time_error_in_ReadProperties_is_reported_and_the_form_still_loads()
    {
        using var run = new Run("", controlCode: "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nErr.Raise 5\nEnd Sub\n",
            formCode: "Private Sub Form_Load()\nDebug.Print \"loaded\"\nEnd Sub\n");
        run.RunTimeErrors.Should().ContainSingle().Which.Error.ErrNo.Should().Be(5);
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value("loaded"));
    }

    // vb6.exe SP6, a built EXE: LocaleID read 1033 under en-GB and de-DE user locales, on a Windows whose system locale
    // and interface languages were US English (oracle PR #4271).
    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("de-DE")]
    public void Ambient_LocaleID_is_1033_whatever_the_user_locale(string culture)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo(culture));
        using var languages = WindowsLanguages.Use(new(1033, 1033, 1033));
        using var run = new Run("", controlCode:
            "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print CStr(Ambient.LocaleID)\nEnd Sub\n");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(new Vb6Value("1033"));
    }

    // Which of the three LocaleID follows is unmeasured, so any of them otherwise, or a host off Windows, refuses.
    [AvaloniaTheory]
    [InlineData(1031, 1033, 1033)]
    [InlineData(1033, 1031, 1033)]
    [InlineData(1033, 1033, 2057)]
    [InlineData(0, 0, 0)]
    public void Ambient_LocaleID_refuses_where_it_has_not_been_measured(int systemLocale, int systemInterface, int userInterface)
    {
        using var languages = WindowsLanguages.Use(systemLocale == 0 ? null : new(systemLocale, systemInterface, userInterface));
        using var run = new Run("", controlCode:
            "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print Ambient.LocaleID\nEnd Sub\n");
        run.Faults.Should().ContainSingle().Which.Message.Should().Match("Ambient.LocaleID*#4266*");
    }

    // These are explicit HexIDE refusals, not invented VB6 results (#4149).
    [AvaloniaTheory]
    [InlineData("Palette", "en-US", "", false)]
    [InlineData("RightToLeft", "en-US", "RightToLeft = -1\n", false)]
    [InlineData("UserMode", "en-US", "", true)]
    public void Ambient_refuses_unmeasured_cases(string member, string culture, string settings, bool frame)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo(culture));
        using var run = new Run("", controlCode:
            $"Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print Ambient.{member}\nEnd Sub\n",
            formSettings: settings, blocks: frame ? Block("VB.Frame", "Frame1", 0, Block("P.Dial", "uc1", 1)) : null);
        // Reported as the run's fault, as a refusal anywhere else is, not thrown past the start (#4266).
        run.Faults.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
            .Which.Message.Should().Match($"Ambient.{member}*#4149*");
    }
}
