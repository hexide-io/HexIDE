using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using static HexIDE.Integration.Tests.Views.ContainerRuntimeHarness;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #84 phase 5 — a PictureBox is a real container at run time. It is the container in four of the six
/// nested corpus forms, so it carries most of the actual VB6 code this issue is about.
///
/// The difference from a Frame is the inset. A Frame's children are measured from the control's own top-left;
/// a bordered PictureBox insets its client area by the border it draws. The child host therefore sits INSIDE
/// the border decorator, which makes the layout offset and the drawn border the same number by construction
/// rather than by two pieces of arithmetic that can drift apart.
/// </summary>
public class PictureBoxContainerRuntimeTests
{
    // Picture1 at (20, 10) with a default (3-D, Fixed Single) border, so its client origin is (22, 12) and
    // Label1 at (10, 10) inside it belongs at (32, 22) on the form.
    private const string BorderedPictureWithChild = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox Picture1
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
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // The same, declared flat and borderless, so there is no inset to apply.
    private const string FlatPictureWithChild = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox Picture1
              Appearance      =   0  'Flat
              BorderStyle     =   0  'None
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
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Options Dialog.frm's shape: a Frame inside a PictureBox, with a control inside the Frame. Two insets
    // accumulate — the PictureBox's two pixels and the Frame's zero.
    private const string ContainerInsideContainer = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox picOptions
              Left            =   300
              Top             =   150
              Width           =   4500
              Height          =   3000
              Begin VB.Frame fraSample
                 Caption         =   "Sample"
                 Left            =   150
                 Top             =   150
                 Width           =   3000
                 Height          =   1500
                 Begin VB.CommandButton cmdInner
                    Caption         =   "Deep"
                    Left            =   150
                    Top             =   450
                    Width           =   1200
                    Height          =   375
                 End
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string TwoPicturesOfOptions = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox picLeft
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
           Begin VB.PictureBox picRight
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

    // ── the border table ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheClientInset_ComesFromBorderStyleAndAppearance()
    {
        // Measured with GetWindowRect against a compiled VB6 binary: a default 3-D bordered PictureBox insets
        // its children by exactly two pixels (30 twips), a flat borderless one by none.
        PictureBoxComponentClass.ClientBorder(VBBorder.FixedSingle, VBAppearance._3D)
            .Should().Be((ClassicBorderStyle.Sunken, new Thickness(2)));
        PictureBoxComponentClass.ClientBorder(VBBorder.None, VBAppearance.Flat)
            .Should().Be((ClassicBorderStyle.None, new Thickness(0)));
        PictureBoxComponentClass.ClientBorder(VBBorder.None, VBAppearance._3D)
            .Should().Be((ClassicBorderStyle.None, new Thickness(0)));

        // NOT measured against VB6 — one pixel is what VB6 draws for a flat border, and keeping the drawn
        // border and the inset the same number is what makes this a coherent guess. On the oracle list.
        PictureBoxComponentClass.ClientBorder(VBBorder.FixedSingle, VBAppearance.Flat)
            .Should().Be((ClassicBorderStyle.Thin, new Thickness(1)));
    }

    [Fact]
    public void APictureBoxWithNoBorderStyleLine_IsBordered()
    {
        // VB6's PictureBox defaults to 1 - Fixed Single and VB6 omits default-valued properties, so a
        // PictureBox with no BorderStyle line is bordered. HexIDE's shared default was None, which made
        // Tip of the Day.frm's Picture1 read as borderless and inset its children by zero.
        var (_, canvas, _, _) = Spawn(BorderedPictureWithChild);

        ((VBPictureBox)Child(canvas, "Picture1")).ClientInset.Should().Be(new Thickness(2));
    }

    // ── hosting and geometry ─────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void APictureBoxsChildren_AreHostedByThePictureBox()
    {
        var (_, canvas, _, _) = Spawn(BorderedPictureWithChild);

        canvas.Children.OfType<Control>().Select(NameOf).Should().Equal("Picture1");
        HostOf(canvas, "Picture1").Children.OfType<Control>().Select(NameOf).Should().Equal("Label1");
    }

