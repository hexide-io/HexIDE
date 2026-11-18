using System.Linq;
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
    // vb6.exe SP6 native and p-code: https://github.com/hexide-io/HexIDE/issues/4448 (oracle PR #4630).
    [AvaloniaTheory]
    [InlineData("Me.BackColor = 255", "BackColor", "255")]
    [InlineData("Me.BackColor = &H8000000F&", "BackColor", "-2147483633")]
    [InlineData("Me.BackColor = Me.BackColor", "", "")]
    [InlineData("Me.ForeColor = 65280", "ForeColor", "65280")]
    [InlineData("Me.ForeColor = Me.ForeColor", "", "")]
    [InlineData("Me.FontName = \"Tahoma\"", "Font", "Tahoma:12:False:False")]
    [InlineData("Me.FontName = Me.FontName", "Font", "Arial:12:False:False")]
    [InlineData("Me.FontSize = 9", "Font", "Arial:9:False:False")]
    [InlineData("Me.FontSize = Me.FontSize", "Font", "Arial:12:False:False")]
    [InlineData("Me.FontItalic = True", "Font", "Arial:12:True:False")]
    [InlineData("Me.FontItalic = Me.FontItalic", "Font", "Arial:12:False:False")]
    [InlineData("Me.Font.Bold = True", "Font", "Arial:12:False:True")]
    [InlineData("Set Me.Font = Picture1.Font", "Font", "Tahoma:9:False:False")]
    [InlineData("Set Font = Picture1.Font", "Font", "Tahoma:9:False:False")]
    [InlineData("Me.Caption = \"x\"", "", "")]
    [InlineData("Picture1.BackColor = 255", "", "")]
    [InlineData("Picture1.FontName = \"Arial\"", "", "")]
    public async System.Threading.Tasks.Task Form_ambient_changes_are_synchronous_and_in_file_order(string statement, string property, string value)
    {
        using var run = AmbientRun("Private Sub Form_Click()\n" + statement + "\nDebug.Print \"after\"\nEnd Sub\n");
        await run.Context.Events.Run("Form_Click", async () =>
        {
            await run.Context.ExecuteControlEventAsync("Form_Click", []);
            return true;
        });
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(property.Length == 0
            ? [new Vb6Value("after")]
            : new[] { new Vb6Value("UC2:" + property + ":" + value), new Vb6Value("UC11:" + property + ":" + value), new Vb6Value("after") });
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task Shared_font_mutation_notifies_the_form()
    {
        using var run = AmbientRun("Private Sub Form_Click()\nDim f As Object\nSet f = Picture1.Font\nSet Me.Font = f\nf.Bold = True\nDebug.Print \"after\"\nEnd Sub\n");
        await run.Context.Events.Run("Form_Click", async () =>
        {
            await run.Context.ExecuteControlEventAsync("Form_Click", []);
            return true;
        });
        run.Lib.Printed.Should().Equal(new[] { "UC2:Font:Tahoma:9:False:False", "UC11:Font:Tahoma:9:False:False", "UC2:Font:Tahoma:9:False:True", "UC11:Font:Tahoma:9:False:True", "after" }.Select(s => new Vb6Value(s)));
    }

    [AvaloniaTheory]
    [InlineData("Me.BackColor = 255", "UC2:BackColor:255")]
    [InlineData("Me.FontName = \"Tahoma\"", "UC2:Font:Tahoma:12:False:False")]
    [InlineData("Picture1.BackColor = 255", "")]
    [InlineData("Picture1.FontName = \"Arial\"", "")]
    public async System.Threading.Tasks.Task A_Frames_UserControl_is_notified_of_the_forms_changes_and_not_the_Frames(string statement, string first)
    {
        // The container is named Picture1 by AmbientRun whatever its type; here it is a Frame.
        using var run = AmbientRun("Private Sub Form_Click()\n" + statement + "\nDebug.Print \"after\"\nEnd Sub\n", container: "VB.Frame");
        await run.Context.Events.Run("Form_Click", async () =>
        {
            await run.Context.ExecuteControlEventAsync("Form_Click", []);
            return true;
        });
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Should().Equal(first.Length == 0
            ? [new Vb6Value("after")]
            : new[] { new Vb6Value(first), new Vb6Value(first.Replace("UC2:", "UC11:")), new Vb6Value("after") });
    }

    [AvaloniaFact]
    public void Form_Load_changes_notify_before_the_form_is_shown()
    {
        using var run = AmbientRun("Private Sub Form_Load()\nMe.BackColor = 255\nDebug.Print \"after\"\nEnd Sub\n");
        run.Lib.Printed.Should().Equal(new[] { "UC2:BackColor:255", "UC11:BackColor:255", "after" }.Select(s => new Vb6Value(s)));
    }

    // A Frame was measured with the PictureBox (#4448): its UserControl reads the form's ambients too.
    [AvaloniaTheory]
    [InlineData("BackColor", "VB.PictureBox")]
    [InlineData("ForeColor", "VB.PictureBox")]
    [InlineData("Font.Name", "VB.PictureBox")]
    [InlineData("Font.Size", "VB.PictureBox")]
    [InlineData("BackColor", "VB.Frame")]
    [InlineData("ForeColor", "VB.Frame")]
    [InlineData("Font.Name", "VB.Frame")]
    [InlineData("Font.Size", "VB.Frame")]
    public void A_PictureBox_or_Frame_child_reads_the_forms_ambient(string member, string container)
    {
        using var run = AmbientRun(readMember: member, container: container);
        var expected = new Vb6Value(member == "Font.Name" ? "Arial" : member == "Font.Size" ? "12" : member == "BackColor" ? "12648447" : "255");
        run.Lib.Printed.Should().Equal(expected, expected);
    }

    private static Run AmbientRun(string formCode = "", string? readMember = null, string container = "VB.PictureBox") => new("", formCode: formCode,
        formSettings: "BackColor = &H00C0FFFF&\nForeColor = &H000000FF&\nBeginProperty Font\nName = \"Arial\"\nSize = 12\nWeight = 400\nItalic = 0\nEndProperty\n",
        blocks: Block(container, "Picture1", 0, Block("P.Dial", "UC2", 1),
            "BackColor = &H00000000&\nForeColor = &H00000000&\nBeginProperty Font\nName = \"Tahoma\"\nSize = 9\nWeight = 400\nItalic = 0\nEndProperty") + Block("P.Dial", "UC11", 2),
        controlCode: """
            Private Sub UserControl_AmbientChanged(PropertyName As String)
                If PropertyName = "Font" Then
                    Debug.Print Ambient.DisplayName & ":" & PropertyName & ":" & Ambient.Font.Name & ":" & CStr(Ambient.Font.Size) & ":" & CStr(Ambient.Font.Italic) & ":" & CStr(Ambient.Font.Bold)
                ElseIf PropertyName = "BackColor" Then
                    Debug.Print Ambient.DisplayName & ":" & PropertyName & ":" & CStr(Ambient.BackColor)
                ElseIf PropertyName = "ForeColor" Then
                    Debug.Print Ambient.DisplayName & ":" & PropertyName & ":" & CStr(Ambient.ForeColor)
                End If
            End Sub
            Private Sub UserControl_Paint()
                Debug.Print "unexpected Paint"
            End Sub
            """ + (readMember is null ? "" : "\nPrivate Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print CStr(Ambient." + readMember + ")\nEnd Sub\n"));

}
