using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A running form's window is the one its BorderStyle, ControlBox, Caption, MinButton and MaxButton describe (#1575). It
/// was a sizable window with a title bar and every button, whatever the file said. The Win32 styles themselves are pinned
/// against vb6.exe in <c>FormChromeTests</c>; this is what F5 gives Avalonia for them, which is the whole of it where the
/// host is not Windows.
/// </summary>
public class FormChromeWindowTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static (bool Resize, WindowDecorations Decorations, bool Min, bool Max) Run(string lines, string caption = "Form1")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var frm = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n" + lines + $"   Caption         =   \"{caption}\"\r\n"
                  + "   ClientHeight    =   1500\r\n   ClientWidth     =   2520\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        var form = new FormDeserializer().Deserialize(project, frm, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            return (window.CanResize, window.WindowDecorations, window.CanMinimize, window.CanMaximize);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_Sizable_form_has_every_button() =>
        Run("").Should().Be((true, WindowDecorations.Full, true, true));

    [AvaloniaFact]
    public void A_Fixed_Single_form_cannot_be_resized_and_keeps_its_minimize_button() =>
        Run("   BorderStyle     =   1  'Fixed Single\r\n").Should().Be((false, WindowDecorations.Full, true, false));

    [AvaloniaFact]
    public void A_Fixed_Dialog_has_neither_button_whatever_the_file_says() =>
        Run("   BorderStyle     =   3  'Fixed Dialog\r\n   MaxButton       =   -1  'True\r\n   MinButton       =   -1  'True\r\n")
            .Should().Be((false, WindowDecorations.Full, false, false));

    [AvaloniaFact]
    public void A_borderless_form_has_no_frame() =>
        Run("   BorderStyle     =   0  'None\r\n").Should().Be((false, WindowDecorations.None, false, false));

    [AvaloniaFact]
    public void MinButton_and_MaxButton_False_take_the_buttons_away() =>
        Run("   MaxButton       =   0   'False\r\n   MinButton       =   0   'False\r\n")
            .Should().Be((true, WindowDecorations.Full, false, false));

    [AvaloniaFact]
    public void ControlBox_False_takes_the_buttons_and_the_close_box_away() =>
        Run("   ControlBox      =   0   'False\r\n").Should().Be((true, WindowDecorations.Full, false, false));

    [AvaloniaFact]
    public void ControlBox_False_with_no_caption_has_no_caption_bar() =>
        Run("   ControlBox      =   0   'False\r\n", caption: "").Should().Be((true, WindowDecorations.BorderOnly, false, false));

    [AvaloniaFact]
    public void A_Sizable_ToolWindow_can_be_resized_and_has_no_buttons() =>
        Run("   BorderStyle     =   5  'Sizable ToolWindow\r\n").Should().Be((true, WindowDecorations.Full, false, false));
}
