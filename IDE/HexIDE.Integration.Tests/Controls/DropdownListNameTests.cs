using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Controls;

/// <summary>
/// VB6's designer draws a Dropdown List's box with the control's name (#1604). Measured in VB6's IDE on the oracle guest's
/// console (the oracle's "What VB6's designer draws in a Dropdown List's box"): a Dropdown List read from a file showed its
/// name, one whose file saved a Text showed its name and not the Text, and one renamed in the Properties window showed its
/// new name. HexIDE's designer drew the box empty, as the running control is.
/// </summary>
public class DropdownListNameTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   3090
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   4680
           ScaleHeight     =   3090
           ScaleWidth      =   4680
           Begin VB.ComboBox Combo4
              Height          =   315
              Left            =   240
              TabIndex        =   3
              Text            =   "Combo4"
              Top             =   1800
              Width           =   2400
           End
           Begin VB.ComboBox Combo3
              Height          =   315
              Left            =   240
              Style           =   2  'Dropdown List
              TabIndex        =   2
              Text            =   "x"
              Top             =   1320
              Width           =   2400
           End
           Begin VB.ComboBox cboList
              Height          =   315
              Left            =   240
              Style           =   2  'Dropdown List
              TabIndex        =   1
              Top             =   840
              Width           =   2400
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   240
              Style           =   2  'Dropdown List
              TabIndex        =   0
              Top             =   360
              Width           =   2400
           End
        End
        Attribute VB_Name = "Form1"

        """;

    private static FormDefinition Load() =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form.ReplaceLineEndings("\r\n"), new NullSink())!;

    private static ComponentInstance Named(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    // The designer's renderer draws the designer's control: two Dropdown Lists that differ only in their names draw
    // differently, where the running control would draw them alike.
    [AvaloniaFact]
    public void The_designer_renders_the_name()
    {
        var form = Load();
        var designer = new FormEditViewModel(null!, new EventBus(), null!, null!, null!, null!, Substitute.For<ILocalizationService>())
            .Initialize(form);

        byte[] Rendered(string name)
        {
            var renderer = new ControlRenderer { Width = 160, Height = 21 };
            var window = new Window { Width = 240, Height = 60, Content = renderer };
            window.Show();
            renderer.Component = new ComponentInstanceViewModel(designer, Named(form, name));
            Dispatcher.UIThread.RunJobs();
            var bitmap = (Bitmap)((Image)renderer.Child!).Source!;
            using var png = new MemoryStream();
            bitmap.Save(png, Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.Close();
            return png.ToArray();
        }

        Rendered("Combo1").Should().NotEqual(Rendered("cboList"));
    }
}
