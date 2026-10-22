using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control HexIDE does not model is drawn in the designer as a box where VB6 draws one, and nothing else (#157).
/// </summary>
/// <remarks>
/// The box is view-only: it is in neither of the designer's component lists, so nothing selects, moves or writes it,
/// and the control still goes out once, from the block it was preserved as. Its size is VB6's (UnmodelledControl):
/// the ImageList below records only extents, and VB6 draws it 1200 x 480 twips, 80 x 32 in the designer's pixels.
/// </remarks>
public class UnmodelledControlPlaceholderTests
{
    private const string Form =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin VB.Frame Frame1 \r\n" +
        "      Caption         =   \"Frame1\"\r\n" +
        "      Height          =   1800\r\n" +
        "      Left            =   600\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   1500\r\n" +
        "      Width           =   3000\r\n" +
        "      Begin NoSuchLib.Gauge Gauge1 \r\n" +
        "         Height          =   300\r\n" +
        "         Left            =   150\r\n" +
        "         TabIndex        =   1\r\n" +
        "         Top             =   450\r\n" +
        "         Width           =   2400\r\n" +
        "         _ExtentX        =   4233\r\n" +
        "         _ExtentY        =   529\r\n" +
        "         _Version        =   393216\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "      Height          =   495\r\n" +
        "      Left            =   3900\r\n" +
        "      TabIndex        =   2\r\n" +
        "      Top             =   300\r\n" +
        "      Width           =   1500\r\n" +
        "   End\r\n" +
        "   Begin MSComctlLib.ImageList imlIcons \r\n" +
        "      Left            =   4200\r\n" +
        "      Top             =   2325\r\n" +
        "      _ExtentX        =   1005\r\n" +
        "      _ExtentY        =   1005\r\n" +
        "      _Version        =   393216\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormEditViewModel Designer(out FormDefinition form, string source = Form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        return new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    [AvaloniaFact]
    public void Each_unmodelled_control_has_a_box_where_VB6_draws_one_and_is_no_component()
    {
        var vm = Designer(out _);

        vm.UnmodelledControls.Select(c => (c.TypeName, c.Name))
            .Should().BeEquivalentTo([("NoSuchLib.Gauge", "Gauge1"), ("MSComctlLib.ImageList", "imlIcons")]);
        vm.AllComponents.Select(c => c.Name).Should().NotContain(["Gauge1", "imlIcons"],
            "a component is what the designer edits and the writer writes; these are carried");

        var images = vm.UnmodelledControls.Single(c => c.Name == "imlIcons");
        (images.Left, images.Top, images.Width, images.Height).Should().Be((280d, 155d, 80d, 32d),
            "4200, 2325 twips, and VB6's 1200 x 480 for a control recording only its extents");
        images.ClassName.Should().Be("ImageList", "the box, 80 pixels wide by default, shows the class as VB6's TypeName does");

        // Inside the frame, measured from the frame's client origin as a modelled child is.
        var frame = vm.AllComponents.Single(c => c.Name == "Frame1");
        var gauge = vm.UnmodelledControls.Single(c => c.Name == "Gauge1");
        (gauge.Left, gauge.Top).Should().Be((frame.ClientOrigin.X + 10, frame.ClientOrigin.Y + 30));
        (gauge.Width, gauge.Height).Should().Be((160d, 20d));
    }

    // A UserControl's designer: the root is no form, and a box on it is measured from the canvas origin all the same.
    private const string UserControl =
        "VERSION 5.00\r\n" +
        "Begin VB.UserControl Dial \r\n" +
        "   ClientHeight    =   3600\r\n" +
        "   ClientLeft      =   0\r\n" +
        "   ClientTop       =   0\r\n" +
        "   ClientWidth     =   4800\r\n" +
        "   Begin MSComctlLib.ImageList imlIcons \r\n" +
        "      Left            =   3000\r\n" +
        "      Top             =   1500\r\n" +
        "      _ExtentX        =   1005\r\n" +
        "      _ExtentY        =   1005\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Dial\"\r\n";

    [AvaloniaFact]
    public void A_box_on_a_UserControl_is_measured_from_the_canvas_origin()
    {
        var vm = Designer(out _, UserControl);

        var images = vm.UnmodelledControls.Should().ContainSingle().Which;
        (images.Left, images.Top, images.Width, images.Height).Should().Be((200d, 100d, 80d, 32d));
    }

    [AvaloniaFact]
    public void A_box_follows_its_container_and_goes_and_comes_back_with_it()
    {
        var vm = Designer(out var form);
        var frame = vm.AllComponents.Single(c => c.Name == "Frame1");
        var gauge = vm.UnmodelledControls.Single(c => c.Name == "Gauge1");

        frame.Left += 20;
        gauge.Left.Should().Be(frame.ClientOrigin.X + 10, "the box sits in the frame, so it moves with it");

        vm.SelectedComponent = frame;
        vm.DeleteSelected();
        vm.UnmodelledControls.Select(c => c.Name).Should().Equal(["imlIcons"],
            "deleting a frame deletes what is in it, as VB6 does");

        vm.UndoStack.Undo();
        vm.UnmodelledControls.Select(c => c.Name).Should().BeEquivalentTo(["Gauge1", "imlIcons"]);

        // And the control itself came back with the frame, not only its box.
        vm.CommitLayout();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Split("Begin NoSuchLib.Gauge Gauge1").Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void The_control_is_written_once_from_its_block()
    {
        var vm = Designer(out var form);
        vm.CommitLayout();

        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");

        text.Split("Begin MSComctlLib.ImageList imlIcons").Should().HaveCount(2);
        text.Split("Begin NoSuchLib.Gauge Gauge1").Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void The_designer_draws_the_box_and_a_press_on_it_selects_nothing()
    {
        var vm = Designer(out _);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var layer = view.FindControl<ItemsControl>("UnmodelledControlsLayer")!;
            var boxes = layer.GetVisualDescendants().OfType<Classic.Avalonia.Theme.ClassicBorderDecorator>().ToList();
            boxes.Select(AutomationProperties.GetName).Should().BeEquivalentTo(["Gauge1", "imlIcons"],
                "automation addresses the box by the control's name, as it does a modelled control");

            var images = boxes.Single(b => AutomationProperties.GetName(b) == "imlIcons");
            images.Bounds.Size.Should().Be(new Size(80, 32));
            images.Cursor.Should().NotBeNull("the denied cursor says the box cannot be moved");

            // dump_visual_tree's match searches every node, so a caller asking for the control by name finds its box. A
            // Pane, as VB6 puts a PictureBox in its place; with no type the tree folds a border away as plumbing, name and all.
            var (matches, _, _) = HexIDE.Automation.UiAutomationDriver.Find(
                HexIDE.Automation.UiAutomationDriver.Dump(window, "Window", 60, interactiveOnly: false), "imlIcons", 10);
            matches.Should().ContainSingle().Which.Name.Should().Be("imlIcons");
            matches[0].ControlType.Should().Be("Pane");

            vm.SelectedComponent = vm.AllComponents.Single(c => c.Name == "Command1");
            // The pointer tool is selected, so the spawner's overlay is hidden, as the IDE hides it. This designer has no
            // toolbox to say so, and a press that reaches the form instead fails on that missing toolbox, loudly.
            view.FindControl<Canvas>("NewControlSpawner")!.IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            // The box inside the frame is on top of it: beneath, the frame's own background painted it over.
            var gauge = boxes.Single(b => AutomationProperties.GetName(b) == "Gauge1");
            Topmost(window, gauge, new Point(80, 10)).Should().BeTrue("the box in the frame must be drawn over the frame");

            var centre = images.TranslatePoint(new Point(40, 16), window)!.Value;
            Topmost(window, images, new Point(40, 16)).Should().BeTrue("the press must land on the box");
            window.MouseDown(centre, MouseButton.Left);
            window.MouseUp(centre, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            vm.SelectedComponent!.Name.Should().Be("Command1", "a press on the box is swallowed: it selects nothing");
        }
        finally { window.Close(); }
    }

    // Whether the point inside the box is the box's to take, nothing drawn over it there.
    private static bool Topmost(Window window, Visual box, Point inside)
    {
        var hit = window.InputHitTest(box.TranslatePoint(inside, window)!.Value) as Visual;
        return hit == box || hit?.GetVisualAncestors().Contains(box) == true;
    }
}
