using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

public class PressKeyWindowInputTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    [AvaloniaTheory]
    [InlineData("Window/Button[cmdTom]")]
    [InlineData("Window")]
    public void Alt_W_clicks_the_running_forms_access_key(string path)
    {
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Form1"
               ClientHeight = 3000
               ClientWidth = 4000
               Begin VB.CommandButton cmdTom
                  Caption = "Tom"
                  Height = 375
                  Left = 120
                  Top = 120
                  Width = 1215
               End
               Begin VB.CommandButton cmdWide
                  Caption = "One &Wide"
                  Height = 375
                  Left = 120
                  Top = 600
                  Width = 1215
               End
               Begin VB.Label lblResult
                  Caption = "waiting"
                  Height = 375
                  Left = 120
                  Top = 1080
                  Width = 1215
               End
            End
            Attribute VB_Name = "Form1"
            Private Sub cmdWide_Click()
                lblResult.Caption = "clicked"
            End Sub
            """.ReplaceLineEndings("\r\n");
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            var target = UiAutomationDriver.Resolve(window, path).control!;
            var label = UiAutomationDriver.Resolve(window, "Window/Text[lblResult]").control!;
            UiAutomationDriver.Inspect(label, "Window/Text[lblResult]").Name.Should().Be("waiting");

            // Avalonia's AccessKeyHandler requires Alt without Control and a key symbol, on every platform.
            var outcome = UiAutomationDriver.PressKey(target, "W", "Alt", path);
            Dispatcher.UIThread.RunJobs();

            outcome.Success.Should().BeTrue(outcome.Error);
            UiAutomationDriver.Inspect(label, "Window/Text[lblResult]").Name.Should().Be("clicked");
            outcome.Mechanism.Should().Be("window-input");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_text_surface_that_cannot_take_focus_keeps_routed_delivery_and_names_it()
    {
        var box = new TextBox { Focusable = false };
        var window = new Window { Content = box };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var seen = false;
            box.KeyDown += (_, _) => seen = true;
            var outcome = UiAutomationDriver.PressKey(box, "F12", null);
            outcome.Success.Should().BeTrue(outcome.Error);
            seen.Should().BeTrue();
            outcome.Mechanism.Should().Be("routed-key");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Alt_F_opens_a_menu_bar_header()
    {
        var file = new MenuItem { Header = "_File", Items = { new MenuItem { Header = "Open" } } };
        var box = new TextBox();
        var window = new Window { Content = new StackPanel { Children = { new Menu { Items = { file } }, box } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var outcome = UiAutomationDriver.PressKey(box, "F", "Alt");
            outcome.Success.Should().BeTrue(outcome.Error);
            file.IsSubMenuOpen.Should().BeTrue();
            outcome.Mechanism.Should().Be("window-input");
        }
        finally { window.Close(); }
    }
}
