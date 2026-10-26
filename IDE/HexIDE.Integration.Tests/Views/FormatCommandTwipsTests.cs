using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The Format commands that halve or divide a distance put a control where VB6's own do, in whole twips (#929). They
/// worked in fractional pixels, so a control centred on an odd difference landed at x.5 px, and a save wrote it as a
/// fractional twip, such as <c>Left = 4192.5</c>, which VB6 never holds.
/// </summary>
/// <remarks>
/// Every expected value here is VB6's, measured in its IDE (docs/vb6-fidelity-oracle/where-the-format-commands-put-a-control.md, "Where the Format commands
/// put a control"). VB6 works in whole twips and halves each size on its own, truncating: Center in Form gives
/// ScaleWidth\2 - Width\2, Align Centers gives the reference's Left + its Width\2 - the control's Width\2, and Make
/// Equal spacing truncates the gap and leaves the last control where it was. Each case is one where the exact answer is
/// a half twip, so a rule that rounds instead, either way, fails one of them.
/// </remarks>
public class FormatCommandTwipsTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static string Form(int clientWidth, int clientHeight, params (string Name, int Left, int Top, int Width, int Height)[] buttons) =>
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        $"   ClientHeight    =   {clientHeight}\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        $"   ClientWidth     =   {clientWidth}\r\n" +
        $"   ScaleHeight     =   {clientHeight}\r\n" +
        $"   ScaleWidth      =   {clientWidth}\r\n" +
        string.Concat(buttons.Select((b, i) =>
            $"   Begin VB.CommandButton {b.Name} \r\n" +
            $"      Caption         =   \"{b.Name}\"\r\n" +
            $"      Height          =   {b.Height}\r\n" +
            $"      Left            =   {b.Left}\r\n" +
            $"      TabIndex        =   {i}\r\n" +
            $"      Top             =   {b.Top}\r\n" +
            $"      Width           =   {b.Width}\r\n" +
            "   End\r\n")) +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static (FormEditViewModel Vm, FormDefinition Form) Open(string source, bool alignToGrid = true)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        var settings = Substitute.For<ISettingsService>();
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        settings.AlignToGrid.Returns(alignToGrid);
        // A toolbox, because adding a control puts the pointer tool back.
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static ComponentInstanceViewModel Control(FormEditViewModel vm, string name) =>
        vm.AllComponents.Single(c => c.Name == name);

    // Selects the controls, the last named as the reference, as VB6's last-clicked control is.
    private static void Select(FormEditViewModel vm, params string[] names)
    {
        vm.SetSelectedComponents(names.Select(n => Control(vm, n)));
        vm.SelectedComponent = Control(vm, names[^1]);
    }

    // The saved line, as text: a fractional twip would come out as "4192.5", which is the defect.
    private static string Saved(FormEditViewModel vm, FormDefinition form, string control, string property)
    {
        vm.CommitLayout();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        var block = text[text.IndexOf($" {control} \r\n", System.StringComparison.Ordinal)..];
        return Regex.Match(block, $@"^\s*{property}\s*=\s*(\S+)", RegexOptions.Multiline).Groups[1].Value;
    }

    [AvaloniaTheory]
    [InlineData(1215, "1733")]   // 2340 - 607: the exact 1732.5 goes up
    [InlineData(1200, "1740")]
    [InlineData(1210, "1735")]   // not a whole pixel wide; the exact answer is whole twips
    public void Center_in_Form_horizontally_puts_a_control_where_VB6_does(int width, string expected)
    {
        var (vm, form) = Open(Form(4680, 3195, ("B", 120, 120, width, 495)));
        Select(vm, "B");

        vm.CenterHorizontally();

        Saved(vm, form, "B", "Left").Should().Be(expected);
    }

    [AvaloniaFact]
    public void Center_in_Form_vertically_truncates_each_half_so_the_exact_half_twip_goes_down()
    {
        // 3195\2 - 510\2 = 1597 - 255 = 1342, where across, 1732.5 went up: a rounding rule fits one or the other.
        var (vm, form) = Open(Form(4680, 3195, ("B", 120, 120, 1215, 510)));
        Select(vm, "B");

        vm.CenterVertically();

        Saved(vm, form, "B", "Top").Should().Be("1342");
    }

    [AvaloniaFact]
    public void Align_Centers_and_Middles_put_a_control_where_VB6_does()
    {
        var (vm, form) = Open(Form(6000, 4500, ("R", 600, 300, 1200, 495), ("O", 3600, 1200, 1215, 510)));
        Select(vm, "O", "R");

        vm.AlignCentersH();
        vm.AlignCentersV();

        Saved(vm, form, "O", "Left").Should().Be("593", "600 + 1200\\2 - 1215\\2, where the exact answer is 592.5");
        Saved(vm, form, "O", "Top").Should().Be("292", "300 + 495\\2 - 510\\2, where the exact answer is 292.5");
        Saved(vm, form, "R", "Left").Should().Be("600", "the reference does not move");
    }

    [AvaloniaFact]
    public void Make_Equal_horizontal_spacing_truncates_the_gap_and_leaves_the_last_control()
    {
        var (vm, form) = Open(Form(6000, 4500,
            ("A", 300, 3000, 1200, 495), ("B", 2000, 3000, 1200, 495), ("C", 4005, 3000, 1200, 495)));
        Select(vm, "A", "B", "C");

        vm.MakeHorizontalSpacingEqual();

        Saved(vm, form, "A", "Left").Should().Be("300");
        Saved(vm, form, "B", "Left").Should().Be("2152", "a gap of 1305\\2 = 652, where the exact one is 652.5");
        Saved(vm, form, "C", "Left").Should().Be("4005", "the last control stays, and its gap takes the twip left over");
    }

    [AvaloniaFact]
    public void Make_Equal_vertical_spacing_truncates_the_gap_and_leaves_the_last_control()
    {
        // The horizontal case turned through a right angle; VB6 measured only across.
        var (vm, form) = Open(Form(6000, 6000,
            ("A", 300, 300, 1200, 495), ("B", 300, 2000, 1200, 495), ("C", 300, 4005, 1200, 495)));
        Select(vm, "A", "B", "C");

        vm.MakeVerticalSpacingEqual();

        Saved(vm, form, "B", "Top").Should().Be("2152", "a gap of (4500 - 300 - 1485)\\2 = 1357, after A's bottom at 795");
        Saved(vm, form, "C", "Top").Should().Be("4005");
    }

    [AvaloniaFact]
    public void A_control_added_by_double_clicking_the_toolbox_is_centred_then_put_on_the_grid()
    {
        // VB6 put a 1215 x 495 button on a 6000 x 4500 form at 2400, 2040: centred at 3000 - 607 = 2393 and
        // 2250 - 247 = 2003, then on the nearest 120-twip grid line.
        var (vm, form) = Open(Form(6000, 4500));

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        var name = vm.AllComponents.Single(c => c.Instance.BaseClass is CommandButtonComponentClass).Name;
        Saved(vm, form, name, "Left").Should().Be("2400");
        Saved(vm, form, name, "Top").Should().Be("2040");
    }

    [AvaloniaFact]
    public void An_added_control_centred_exactly_between_two_grid_lines_goes_to_the_higher()
    {
        // VB6 put a button whose centre was 12.5 grid units down at 13 (measured). On a 4694 x 3974 form a 1215 x 495
        // button centres at 2347 - 607 = 1740 and 1987 - 247 = 1740, 14.5 units of 120: the higher line is 1800, and
        // to-even gives 1680.
        var (vm, form) = Open(Form(4694, 3974));

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        var name = vm.AllComponents.Single(c => c.Instance.BaseClass is CommandButtonComponentClass).Name;
        Saved(vm, form, name, "Left").Should().Be("1800");
        Saved(vm, form, name, "Top").Should().Be("1800");
    }

    [AvaloniaFact]
    public void Without_Align_to_Grid_the_added_control_is_centred_in_whole_twips()
    {
        var (vm, form) = Open(Form(6000, 4500), alignToGrid: false);

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        var name = vm.AllComponents.Single(c => c.Instance.BaseClass is CommandButtonComponentClass).Name;
        Saved(vm, form, name, "Left").Should().Be("2393");
        Saved(vm, form, name, "Top").Should().Be("2003");
    }

    // Each control VB6 added by a double-click on its tool, on a 6000 x 4500 form with the 120-twip grid, as it saved it
    // (docs/vb6-fidelity-oracle/the-size-of-a-control-added-from-the-toolbox.md, "The size of a control added from the toolbox"): its own size, centred by that size,
    // then on the grid.
    [AvaloniaTheory]
    [InlineData("CommandButton", "1215", "495", "2400", "2040")]
    [InlineData("Label", "1215", "495", "2400", "2040")]
    [InlineData("TextBox", "1215", "495", "2400", "2040")]
    [InlineData("Frame", "1215", "495", "2400", "2040")]
    [InlineData("CheckBox", "1215", "495", "2400", "2040")]
    [InlineData("OptionButton", "1215", "495", "2400", "2040")]
    [InlineData("PictureBox", "1215", "495", "2400", "2040")]
    [InlineData("Shape", "1215", "495", "2400", "2040")]
    [InlineData("ComboBox", "1215", "315", "2400", "2040")]
    [InlineData("ListBox", "1215", "450", "2400", "2040")]
    [InlineData("HScrollBar", "1215", "255", "2400", "2160")]
    [InlineData("VScrollBar", "255", "1335", "2880", "1560")]
    [InlineData("Timer", "", "", "2400", "2040")]
    public void A_control_added_from_the_toolbox_takes_the_size_VB6_gives_its_class(
        string vbClass, string width, string height, string left, string top)
    {
        var type = (ComponentBaseClass)typeof(ComponentBaseClass).Assembly
            .GetType($"HexIDE.Runtime.Components.{vbClass}ComponentClass")!
            .GetProperty("Instance")!.GetValue(null)!;
        var (vm, form) = Open(Form(6000, 4500));

        vm.SpawnControlCenter(type);

        var name = vm.AllComponents.Single(c => c.Instance.BaseClass == type).Name;
        (Saved(vm, form, name, "Width"), Saved(vm, form, name, "Height"), Saved(vm, form, name, "Left"),
            Saved(vm, form, name, "Top")).Should().Be((width, height, left, top));
    }
}
