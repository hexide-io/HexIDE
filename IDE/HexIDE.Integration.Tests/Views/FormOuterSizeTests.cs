using System.Collections.Generic;
using System.Threading;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

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
    private static Run RunLoad(string frm, string load)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var code = "Private Sub Form_Load()\r\n" + load.ReplaceLineEndings("\r\n") + "\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(project, frm + code, new NullSink())!;
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
}
