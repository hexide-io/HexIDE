using System.Text.Json;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Runtime;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Automation;

public class InspectFrameTests
{
    private static JsonElement Inspect(Control control) => JsonSerializer.SerializeToElement(
        UiAutomationDriver.Inspect(control, "Window"),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [AvaloniaFact]
    public void A_window_reports_its_frame_properties_and_reads_changes_back()
    {
        var window = new Window
        {
            CanResize = false, WindowDecorations = WindowDecorations.BorderOnly,
            CanMinimize = false, CanMaximize = false, ShowInTaskbar = false,
        };
        window.Show();
        try
        {
            var frame = Inspect(window).GetProperty("frame");
            frame.GetProperty("canResize").GetBoolean().Should().BeFalse();
            frame.GetProperty("windowDecorations").GetString().Should().Be("BorderOnly");
            frame.GetProperty("canMinimize").GetBoolean().Should().BeFalse();
            frame.GetProperty("canMaximize").GetBoolean().Should().BeFalse();
            frame.GetProperty("showInTaskbar").GetBoolean().Should().BeFalse();
            frame.GetProperty("windowState").GetString().Should().Be("Normal");
            frame.TryGetProperty("nativeStyle", out _).Should().BeFalse();
            frame.TryGetProperty("nativeExStyle", out _).Should().BeFalse();
            frame.GetProperty("nativeStyleNote").GetString().Should().Contain("not reported");

            window.CanResize = true;
            window.CanMinimize = true;
            window.CanMaximize = true;
            window.ShowInTaskbar = true;
            window.WindowDecorations = WindowDecorations.None;
            window.WindowState = WindowState.Maximized;
            frame = Inspect(window).GetProperty("frame");
            frame.GetProperty("canResize").GetBoolean().Should().BeTrue();
            frame.GetProperty("canMinimize").GetBoolean().Should().BeTrue();
            frame.GetProperty("canMaximize").GetBoolean().Should().BeTrue();
            frame.GetProperty("showInTaskbar").GetBoolean().Should().BeTrue();
            frame.GetProperty("windowDecorations").GetString().Should().Be("None");
            frame.GetProperty("windowState").GetString().Should().Be("Maximized");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_non_window_has_no_frame_section_even_inside_a_window()
    {
        var button = new Button();
        var window = new Window { Content = button };
        window.Show();
        try { Inspect(button).TryGetProperty("frame", out _).Should().BeFalse(); }
        finally { window.Close(); }
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaFact]
    public void A_running_fixed_dialog_reports_the_frame_the_BorderStyle_mapping_sets()
    {
        const string source = """
            VERSION 5.00
            Begin VB.Form Form1
               BorderStyle = 3
               Caption = "Fixed Dialog"
               ShowInTaskbar = 0
               ClientHeight = 1500
               ClientWidth = 2520
            End
            Attribute VB_Name = "Form1"
            """;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var window);
        Dispatcher.UIThread.RunJobs();
        try
        {
            // FormChrome maps BorderStyle 3 to Full decorations and no resize/minimize/maximize.
            var frame = Inspect(window).GetProperty("frame");
            frame.GetProperty("canResize").GetBoolean().Should().BeFalse();
            frame.GetProperty("windowDecorations").GetString().Should().Be("Full");
            frame.GetProperty("canMinimize").GetBoolean().Should().BeFalse();
            frame.GetProperty("canMaximize").GetBoolean().Should().BeFalse();
            frame.GetProperty("showInTaskbar").GetBoolean().Should().BeFalse();
            frame.GetProperty("windowState").GetString().Should().Be("Normal");
        }
        finally { window.Close(); }
    }
}
