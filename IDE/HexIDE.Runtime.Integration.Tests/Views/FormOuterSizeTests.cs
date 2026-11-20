using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Platform;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

/// <summary>
/// A form's Width and Height at run time are its outer size, border, caption bar and menu bar included (#1072). Every
/// expectation was measured on vb6.exe, built EXEs on the themed Windows 10 desktop at 96 dpi, native and p-code agreeing:
/// the oracle's "A form's Width and Height: the frame round its client area".
/// </summary>
/// <remarks>
/// Each form has a 2520 by 2520 twip client area. Session 0's unthemed frame is smaller, and HexIDE follows the themed
/// one, which is what a user's desktop draws.
/// </remarks>
public class FormOuterSizeTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string NoCaptionBar = "   ControlBox      =   0   'False\r\n   MaxButton       =   0   'False\r\n   MinButton       =   0   'False\r\n";

    private const string Menu = "   Begin VB.Menu mnuFile \r\n      Caption         =   \"File\"\r\n   End\r\n";

    private static string Form(string lines, string caption, bool menu) =>
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + lines + $"   Caption         =   \"{caption}\"\r\n"
      + "   ClientHeight    =   2520\r\n   ClientLeft      =   120\r\n   ClientTop       =   465\r\n   ClientWidth     =   2520\r\n"
      + "   ScaleHeight     =   2520\r\n   ScaleWidth      =   2520\r\n" + (menu ? Menu : "")
      + "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static string Style(int style) => style switch
    {
        0 => "   BorderStyle     =   0  'None\r\n",
        1 => "   BorderStyle     =   1  'Fixed Single\r\n",
        // Sizable is the default, and a form's file names it only where it was changed and back.
        2 => "",
        3 => "   BorderStyle     =   3  'Fixed Dialog\r\n",
        4 => "   BorderStyle     =   4  'Fixed ToolWindow\r\n",
        _ => "   BorderStyle     =   5  'Sizable ToolWindow\r\n",
    };

    private sealed record Run(List<string> Printed, List<System.Exception> Errors);

    /// <summary>Run Form1 as F5 does, with <paramref name="load"/> as its Load, then close it.</summary>
    private static Run RunLoad(string frm, string load, string extra = "", System.Action<Window>? afterLoad = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(project, frm + code + extra.ReplaceLineEndings("\r\n"), new NullSink())!;
        project.AddForm(form);

        var run = new Run([], []);
        void OnFaulted(VBWindowContext _, System.Exception e) => run.Errors.Add(e);
        void OnRunTimeError(VBWindowContext _, VBRunTimeException e) => run.Errors.Add(e);
        void OnCompileError(VBWindowContext _, VBCompileErrorException e) => run.Errors.Add(e);
        VBDebugConsole.Output += run.Printed.Add;
        VBWindowContext.Faulted += OnFaulted;
        VBWindowContext.RunTimeError += OnRunTimeError;
        VBWindowContext.CompileError += OnCompileError;
        try
        {
            VBLoader.RunForm(form, CancellationToken.None, out var window);
            Dispatcher.UIThread.RunJobs();
            afterLoad?.Invoke(window);
            window.Close();
        }
        finally
        {
            VBDebugConsole.Output -= run.Printed.Add;
            VBWindowContext.Faulted -= OnFaulted;
            VBWindowContext.RunTimeError -= OnRunTimeError;
            VBWindowContext.CompileError -= OnCompileError;
        }
        return run;
    }

    // Issue #4995's final supervisor comment specifies these platform read-backs and expectations.
    // The frame and Width-change invariant were measured in menus-at-run-time-and-the-menu-bar.md.
    [AvaloniaTheory]
    [InlineData(1.5, "Width = Width + 3000", false, WindowResizeReason.Layout, 229, "3435 3870")]
    [InlineData(1, "Width = Width + 3000", false, WindowResizeReason.Layout, 229, "3435 3870")]
    [InlineData(1.5, "Height = 3870\nWidth = Width + 3000", false, WindowResizeReason.Layout, 229, "3435 3870")]
    [InlineData(1.5, "Height = Height + 1500", true, WindowResizeReason.Layout, 197, "2955 3045")]
    [InlineData(1.5, "", false, WindowResizeReason.User, 229, "3420 3855")]
    [InlineData(1.5, "", false, WindowResizeReason.DpiChange, 229, "3420 3855")]
    // Review: Win32 reports Unspecified for the first show at the truncated size (seen live), and for a maximize or
    // restore, which the one-pixel test hands to the platform.
    [InlineData(1.5, "", false, WindowResizeReason.Unspecified, 229, "3435 3870")]
    [InlineData(1.5, "", false, WindowResizeReason.Unspecified, 400, "6000 6435")]
    [InlineData(1.5, "Width = Width + 3000", false, WindowResizeReason.Layout, 226, "3390 3825")]
    public void Platform_rounding_preserves_only_the_size_the_program_assigned(double scale, string statements,
        bool across, WindowResizeReason reason, double dips, string expected)
    {
        var frm = Form(Style(1), "Form1", menu: false)
            .Replace("ClientHeight    =   2520", "ClientHeight    =   3435")
            .Replace("ClientWidth     =   2520", "ClientWidth     =   2955");
        var extra = "Private Sub ChangeSize()\n" + statements + "\nEnd Sub\n"
            + "Private Sub Probe()\nDebug.Print CStr(" + (across ? "ScaleWidth" : "ScaleHeight")
            + ") & \" \" & CStr(" + (across ? "Width" : "Height") + ")\nEnd Sub\n"
            + "Private Sub Narrower()\nWidth = Width - 3000\nEnd Sub\n";
        var run = RunLoad(frm, "", extra, window =>
        {
            window.SetRenderScaling(scale);
            ((VBFormRuntime)window).ExecuteSub("ChangeSize");
            Dispatcher.UIThread.RunJobs();
            var rounded = System.Math.Floor(dips * scale) / scale;
            // Resize is internal in Avalonia 12; invoke the real headless platform implementation.
            typeof(IWindowImpl).GetMethod("Resize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                .Invoke(window.PlatformImpl, [across ? new Size(rounded, window.Height) : new Size(window.Width, rounded), reason]);
            Dispatcher.UIThread.RunJobs();
            ((VBFormRuntime)window).ExecuteSub("Probe");
            if (statements.Contains("Width = Width + 3000"))
            {
                ((VBFormRuntime)window).ExecuteSub("Narrower");
                Dispatcher.UIThread.RunJobs();
                typeof(IWindowImpl).GetMethod("Resize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(window.PlatformImpl, [new Size(window.Width, rounded), reason]);
                Dispatcher.UIThread.RunJobs();
                ((VBFormRuntime)window).ExecuteSub("Probe");
            }
        });
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(statements.Contains("Width = Width + 3000") ? [expected, expected] : [expected]);
    }

    // Review of #4995: a wrapped bar makes the window taller after FormFrame.Set records it (#4978), so the bar's
    // initialisation records the new size too. Form3's two rows make a 267-pixel window, which 150% truncates.
    [AvaloniaFact]
    public void Platform_rounding_preserves_a_wrapped_bars_window_height()
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths[caption];
        try
        {
            var extra = """
                Private Sub Wider()
                Width = Width + 3000
                End Sub
                Private Sub Narrower()
                Width = Width - 3000
                End Sub
                Private Sub Probe()
                Debug.Print CStr(ScaleHeight) & " " & CStr(Height)
                End Sub

                """;
            var run = RunLoad(WrappedForm(false), "", extra, window =>
            {
                window.SetRenderScaling(1.5);
                var resize = typeof(IWindowImpl).GetMethod("Resize",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
                var truncated = System.Math.Floor(window.Height * 1.5) / 1.5;
                foreach (var change in new[] { "Wider", "Narrower" })
                {
                    ((VBFormRuntime)window).ExecuteSub(change);
                    Dispatcher.UIThread.RunJobs();
                    resize.Invoke(window.PlatformImpl, [new Size(window.Width, truncated), WindowResizeReason.Layout]);
                    Dispatcher.UIThread.RunJobs();
                    ((VBFormRuntime)window).ExecuteSub("Probe");
                }
            });
            run.Errors.Should().BeEmpty();
            run.Printed.Select(line => line.Trim()).Should().Equal("3705 4440", "3420 4440");
        }
        finally { MenuBarLayout.MeasureCaption = null; }
    }

    private const string Dims = "Debug.Print CStr(Width) & \" \" & CStr(Height) & \" \" & CStr(ScaleWidth) & \" \" & CStr(ScaleHeight)";

    [AvaloniaTheory]
    [InlineData(0, false, false, "2520 2520")]
    [InlineData(1, false, false, "2610 2955")]
    [InlineData(2, false, false, "2760 3105")]
    [InlineData(3, false, false, "2610 2955")]
    [InlineData(4, false, false, "2610 2955")]
    [InlineData(5, false, false, "2760 3105")]
    // A menu bar adds 300 down; a borderless form with a menu has Fixed Single's frame and caption.
    [InlineData(0, true, false, "2610 3255")]
    [InlineData(1, true, false, "2610 3255")]
    [InlineData(2, true, false, "2760 3405")]
    [InlineData(3, true, false, "2610 3255")]
    [InlineData(4, true, false, "2610 3255")]
    // No caption bar, with ControlBox False and an empty Caption: the border alone, all round.
    [InlineData(1, false, true, "2550 2550")]
    [InlineData(2, false, true, "2760 2760")]
    [InlineData(3, false, true, "2610 2610")]
    [InlineData(4, false, true, "2550 2550")]
    [InlineData(5, false, true, "2760 2760")]
    public void Width_and_Height_are_the_outer_size(int style, bool menu, bool noCaptionBar, string outer)
    {
        var frm = Form(Style(style) + (noCaptionBar ? NoCaptionBar : ""), noCaptionBar ? "" : "Form1", menu);

        var run = RunLoad(frm, Dims);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(outer + " 2520 2520");
    }

    // ControlBox False with a caption keeps the caption bar; so does an empty caption with the control box.
    [AvaloniaTheory]
    [InlineData(false, "")]
    [InlineData(true, "Form1")]
    public void A_caption_bar_goes_only_with_both_the_control_box_and_the_caption(bool noControlBox, string caption)
    {
        var frm = Form(Style(2) + (noControlBox ? "   ControlBox      =   0   'False\r\n" : ""), caption, menu: false);

        var run = RunLoad(frm, Dims);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("2760 3105 2520 2520");
    }

    // Measured: Width and Height read back as assigned, while the window is whole pixels, 4000 twips being 266.67 of them,
    // so the client area is what is left of 267.
    [AvaloniaTheory]
    [InlineData(0, false, "4000 3000 4005 3000")]
    [InlineData(1, false, "4000 3000 3915 2565")]
    [InlineData(2, false, "4000 3000 3765 2415")]
    [InlineData(1, true, "4000 3000 3915 2265")]
    [InlineData(2, true, "4000 3000 3765 2115")]
    public void An_assigned_size_reads_back_as_assigned_and_the_client_area_is_what_is_left(int style, bool menu, string after)
    {
        var run = RunLoad(Form(Style(style), "Form1", menu), "Width = 4000\nHeight = 3000\n" + Dims);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(after);
    }

    [AvaloniaFact]
    public void The_fit_to_content_idiom_lands_on_the_client_size_asked_for()
    {
        var run = RunLoad(Form(Style(1), "Form1", menu: false),
            "Width = (Width - ScaleWidth) + 3000\nHeight = (Height - ScaleHeight) + 2000\n" + Dims);

        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("3090 2435 3000 1995");
    }

    // Timing and 300-twip bar measured in issue #1140's final supervisor comment (2026-11-18).
    // The real walk, Posting queue and DoEvents must all participate in these assertions.
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void Menu_bar_changes_wait_for_DoEvents(bool shown, bool maximized, bool hidden)
    {
        var frm = Form(Style(1), "Form1", menu: true)
            .Replace("2520", "3420")
            .Replace("ClientWidth     =   3420", "ClientWidth     =   2940")
            .Replace("ScaleWidth      =   3420", "ScaleWidth      =   2940")
            .Replace("mnuFile", "mnuGame");
        if (hidden)
            frm = frm.Replace("      Caption", "      Visible = 0\r\n      Caption");
        var setup = (shown ? "Me.Show\n" : "") + (maximized ? "WindowState = 2\nDoEvents\n" : "");
        var run = RunLoad(frm, setup + (maximized ? "" : "Debug.Print CStr(ScaleHeight)\n") + """
            Dim before As Single, outer As Single
            before = ScaleHeight
            outer = Height
            watching = True
            mnuGame.Visible = Not mnuGame.Visible
            Debug.Print ScaleHeight = before
            Debug.Print Height = outer
            Debug.Print changes
            DoEvents
            Debug.Print ScaleHeight - before
            Debug.Print Height = outer
            Debug.Print changes
            Debug.Print observed = ScaleHeight
            changes = 0
            mnuGame.Visible = Not mnuGame.Visible
            Debug.Print ScaleHeight - before
            Debug.Print changes
            DoEvents
            Debug.Print ScaleHeight = before
            Debug.Print Height = outer
            Debug.Print changes
            Debug.Print observed = ScaleHeight
            watching = False
            """, """
            Private watching As Boolean
            Private changes As Integer
            Private observed As Single
            Private Sub Form_Resize()
                If watching Then
                    changes = changes + 1
                    observed = ScaleHeight
                End If
            End Sub
            """);
        run.Errors.Should().BeEmpty();
        var delta = hidden ? "-300" : "300";
        run.Printed.Select(line => line.Trim()).Should().Equal(
            (maximized ? System.Array.Empty<string>() : new[] { "3420" })
            .Concat(new[] { "True", "True", "0", delta, "True", "1", "True", delta, "0", "True", "True", "1", "True" }));
    }

    [AvaloniaFact]
    public void Unloading_the_last_visible_menu_posts_the_bar_change()
    {
        var frm = Form(Style(1), "Form1", menu: true).Replace("2520", "3420")
            .Replace("   Begin VB.Menu mnuFile", "   Begin VB.Menu mnuFile\r\n      Index = 0");
        var run = RunLoad(frm, """
            Me.Show
            Load mnuFile(1)
            mnuFile(0).Visible = False
            Dim outer As Single
            outer = Height
            watching = True
            Unload mnuFile(1)
            Debug.Print ScaleHeight
            Debug.Print Height = outer
            Debug.Print changes
            DoEvents
            Debug.Print ScaleHeight
            Debug.Print Height = outer
            Debug.Print changes
            Debug.Print observed
            watching = False
            """, """
            Private watching As Boolean
            Private changes As Integer
            Private observed As Single
            Private Sub Form_Resize()
                If watching Then
                    changes = changes + 1
                    observed = ScaleHeight
                End If
            End Sub
            """);
        run.Errors.Should().BeEmpty();
        run.Printed.Select(line => line.Trim()).Should().Equal("3420", "True", "0", "3720", "True", "1", "3720");
    }

    [AvaloniaTheory]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void MDI_and_wrapped_bar_changes_stay_refused(bool mdi, bool hidden, bool shown)
    {
        var frm = Form(Style(1), "Form1", menu: true);
        frm = mdi ? frm.Replace("   BorderStyle", "   MDIChild = -1\r\n   BorderStyle")
            : frm.Replace("\"File\"", "\"" + new string('W', 100) + "\"")
                .Replace("End\r\nAttribute", Menu.Replace("mnuFile", "mnuSecond") + "End\r\nAttribute");
        if (hidden)
            frm = frm.Replace("mnuFile \r\n", "mnuFile \r\n      Visible = 0\r\n");
        var run = RunLoad(frm, (shown ? "Me.Show\nDoEvents\n" : "")
            + "mnuFile.Visible = Not mnuFile.Visible\nDoEvents");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
            .Which.Message.Should().Contain("#4946");
    }
    // Issue #4946's final comment records the native/p-code measurements from oracle PR #4977.
    [AvaloniaTheory]
    [InlineData(false, false, false, "", "mnuFile.Visible = False\nmnuFile.Visible = True", 0, 0)]
    [InlineData(false, false, false, "", "mnuFile.Visible = False\nmnuFile.Visible = True\nmnuFile.Visible = False", 300, 1)]
    [InlineData(false, false, false, "", "mnuFile.Visible = False\nmnuFile.Visible = False", 300, 1)]
    [InlineData(false, false, false, "mnuFile.Visible = False\nDoEvents", "mnuFile.Visible = True\nmnuFile.Visible = False", 0, 0)]
    [InlineData(true, false, false, "", "mnuGame.Visible = False\nmnuFile.Visible = True", 0, 0)]
    [InlineData(true, false, false, "mnuGame.Visible = False\nmnuFile.Visible = True\nDoEvents", "mnuFile.Visible = False\nmnuGame.Visible = True", 0, 0)]
    [InlineData(false, false, true, "", "mnuFile.Visible = False\nmnuFile.Visible = True", 0, 0)]
    [InlineData(false, true, false, "", "mnuFile.Visible = True\nmnuFile.Visible = False", 0, 0)]
    [InlineData(false, true, false, "Load mnuFile(1)\nmnuFile(1).Visible = True\nDoEvents", "Unload mnuFile(1)\nmnuFile(0).Visible = True", 0, 0)]
    [InlineData(false, true, false, "", "Load mnuFile(1)\nmnuFile(1).Visible = True\nUnload mnuFile(1)", 0, 0)]
    [InlineData(false, true, false, "Load mnuFile(1)\nmnuFile(1).Visible = True\nDoEvents", "Unload mnuFile(1)\nLoad mnuFile(1)\nmnuFile(1).Visible = True", 0, 0)]
    public void Pending_menu_bar_changes_apply_the_final_target(bool secondMenu, bool hidden, bool unshown,
        string setup, string statements, int delta, int resizes)
    {
        var array = statements.Contains("mnuFile(");
        var frm = Form(Style(1), "Form1", menu: true).Replace("2520", "3420");
        if (hidden || secondMenu)
            frm = frm.Replace("      Caption", "      Visible = 0\r\n      Caption");
        if (array)
            frm = frm.Replace("   Begin VB.Menu mnuFile", "   Begin VB.Menu mnuFile\r\n      Index = 0");
        if (secondMenu)
            frm = frm.Replace("End\r\nAttribute", Menu.Replace("mnuFile", "mnuGame") + "End\r\nAttribute");
        var run = RunLoad(frm, (unshown ? "" : "Me.Show\nDoEvents\n") + setup + "\n" + """
            Dim before As Single, outer As Single
            before = ScaleHeight
            outer = Height
            watching = True
            """ + "\n" + statements + "\n" + """
            DoEvents
            Debug.Print ScaleHeight - before
            Debug.Print Height = outer
            Debug.Print changes
            watching = False
            """, """
            Private watching As Boolean
            Private changes As Integer
            Private Sub Form_Resize()
                If watching Then changes = changes + 1
            End Sub
            """);
        run.Errors.Should().BeEmpty();
        run.Printed.Select(line => line.Trim()).Should().Equal(delta.ToString(), "True", resizes.ToString());
    }

    // #4978's final brief records the console measurements from oracle PRs #4977/#4985.
    private static readonly Dictionary<string, double> ConsoleWidths = new()
    {
        ["Alpha"] = 31, ["Bravo"] = 30, ["Charlie"] = 37, ["Delta"] = 27, ["Echo"] = 26, ["Foxtrot"] = 38,
        ["File"] = 18, ["Edit"] = 20, ["View"] = 25, ["Project"] = 37, ["Format"] = 38, ["Debug"] = 35, ["Run"] = 21
    };

    private static string WrappedForm(bool form4)
    {
        var captions = form4 ? new[] { "File", "Edit", "View", "Project", "Format", "Debug", "Run" }
            : new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" };
        return Form(Style(1), "Form1", false).Replace("2520", "3420")
            .Replace("ClientWidth     =   3420", "ClientWidth     =   " + (form4 ? "1500" : "2940"))
            .Replace("ScaleWidth      =   3420", "ScaleWidth      =   " + (form4 ? "1500" : "2940"))
            .Replace("End\r\nAttribute", string.Concat(captions.Select(caption =>
                $"   Begin VB.Menu mnu{caption}\r\n      Caption = \"{caption}\"\r\n"
                + $"      Begin VB.Menu itm{caption}\r\n         Caption = \"Item\"\r\n      End\r\n   End\r\n")) + "End\r\nAttribute");
    }

    [AvaloniaTheory]
    [InlineData(false, false, "", "3420 4440")]
    [InlineData(false, true, "", "3420 4440")]
    [InlineData(false, false, "Width = Width + 3000", "3705 4440")]
    [InlineData(false, true, "Width = Width + 3000", "3705 4440")]
    [InlineData(false, true, "Width = Width + 3000\nWidth = Width - 3000", "3420 4440")]
    [InlineData(false, true, "Height = (Height - ScaleHeight) + 2000", "1995 3020")]
    [InlineData(true, false, "", "3420 5010")]
    [InlineData(true, true, "", "3420 5010")]
    [InlineData(true, true, "Width = Width - 60", "3135 5010")]
    [InlineData(true, true, "Width = Width - 150", "2850 5010")]
    [InlineData(true, true, "Width = Width - 450", "2565 5010")]
    public void Wrapped_bar_counts_in_outer_and_client_sizes(bool form4, bool shown, string statements, string expected)
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths[caption];
        try
        {
            var widthChanges = statements.Split('\n').Count(line => line.StartsWith("Width ="));
            var run = RunLoad(WrappedForm(form4), (shown ? "Me.Show\nDoEvents\n" : "")
                + "watching = True\n" + statements
                + "\nDebug.Print CStr(ScaleHeight) & \" \" & CStr(Height)"
                + (widthChanges > 0 ? "\nDebug.Print CStr(changes) & \" \" & CStr(observed = ScaleHeight)\nDoEvents\nDebug.Print changes" : "")
                + "\nwatching = False", """
                Private watching As Boolean
                Private changes As Integer
                Private observed As Single
                Private Sub Form_Resize()
                    If watching Then
                        changes = changes + 1
                        observed = ScaleHeight
                    End If
                End Sub
                """);
            run.Errors.Should().BeEmpty();
            run.Printed.Select(line => line.Trim()).Should().Equal(widthChanges > 0
                ? new[] { expected, widthChanges + " True", widthChanges.ToString() } : new[] { expected });
        }
        finally { MenuBarLayout.MeasureCaption = null; }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Width_rewraps_before_its_single_Resize(bool shown)
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths[caption];
        try
        {
            var run = RunLoad(WrappedForm(false), (shown ? "Me.Show\nDoEvents\n" : "") + """
                watching = True
                Width = Width + 3000
                Debug.Print CStr(changes) & " " & CStr(observed) & " " & CStr(Height)
                DoEvents
                Debug.Print changes
                changes = 0
                Width = Width - 3000
                Debug.Print CStr(changes) & " " & CStr(observed) & " " & CStr(Height)
                DoEvents
                Debug.Print changes
                watching = False
                """, """
                Private watching As Boolean
                Private changes As Integer
                Private observed As Single
                Private Sub Form_Resize()
                    If watching Then
                        changes = changes + 1
                        observed = ScaleHeight
                    End If
                End Sub
                """);
            run.Errors.Should().BeEmpty();
            run.Printed.Select(line => line.Trim()).Should().Equal("1 3705 4440", "1", "1 3420 4440", "1");
        }
        finally { MenuBarLayout.MeasureCaption = null; }
    }

    [AvaloniaTheory]
    [InlineData("mnuFoxtrot.Visible = False", false)]
    [InlineData("mnuFoxtrot.Visible = False\nmnuEcho.Visible = False", true)]
    public void Menu_edits_may_keep_but_cannot_change_the_row_count(string statements, bool refused)
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths[caption];
        try
        {
            var run = RunLoad(WrappedForm(false), "Me.Show\nDoEvents\nwatching = True\n" + statements
                + "\nDoEvents\nDebug.Print CStr(ScaleHeight) & \" \" & CStr(Height)\nDebug.Print changes\nwatching = False", """
                Private watching As Boolean
                Private changes As Integer
                Private Sub Form_Resize()
                    If watching Then changes = changes + 1
                End Sub
                """);
            if (refused)
                run.Errors.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
                    .Which.Message.Should().Contain("#4946");
            else
            {
                run.Errors.Should().BeEmpty();
                run.Printed.Select(line => line.Trim()).Should().Equal("3420 4440", "0");
            }
        }
        finally { MenuBarLayout.MeasureCaption = null; }
    }

    [AvaloniaTheory]
    [InlineData("", "mnuFile(0).Caption = \"FileFileFileFileFile\"")]
    [InlineData("", "Load mnuFile(1)")]
    [InlineData("Width = Width + 1440\nLoad mnuFile(1)\nWidth = Width - 1440", "Unload mnuFile(1)")]
    public void Caption_Load_and_Unload_refuse_a_new_row_count(string setup, string statement)
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths.TryGetValue(caption, out var width) ? width : 200;
        try
        {
            var frm = WrappedForm(true).Replace("Begin VB.Menu mnuFile", "Begin VB.Menu mnuFile\r\n      Index = 0");
            var run = RunLoad(frm, "Me.Show\nDoEvents\n" + setup + "\n" + statement);
            run.Errors.Should().ContainSingle().Which.Should().BeOfType<System.NotImplementedException>()
                .Which.Message.Should().Contain("#4946");
        }
        finally { MenuBarLayout.MeasureCaption = null; }
    }

    [Theory]
    [InlineData(false, 196, "4,2")]
    [InlineData(false, 226, "4,2")]
    [InlineData(false, 227, "4,2")]
    [InlineData(false, 228, "5,1")]
    [InlineData(false, 236, "5,1")]
    [InlineData(false, 276, "5,1")]
    [InlineData(true, 101, "2,2,1,2")]
    [InlineData(true, 100, "2,2,1,2")]
    [InlineData(true, 97, "2,2,1,2")]
    [InlineData(true, 96, "2,1,1,1,2")]
    [InlineData(true, 91, "2,1,1,1,2")]
    [InlineData(true, 90, "2,1,1,1,1,1")]
    [InlineData(true, 80, "2,1,1,1,1,1")]
    [InlineData(true, 70, "1,1,1,1,1,1,1")]
    [InlineData(true, 60, "1,1,1,1,1,1,1")]
    [InlineData(true, 54, "1,1,1,1,1,1,1")]
    public void Console_widths_break_at_the_measured_spare_margin(bool form4, int width, string rows)
    {
        var widths = form4 ? new double[] { 32, 34, 39, 51, 52, 49, 35 } : new double[] { 45, 44, 51, 41, 40, 52 };
        MenuBarLayout.Rows(widths, width).Should().Equal(rows.Split(',').Select(int.Parse));
    }

    [Theory]
    [InlineData(6, "2,2,1,1")]
    [InlineData(5, "2,2,1")]
    [InlineData(4, "2,2")]
    [InlineData(3, "2,1")]
    [InlineData(2, "2")]
    public void Hidden_items_leave_the_measured_rows(int count, string rows) =>
        MenuBarLayout.Rows(new double[] { 32, 34, 39, 51, 52, 49, 35 }.Take(count), 100)
            .Should().Equal(rows.Split(',').Select(int.Parse));

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 39)]
    [InlineData(3, 58)]
    [InlineData(4, 77)]
    [InlineData(6, 115)]
    [InlineData(7, 134)]
    public void Console_rows_have_the_measured_height(int rows, int height) => MenuBarLayout.Height(rows).Should().Be(height);

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    public void The_drawn_rows_use_the_counted_widths_and_height(double scaling)
    {
        MenuBarLayout.MeasureCaption = caption => ConsoleWidths[caption];
        var bar = new VBMenuBar();
        var window = new Avalonia.Controls.Window { Width = 196, Height = 248, Content = bar };
        try
        {
            foreach (var caption in new[] { "&Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot" })
                bar.Items.Add(new VBMenu { Header = caption, FontSize = 36 });
            VBMenuBar.Initialize(window, bar);
            window.SetRenderScaling(scaling);
            window.Show();
            window.UpdateLayout();
            bar.Height.Should().Be(39);
            var items = bar.Items.Cast<VBMenu>().ToArray();
            items.Select(item => item.FontSize).Should().AllBeEquivalentTo(bar.FontSize);
            items.Select(item => item.Bounds.Width).Should().Equal(45, 44, 51, 41, 40, 52);
            items.Select(item => item.Bounds.Y).Should().Equal(0, 0, 0, 0, 19, 19);
            items.Select(item => item.Bounds.X).Should().Equal(0, 45, 89, 140, 0, 40);
        }
        finally
        {
            window.Close();
            MenuBarLayout.MeasureCaption = null;
        }
    }

}
