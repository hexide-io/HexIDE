using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// A VB6 font's size is in points, and is drawn at 96 pixels to the inch (#910). It was handed to Avalonia as pixels, so
/// every VB6 font drew a quarter too small, and the default was held as 11, the pixels its 8.25 points draw at.
/// </summary>
public class FontSizeInPointsTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Probe"
           Begin VB.Label Label1
              Caption         =   "twelve"
              BeginProperty Font
                 Name            =   "Arial"
                 Size            =   12
                 Charset         =   0
                 Weight          =   400
                 Underline       =   0   'False
                 Italic          =   0   'False
                 Strikethrough   =   0   'False
              EndProperty
              Height          =   375
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   2000
           End
           Begin VB.CommandButton Command1
              Caption         =   "nine point six"
              BeginProperty Font
                 Name            =   "Tahoma"
                 Size            =   9.6
                 Charset         =   0
                 Weight          =   400
                 Underline       =   0   'False
                 Italic          =   0   'False
                 Strikethrough   =   0   'False
              EndProperty
              Height          =   375
              Left            =   120
              TabIndex        =   1
              Top             =   600
              Width           =   2000
           End
           Begin VB.Label Label2
              Caption         =   "no font of its own"
              Height          =   375
              Left            =   120
              TabIndex        =   2
              Top             =   1080
              Width           =   2000
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public void A_size_in_points_draws_at_four_thirds_as_many_pixels()
    {
        new VBFont("Arial", 12).ToFontSize().Should().Be(16);
        new VBFont("Tahoma", 9.6).ToFontSize().Should().BeApproximately(12.8, 1e-9);
        VBFont.Default.Should().Be(new VBFont("MS Sans Serif", 8.25), "VB6's default, measured on a Label with no Font block");
        VBFont.Default.ToFontSize().Should().Be(11, "so a control on the default font draws as it always did");
    }

    [AvaloniaFact]
    public void A_control_draws_its_font_s_points_as_pixels()
    {
        var (_, canvas, _, _) = Spawn(Form);
        var controls = canvas.GetLogicalDescendants().OfType<Control>().Where(c => NameOf(c) is not null).ToDictionary(c => NameOf(c)!);

        controls["Label1"].As<TemplatedControl>().FontSize.Should().Be(16);
        controls["Command1"].As<TemplatedControl>().FontSize.Should().BeApproximately(12.8, 1e-9);
        controls["Label2"].As<TemplatedControl>().FontSize.Should().Be(11);
    }
}
