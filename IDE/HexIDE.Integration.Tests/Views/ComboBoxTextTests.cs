using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ComboBox's Text (#926). Every expectation is vb6.exe's, native and p-code agreeing, and a Dropdown Combo (Combo1)
/// and a Simple Combo (Combo2) agreed row for row (the oracle's "A ComboBox's Text, at each Style"). A Dropdown List
/// (Combo3) is #1428's, so its Text is refused. Each run case starts from <c>apple</c>, <c>Banana</c> and <c>cherry</c>.
/// </summary>
public class ComboBoxTextTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ComboBox Combo3
              Height          =   315
              Left            =   120
              Style           =   2  'Dropdown List
              TabIndex        =   2
              Text            =   "x"
              Top             =   1320
              Width           =   2000
           End
           Begin VB.ComboBox Combo2
              Height          =   900
              Left            =   120
              Style           =   1  'Simple Combo
              TabIndex        =   1
              Text            =   "Combo2"
              Top             =   360
              Width           =   2000
           End
           Begin VB.ComboBox Combo1
              Height          =   315
              Left            =   120
              TabIndex        =   0
              Text            =   "Combo1"
              Top             =   0
              Width           =   2000
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<List<string>> Run(string control, string code, bool fill = true)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var program = "On Error Resume Next\r\n"
            + (fill ? "X.AddItem \"apple\"\r\nX.AddItem \"Banana\"\r\nX.AddItem \"cherry\"\r\n" : "")
            + code.ReplaceLineEndings("\r\n");
        var printed = await ContainerRuntimeHarness.Run(ctx, env, program.Replace("X.", control + "."));
        return printed.Select(v => VbDisplay.ToText(v, CultureInfo.InvariantCulture)).ToList();
    }

    // Each statement, then the Text and ListIndex it left, and the error it raised.
    private const string Report = "Debug.Print \"[\" & X.Text & \"] \" & X.ListIndex & \" \" & Err.Number: Err.Clear\n";

    [AvaloniaTheory]
    [InlineData("Combo1")]
    [InlineData("Combo2")]
    public async Task At_load_a_combo_s_Text_is_the_one_saved_and_a_String(string control)
    {
        var printed = await Run(control, "Debug.Print TypeName(X.Text)\n" + Report, fill: false);

        printed.Should().Equal("String", $"[{control}] -1 0");
    }

    [AvaloniaTheory]
    [InlineData("Combo1", "X.Text = \"Banana\"", "[Banana] -1 0")]
    [InlineData("Combo1", "X.Text = \"banana\"", "[banana] -1 0")]
    [InlineData("Combo1", "X.Text = \"Ban\"", "[Ban] -1 0")]
    [InlineData("Combo1", "X.Text = \"\"", "[] -1 0")]
    [InlineData("Combo1", "X.Text = 5", "[5] -1 0")]
    [InlineData("Combo1", "X.Text = Null", "[Combo1] -1 13")]
    [InlineData("Combo1", "X.ListIndex = 0", "[apple] 0 0")]
    [InlineData("Combo2", "X.Text = \"Banana\"", "[Banana] -1 0")]
    [InlineData("Combo2", "X.Text = Null", "[Combo2] -1 13")]
    [InlineData("Combo2", "X.ListIndex = 0", "[apple] 0 0")]
    public async Task Text_is_the_edit_box_s_and_an_item_s_text_selects_nothing(string control, string statement, string expected)
    {
        var printed = await Run(control, statement + "\n" + Report);

        printed.Should().Equal(expected);
    }

    // With an item selected, any other Text clears the selection, even another item's; its own changes nothing.
    [AvaloniaTheory]
    [InlineData("Combo1")]
    [InlineData("Combo2")]
    public async Task With_an_item_selected_a_new_Text_clears_the_selection_and_its_own_changes_nothing(string control)
    {
        var printed = await Run(control,
            "X.ListIndex = 1\n" + Report
            + "X.Text = \"zz\"\n" + Report
            + "X.ListIndex = 1\nX.Text = \"cherry\"\n" + Report
            + "X.ListIndex = 1\nX.Text = \"Banana\"\n" + Report
            + "X.Text = \"banana\"\n" + Report
            + "X.ListIndex = 2\nX.ListIndex = -1\n" + Report
            + "X.ListIndex = 0\nX.Clear\n" + Report);

        printed.Should().Equal("[Banana] 1 0", "[zz] -1 0", "[cherry] -1 0", "[Banana] 1 0", "[banana] -1 0", "[] -1 0", "[] -1 0");
    }

    // The two rows of #821's that a combo's Text was needed for (the oracle's "A list's members").
    [AvaloniaTheory]
    [InlineData("X.ListIndex = 1\nX.List(1) = \"B\"", "[B] 1 0")]
    [InlineData("X.ListIndex = 0\nX.RemoveItem 0", "[] -1 0")]
    public async Task The_selected_item_s_text_follows_it_when_it_is_replaced_or_removed(string statements, string expected)
    {
        var printed = await Run("Combo1", statements + "\n" + Report);

        printed.Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print X.Text\n")]
    [InlineData("X.Text = \"apple\"\n")]
    public async Task A_Dropdown_List_s_Text_is_refused_naming_the_issue(string statement)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var act = () => ContainerRuntimeHarness.Run(ctx, env, ("Combo3.AddItem \"apple\"\n" + statement.Replace("X.", "Combo3.")).ReplaceLineEndings("\r\n"));

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should()
            .Contain("Style is 2").And.Contain("#1428");
    }

    // At a combo's height, a TextBox's own padding cut its text off and showed the top of a vertical scroll bar, on the
    // running form and in the designer, which draws the same control. Its edit box scrolls only sideways, as a single-line
    // TextBox does (#914).
    [AvaloniaFact]
    public void A_combo_s_edit_box_fits_its_height_and_never_scrolls_down()
    {
        var combo = new HexIDE.Runtime.BuiltinControls.VBComboBox { Width = 145, Height = 21, Text = "Banana" };
        var window = new Avalonia.Controls.Window { Content = combo, Width = 300, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var box = combo.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().Single(b => b.Name == "PART_EditableTextBox");

            Avalonia.Controls.ScrollViewer.GetVerticalScrollBarVisibility(box).Should().Be(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
            box.Bounds.Height.Should().BeLessThanOrEqualTo(combo.Bounds.Height);
            box.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ScrollBar>().Where(b => b.IsVisible)
                .Should().NotContain(b => b.Orientation == Avalonia.Layout.Orientation.Vertical);
        }
        finally
        {
            window.Close();
        }
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    // A control drawn in the designer starts with its name as its Text, as VB6's designer writes it (the oracle's "Drawn
    // in the VB6 IDE's designer"), and has no Caption it was never given.
    [AvaloniaTheory]
    [InlineData("TextBox")]
    [InlineData("ComboBox")]
    public void A_drawn_TextBox_or_ComboBox_starts_with_its_name_as_its_Text(string kind)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;
        project.AddForm(form);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        ComponentBaseClass type = kind == "TextBox" ? TextBoxComponentClass.Instance : ComboBoxComponentClass.Instance;

        vm.SpawnControlAt(type, new Rect(10, 10, 80, 25));

        var drawn = vm.SelectedComponent!.Instance;
        drawn.GetPropertyOrDefault(VBProperties.TextProperty).Should().Be(vm.SelectedComponent.Name);
        drawn.GetAllSetProperties().Keys.Should().NotContain(VBProperties.CaptionProperty);
        vm.FlushComponentsToModel();
        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain($"      Text            =   \"{vm.SelectedComponent.Name}\"\r\n");
    }
}
