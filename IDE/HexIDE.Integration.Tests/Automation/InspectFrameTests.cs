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
    public void A_window_without_an_owner_reports_null()
    {
        var window = new Window();
        window.Show();
        try
        {
            var frame = Inspect(window).GetProperty("frame");
            frame.GetProperty("owner").ValueKind.Should().Be(JsonValueKind.Null);
            frame.TryGetProperty("nativeOwner", out _).Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void A_window_shown_with_an_owner_reports_its_name_and_title()
    {
        var owner = new Window { Title = "Owner caption" };
        var window = new Window();
        owner.Show();
        window.Show(owner);
        try
        {
            var identity = Inspect(window).GetProperty("frame").GetProperty("owner");
            identity.GetProperty("name").GetString().Should().Be("Owner caption");
            identity.GetProperty("title").GetString().Should().Be("Owner caption");
            owner.Title = "Changed caption";
            Inspect(window).GetProperty("frame").GetProperty("owner")
                .GetProperty("title").GetString().Should().Be("Changed caption");
        }
        finally { window.Close(); owner.Close(); }
    }

    // An owner HexIDE can't name has no 'name' at all, rather than a value the window parameter would refuse (#4795).
    [AvaloniaFact]
    public void An_owner_with_no_title_or_form_has_no_name()
    {
        var owner = new Window { Title = "" };
        var window = new Window();
        owner.Show();
        window.Show(owner);
        try
        {
            var identity = Inspect(window).GetProperty("frame").GetProperty("owner");
            identity.TryGetProperty("name", out _).Should().BeFalse();
            identity.TryGetProperty("className", out _).Should().BeFalse("className is only for a native owner HexIDE didn't make");
        }
        finally { window.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public void A_running_form_owner_reports_its_form_name_separately_from_its_caption()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, """
            VERSION 5.00
            Begin VB.Form Form1
               Caption = "Owner caption"
            End
            Attribute VB_Name = "Form1"
            """, new NullSink())!;
        project.AddForm(form);
        VBLoader.RunForm(form, CancellationToken.None, out var owner);
        var window = new Window();
        window.Show(owner);
        try
        {
            var identity = Inspect(window).GetProperty("frame").GetProperty("owner");
            identity.GetProperty("name").GetString().Should().Be("Form1");
            identity.GetProperty("title").GetString().Should().Be("Owner caption");
        }
        finally { window.Close(); owner.Close(); }
    }

    [AvaloniaFact]
    public async Task A_headless_window_explains_why_native_styles_are_unavailable()
    {
        var window = new Window();
        window.Show();
        try
        {
            var frame = Inspect(window).GetProperty("frame");
            frame.GetProperty("nativeStyleNote").GetString().Should().Contain("HWND");
            frame.TryGetProperty("nativeStyle", out _).Should().BeFalse();
            frame.TryGetProperty("nativeExStyle", out _).Should().BeFalse();
            var (capture, error) = await SnapshotComposer.CaptureFrameAsync(window);
            capture.Should().BeNull();
            error.Should().Contain("Windows HWND").And.Contain("includeFrame false");
        }
        finally { window.Close(); }
    }

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