    [AvaloniaFact]
    public void AContainedControl_IsDrawnInsideTheBorder()
    {
        var (canvas, _, _, _) = Laid(BorderedPictureWithChild);
        var label = Child(HostOf(canvas, "Picture1"), "Label1");

        // Picture1 is at 20px, its border is 2px, Label1 records 10px inside it: 32px on the form. The two
        // pixels are the whole difference between this and the Frame case.
        var origin = label.TranslatePoint(new Point(0, 0), canvas)!.Value;
        origin.X.Should().BeApproximately(32, 0.5);
        origin.Y.Should().BeApproximately(22, 0.5);
    }

    [AvaloniaFact]
    public void AFlatBorderlessPictureBox_InsetsNothing()
    {
        var (canvas, _, _, _) = Laid(FlatPictureWithChild);
        var label = Child(HostOf(canvas, "Picture1"), "Label1");

        var origin = label.TranslatePoint(new Point(0, 0), canvas)!.Value;
        origin.X.Should().BeApproximately(30, 0.5);
        origin.Y.Should().BeApproximately(20, 0.5);
    }

    [AvaloniaFact]
    public void AContainerInsideAContainer_AccumulatesBothOrigins()
    {
        var (canvas, _, _, _) = Laid(ContainerInsideContainer);

        // Options Dialog.frm's shape. picOptions at 20 + its 2px border + fraSample at 10 + a Frame's zero
        // inset + cmdInner at 10 = 42; vertically 10 + 2 + 10 + 0 + 30 = 52.
        var frameHost = HostOf(canvas, "picOptions");
        var frame = frameHost.Children.OfType<VBFrame>().Single();
        var button = frame.ChildHost!.Children.OfType<Control>().Single();

        var origin = button.TranslatePoint(new Point(0, 0), canvas)!.Value;
        origin.X.Should().BeApproximately(42, 0.5);
        origin.Y.Should().BeApproximately(52, 0.5);
    }

    [AvaloniaFact]
    public void APictureBoxsHost_ClipsWhatOverflowsIt()
    {
        var (_, canvas, _, _) = Spawn(BorderedPictureWithChild);

        HostOf(canvas, "Picture1").ClipToBounds.Should().BeTrue();
    }

    [AvaloniaFact]
    public void OptionButtonsInDifferentPictureBoxes_AreDifferentGroups()
    {
        var (canvas, _, _, _) = Laid(TwoPicturesOfOptions);

        var left = (VBOptionButton)Child(HostOf(canvas, "picLeft"), "optLeft");
        var right = (VBOptionButton)Child(HostOf(canvas, "picRight"), "optRight");

        left.IsChecked = true;
        right.IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        // A PictureBox scopes an option group exactly as a Frame does. Flat on one canvas these were a single
        // group and checking the second cleared the first.
        left.IsChecked.Should().BeTrue();
        right.IsChecked.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ADisabledPictureBox_DisablesItsContents()
    {
        var (canvas, _, ctx, env) = Laid(BorderedPictureWithChild);
        var label = Child(HostOf(canvas, "Picture1"), "Label1");

        await Run(ctx, env, "Picture1.Enabled = False\r\n");
        Dispatcher.UIThread.RunJobs();

        label.IsEffectivelyEnabled.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task AHiddenPictureBox_KeepsItsContentsRealised()
    {
        var (canvas, _, ctx, env) = Laid(BorderedPictureWithChild);
        var box = (VBPictureBox)Child(canvas, "Picture1");
        var label = Child(box.ChildHost!, "Label1");

        await Run(ctx, env, "Picture1.Visible = False\r\n");
        Dispatcher.UIThread.RunJobs();

        // Same reason as a Frame: Avalonia never applies a hidden control's template, and an unrealised
        // control dispatches nothing.
        box.IsVisible.Should().BeTrue();
        box.Opacity.Should().Be(0);
        TopLevel.GetTopLevel(label).Should().NotBeNull();
    }
}
