using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A TextBox's Font block is the control's Font now (#915), where it was kept as the file's own lines. So it is read into
/// the model and written back from it, and must come back byte for byte: VB6's own template corpus can't guard this, since
/// none of its TextBoxes has a Font block.
/// </summary>
public class TextBoxFontRoundTripTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    // A fractional size and a charset other than 0, the two the model once lost (serialization-fidelity row C6).
    private const string Form =
        "VERSION 5.00\r\n"
        + "Begin VB.Form Form1 \r\n"
        + "   Caption         =   \"Form1\"\r\n"
        + "   ClientHeight    =   3195\r\n"
        + "   ClientLeft      =   60\r\n"
        + "   ClientTop       =   345\r\n"
        + "   ClientWidth     =   4680\r\n"
        + "   LinkTopic       =   \"Form1\"\r\n"
        + "   ScaleHeight     =   3195\r\n"
        + "   ScaleWidth      =   4680\r\n"
        + "   StartUpPosition =   3  'Windows Default\r\n"
        + "   Begin VB.TextBox Text1 \r\n"
        + "      BeginProperty Font \r\n"
        + "         Name            =   \"MS UI Gothic\"\r\n"
        + "         Size            =   9.6\r\n"
        + "         Charset         =   128\r\n"
        + "         Weight          =   400\r\n"
        + "         Underline       =   0   'False\r\n"
        + "         Italic          =   0   'False\r\n"
        + "         Strikethrough   =   0   'False\r\n"
        + "      EndProperty\r\n"
        + "      Height          =   375\r\n"
        + "      Left            =   240\r\n"
        + "      TabIndex        =   0\r\n"
        + "      Text            =   \"Text1\"\r\n"
        + "      Top             =   240\r\n"
        + "      Width           =   2175\r\n"
        + "   End\r\n"
        + "End\r\n"
        + "Attribute VB_Name = \"Form1\"\r\n"
        + "Attribute VB_GlobalNameSpace = False\r\n"
        + "Attribute VB_Creatable = False\r\n"
        + "Attribute VB_PredeclaredId = True\r\n"
        + "Attribute VB_Exposed = False\r\n";

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    [Fact]
    public void A_TextBox_s_Font_block_is_its_Font_and_saves_byte_for_byte()
    {
        var form = Load(Form);
        var box = form.Components.Single(c => c.BaseClass is TextBoxComponentClass);

        box.GetPropertyOrDefault(FontProperty).Should().Be(new VBFont("MS UI Gothic", 9.6, charset: 128));
        box.UnknownRawProperties.Should().NotContain(p => p.Name == "Font");
        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().Be(Form);
    }
}
