using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control's Move, and a form's ScaleWidth and ScaleHeight (#1071), which the Battleship demo's board needs to lay out
/// its grid and fit itself round it. Every expected line is what vb6.exe printed for the same statements: the probe and
/// its log are in docs/vb6-fidelity-oracle/move-scalewidth-and-scaleheight.md, "Move, ScaleWidth and ScaleHeight".
/// </summary>
public sealed class MoveAndScaleSizeTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "hexide-move-" + Guid.NewGuid().ToString("N"));

    public MoveAndScaleSizeTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Form1 = """
        VERSION 5.00
        Begin VB.Form Form1
           BorderStyle     =   1  'Fixed Single
           Caption         =   "Form1"
           ClientHeight    =   2520
           ClientLeft      =   45
           ClientTop       =   330
           ClientWidth     =   2520
           ScaleHeight     =   2520
           ScaleWidth      =   2520
           Begin VB.Label Label1
              BorderStyle     =   1  'Fixed Single
              Height          =   360
              Left            =   120
              TabIndex        =   0
              Top             =   120
              Width           =   360
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub P()
            Debug.Print Label1.Left & " " & Label1.Top & " " & Label1.Width & " " & Label1.Height
        End Sub
        Private Sub Form_Load()
            Debug.Print ScaleWidth & " " & ScaleHeight & " " & TypeName(ScaleWidth)
            Label1.Move 100
            P
            Label1.Move 200, 300
            P
            Label1.Move 400, 500, 600, 700
            P
            Label1.Move 450.5, 500, 600, 700
            P
            On Error Resume Next
            Label1.Move 400, 500, -10, 700
            Debug.Print "negative " & Err.Number
            On Error GoTo 0
            P
            Width = (Width - ScaleWidth) + 3000
            Height = (Height - ScaleHeight) + 2000
            Debug.Print ScaleWidth & " " & ScaleHeight
        End Sub

        """;

    // Run Form1 from `frm` as a made program, and hand back what it printed, the faults it raised and its window.
    private async Task<(List<string> Printed, List<Exception> Errors, Window Window)> RunAsync(string frm, CancellationToken token)
    {
        File.WriteAllText(Path.Combine(dir, "Form1.frm"), frm.ReplaceLineEndings("\r\n"), Encoding.ASCII);
        File.WriteAllText(Path.Combine(dir, "Probe.vbp"), "Type=Exe\r\nForm=Form1.frm\r\nStartup=\"Form1\"\r\nName=\"Probe\"\r\n", Encoding.ASCII);
        var project = (await new ProjectLoader().LoadAsync(Path.Combine(dir, "Probe.vbp"), new NullSink())).Project;
        var printed = new List<string>();
        var errors = new List<Exception>();
        Action<string> print = printed.Add;
        void OnFaulted(VBWindowContext _, Exception e) => errors.Add(e);
        VBDebugConsole.Output += print;
        VBWindowContext.Faulted += OnFaulted;
        try
        {
            _ = VBLoader.RunMadeProgram(project, token, out var startup);
            for (var i = 0; i < 20; i++)
                Dispatcher.UIThread.RunJobs();
            return (printed, errors, startup!);
        }
        finally
        {
            VBDebugConsole.Output -= print;
            VBWindowContext.Faulted -= OnFaulted;
        }
    }

    [AvaloniaFact]
    public async Task Move_and_ScaleWidth_answer_as_vb6_exe_did()
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (var printed, var errors, window) = await RunAsync(Form1, cancel.Token);

            errors.Should().BeEmpty();
            printed.Should().Equal(
                "2520 2520 Single",
                "100 120 360 360",
                "200 300 360 360",
                "400 500 600 700",
                "450 500 600 700",
                "negative 5",
                "450 500 600 700",
                "3000 1995");
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }

    // The probe in docs/vb6-fidelity-oracle/menus-at-run-time-and-the-menu-bar.md, "Menus at run time, and the menu bar": a Fixed Single form with a Game
    // menu, and a Label along the bottom of its client area.
    private const string MenuForm = """
        VERSION 5.00
        Begin VB.Form Form1
           BorderStyle     =   1  'Fixed Single
           Caption         =   "Form1"
           ClientHeight    =   3420
           ClientLeft      =   45
           ClientTop       =   615
           ClientWidth     =   2940
           ScaleHeight     =   3420
           ScaleWidth      =   2940
           Begin VB.Label lblBottom
              BackColor       =   &H000000FF&
              Height          =   300
              Left            =   0
              TabIndex        =   0
              Top             =   3120
              Width           =   2940
           End
           Begin VB.Menu mnuGame
              Caption         =   "&Game"
              Begin VB.Menu mnuNew
                 Caption         =   "&New"
              End
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub Form_Load()
            Debug.Print ScaleWidth & " " & ScaleHeight & " " & TypeName(ScaleHeight)
            Height = (Height - ScaleHeight) + 2000
            Debug.Print ScaleHeight
            Height = (Height - ScaleHeight) + 3420
            Debug.Print ScaleHeight
        End Sub
        """;

    [AvaloniaFact]
    public async Task A_menu_bar_sits_above_the_client_area_as_vb6_exe_measured()
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (var printed, var errors, window) = await RunAsync(MenuForm, cancel.Token);

            // ScaleHeight is the area below the bar, which used to be refused (#1127), and the fit-to-content idiom lands
            // on it, whole pixels included: 2000 asked for is 1995.
            errors.Should().BeEmpty();
            // A number printed alone has Print's spaces, for its sign and after it (#1380).
            printed.Should().Equal("2940 3420 Single", " 1995 ", " 3420 ");

            // The client area under the bar is the whole 3420 twips, so the Label along its bottom edge is not cut off by
            // the bar's height, as it was.
            var canvas = window.GetLogicalDescendants().OfType<Canvas>().First();
            var bar = window.GetLogicalDescendants().OfType<Menu>().Single();
            bar.IsVisible.Should().BeTrue();
            bar.Bounds.Height.Should().Be(VBLoader.MenuBarHeight);
            // The last fit asked for 3420 twips, 228 pixels; layout rounding may give the canvas the part pixel left over.
            canvas.Bounds.Top.Should().Be(VBLoader.MenuBarHeight);
            canvas.Bounds.Height.Should().BeGreaterThanOrEqualTo(3420 / 15.0);
            var bottom = canvas.Children.Single();
            (Canvas.GetTop(bottom) + bottom.Bounds.Height).Should().BeLessThanOrEqualTo(canvas.Bounds.Height);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }

    // A form that keeps its only top-level menu hidden, for PopupMenu: VB6 shows no bar, and its Height is the same form's
    // without a menu (measured, #1127).
    private const string PopupOnlyForm = """
        VERSION 5.00
        Begin VB.Form Form1
           BorderStyle     =   1  'Fixed Single
           Caption         =   "Form1"
           ClientHeight    =   3420
           ClientLeft      =   45
           ClientTop       =   330
           ClientWidth     =   2940
           ScaleHeight     =   3420
           ScaleWidth      =   2940
           Begin VB.Menu mnuPop
              Caption         =   "Pop"
              Visible         =   0   'False
              Begin VB.Menu mnuPopA
                 Caption         =   "A"
              End
           End
        End
        Attribute VB_Name = "Form1"
        Private Sub Form_Load()
            Debug.Print ScaleHeight
            Height = (Height - ScaleHeight) + 2000
            Debug.Print ScaleHeight
        End Sub
        """;

    [AvaloniaFact]
    public async Task A_form_whose_menus_are_all_hidden_has_no_bar()
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (var printed, var errors, window) = await RunAsync(PopupOnlyForm, cancel.Token);

            errors.Should().BeEmpty();
            printed.Should().Equal(" 3420 ", " 1995 ");
            window.GetLogicalDescendants().OfType<Menu>().Single().IsVisible.Should().BeFalse();
            window.GetLogicalDescendants().OfType<Canvas>().First().Bounds.Top.Should().Be(0);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_form_without_menus_has_no_bar_and_keeps_its_client_area()
    {
        using var cancel = new CancellationTokenSource();
        Window? window = null;
        try
        {
            (_, var errors, window) = await RunAsync(Form1, cancel.Token);

            errors.Should().BeEmpty();
            window.GetLogicalDescendants().OfType<Menu>().Single().IsVisible.Should().BeFalse();
            window.GetLogicalDescendants().OfType<Canvas>().First().Bounds.Top.Should().Be(0);
        }
        finally
        {
            cancel.Cancel();
            window?.Close();
        }
    }
}
