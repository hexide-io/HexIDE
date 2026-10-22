using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A TextBox's MultiLine and ScrollBars (#1340). The form is vb6.exe's probe, six boxes for the five ways a box can be
/// laid out and a single-line one asking for scroll bars, and every line it prints is the line vb6.exe logged, native and
/// p-code alike (the oracle's "A TextBox's MultiLine and ScrollBars").
/// </summary>
public class TextBoxMultiLineTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static readonly (bool MultiLine, int ScrollBars)[] Boxes =
        [(false, 0), (true, 0), (true, 1), (true, 2), (true, 3), (false, 3)];

    private static string Probe()
    {
        var form = "VERSION 5.00\r\n"
            + "Begin VB.Form Form1\r\n"
            + "   Caption         =   \"tbprobe\"\r\n"
            + "   ClientHeight    =   7800\r\n"
            + "   ClientLeft      =   60\r\n"
            + "   ClientTop       =   450\r\n"
            + "   ClientWidth     =   4800\r\n"
            + "   ScaleHeight     =   7800\r\n"
            + "   ScaleWidth      =   4800\r\n";
        for (var i = 0; i < Boxes.Length; i++)
            form += "   Begin VB.TextBox txt\r\n"
                + "      Height          =   1100\r\n"
                + $"      Index           =   {i}\r\n"
                + "      Left            =   120\r\n"
                + (Boxes[i].MultiLine ? "      MultiLine       =   -1  'True\r\n" : "")
                + (Boxes[i].ScrollBars != 0 ? $"      ScrollBars      =   {Boxes[i].ScrollBars}\r\n" : "")
                + $"      TabIndex        =   {i}\r\n"
                + $"      Top             =   {120 + i * 1250}\r\n"
                + "      Width           =   3000\r\n"
                + "   End\r\n";
        return form
            + "End\r\n"
            + "Attribute VB_Name = \"Form1\"\r\n"
            + "Option Explicit\r\n"
            + "Private Function Shown(ByVal s As String) As String\r\n"
            + "    Shown = Replace(Replace(Replace(s, vbCr, \"<CR>\"), vbLf, \"<LF>\"), vbTab, \"<TAB>\")\r\n"
            + "End Function\r\n"
            + "Private Sub Form_Load()\r\n"
            + "    Dim i As Integer, o As Object\r\n"
            + "    On Error Resume Next\r\n"
            + "    For i = 0 To 5\r\n"
            + "        Debug.Print \"t\" & i & \" MultiLine=\" & txt(i).MultiLine & \" \" & TypeName(txt(i).MultiLine) & \" ScrollBars=\" _\r\n"
            + "            & txt(i).ScrollBars & \" \" & TypeName(txt(i).ScrollBars)\r\n"
            + "    Next i\r\n"
            + "    Set o = txt(1)\r\n"
            + "    o.MultiLine = False\r\n"
            + "    Debug.Print \"late-bound set MultiLine err=\" & Err.Number & \" [\" & Err.Description & \"] now=\" & txt(1).MultiLine: Err.Clear\r\n"
            + "    o.ScrollBars = 0\r\n"
            + "    Debug.Print \"late-bound set ScrollBars err=\" & Err.Number & \" [\" & Err.Description & \"] now=\" & txt(1).ScrollBars: Err.Clear\r\n"
            + "    For i = 0 To 5\r\n"
            + "        txt(i).Text = \"a\" & vbCrLf & \"b\" & vbLf & \"c\" & vbCr & \"d\"\r\n"
            + "        Debug.Print \"t\" & i & \" len=\" & Len(txt(i).Text) & \" text=\" & Shown(txt(i).Text)\r\n"
            + "    Next i\r\n"
            + "End Sub\r\n"
            + "\r\n";
    }

    // Runs the probe and hands back what it printed and its six boxes, laid out at the form's size.
    private static (List<string> Printed, VBTextBox[] Boxes) Run(Action<VBTextBox[]>? whileRunning = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddForm(new FormDeserializer().Deserialize(project, Probe(), new NullSink())!);
        var printed = new List<string>();
        Action<string> print = printed.Add;
        VBDebugConsole.Output += print;
        var cancel = new CancellationTokenSource();
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out var window);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var boxes = window.GetVisualDescendants().OfType<VBTextBox>().OrderBy(box => box.Bounds.Top).ToArray();
            whileRunning?.Invoke(boxes);
            return (printed, boxes);
        }
        finally
        {
            cancel.Cancel();
            Dispatcher.UIThread.RunJobs();
            VBDebugConsole.Output -= print;
        }
    }

    [AvaloniaFact]
    public void MultiLine_and_ScrollBars_read_as_vb6_exe_s_did_and_cannot_be_set()
    {
        Run().Printed.Should().Equal(
            "t0 MultiLine=False Boolean ScrollBars=0 Integer",
            "t1 MultiLine=True Boolean ScrollBars=0 Integer",
            "t2 MultiLine=True Boolean ScrollBars=1 Integer",
            "t3 MultiLine=True Boolean ScrollBars=2 Integer",
            "t4 MultiLine=True Boolean ScrollBars=3 Integer",
            "t5 MultiLine=False Boolean ScrollBars=3 Integer",
            "late-bound set MultiLine err=382 ['MultiLine' property cannot be set at run time] now=True",
            "late-bound set ScrollBars err=382 ['ScrollBars' property cannot be set at run time] now=0",
            // Every character of Text reads back in either kind of box: a lone LF or CR is kept, not turned into CR LF.
            "t0 len=8 text=a<CR><LF>b<LF>c<CR>d",
            "t1 len=8 text=a<CR><LF>b<LF>c<CR>d",
            "t2 len=8 text=a<CR><LF>b<LF>c<CR>d",
            "t3 len=8 text=a<CR><LF>b<LF>c<CR>d",
            "t4 len=8 text=a<CR><LF>b<LF>c<CR>d",
            "t5 len=8 text=a<CR><LF>b<LF>c<CR>d");
    }

    [AvaloniaFact]
    public void A_box_wraps_and_draws_scroll_bars_as_its_window_style_did()
    {
        // vb6.exe: a 368-character line was 18 lines in a box with no horizontal scroll bar and 1 in every other.
        var longLine = new string('x', 40) + string.Concat(Enumerable.Repeat(" " + new string('y', 40), 8));
        var lines = new int[Boxes.Length];
        var bars = new (bool Horizontal, bool Vertical)[Boxes.Length];
        Run(boxes =>
        {
            for (var i = 0; i < boxes.Length; i++)
            {
                boxes[i].Text = longLine;
                boxes[i].UpdateLayout();
                lines[i] = boxes[i].GetVisualDescendants().OfType<TextPresenter>().Single().TextLayout.TextLines.Count;
                var scrollBars = boxes[i].GetVisualDescendants().OfType<ScrollBar>().ToArray();
                bars[i] = (scrollBars.Any(bar => bar.Orientation == Avalonia.Layout.Orientation.Horizontal && bar.IsEffectivelyVisible),
                    scrollBars.Any(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical && bar.IsEffectivelyVisible));
            }
        });

        lines.Select(count => count > 1).Should().Equal(false, true, false, true, false, false);
        // A bar VB6 asks for is drawn even with nothing to scroll; one it has no style bit for is not, which is also what
        // keeps a single-line box from showing part of a vertical bar (#914).
        bars.Should().Equal((false, false), (false, false), (true, false), (false, true), (true, true), (false, false));
    }

    [AvaloniaFact]
    public void Enter_breaks_a_MultiLine_box_s_line_with_CR_LF_and_does_nothing_in_a_single_line_one()
    {
        var typed = new string[3];
        Run(boxes =>
        {
            foreach (var (box, index, modifiers) in new[] { (boxes[0], 0, KeyModifiers.None), (boxes[1], 1, KeyModifiers.None),
                         (boxes[3], 2, KeyModifiers.Control) })
            {
                box.Text = "xy";
                box.Focus();
                box.CaretIndex = 1;
                box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = modifiers });
                typed[index] = box.Text ?? "";
            }
        });

        // vb6.exe: Enter in a single-line box adds nothing, and Enter or Ctrl+Enter in a MultiLine one adds CR LF, whatever
        // the host's own line break is.
        typed.Should().Equal("xy", "x\r\ny", "x\r\ny");
    }

    [AvaloniaFact]
    public void A_single_line_box_at_VB6_s_default_height_shows_no_part_of_a_vertical_scroll_bar()
    {
        // #914: at 19 px (285 twips) SimpleTheme's Auto vertical bar showed a fragment, since its padding leaves the line
        // more than the viewport holds. VB6 gives a single-line box no vertical bar at all.
        var box = new VBTextBox { Width = 120, Height = 19, Text = "hello" };
        var window = new Window { Content = new Canvas { Children = { box } }, Width = 200, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            box.GetVisualDescendants().OfType<ScrollBar>()
                .Where(bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical)
                .Should().OnlyContain(bar => !bar.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Each_box_is_laid_out_as_its_MultiLine_and_ScrollBars_say()
    {
        var boxes = Run().Boxes;

        boxes.Select(box => (box.MultiLine, box.ScrollBars)).Should().Equal(Boxes);
        boxes.Select(box => box.AcceptsReturn).Should().Equal(false, true, true, true, true, false);
        boxes.Select(box => box.TextWrapping).Should().Equal(TextWrapping.NoWrap, TextWrapping.Wrap, TextWrapping.NoWrap,
            TextWrapping.Wrap, TextWrapping.NoWrap, TextWrapping.NoWrap);
        boxes.Should().OnlyContain(box => box.NewLine == "\r\n");
    }
}
