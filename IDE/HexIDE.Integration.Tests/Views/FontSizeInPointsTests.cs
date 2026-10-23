using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

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

    [AvaloniaFact]
    public void The_Font_dialog_keeps_a_fractional_size_and_what_it_has_no_say_over()
    {
        var font = new VBFont("Tahoma", 9.6, charset: 128, underline: true, strikethrough: true, weight: 600);
        static FontDialogResult Answer(string family, FontWeight weight, double size) =>
            new(new FontFamily(family), FontStyle.Normal, weight, size);

        // OK on what it opened with: nothing changes, where the size used to come back 9 and the rest reset.
        PropertyFontBox.Chosen(font, Answer("Tahoma", FontWeight.Normal, 9.6)).Should().Be(font);
        // Bold, chosen: the weight is the bold one, and the rest is kept.
        PropertyFontBox.Chosen(font, Answer("Tahoma", FontWeight.Bold, 9.6)).Should()
            .Be(new VBFont("Tahoma", 9.6, charset: 128, underline: true, strikethrough: true, weight: 700));
        // A new family takes charset 0, as it did.
        PropertyFontBox.Chosen(font, Answer("Arial", FontWeight.Normal, 8.25)).Should()
            .Be(new VBFont("Arial", 8.25, underline: true, strikethrough: true, weight: 600));
    }

    [AvaloniaFact]
    public void The_Font_dialog_offers_the_font_s_own_size_and_draws_its_sample_in_points()
    {
        var odd = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 8.25));
        odd.FontSizes.Should().ContainInOrder(8, 8.25, 9).And.BeInAscendingOrder();
        odd.SelectedSize.Should().Be(8.25);
        odd.PreviewFontSize.Should().Be(11);

        var usual = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 12));
        usual.FontSizes.Should().Equal(8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72);
        usual.PreviewFontSize.Should().Be(16);

        // And the sample redraws when another size or style is chosen. A preview that read its property's field was
        // never told of the change, so the sample kept the size and style the dialog opened with.
        var raised = new System.Collections.Generic.List<string?>();
        usual.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        usual.SelectedSize = 24;
        usual.SelectedStyle = "Bold Italic";
        raised.Should().Contain(nameof(FontDialogViewModel.PreviewFontSize))
            .And.Contain(nameof(FontDialogViewModel.PreviewFontStyle))
            .And.Contain(nameof(FontDialogViewModel.PreviewFontWeight));
        usual.PreviewFontSize.Should().Be(32);
        usual.PreviewFontStyle.Should().Be(FontStyle.Italic);
        usual.PreviewFontWeight.Should().Be(FontWeight.Bold);
    }

    [AvaloniaFact]
    public void The_Font_dialog_s_sample_draws_in_the_face_chosen()
    {
        var vm = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 12));
        var window = new Window { Content = new HexIDE.Forms.Views.FontDialog { DataContext = vm } };
        window.Show();
        try
        {
            var sample = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "AaBbCcDdEe 0123");
            sample.FontFamily.ToString().Should().Be("Arial", "it opened on Arial");

            vm.SelectedFamily = "Courier New";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            sample.FontFamily.ToString().Should().Be("Courier New", "Courier New was chosen");
        }
        finally { window.Close(); }
    }
}
