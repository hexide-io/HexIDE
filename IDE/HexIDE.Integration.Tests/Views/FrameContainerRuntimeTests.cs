using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #84 phase 4 — a Frame is a real container at run time.
///
/// Until now every control in a form was a sibling on one canvas, so a control read from inside a Frame was
/// drawn at its FRAME-relative coordinates against the FORM's origin: usually somewhere near the top-left,
/// occasionally off-screen. The .frm round-trips as of phase 3; this is the half that makes the running form
/// agree with it.
///
/// These need real headless Avalonia — layout, visual parents, effective enabled-ness — so they live here
/// rather than in the runtime unit suite.
/// </summary>
public class FrameContainerRuntimeTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private class CaptureLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string title, string defaultText)
            => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) => debug.Add(value);
    }

    // Twips divided by 15 give pixels, so the Frame lands at (20, 10) and is 200x100; Text1 is at (10, 30)
    // INSIDE it, which is (30, 40) on the form. Those are the two numbers this whole phase is about.
    private const string FrameWithChildren = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.Frame Frame1
              Caption         =   "Group"
              Left            =   300
              Top             =   150
              Width           =   3000
              Height          =   1500
              Begin VB.Label Label1
                 Caption         =   "Inside"
                 Left            =   150
                 Top             =   150
                 Width           =   1200
                 Height          =   240
              End
              Begin VB.TextBox Text1
                 Left            =   150
                 Top             =   450
                 Width           =   1200
                 Height          =   300
              End
           End
           Begin VB.CommandButton Command1
              Caption         =   "Outside"
              Left            =   300
              Top             =   1800
              Width           =   1200
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string FrameWithTimer = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Frame Frame1
              Left            =   300
              Top             =   150
              Width           =   3000
              Height          =   1500
              Begin VB.Timer Timer1
                 Interval        =   100
                 Left            =   150
                 Top             =   150
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string TwoFramesOfOptions = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Frame fraLeft
              Left            =   0
              Top             =   0
              Width           =   1500
              Height          =   1500
              Begin VB.OptionButton optLeft
                 Left            =   150
                 Top             =   150
                 Width           =   1200
                 Height          =   240
              End
           End
           Begin VB.Frame fraRight
              Left            =   1800
              Top             =   0
              Width           =   1500
              Height          =   1500
              Begin VB.OptionButton optRight
                 Left            =   150
                 Top             =   150
                 Width           =   1200
                 Height          =   240
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // A control array whose elements live INSIDE a Frame, plus a Frame that is itself an array element.
    // Both shapes are real: Treeview Listview Splitter.frm puts a two-element lblTitle array entirely inside
    // one picTitles, and ODBC Log In.frm has a Frame fraStep3 that carries Index = 0.
    private const string ArraysAndContainers = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Frame fraStep
              Index           =   0
              Left            =   300
              Top             =   150
              Width           =   3000
              Height          =   1500
              Begin VB.CommandButton Command1
                 Index           =   0
                 Caption         =   "one"
                 Left            =   150
                 Top             =   150
                 Width           =   1200
                 Height          =   375
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (Control root, Canvas canvas, ModuleExecutionContext ctx, ExecutionEnvironment env) Spawn(string frm)
    {
        var form = new FormDeserializer().Deserialize(Project, frm, NullSink.Instance)!;
        var ctx = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        var root = VBLoader.SpawnComponents(form, ctx, env);
        // SpawnComponents hands back a DockPanel of [menu, canvas]; the canvas is where controls live and
        // must stay the DockPanel's child, so anything hosting this hosts the whole root.
        var canvas = ((DockPanel)root).Children.OfType<Canvas>().Single();
        return (root, canvas, ctx, env);
    }

    /// <summary>Spawns, hosts in a real window and runs a layout pass, so positions and enabled-ness resolve.</summary>
    private static (Canvas canvas, Window window, ModuleExecutionContext ctx, ExecutionEnvironment env) Laid(string frm)
    {
        var (root, canvas, ctx, env) = Spawn(frm);
        var window = new Window { Width = 400, Height = 300, Background = Brushes.White };

        // VBFrame now has a ControlTheme of its own rather than borrowing SimpleTheme's
        // HeaderedContentControl, so without HexIDE's dictionary it gets no template, no content presenter
        // and therefore no realised children — the bare headless test app carries neither this nor the
        // SystemColors brushes it resolves, exactly as ClassicRenderTests notes.
        window.Resources.MergedDictionaries.Add(
            new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
            });
        window.Resources[Classic.CommonControls.SystemColors.ControlTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.WindowBrushKey] = new SolidColorBrush(Colors.White);
        window.Resources[Classic.CommonControls.SystemColors.WindowTextBrushKey] = new SolidColorBrush(Colors.Black);
        window.Resources[Classic.CommonControls.SystemColors.GrayTextBrushKey] = new SolidColorBrush(Color.Parse("#808080"));

        window.Content = root;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        Dispatcher.UIThread.RunJobs();
        return (canvas, window, ctx, env);
    }

    private static async Task<List<Vb6Value>> Run(ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        var debug = new List<Vb6Value>();
        await new BasicInterpreter(new CaptureLib(debug), ctx, env, code).Execute();
        return debug;
    }

    private static string? NameOf(Control c) => VBProps.GetName(c);

    private static Control Child(Canvas canvas, string name) =>
        canvas.Children.OfType<Control>().First(c => NameOf(c) == name);

    private static VBFrame Frame(Canvas canvas, string name) => (VBFrame)Child(canvas, name);

    // ── hosting ──────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFramesChildren_AreHostedByTheFrame_NotByTheForm()
    {
        var (_, canvas, _, _) = Spawn(FrameWithChildren);

        canvas.Children.OfType<Control>().Select(NameOf).Should().Equal("Frame1", "Command1");
        // The file's order inside a container is its z-order, so the host keeps it.
        Frame(canvas, "Frame1").ChildHost!.Children.OfType<Control>().Select(NameOf)
            .Should().Equal("Label1", "Text1");
    }

    [AvaloniaFact]
    public void AContainedControl_IsDrawnAtItsContainersOrigin()
    {
        var (canvas, _, _, _) = Laid(FrameWithChildren);
        var text = Child(Frame(canvas, "Frame1").ChildHost!, "Text1");

        // The whole defect in one assertion. Text1 records Left=150 twips = 10px, which is measured from the
        // Frame at 20px — so it belongs at 30px on the form. Drawn flat it landed at 10px, ten pixels from
        // the form's edge and nowhere near its Frame.
        var origin = text.TranslatePoint(new Point(0, 0), canvas)!.Value;
        origin.X.Should().BeApproximately(30, 0.5);
        origin.Y.Should().BeApproximately(40, 0.5);
    }

    [AvaloniaFact]
    public void AFramesHost_ClipsWhatOverflowsIt()
    {
        var (_, canvas, _, _) = Spawn(FrameWithChildren);

        // VB6 clips a container's contents to the container. Without this a control dragged past the frame's
        // edge would keep drawing across the form.
        Frame(canvas, "Frame1").ChildHost!.ClipToBounds.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AFramesHost_IsNotItsOwnTabNavigationScope()
    {
        var (_, canvas, _, _) = Spawn(FrameWithChildren);
        var host = Frame(canvas, "Frame1").ChildHost!;

        // VB6's tab order is one flat form-wide TabIndex sequence. Avalonia resolves TabIndex among siblings
        // within a scope and only then descends, so a scope here would make ODBC Log In.frm tab its two
        // form-level buttons (13, 12), then the Frame (14), and only then its contents (0-11).
        Avalonia.Input.KeyboardNavigation.GetTabNavigation(host)
            .Should().Be(Avalonia.Input.KeyboardNavigationMode.Continue);
    }

    [AvaloniaFact]
    public void ANonVisualControlInsideAFrame_IsHostedOnTheFormAndNotDrawn()
    {
        var (_, canvas, _, _) = Spawn(FrameWithTimer);

        // A Timer is not drawn, so it has no business inside a Frame's clipped host — but the model still
        // records the container so the file round-trips. It is also explicitly hidden: a Timer has no
        // Width/Height in the .frm, those read back as zero, and VBTimer's template pins Min/MaxWidth to 28
        // — which is why a running form used to show a clock face in its corner.
        var timer = Child(canvas, "Timer1");
        timer.IsVisible.Should().BeFalse();
        Frame(canvas, "Frame1").ChildHost!.Children.Should().BeEmpty();
    }

    // ── Visible is not IsVisible ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task AHiddenFrame_KeepsItsContentsRealised()
    {
        var (canvas, _, ctx, env) = Laid(FrameWithChildren);
        var frame = Frame(canvas, "Frame1");
        var text = Child(frame.ChildHost!, "Text1");

        await Run(ctx, env, "Frame1.Visible = False\r\n");
        Dispatcher.UIThread.RunJobs();

        // Avalonia never applies a hidden control's template, so IsVisible=false on a container would
        // unrealise everything inside it — and an unrealised control dispatches nothing, because event
        // dispatch walks the VISUAL tree to find the module execution root. In VB6 a Timer inside a hidden
        // Frame keeps firing and a TextBox inside one keeps raising Change.
        frame.IsVisible.Should().BeTrue();
        frame.Opacity.Should().Be(0);
        frame.IsHitTestVisible.Should().BeFalse();
        TopLevel.GetTopLevel(text).Should().NotBeNull("the subtree must stay attached to dispatch events");
    }

    [AvaloniaFact]
    public async Task AHiddenFrame_ReadsBackAsInvisible()
    {
        var (_, _, ctx, env) = Laid(FrameWithChildren);

        var debug = await Run(ctx, env,
            "Frame1.Visible = False\r\n" +
            "Debug.Print Frame1.Visible\r\n" +
            "Frame1.Visible = True\r\n" +
            "Debug.Print Frame1.Visible\r\n");

        // The opacity trick must not leak into what VB6 code sees.
        debug.Should().HaveCount(2);
        debug[0].Value.Should().Be(false);
        debug[1].Value.Should().Be(true);
    }

    [AvaloniaFact]
    public async Task ADisabledFrame_DisablesItsContents()
    {
        var (canvas, _, ctx, env) = Laid(FrameWithChildren);
        var text = Child(Frame(canvas, "Frame1").ChildHost!, "Text1");

        await Run(ctx, env, "Frame1.Enabled = False\r\n");
        Dispatcher.UIThread.RunJobs();

        text.IsEffectivelyEnabled.Should().BeFalse();
    }

    // ── option-button grouping ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void OptionButtonsInDifferentFrames_AreDifferentGroups()
    {
        var (canvas, _, _, _) = Laid(TwoFramesOfOptions);

        var left = (VBOptionButton)Child(Frame(canvas, "fraLeft").ChildHost!, "optLeft");
        var right = (VBOptionButton)Child(Frame(canvas, "fraRight").ChildHost!, "optRight");

        // Driven through the controls rather than through VB6 code because OptionButton.Value is not a
        // modelled property yet — `optLeft.Value = True` raises error 461. That gap is real and tracked
        // separately; the grouping this asserts is not affected by it.
        left.IsChecked = true;
        right.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // The VB6 rule: a Frame is what scopes an option group, and scoping one is the main reason to put a
        // group of options in a Frame at all. Avalonia groups un-named radio buttons by their parent, so
        // flat on one canvas these were a single group and checking the second cleared the first.
        left.IsChecked.Should().BeTrue();
        right.IsChecked.Should().BeTrue();
    }

    // ── control arrays that span containers ──────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task LoadingAnArrayElement_ClonesItIntoTheSameContainer()
    {
        var (canvas, _, ctx, env) = Laid(ArraysAndContainers);
        var host = Frame(canvas, "fraStep").ChildHost!;

        await Run(ctx, env, "Load Command1(1)\r\n");
        Dispatcher.UIThread.RunJobs();

        // The group used to hold ONE canvas for the whole array, so a new element cloned into whichever
        // container happened to be registered last — which, once containers exist, is not necessarily the
        // one the template came from.
        host.Children.OfType<Control>().Select(NameOf).Should().Equal("Command1", "Command1");
        canvas.Children.OfType<Control>().Select(NameOf).Should().Equal("fraStep");
    }

    [AvaloniaFact]
    public async Task LoadingAContainerArrayElement_ClonesTheContainerAlone()
    {
        var (canvas, _, ctx, env) = Laid(ArraysAndContainers);

        await Run(ctx, env, "Load fraStep(1)\r\n");
        Dispatcher.UIThread.RunJobs();

        var frames = canvas.Children.OfType<VBFrame>().ToList();
        frames.Should().HaveCount(2);

        // Pinned as CURRENT BEHAVIOUR, not asserted as correct: what VB6 does when you Load a new element
        // of a Frame array — clone the frame alone, or clone its contents with it — is an open oracle
        // question recorded in the change's open list. Locking it down here means the answer changes this
        // test deliberately rather than surprising someone later.
        frames[1].ChildHost!.Children.Should().BeEmpty();
    }

    // ── the click guard ──────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AClickOnAContainedControl_DoesNotBelongToTheContainer()
    {
        var (_, canvas, _, _) = Spawn(FrameWithChildren);
        var frame = Frame(canvas, "Frame1");
        var label = Child(frame.ChildHost!, "Label1");

        // PointerReleased bubbles and the handler is a CLASS handler, so without the guard one click on the
        // label raises Label1_Click AND Frame1_Click. VB6 raises the innermost control's Click and nothing
        // above it.
        AttachedEvents.OwnsThePointerEvent(label, label).Should().BeTrue();
        AttachedEvents.OwnsThePointerEvent(frame, label).Should().BeFalse();
    }

    [AvaloniaFact]
    public void AClickOnAFramesOwnSurface_BelongsToTheFrame()
    {
        var (canvas, _, _, _) = Laid(FrameWithChildren);
        var frame = Frame(canvas, "Frame1");

        // The guard must not go too far the other way: a click landing on one of the Frame's own template
        // parts — its border, its caption — is still the Frame's click, because a template part is not a VB6
        // control and carries no name to stop the walk.
        var part = frame.GetVisualDescendants().OfType<Control>()
                        .First(c => NameOf(c) is null && !ReferenceEquals(c, frame));

        AttachedEvents.OwnsThePointerEvent(frame, part).Should().BeTrue();
    }
}
