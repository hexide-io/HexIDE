using System.Linq;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The Properties window shows a position as VB6's does (#1636): a control's in its container's ScaleMode, a form's own size
/// with its frame, in twips. Every expectation is a value VB6's Properties window showed for the same form, read in the IDE
/// on the oracle guest's console (the oracle's "What VB6's Properties window shows a position in").
/// </summary>
public class DesignPositionsTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string Form(string root = "", string controls = "", string scale = "   ScaleHeight     =   3090\r\n   ScaleWidth      =   4680\r\n",
        string caption = "   Caption         =   \"Form1\"\r\n") =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + root + caption
      + "   ClientHeight    =   3090\r\n   ClientLeft      =   60\r\n   ClientTop       =   450\r\n   ClientWidth     =   4680\r\n"
      + "   LinkTopic       =   \"Form1\"\r\n" + scale + "   StartUpPosition =   3  'Windows Default\r\n" + controls
      + "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static string TextBox(string name, int left, int top, int width, int height, string indent = "   ") =>
        $"{indent}Begin VB.TextBox {name} \r\n{indent}   Height          =   {height}\r\n{indent}   Left            =   {left}\r\n"
      + $"{indent}   Text            =   \"{name}\"\r\n{indent}   Top             =   {top}\r\n{indent}   Width           =   {width}\r\n{indent}End\r\n";

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new NullSink())!;

    private static ComponentInstance Named(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    private static double[] Shown(FormDefinition form, string name)
    {
        var c = Named(form, name);
        return [.. new[] { VBProperties.LeftProperty, VBProperties.TopProperty, VBProperties.WidthProperty, VBProperties.HeightProperty }
            .Select(p => DesignPositions.Shown(form, c, p, c.GetPropertyOrDefault((PropertyClass<double>)p)))];
    }

    // The first probe's twips form: a TextBox on it, and a PictureBox whose ScaleMode is pixels holding another.
    private static readonly string TwipForm = Form(controls:
        "   Begin VB.PictureBox Picture1 \r\n      Height          =   1500\r\n      Left            =   120\r\n"
      + "      ScaleHeight     =   96\r\n      ScaleMode       =   3  'Pixel\r\n      ScaleWidth      =   156\r\n"
      + "      Top             =   840\r\n      Width           =   2400\r\n" + TextBox("Text3", 150, 75, 1215, 285, "      ")
      + "   End\r\n" + TextBox("Text1", 120, 120, 1215, 495));

    // Its pixel form: a TextBox on it, and a Frame holding another.
    private static readonly string PixelForm = Form(
        scale: "   ScaleHeight     =   206\r\n   ScaleMode       =   3  'Pixel\r\n   ScaleWidth      =   312\r\n",
        controls: "   Begin VB.Frame Frame2 \r\n      Caption         =   \"Frame2\"\r\n      Height          =   1500\r\n"
                + "      Left            =   2640\r\n      Top             =   840\r\n      Width           =   1800\r\n"
                + TextBox("Text6", 240, 360, 1215, 285, "      ") + "   End\r\n" + TextBox("Text5", 120, 120, 1215, 495));

    [Theory]
    [InlineData("Text1", 120, 120, 1215, 495)]   // on a twips form: twips
    [InlineData("Picture1", 120, 840, 2400, 1500)]
    [InlineData("Text3", 10, 5, 81, 19)]         // in a pixel PictureBox: pixels
    public void On_a_twips_form_each_is_in_its_containers_ScaleMode(string name, double left, double top, double width, double height) =>
        Shown(Load(TwipForm), name).Should().Equal(left, top, width, height);

    [Theory]
    [InlineData("Text5", 8, 8, 81, 33)]          // on a pixel form: pixels
    [InlineData("Frame2", 176, 56, 120, 100)]
    [InlineData("Text6", 240, 360, 1215, 285)]   // in a Frame, which has no ScaleMode: twips
    public void On_a_pixel_form_each_is_in_its_containers_ScaleMode(string name, double left, double top, double width, double height) =>
        Shown(Load(PixelForm), name).Should().Equal(left, top, width, height);

    // A form's own position is twips whatever its ScaleMode: its client rectangle and the frame round it.
    [Theory]
    [InlineData("", -60, -15, 4920, 3675)]
    [InlineData("   BorderStyle     =   0  'None\r\n", 60, 450, 4680, 3090)]
    [InlineData("   BorderStyle     =   1  'Fixed Single\r\n", 15, 60, 4770, 3525)]
    [InlineData("   BorderStyle     =   3  'Fixed Dialog\r\n", 15, 60, 4770, 3525)]
    [InlineData("   BorderStyle     =   4  'Fixed ToolWindow\r\n", 15, 60, 4770, 3525)]
    [InlineData("   BorderStyle     =   5  'Sizable ToolWindow\r\n", -60, -15, 4920, 3675)]
    public void A_forms_own_size_has_its_frame(string borderStyle, double left, double top, double width, double height) =>
        Shown(Load(Form(borderStyle)), "Form1").Should().Equal(left, top, width, height);

    [Fact]
    public void A_pixel_forms_own_size_is_twips() =>
        Shown(Load(PixelForm), "Form1").Should().Equal(-60, -15, 4920, 3675);

    [Fact]
    public void A_Fixed_Single_form_with_no_control_box_and_no_caption_has_no_caption_bar() =>
        Shown(Load(Form("   BorderStyle     =   1  'Fixed Single\r\n   ControlBox      =   0   'False\r\n", caption: "   Caption         =   \"\"\r\n")),
            "Form1").Should().Equal(45, 435, 4710, 3120);

    // With a menu the frame is 300 taller. VB6 read the form's Left and Top as 105 and 105, which no rule here gives, so only
    // its size is pinned.
    [Fact]
    public void A_menu_makes_the_frame_taller()
    {
        var form = Load(Form(controls: "   Begin VB.Menu mnuFile \r\n      Caption         =   \"File\"\r\n   End\r\n"));

        Shown(form, "Form1")[2..].Should().Equal(4920, 3975);
    }

    // A UserControl has no frame: its size is its client area (#1585's probe).
    [Fact]
    public void A_UserControls_size_is_its_client_area()
    {
        var uc = Load("VERSION 5.00\r\nBegin VB.UserControl UserControl1 \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
                    + "   ClientTop       =   0\r\n   ClientWidth     =   4800\r\n   ScaleHeight     =   3600\r\n   ScaleWidth      =   4800\r\n"
                    + "End\r\nAttribute VB_Name = \"UserControl1\"\r\n");

        Shown(uc, "UserControl1")[2..].Should().Equal(4800, 3600);
    }

    private const string Text1 = "   Begin VB.TextBox Text1 \r\n      Height          =   495\r\n      Left            =   1200\r\n"
                               + "      Text            =   \"Text1\"\r\n      Top             =   600\r\n      Width           =   1215\r\n   End\r\n";

    // A User scale maps the client area onto ScaleLeft, ScaleTop, ScaleWidth and ScaleHeight, and a value that is not whole
    // shows three decimals at most.
    [Fact]
    public void A_User_scale_maps_the_client_area() =>
        Shown(Load(Form(controls: Text1, scale: "   ScaleHeight     =   500\r\n   ScaleLeft       =   100\r\n   ScaleMode       =   0  'User\r\n"
                                              + "   ScaleTop        =   50\r\n   ScaleWidth      =   1000\r\n")), "Text1")
            .Should().Equal(356.41, 147.087, 259.615, 80.097);

    [Theory]
    [InlineData("7  'Centimeter", 2.117, 1.058, 2.143, 0.873)]
    [InlineData("6  'Millimeter", 21.167, 10.583, 21.431, 8.731)]
    public void Centimetres_and_millimetres(string mode, double left, double top, double width, double height) =>
        Shown(Load(Form(controls: Text1, scale: $"   ScaleMode       =   {mode}\r\n")), "Text1").Should().Equal(left, top, width, height);

    // VB6's own About Dialog template, whose form has a User scale.
    [Fact]
    public void VB6s_About_Dialog()
    {
        var about = Load("VERSION 5.00\r\nBegin VB.Form frmAbout \r\n   ClientHeight    =   3555\r\n   ClientLeft      =   2340\r\n"
                       + "   ClientTop       =   1935\r\n   ClientWidth     =   5730\r\n   ScaleHeight     =   2453.724\r\n"
                       + "   ScaleMode       =   0  'User\r\n   ScaleWidth      =   5380.766\r\n"
                       + "   Begin VB.CommandButton cmdOK \r\n      Caption         =   \"OK\"\r\n      Height          =   345\r\n"
                       + "      Left            =   4245\r\n      Top             =   2625\r\n      Width           =   1260\r\n   End\r\n"
                       + "End\r\nAttribute VB_Name = \"frmAbout\"\r\n");

        Shown(about, "cmdOK").Should().Equal(3986.274, 1811.822, 1183.205, 238.125);
    }

    // A User-scale PictureBox maps its client area, inside its border: 60 twips less across and down for the default 3D
    // Fixed Single border, nothing for none.
    [Theory]
    [InlineData("", 138.205, 57.083, 519.231, 98.958)]
    [InlineData("      BorderStyle     =   0  'None\r\n", 135, 55, 506.25, 95)]
    public void A_User_scale_PictureBox_maps_its_client_area(string border, double left, double top, double width, double height) =>
        Shown(Load(Form(controls: "   Begin VB.PictureBox Picture1 \r\n" + border + "      Height          =   1500\r\n"
            + "      Left            =   600\r\n      ScaleHeight     =   500\r\n      ScaleLeft       =   10\r\n"
            + "      ScaleMode       =   0  'User\r\n      ScaleTop        =   5\r\n      ScaleWidth      =   1000\r\n"
            + "      Top             =   600\r\n      Width           =   2400\r\n"
            + TextBox("Text1", 300, 150, 1215, 285, "      ") + "   End\r\n")), "Text1")
            .Should().Equal(left, top, width, height);

    // What is typed in is in the same units, rounded to whole twips, half to even, as an assignment at run time is.
    [Fact]
    public void A_value_typed_in_is_whole_twips_in_the_containers_units()
    {
        var twips = Load(TwipForm);
        var text1 = Named(twips, "Text1");
        DesignPositions.ToPixels(twips, text1, VBProperties.WidthProperty, 1000).Should().Be(1000 / 15d);

        var pixels = Load(PixelForm);
        var text5 = Named(pixels, "Text5");
        DesignPositions.ToPixels(pixels, text5, VBProperties.LeftProperty, 10.5).Should().Be(158 / 15d);
        DesignPositions.ToPixels(pixels, Named(pixels, "Text6"), VBProperties.LeftProperty, 300).Should().Be(20);

        var root = Named(twips, "Form1");
        DesignPositions.ToPixels(twips, root, VBProperties.WidthProperty, 4920).Should().Be(4680 / 15d);
        DesignPositions.ToPixels(twips, root, VBProperties.TopProperty, -15).Should().Be(450 / 15d);
    }

    [Fact]
    public void A_value_typed_in_a_User_scale_comes_back_as_whole_twips()
    {
        var form = Load(Form(controls: Text1, scale: "   ScaleHeight     =   500\r\n   ScaleLeft       =   100\r\n   ScaleMode       =   0  'User\r\n"
                                                + "   ScaleTop        =   50\r\n   ScaleWidth      =   1000\r\n"));

        DesignPositions.ToPixels(form, Named(form, "Text1"), VBProperties.LeftProperty, 356.41).Should().Be(1200 / 15d);
    }

    [Fact]
    public void Each_position_names_its_unit()
    {
        var pixels = Load(PixelForm);

        DesignPositions.Unit(pixels, Named(pixels, "Text5"), VBProperties.LeftProperty).Should().Be("pixels");
        DesignPositions.Unit(pixels, Named(pixels, "Text6"), VBProperties.LeftProperty).Should().Be("twips");
        DesignPositions.Unit(pixels, Named(pixels, "Form1"), VBProperties.WidthProperty).Should().Be("twips");
    }
}
