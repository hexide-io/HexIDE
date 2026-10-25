using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What the designer can show of a control HexIDE does not model, read from the block it was preserved as (#157):
/// its class and name, and the position and size VB6 gives its placeholder. Reading them is the container syntax
/// every block shares, so no knowledge of the control is needed.
/// </summary>
/// <remarks>
/// VB6's answers are from its IDE (docs/vb6-fidelity-oracle/a-control-vb6-cannot-load-renders-as-an-empty-box.md, "Its size when the file records none, and what the
/// box is"): <c>Width</c> and <c>Height</c> size the box, and with neither it is 1200 × 480 twips. <c>_ExtentX</c> and
/// <c>_ExtentY</c> play no part.
/// </remarks>
public class UnmodelledControlTests
{
    // Web Browser.frm's toolbar, its own properties and the start of its Buttons block.
    private const string Toolbar =
        "   Begin MSComctlLib.Toolbar tbToolBar \r\n" +
        "      Align           =   1  'Align Top\r\n" +
        "      Height          =   540\r\n" +
        "      Left            =   0\r\n" +
        "      TabIndex        =   3\r\n" +
        "      Top             =   0\r\n" +
        "      Width           =   6540\r\n" +
        "      _ExtentX        =   11536\r\n" +
        "      _ExtentY        =   953\r\n" +
        "      BeginProperty Buttons {66833FE8-8583-11D1-B16A-00C0F0283628} \r\n" +
        "         NumButtons      =   1\r\n" +
        "         BeginProperty Button1 {66833FEA-8583-11D1-B16A-00C0F0283628} \r\n" +
        "            Key             =   \"Back\"\r\n" +
        "         EndProperty\r\n" +
        "      EndProperty\r\n" +
        "   End";

    // Web Browser.frm's ImageList: a position and its extents, no Width or Height.
    private const string ImageList =
        "   Begin MSComctlLib.ImageList imlIcons \r\n" +
        "      Left            =   2670\r\n" +
        "      Top             =   2325\r\n" +
        "      _ExtentX        =   1005\r\n" +
        "      _ExtentY        =   1005\r\n" +
        "      BeginProperty Images {2C247F25-8591-11D1-B16A-00C0F0283628} \r\n" +
        "         NumListImages   =   1\r\n" +
        "         BeginProperty ListImage1 {2C247F27-8591-11D1-B16A-00C0F0283628} \r\n" +
        "            Picture         =   \"Web Browser.frx\":0000\r\n" +
        "            Key             =   \"\"\r\n" +
        "         EndProperty\r\n" +
        "      EndProperty\r\n" +
        "   End";

    [Fact]
    public void The_Begin_line_names_its_class_and_its_name()
    {
        var control = UnmodelledControl.Read(Toolbar)!;

        control.TypeName.Should().Be("MSComctlLib.Toolbar");
        control.Name.Should().Be("tbToolBar");
    }

    [Fact]
    public void Its_own_position_and_size_are_read_in_twips()
    {
        var control = UnmodelledControl.Read(Toolbar)!;

        (control.Left, control.Top, control.Width, control.Height).Should().Be((0d, 0d, 6540d, 540d));
    }

    [Fact]
    public void Extents_alone_give_VB6s_default_size_not_their_own()
    {
        // VB6: an unloadable control recording only _ExtentX/_ExtentY is 1200 x 480 twips, whatever its extents say.
        var control = UnmodelledControl.Read(ImageList)!;

        (control.Left, control.Top, control.Width, control.Height).Should().Be((2670d, 2325d, 1200d, 480d));
    }

    [Fact]
    public void A_property_inside_a_nested_block_is_not_the_controls_own()
    {
        // A ListView's column headers carry a Width inside a BeginProperty block, and an unmodelled container's
        // children carry a whole geometry inside their own Begin block. Neither is this control's.
        const string container =
            "   Begin Some.Container Holder1 \r\n" +
            "      Left            =   120\r\n" +
            "      Top             =   240\r\n" +
            "      BeginProperty ColumnHeader1 {BDD1F052-858B-11D1-B16A-00C0F0283628} \r\n" +
            "         Width           =   9999\r\n" +
            "      EndProperty\r\n" +
            "      Begin VB.Label Inner \r\n" +
            "         Height          =   7777\r\n" +
            "         Left            =   5555\r\n" +
            "         Top             =   6666\r\n" +
            "         Width           =   8888\r\n" +
            "      End\r\n" +
            "   End";

        var control = UnmodelledControl.Read(container)!;

        (control.Left, control.Top, control.Width, control.Height).Should().Be((120d, 240d, 1200d, 480d));
    }

    [Fact]
    public void A_property_name_is_read_in_any_case_as_the_reader_reads_one()
    {
        var control = UnmodelledControl.Read("Begin X.Y Z1 \r\n   width = 300\r\n   HEIGHT = 200\r\nEnd")!;

        (control.Width, control.Height).Should().Be((300d, 200d));
    }

    [Fact]
    public void Text_that_does_not_open_a_Begin_block_is_not_read()
    {
        UnmodelledControl.Read("   Left            =   0\r\n").Should().BeNull();
        UnmodelledControl.Read("").Should().BeNull();
    }
}
