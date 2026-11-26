using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// The CommonDialog control (#1084): ShowOpen, ShowSave and ShowFont, through the IDE's own dialogs. Every expected value
/// is what vb6.exe gave for the same program, as p-code on Windows 10 (the oracle's "The CommonDialog control").
/// </summary>
public class CommonDialogTests
{
    [AvaloniaFact]
    public void Unknown_font_keeps_previous_name_and_real_face_keeps_spelling()
    {
        var dialog = new VBCommonDialog(null!);
        dialog.TrySetProperty("FontName", new Vb6Value("arial")).Should().BeTrue();
        dialog.TryGetProperty("FontName", out var before).Should().BeTrue();
        before.Should().Be(new Vb6Value("arial"));
        dialog.TrySetProperty("FontName", new Vb6Value("HexIDE nonexistent font 1084")).Should().BeTrue();
        dialog.TryGetProperty("FontName", out var after).Should().BeTrue();
        after.Should().Be(before);
    }

    private const string Dialog =
        "   Begin MSComDlg.CommonDialog dlg \r\n"
      + "      Left            =   120\r\n"
      + "      Top             =   120\r\n"
      + "      _ExtentX        =   847\r\n"
      + "      _ExtentY        =   847\r\n"
      + "      _Version        =   393216\r\n"
      + "   End\r\n";

    private static string Form(string controls, string load) =>
        ("VERSION 5.00\r\n"
       + "Object = \"{F9043C88-F6F2-101A-A3C9-08002B2F49FB}#1.2#0\"; \"COMDLG32.OCX\"\r\n"
       + "Begin VB.Form Form1 \r\n"
       + "   Caption         =   \"Form1\"\r\n"
       + "   ClientHeight    =   3300\r\n"
       + "   ClientWidth     =   4000\r\n"
       + controls
       + "End\r\n"
       + "Attribute VB_Name = \"Form1\"\r\n"
       + "Private Sub Form_Load()\r\n"
       + "    On Error Resume Next\r\n"
       + load
       + "    End\r\n"
       + "End Sub\r\n").ReplaceLineEndings("\r\n");

    private sealed class ListSink : IDeserializeErrorSink
    {
        public readonly List<string> Errors = [];
        public void LogError(string error) => Errors.Add(error);
    }

    [AvaloniaFact]
    public void Opening_a_form_that_holds_one_warns_of_nothing()
    {
        var sink = new ListSink();
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form(Dialog, ""), sink);

        sink.Errors.Should().BeEmpty();
    }
}
