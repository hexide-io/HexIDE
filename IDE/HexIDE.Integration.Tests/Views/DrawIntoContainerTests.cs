using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control drawn inside a Frame or PictureBox goes into the innermost one under the point the drag began from, and the
/// drag is held to it (#2039). Every case is vb6.exe's, drawn with the mouse in its designer on the oracle VM's console, in
/// the same form: Frame1 at 600, 600, 3600 × 2400, holding Picture1 at 1800, 600, 1500 × 1200, and Command0 on the form.
/// The grid is VB6's default, 120 twips, which is 8 pixels.
/// </summary>
/// <remarks>
/// VB6 ends a drawn control one pixel past its far grid line, which HexIDE doesn't yet (#2058). Each expected size below
/// is vb6.exe's less that pixel, 15 twips, and says so.
/// </remarks>
public class DrawIntoContainerTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private const string Probe = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   4500
           ClientWidth     =   6000
           ScaleHeight     =   4500
           ScaleWidth      =   6000
           Begin VB.CommandButton Command0
              Caption         =   "Command0"
              Height          =   495
              Left            =   4320
              TabIndex        =   2
              Top             =   3600
              Width           =   1215
           End
           Begin VB.Frame Frame1
              Caption         =   "Frame1"
              Height          =   2400
              Left            =   600
              TabIndex        =   0
              Top             =   600
              Width           =   3600
              Begin VB.PictureBox Picture1
                 Height          =   1200
                 Left            =   1800
                 ScaleHeight     =   1140
                 ScaleWidth      =   1440
                 TabIndex        =   1
                 Top             =   600
                 Width           =   1500
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Frame1's top-left on the canvas, in pixels, and Picture1's client origin inside its 2-pixel border.
    private static readonly Point Frame = new(40, 40);
    private static readonly Point PictureClient = new(40 + 120 + 2, 40 + 40 + 2);

    private static (FormEditViewModel vm, FormDefinition form) Designer()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Probe, new NullSink())!;
        project.AddForm(form);
        var settings = Substitute.For<ISettingsService>();
        settings.AlignToGrid.Returns(true);
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            settings, Substitute.For<ILocalizationService>()).Initialize(form);
        return (vm, form);
    }

    private static Rect At(Point origin, double left, double top, double width, double height) =>
        new(origin.X + left, origin.Y + top, width, height);

    private static (string? Container, int Left, int Top, int Width, int Height) Drawn(FormEditViewModel vm, Rect rect, Point? pressedAt)
    {
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, rect, pressedAt);
        vm.CommitLayout();
        var placed = vm.SelectedComponent!.Instance;
        return (placed.Container?.GetPropertyOrDefault(VBProperties.NameProperty),
            Twips(placed.GetPropertyOrDefault(VBProperties.LeftProperty)), Twips(placed.GetPropertyOrDefault(VBProperties.TopProperty)),
            Twips(placed.GetPropertyOrDefault(VBProperties.WidthProperty)), Twips(placed.GetPropertyOrDefault(VBProperties.HeightProperty)));

        static int Twips(double pixels) => (int)System.Math.Round(pixels * 15);
    }

    // a: vb6.exe saved Frame1's Command1 at 240, 1200, 1215 × 735.
    [AvaloniaFact]
    public void A_control_drawn_inside_a_Frame_goes_into_it()
    {
        var (vm, _) = Designer();
        var rect = At(Frame, 16, 80, 80, 48);

        Drawn(vm, rect, rect.TopLeft).Should().Be(("Frame1", 240, 1200, 1215 - 15, 735 - 15));
    }

    // b: pressed inside Frame1 and released on the form beyond it. vb6.exe saved 480, 1920, 3135 × 495: the far corner
    // stopped at the Frame's 240 × 160 pixels.
    [AvaloniaFact]
    public void A_drag_begun_in_a_Frame_is_held_to_it()
    {
        var (vm, _) = Designer();
        var rect = At(Frame, 32, 128, 400, 64);

        Drawn(vm, rect, rect.TopLeft).Should().Be(("Frame1", 480, 1920, 3135 - 15, 495 - 15));
    }

    // c: pressed on the form above Frame1 and released inside it. vb6.exe saved it on the form, 240, 240, 1455 × 1455.
    [AvaloniaFact]
    public void A_drag_begun_on_the_form_stays_on_the_form_wherever_it_ends()
    {
        var (vm, _) = Designer();
        var rect = new Rect(16, 16, 96, 96);

        Drawn(vm, rect, rect.TopLeft).Should().Be((null, 240, 240, 1455 - 15, 1455 - 15));
    }

    // d: inside Picture1, inside Frame1. vb6.exe saved Picture1's Command4 at 240, 240, 855 × 615.
    [AvaloniaFact]
    public void A_control_drawn_inside_a_nested_PictureBox_goes_into_the_innermost_container()
    {
        var (vm, _) = Designer();
        var rect = At(PictureClient, 16, 16, 56, 40);

        Drawn(vm, rect, rect.TopLeft).Should().Be(("Picture1", 240, 240, 855 - 15, 615 - 15));
    }

    // i: pressed inside Picture1 and released far outside Frame1. vb6.exe saved 240, 240, 1215 × 975: from its client
    // origin the drag stopped at Picture1's outer 100 × 80 pixels snapped down to the grid, 96 × 80, past its 96 × 76
    // client area.
    [AvaloniaFact]
    public void A_drag_begun_in_a_PictureBox_stops_at_its_outer_size_snapped_down()
    {
        var (vm, _) = Designer();
        var rect = At(PictureClient, 16, 16, 400, 300);

        Drawn(vm, rect, rect.TopLeft).Should().Be(("Picture1", 240, 240, 1215 - 15, 975 - 15));
    }

    // k: pressed inside Picture1 and dragged up and left out of it, over Frame1. vb6.exe saved it in Picture1 at 0, 0,
    // 1095 × 855: held at Picture1's client origin, from its pressed corner at 72, 56.
    [AvaloniaFact]
    public void A_drag_out_of_a_container_up_and_left_stops_at_its_client_origin()
    {
        var (vm, _) = Designer();
        var rect = At(PictureClient, -40, -30, 112, 86);

        Drawn(vm, rect, new Point(PictureClient.X + 72, PictureClient.Y + 56)).Should().Be(("Picture1", 0, 0, 1095 - 15, 855 - 15));
    }

    // k again, drawn through the designer itself: the view hands over where the drag began, not its rectangle's top-left,
    // which here lies in Frame1.
    [AvaloniaFact]
    public void A_drag_drawn_in_the_designer_goes_into_the_container_it_began_in()
    {
        var (vm, _) = Designer();
        vm.ToolsBoxToolViewModel.SelectedComponent =
            vm.ToolsBoxToolViewModel.Components.Single(c => c.BaseClass is CommandButtonComponentClass);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var spawner = view.FindControl<Canvas>("NewControlSpawner")!;
            Point OnWindow(Point canvas) => spawner.TranslatePoint(canvas, window)!.Value;

            window.MouseDown(OnWindow(new Point(PictureClient.X + 72, PictureClient.Y + 56)), MouseButton.Left);
            window.MouseMove(OnWindow(new Point(Frame.X + 60, Frame.Y + 30)));
            window.MouseMove(OnWindow(new Point(Frame.X + 16, Frame.Y + 16)));
            window.MouseUp(OnWindow(new Point(Frame.X + 16, Frame.Y + 16)), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            vm.CommitLayout();

            var placed = vm.AllComponents.Single(c => c.Name == "Command1").Instance;
            placed.Container?.GetPropertyOrDefault(VBProperties.NameProperty).Should().Be("Picture1");
            (placed.GetPropertyOrDefault(VBProperties.LeftProperty), placed.GetPropertyOrDefault(VBProperties.TopProperty),
                placed.GetPropertyOrDefault(VBProperties.WidthProperty), placed.GetPropertyOrDefault(VBProperties.HeightProperty))
                .Should().Be((0.0, 0.0, 72.0, 56.0), "vb6.exe's 1095 × 855 is 73 × 57 pixels, one more each way (#2058)");
        }
        finally
        {
            window.Close();
        }
    }

    // e and g: a double-click on the toolbox places on the form, whatever container is selected (measured).
    [AvaloniaFact]
    public void A_control_placed_by_double_click_goes_on_the_form()
    {
        var (vm, _) = Designer();
        vm.SelectedComponent = vm.AllComponents.Single(c => c.Name == "Frame1");

        vm.SpawnControlCenter(CommandButtonComponentClass.Instance);

        (vm.SelectedComponent!.Instance.Container is null or { BaseClass: FormComponentClass }).Should().BeTrue("it is on the form");
    }

    // A drawn control is its container's first Begin block, as on the form (#1481), and undoing it leaves Frame1 as it was.
    [AvaloniaFact]
    public void A_drawn_control_is_its_containers_first_block_and_undo_takes_it_out()
    {
        var (vm, form) = Designer();
        var rect = At(Frame, 16, 80, 80, 48);
        Drawn(vm, rect, rect.TopLeft);

        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().MatchRegex(@"Begin VB\.Frame Frame1\s*\r\n(?:[^\r\n]*=[^\r\n]*\r\n)*\s*Begin VB\.CommandButton Command1");
        // On top within Frame1, and still under Command0, the form's top control, whose block comes before Frame1's: a
        // container's contents are drawn with it, so nothing in Frame1 rises over a control on top of Frame1.
        var order = vm.Components.Select(c => c.Name).ToList();
        order.IndexOf("Command1").Should().BeGreaterThan(order.IndexOf("Picture1"))
            .And.BeLessThan(order.IndexOf("Command0"));

        vm.UndoStack.Undo();
        vm.CommitLayout();
        form.Components.Should().NotContain(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Command1");
    }
}
