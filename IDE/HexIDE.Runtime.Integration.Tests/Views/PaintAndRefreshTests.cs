using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #991: SP6 native/p-code console measurements, "AutoRedraw and Paint" and "When Paint is raised"
// in docs/vb6-fidelity-oracle/the-drawing-methods-line-pset-circle-cls-and-point.md.
public partial class DrawingMethodsTests
{
    private const string PaintHandlers = """
        Private Sub Form_Resize()
            Debug.Print "R"
        End Sub
        Private Sub Form_Activate()
            Debug.Print "A"
        End Sub
        Private Sub Form_Paint()
            Debug.Print "P"
        End Sub
        Private Sub Picture1_Resize()
            Debug.Print "r"
        End Sub
        Private Sub Picture1_Paint()
            Debug.Print "p"
        End Sub
        """;

    private static string PaintForm => Form.Replace("      Left = 2160", "      BorderStyle = 0\n      Left = 2160");

    [AvaloniaFact]
    public void Paint_first_show_follows_one_resize_and_activate()
    {
        var run = RunLoad(PaintForm, "Debug.Print \"L\"", procedures: PaintHandlers);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("L", "R", "A", "P", "p");
    }

    [AvaloniaTheory]
    [InlineData("Width = Width + 600", "R|next|P")]
    [InlineData("Height = Height + 600", "R|next|P")]
    [InlineData("Move Left, Top, Width + 600, Height", "R|next|P")]
    [InlineData("Width = Width - 600\nWidth = Width + 600", "R|R|next|P")]
    [InlineData("Hide\nShow", "next|P|p")]
    [InlineData("Picture1.Visible = False", "next|P")]
    [InlineData("Picture1.Width = Picture1.Width + 4500", "r|next|p")]
    [InlineData("Me.Refresh", "P|next")]
    [InlineData("Picture1.Refresh", "p|next")]
    // Rows already correct on main are regression guards, not teeth.
    [InlineData("Width = Width - 600", "R|next")]
    [InlineData("Height = Height - 600", "R|next")]
    [InlineData("Move Left + 300, Top", "next")]
    [InlineData("WindowState = 1", "R|next")]
    [InlineData("Cls\nPSet (10, 10)\nCaption = \"x\"\nBackColor = vbRed", "next")]
    [InlineData("Picture1.Cls\nPicture1.PSet (10, 10)\nPicture1.BackColor = vbRed", "next")]
    public void Paint_changes_raise_resize_now_and_paint_afterwards(string statement, string expected) =>
        CheckPaintAction(statement, expected);

    private static void CheckPaintAction(string statement, string expected, string setup = "", bool auto = false)
    {
        var procedures = PaintHandlers + "\nPublic Sub Probe()\n" + statement
            + "\nDebug.Print \"next\"\nDoEvents\nDebug.Print \"done\"\nEnd Sub\n"
            + "Public Sub Setup()\n" + setup + "\nEnd Sub\n";
        var printed = new System.Collections.Generic.List<string>();
        var result = RunLoad(PaintForm, auto ? "AutoRedraw = True\nPicture1.AutoRedraw = True" : "", window =>
        {
            var form = (VBFormRuntime)window;
            form.ExecuteSub("Setup");
            Dispatcher.UIThread.RunJobs();
            // Keep the startup and setup logs out of the per-step oracle read.
            void Output(string line) => printed.Add(line);
            VBDebugConsole.Output += Output;
            try
            {
                form.ExecuteSub("Probe");
                Dispatcher.UIThread.RunJobs();
            }
            finally { VBDebugConsole.Output -= Output; }
        }, procedures);
        result.Errors.Should().BeEmpty();
        printed.Should().Equal((expected + "|done").Split('|'));
    }

    [AvaloniaFact]
    public void Paint_picturebox_shown_is_posted() =>
        CheckPaintAction("Picture1.Visible = True", "next|p", "Picture1.Visible = False");

    [AvaloniaFact]
    public void Paint_restore_includes_picturebox_after_resize() =>
        CheckPaintAction("WindowState = 0", "R|next|P|p", "WindowState = 1");

    [AvaloniaTheory]
    [InlineData("Width = Width + 600", "R|next")]
    [InlineData("Hide\nShow", "next")]
    [InlineData("Picture1.Visible = False\nPicture1.Visible = True", "next")]
    [InlineData("Picture1.Width = Picture1.Width + 4500", "r|next")]
    [InlineData("Me.Refresh\nPicture1.Refresh", "next")]
    [InlineData("WindowState = 0", "R|next", "WindowState = 1")]
    public void Paint_autoredraw_suppresses_all_paint(string statement, string expected, string setup = "") =>
        CheckPaintAction(statement, expected, setup, auto: true);

