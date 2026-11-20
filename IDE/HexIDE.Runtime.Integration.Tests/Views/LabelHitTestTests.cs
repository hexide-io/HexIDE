using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// A Label takes the pointer across its whole rectangle, transparent or opaque, with a border or without, over an Image
/// or over the form (#4966). vb6.exe SP6 raised MouseMove on the Label at its centre and one pixel inside its edge, native
/// and p-code alike, with the real cursor moved by SetCursorPos in the console session. HexIDE's transparent Label drew
/// nothing, so only its border took the pointer and the Image beneath raised the rest.
/// </summary>
public class LabelHitTestTests : WindowTestFixture
{
    // The probe's form, 400 by 300 pixels: Image1 behind three Labels, and a fourth Label over the form itself.
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           BackColor       =   &H00C0FFFF&
           ClientHeight    =   4500
           ClientWidth     =   6000
           Begin VB.Label LblFormTransparent
              BackStyle       =   0  'Transparent
              Height          =   900
              Left            =   4200
              Top             =   3300
              Width           =   900
           End
           Begin VB.Label LblOpaque
              BackColor       =   &H00FFC0C0&
              BorderStyle     =   1  'Fixed Single
              Height          =   900
              Left            =   2700
              Top             =   300
              Width           =   900
           End
           Begin VB.Label LblBordered
              Alignment       =   2  'Center
              BackColor       =   &H00F5E1C8&
              BackStyle       =   0  'Transparent
              BorderStyle     =   1  'Fixed Single
              Height          =   900
              Left            =   1500
              Top             =   300
              Width           =   900
           End
           Begin VB.Label LblPlain
              BackStyle       =   0  'Transparent
              Height          =   900
              Left            =   300
              Top             =   300
              Width           =   900
           End
           Begin VB.Image Image1
              Height          =   2700
              Left            =   0
              Stretch         =   -1  'True
              Top             =   0
              Width           =   3900
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Each row: the cursor's point in the form's client pixels, then the event vb6.exe raised and its X and Y in twips.
    private static readonly (double X, double Y, string Event, float EventX, float EventY)[] Moves =
    [
        (333, 133, "Form_MouseMove", 4995, 1995),
        (40, 133, "Image1_MouseMove", 600, 1995),
        (50, 50, "LblPlain_MouseMove", 450, 450),
        (55, 55, "LblPlain_MouseMove", 525, 525),
        (40, 133, "Image1_MouseMove", 600, 1995),
        (130, 50, "LblBordered_MouseMove", 450, 450),
        (135, 55, "LblBordered_MouseMove", 525, 525),
        (101, 50, "LblBordered_MouseMove", 15, 450),
        (40, 133, "Image1_MouseMove", 600, 1995),
        (210, 50, "LblOpaque_MouseMove", 450, 450),
        (333, 133, "Form_MouseMove", 4995, 1995),
        (310, 250, "LblFormTransparent_MouseMove", 450, 450),
        (315, 255, "LblFormTransparent_MouseMove", 525, 525),
        (333, 133, "Form_MouseMove", 4995, 1995),
    ];

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public async Task A_Label_takes_the_pointer_across_its_whole_rectangle(double scaling)
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        await ContainerRuntimeHarness.Run(ctx, env, "Image1.ZOrder 1");
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        window.SetRenderScaling(scaling);
        Dispatcher.UIThread.RunJobs();
        foreach (var (x, y, name, eventX, eventY) in Moves)
        {
            recorder.Calls.Clear();
            window.MouseMove(canvas.TranslatePoint(new Point(x, y), window)!.Value);
            recorder.Calls.Select(c => c.Name).Should().Equal([name], $"the cursor at ({x}, {y}) is over that control");
            recorder.Calls[0].Args!.Select(v => v.Value).Should().Equal(0, 0, eventX, eventY);
        }
    }
}
