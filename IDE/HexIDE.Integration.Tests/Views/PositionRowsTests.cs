using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Properties window shows a control's Left, Top, Width and Height as VB6's does, in its container's ScaleMode, and a
/// form's own size with its frame, in twips; and a value typed in is in those units (#1636). It showed the model's
/// pixels. The values are VB6's for the same form (the oracle's "What VB6's Properties window shows a position in").
/// </summary>
public class PositionRowsTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // A pixel form holding a TextBox, and a Frame holding another, as the first probe's Form2.
    private const string PixelForm =
        "VERSION 5.00\r\nBegin VB.Form Form2 \r\n   Caption         =   \"Form2\"\r\n   ClientHeight    =   3090\r\n"
      + "   ClientLeft      =   60\r\n   ClientTop       =   450\r\n   ClientWidth     =   4680\r\n   ScaleHeight     =   206\r\n"
      + "   ScaleMode       =   3  'Pixel\r\n   ScaleWidth      =   312\r\n"
      + "   Begin VB.Frame Frame2 \r\n      Caption         =   \"Frame2\"\r\n      Height          =   1500\r\n"
      + "      Left            =   2640\r\n      Top             =   840\r\n      Width           =   1800\r\n"
      + "      Begin VB.TextBox Text6 \r\n         Height          =   285\r\n         Left            =   240\r\n"
      + "         Text            =   \"Text6\"\r\n         Top             =   360\r\n         Width           =   1215\r\n      End\r\n"
      + "   End\r\n"
      + "   Begin VB.TextBox Text5 \r\n      Height          =   495\r\n      Left            =   120\r\n"
      + "      Text            =   \"Text5\"\r\n      Top             =   120\r\n      Width           =   1215\r\n   End\r\n"
      + "End\r\nAttribute VB_Name = \"Form2\"\r\n";

    private static (FormDefinition File, FormEditViewModel Designer, PropertiesToolViewModel Window) Open(string source = PixelForm)
    {
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var designer = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var window = new PropertiesToolViewModel(dock, Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), localization);
        return (file, designer, window);
    }

    private static double[] Rows(PropertiesToolViewModel window) =>
        [.. new[] { "Left", "Top", "Width", "Height" }.Select(name => (double)window.Properties.Single(p => p.Name == name).Value!)];

    private static void Select(FormEditViewModel designer, string name) =>
        designer.SelectedComponent = designer.AllComponents.Single(c => c.Name == name);

    [AvaloniaTheory]
    [InlineData("Text5", 8, 8, 81, 33)]          // on a pixel form: pixels
    [InlineData("Frame2", 176, 56, 120, 100)]
    [InlineData("Text6", 240, 360, 1215, 285)]   // in a Frame: twips
    [InlineData("Form2", -60, -15, 4920, 3675)]  // the form's own: twips, with its frame
    public void Each_row_is_in_the_units_VB6s_window_shows(string name, double left, double top, double width, double height)
    {
        var (_, designer, window) = Open();

        Select(designer, name);

        Rows(window).Should().Equal(left, top, width, height);
    }

    // Typed in pixels on a pixel form, rounded to whole twips, half to even: 10.5 pixels is 158 twips.
    [AvaloniaFact]
    public void A_value_typed_in_is_in_the_rows_units()
    {
        var (file, designer, window) = Open();
        Select(designer, "Text5");

        window.Properties.Single(p => p.Name == "Left").Value = "10.5";

        var text5 = file.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Text5");
        text5.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(158 / 15d);
        new FormSerializer().Serialize(file, "Form2.frm").Item1.Should().Contain("      Left            =   158\r\n");
        window.Properties.Single(p => p.Name == "Left").Value.Should().Be(10.533);
    }

    // The form's own Width is its client area and its frame, so typing VB6's 4920 keeps the 4680 client.
    [AvaloniaFact]
    public void The_forms_own_Width_takes_its_frame_back_off()
    {
        var (file, designer, window) = Open();
        Select(designer, "Form2");

        window.Properties.Single(p => p.Name == "Width").Value = "5040";

        new FormSerializer().Serialize(file, "Form2.frm").Item1.Should().Contain("   ClientWidth     =   4800\r\n");
        Rows(window)[2].Should().Be(5040);
    }

    // A Fixed Single form with no control box has a caption bar only while it has a caption (measured), so clearing the
    // Caption takes 345 twips off the Height its row shows: 3525, then 3120.
    [AvaloniaFact]
    public void A_row_follows_a_change_that_changes_the_frame()
    {
        var (_, designer, window) = Open(
            "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   BorderStyle     =   1  'Fixed Single\r\n   Caption         =   \"Form1\"\r\n"
          + "   ClientHeight    =   3090\r\n   ClientLeft      =   60\r\n   ClientTop       =   450\r\n   ClientWidth     =   4680\r\n"
          + "   ControlBox      =   0   'False\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        Select(designer, "Form1");
        Rows(window)[3].Should().Be(3525);

        window.Properties.Single(p => p.Name == "Caption").Value = "";

        Rows(window)[3].Should().Be(3120);
    }
}