    [AvaloniaFact]
    public void Paint_autoredraw_suppresses_startup_paint()
    {
        var run = RunLoad(PaintForm, "AutoRedraw = True\nPicture1.AutoRedraw = True", procedures: PaintHandlers);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("R", "A");
    }

    [AvaloniaTheory]
    [InlineData("Me.")]
    [InlineData("Picture1.")]
    public void Refresh_discards_only_window_drawing_and_Point_reads_it_until_then(string target)
    {
        CheckShownRefresh($"{target}ScaleMode = vbPixels\n{target}AutoRedraw = True\n{target}PSet (10, 10), vbRed\n"
            + $"{target}AutoRedraw = False\n{target}PSet (20, 20), vbRed\nDebug.Print CStr({target}Point(20, 20))\n"
            + $"{target}Refresh\nDebug.Print CStr({target}Point(10, 10))\nDebug.Print CStr({target}Point(20, 20))",
            "255", "255", "15790320");
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Paint_native_growth_uses_the_dispatcher_and_respects_AutoRedraw(bool auto)
    {
        var printed = new System.Collections.Generic.List<string>();
        var run = RunLoad(PaintForm, auto ? "AutoRedraw = True" : "", window =>
        {
            void Output(string line) => printed.Add(line);
            VBDebugConsole.Output += Output;
            try
            {
                var platform = window.PlatformImpl!;
                var size = new Avalonia.Size(window.ClientSize.Width + 40, window.ClientSize.Height);
                platform.GetType().GetProperty("ClientSize")!.SetValue(platform, size);
                var resized = (Action<Avalonia.Size, WindowResizeReason>)platform.GetType().GetProperty("Resized")!.GetValue(platform)!;
                resized(size, WindowResizeReason.User);
                printed.Should().BeEmpty("native notifications wait for the dispatcher to settle");
                Dispatcher.UIThread.RunJobs();
            }
            finally { VBDebugConsole.Output -= Output; }
        }, PaintHandlers);
        run.Errors.Should().BeEmpty();
        printed.Should().Equal(auto ? ["R"] : new[] { "R", "P" });
    }

    [AvaloniaTheory]
    [InlineData("Me.")]
    [InlineData("Picture1.")]
    public void Refresh_kept_drawing_is_visible_with_AutoRedraw_True(string target) =>
        CheckShownRefresh($"{target}AutoRedraw = True\n{target}PSet (150, 150), vbRed\n{target}Refresh\nDebug.Print CStr({target}Point(150, 150))", "255");

    [AvaloniaFact]
    public void Refresh_form_and_PictureBox_keep_independent_window_layers() =>
        CheckShownRefresh("PSet (150, 150), vbRed\nPicture1.PSet (150, 150), vbRed\nMe.Refresh\n"
            + "Debug.Print CStr(Point(150, 150))\nDebug.Print CStr(Picture1.Point(150, 150))\n"
            + "Picture1.Refresh\nDebug.Print CStr(Picture1.Point(150, 150))", "15790320", "255", "15790320");

    [AvaloniaTheory]
    [InlineData("Me.", "Form_Paint")]
    [InlineData("Picture1.", "Picture1_Paint")]
    public void Refresh_waits_for_its_Paint_handler_drawing(string target, string handler)
    {
        var run = RunShownRefresh($"{target}ScaleMode = vbPixels\n{target}Cls\n{target}Refresh\nDebug.Print CStr({target}Point(10, 10))",
            $"Private Sub {handler}()\n{target}PSet (10, 10), vbRed\nEnd Sub\n");
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal("255");
    }

    [AvaloniaTheory]
    [InlineData("Me.")]
    [InlineData("Picture1.")]
    public void Refresh_as_a_value_remains_explicitly_unimplemented(string target)
    {
        var run = RunShownRefresh($"On Error Resume Next\nDebug.Print {target}Refresh\nDebug.Print \"continued\"");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Be("Refresh used as a value is not implemented (#991)");
        run.Printed.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("Me")]
    [InlineData("Picture1")]
    public void Refresh_CallByName_result_remains_explicitly_unimplemented(string target)
    {
        var run = RunShownRefresh($"On Error Resume Next\nCallByName {target}, \"Refresh\", vbMethod\nDebug.Print \"continued\"");
        run.Errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Be("CallByName Refresh result is unmeasured and not implemented (#991)");
        run.Printed.Should().BeEmpty();
    }

    private static Run RunShownRefresh(string code, string procedures = "") =>
        RunLoad(PaintForm, "", window =>
        {
            ((VBFormRuntime)window).ExecuteSub("Probe");
            Dispatcher.UIThread.RunJobs();
        }, "Public Sub Probe()\n" + code + "\nEnd Sub\n" + procedures);

    private static void CheckShownRefresh(string code, params string[] expected)
    {
        var run = RunShownRefresh(code);
        run.Errors.Should().BeEmpty();
        run.Printed.Should().Equal(expected);
    }

}
