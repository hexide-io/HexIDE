using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The designer draws the box for a control it carries as text (#157) in that control's place in the stack, among the
/// controls it models, as VB6 stacks a control it knows (#1553). The box used to be drawn over every modelled control, so
/// one brought in front of it was saved in front of it (#1498) but still drawn under it.
/// </summary>
public class CarriedBoxStackingTests
{
    // An unregistered ImageList stand-in (#1083 models the real one) is 280, 155 and 80 x 32 pixels. Command2 sits inside it, and is written after it, so it is under it.
    // In the frame, Command3 is written before Gauge1, so it is over it, and both are over the frame.
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
        "      Begin VB.CommandButton Command3 \r\n" +
        "         Caption         =   \"Command3\"\r\n" +
        "         Height          =   600\r\n" +
        "         Left            =   300\r\n" +
        "         TabIndex        =   1\r\n" +
        "         Top             =   300\r\n" +
        "         Width           =   600\r\n" +
        "      End\r\n" +
        "      Begin NoSuchLib.Gauge Gauge1 \r\n" +
        "         Height          =   300\r\n" +
        "         Left            =   150\r\n" +
        "         TabIndex        =   2\r\n" +
        "         Top             =   450\r\n" +
        "         Width           =   2400\r\n" +
        "         _ExtentX        =   4233\r\n" +
        "         _ExtentY        =   529\r\n" +
        "         _Version        =   393216\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "   Begin NoSuchLib.ImageList imlIcons \r\n" +
        "      Left            =   4200\r\n" +
        "      Top             =   2325\r\n" +
        "      _ExtentX        =   1005\r\n" +
        "      _ExtentY        =   1005\r\n" +
        "      _Version        =   393216\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton Command2 \r\n" +
        "      Caption         =   \"Command2\"\r\n" +
        "      Height          =   300\r\n" +
        "      Left            =   4350\r\n" +
        "      TabIndex        =   3\r\n" +
        "      Top             =   2400\r\n" +
        "      Width           =   600\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    // The same ImageList and Command2, alone: Command2 is the only control the designer models, so it is already at the top
    // of the canvas list, and Bring to Front changes nothing there but how many carried controls are under it.
    private const string Alone =
        "VERSION 5.00\r\n" +
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   4500\r\n" +
        "   ClientLeft      =   60\r\n" +
        "   ClientTop       =   345\r\n" +
        "   ClientWidth     =   6000\r\n" +
        "   Begin NoSuchLib.ImageList imlIcons \r\n" +
        "      Left            =   4200\r\n" +
        "      Top             =   2325\r\n" +
        "      _ExtentX        =   1005\r\n" +
        "      _ExtentY        =   1005\r\n" +
        "      _Version        =   393216\r\n" +
        "   End\r\n" +
        "   Begin VB.CommandButton Command2 \r\n" +
        "      Caption         =   \"Command2\"\r\n" +
        "      Height          =   300\r\n" +
        "      Left            =   4350\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   2400\r\n" +
        "      Width           =   600\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private sealed record Designed(FormEditViewModel Vm, FormEditView View, Window Window);

    private static Designed Open(string source = Form)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(), null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        // The pointer tool's overlay is hidden, as the IDE hides it; this designer has no toolbox to say so.
        view.FindControl<Canvas>("NewControlSpawner")!.IsVisible = false;
        Render();
        return new Designed(vm, view, window);
    }

    // A hit test is against what has been rendered (#1065's rule): a frame is forced, not left to the headless timer.
    private static void Render()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static Visual Box(Designed d, string name) =>
        d.View.GetVisualDescendants().OfType<Classic.Avalonia.Theme.ClassicBorderDecorator>()
            .Single(b => AutomationProperties.GetName(b) == name);

    private static Visual Item(Designed d, string name) =>
        d.View.GetVisualDescendants().OfType<ControlItem>().Single(i => AutomationProperties.GetName(i) == name);

    // Whether the point is the visual's to take, nothing drawn over it there.
    private static bool Topmost(Designed d, Visual visual, Point inside)
    {
        var hit = d.Window.InputHitTest(visual.TranslatePoint(inside, d.Window)!.Value) as Visual;
        return hit == visual || hit?.GetVisualAncestors().Contains(visual) == true;
    }

    [AvaloniaFact]
    public void A_control_written_after_a_carried_one_is_drawn_under_its_box()
    {
        var d = Open();
        try
        {
            Topmost(d, Box(d, "imlIcons"), new Point(30, 15)).Should().BeTrue("Command2 is written after it, so it is under it");
        }
        finally { d.Window.Close(); }
    }

    [AvaloniaFact]
    public void Bring_to_Front_draws_the_control_over_the_box_and_Send_to_Back_under_it()
    {
        var d = Open();
        try
        {
            var command2 = d.Vm.AllComponents.Single(c => c.Name == "Command2");

            d.Vm.BringToFront(command2);
            Render();
            Topmost(d, Item(d, "Command2"), new Point(20, 10)).Should().BeTrue("it was brought in front of the ImageList");

            d.Vm.SendToBack(command2);
            Render();
            Topmost(d, Box(d, "imlIcons"), new Point(30, 15)).Should().BeTrue("and sent back behind it again");

            d.Vm.UndoStack.Undo();
            Render();
            Topmost(d, Item(d, "Command2"), new Point(20, 10)).Should().BeTrue("undoing Send to Back brings it in front again");
        }
        finally { d.Window.Close(); }
    }

    [AvaloniaFact]
    public void Bring_to_Front_on_the_only_modelled_control_still_draws_it_over_the_box()
    {
        var d = Open(Alone);
        try
        {
            Topmost(d, Box(d, "imlIcons"), new Point(30, 15)).Should().BeTrue("the premise: Command2 is under the ImageList");

            d.Vm.BringToFront(d.Vm.AllComponents.Single(c => c.Name == "Command2"));
            Render();

            Topmost(d, Item(d, "Command2"), new Point(20, 10)).Should().BeTrue(
                "nothing moved on the canvas list, but the ImageList is under it now");
        }
        finally { d.Window.Close(); }
    }

    // The designer's view is reused across its tabs, so the boxes follow the form the view is given, not the first one.
    [AvaloniaFact]
    public void The_boxes_follow_the_designer_the_view_is_given()
    {
        var d = Open();
        try
        {
            var other = Open(Alone);
            other.Window.Close();
            d.View.DataContext = other.Vm;
            Render();

            d.View.GetVisualDescendants().OfType<Classic.Avalonia.Theme.ClassicBorderDecorator>()
                .Select(AutomationProperties.GetName).Where(name => name is "imlIcons" or "Gauge1")
                .Should().Equal("imlIcons");
        }
        finally { d.Window.Close(); }
    }

    [AvaloniaFact]
    public void In_a_frame_the_box_is_over_the_frame_and_under_a_control_written_before_it()
    {
        var d = Open();
        try
        {
            var gauge = Box(d, "Gauge1");
            // Gauge1 is at 10, 30 in the frame and 160 x 20; Command3 at 20, 20 and 40 x 40 covers its left end.
            Topmost(d, gauge, new Point(120, 10)).Should().BeTrue("the box is drawn over its frame's background");
            Topmost(d, Item(d, "Command3"), new Point(20, 20)).Should().BeTrue("Command3 is written before Gauge1, so it is over it");
        }
        finally { d.Window.Close(); }
    }
}
