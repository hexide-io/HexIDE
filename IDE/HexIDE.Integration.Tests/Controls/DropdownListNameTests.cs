using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinControls;
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

    // The text the box shows: every TextBlock in the combo's template that is showing, in a window, as the designer's
    // renderer lays it out.
    private static string[] Shown(Control combo)
    {
        var window = new Window { Width = 240, Height = 60, Content = combo };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var shown = combo.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToArray();
        window.Close();
        return shown;
    }

    private static Control ForDesigner(ComponentInstance combo) =>
        ((ComponentBaseClass)combo.BaseClass).InstantiateForDesigner(combo);

    [AvaloniaTheory]
    [InlineData("Combo1")]
    [InlineData("cboList")]
    public void The_designer_draws_a_Dropdown_List_with_its_name(string name) =>
        Shown(ForDesigner(Named(Load(), name))).Should().Equal(name);

    [AvaloniaFact]
    public void A_Text_the_file_saved_is_not_drawn() =>
        Shown(ForDesigner(Named(Load(), "Combo3"))).Should().Equal("Combo3");

    [AvaloniaFact]
    public void A_renamed_Dropdown_List_shows_its_new_name()
    {
        var combo = Named(Load(), "Combo1");

        combo.SetProperty(VBProperties.NameProperty, "cboNewCombo1");

        Shown(ForDesigner(combo)).Should().Equal("cboNewCombo1");
    }

    // The name is drawn as an item would be, in the control's ForeColor, not in the grey of a hint.
    [AvaloniaFact]
    public void The_name_is_drawn_in_the_controls_ForeColor()
    {
        var combo = (ComboBox)ForDesigner(Named(Load(), "Combo1"));

        combo.PlaceholderForeground.Should().Be(combo.Foreground);
    }

    // The running control shows nothing in the box, as vb6.exe's does with no item selected (#926).
    [AvaloniaFact]
    public void The_running_control_shows_nothing()
    {
        var combo = Named(Load(), "Combo1");

        Shown(((ComponentBaseClass)combo.BaseClass).Instantiate(combo)).Should().BeEmpty();
    }

    // A Dropdown Combo's box is its Text, in the designer as before.
    [AvaloniaFact]
    public void A_Dropdown_Combo_still_shows_its_Text()
    {
        var combo = (ComboBox)ForDesigner(Named(Load(), "Combo4"));

        combo.PlaceholderText.Should().BeNull();
        combo.Text.Should().Be("Combo4");
    }

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
            bitmap.Save(png);
            window.Close();
            return png.ToArray();
        }

        Rendered("Combo1").Should().NotEqual(Rendered("cboList"));
    }
}
