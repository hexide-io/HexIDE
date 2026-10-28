using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// VB6 SP6 native and p-code: oracle "In-box controls: the UpDown", object model (#1093 part A).
public class UpDownRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin VB.TextBox Text1
              Left = 120
              Top = 120
              Width = 1200
              Height = 315
              TabIndex = 0
              Text = "2"
           End
           Begin MSComCtl2.UpDown UpDown1
              Left = 3000
              Top = 120
              Width = 255
              Height = 495
              TabIndex = 1
           End
           Begin VB.TextBox Text2
              Left = 120
              Top = 600
              Width = 1200
              Height = 315
              TabIndex = 2
           End
           Begin VB.Label Label1
              Left = 120
              Top = 1200
              Width = 1200
              Height = 315
              Caption = "label"
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static string CorpusSource()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "corpus"))) directory = directory.Parent;
        directory.Should().NotBeNull();
        var basePath = Path.Join(directory!.FullName, "corpus", "inbox-controls");
        var path = Path.Join(basePath, "saved", "updown", "Form1.frm");
        return HexIDE.IDE.Vb6TextFile.ReadAllText(path);
    }

    [AvaloniaFact]
    public void Saved_corpus_preserves_all_four_blocks_even_when_the_form_is_edited()
    {
        var source = CorpusSource();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, source, new Sink())!;
        form.UnknownChildSubtreeTexts.Count(b => b.Contains("MSComCtl2.UpDown")).Should().Be(4);
        var serializer = new FormSerializer();
        serializer.Serialize(form, "Form1.frm").frmText.ReplaceLineEndings().Should().Be(source.ReplaceLineEndings());
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Edited");
        var edited = serializer.Serialize(form, "Form1.frm").frmText.ReplaceLineEndings();
        foreach (var block in form.UnknownChildSubtreeTexts)
            edited.Should().Contain(block.ReplaceLineEndings());
    }

    [AvaloniaFact]
    public void Saved_corpus_loads_all_four_controls_and_their_own_properties()
    {
        var (_, canvas, _, _) = ContainerRuntimeHarness.Spawn(CorpusSource());
        canvas.Children.OfType<VBUpDown>().Count().Should().Be(4);
        var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
        foreach (var (property, expected) in new[] { ("Value", 5L), ("Min", -5L), ("Max", 50L), ("Increment", 2L), ("Orientation", 1L), ("Alignment", 0L), ("OLEDropMode", 1L) })
        {
            spin.TryGetProperty(property, out var value).Should().BeTrue();
            value.Value.Should().Be(expected);
        }
        spin.IsEnabled.Should().BeFalse();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    [AvaloniaTheory]
    [InlineData("BuddyControl = \"NoSuch\"\n AutoBuddy = -1\n SyncBuddy = -1")]
    [InlineData("AutoBuddy = -1")]
    [InlineData("SyncBuddy = -1")]
    public async Task Saved_missing_buddy_and_automatic_flags_load_unbound(string properties)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form.Replace("TabIndex = 1", "TabIndex = 1\n" + properties));
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print UpDown1.BuddyControl, UpDown1.AutoBuddy, UpDown1.SyncBuddy");
        printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").Should().Equal("", "False", "False");
    }

    [AvaloniaFact]
    public async Task Saved_buddy_without_original_rectangle_restores_the_measured_fallback()
    {
        var source = Form.Replace("TabIndex = 1", "TabIndex = 1\n BuddyControl = \"Text2\"\n SyncBuddy = -1\n BuddyProperty = 0");
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(source);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, """
            UpDown1.BuddyControl = ""
            Debug.Print UpDown1.Left, UpDown1.Top, UpDown1.Width, UpDown1.Height, Text2.Width
            """);
        printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").Should().Equal("0", "0", "30", "30", "1455");
    }

    [AvaloniaTheory]
    [InlineData("0", "7", "120")]
    [InlineData("65540", "", "7")]
    [InlineData("\"Tag\"", "", "120")]
    [InlineData(null, "", "120")]
    public async Task Saved_buddy_resolves_after_placement_without_moving_either_control(string? dispatch, string text, string left)
    {
        var source = Form.Replace("TabIndex = 1", "TabIndex = 1\n BuddyControl = \"Text2\"\n SyncBuddy = -1\n OrigLeft = 3000\n OrigTop = 1800\n OrigRight = 3255\n OrigBottom = 2295"
            + (dispatch is null ? "" : "\n BuddyProperty = " + dispatch));
        var (_, window, ctx, env) = ContainerRuntimeHarness.Laid(source);
        try
        {
            var printed = await ContainerRuntimeHarness.Run(ctx, env, """
                On Error Resume Next
                Debug.Print UpDown1.Left, Text2.Width, UpDown1.BuddyControl
                UpDown1.Value = 7
                Debug.Print Err.Number, Text2.Text, Text2.Left
                UpDown1.BuddyControl = ""
                Debug.Print UpDown1.Left, UpDown1.Top, UpDown1.Width, UpDown1.Height, Text2.Width
                """);
            printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").Should().Equal(
                "3000", "1200", "Text2", dispatch is "0" or "65540" ? "0" : "438", text, left, "3000", "1800", "255", "495", "1455");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, 3, true, 6)]
    [InlineData(4, 3, false, 0)]
    [InlineData(8, 3, false, 3)]
    [InlineData(-10, 3, true, -5)]
    [InlineData(-7, 3, true, -2)]
    [InlineData(-5, 3, false, -10)]
    public void Pointer_steps_use_the_measured_magnitude_formula(int value, int increment, bool up, int expected)
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            spin.TrySetProperty("Min", new Vb6Value(-10L));
            spin.TrySetProperty("Max", new Vb6Value(20L));
            spin.TrySetProperty("Increment", new Vb6Value((long)increment));
            spin.TrySetProperty("Value", new Vb6Value((long)value));
            var point = spin.TranslatePoint(new Point(5, up ? 3 : spin.Bounds.Height - 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            spin.Value.Should().Be(expected);
            window.MouseUp(point, MouseButton.Left);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("&H5", 0, 6)]
    [InlineData("$3", 0, 4)]
    [InlineData("(2)", -10, -1)]
    [InlineData("2.5", 0, 3)]
    [InlineData("3.7", 0, 5)]
    [InlineData("9x", 5, 6)]
    [InlineData("", 0, 1)]
    public void Click_reads_and_converts_buddy_text(string text, int min, int expected)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-US"));
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            spin.TrySetProperty("Min", new Vb6Value((long)min));
            spin.TrySetProperty("BuddyControl", new Vb6Value("Text1"));
            spin.TrySetProperty("SyncBuddy", new Vb6Value(true));
            ((VBTextBox)ContainerRuntimeHarness.Child(canvas, "Text1")).Text = text;
            window.UpdateLayout();
            var point = spin.TranslatePoint(new Point(5, 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Zero_increment_only_raises_the_click_event(bool up)
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            spin.TrySetProperty("Value", new Vb6Value(4L));
            spin.TrySetProperty("Increment", new Vb6Value(0L));
            recorder.Calls.Clear();
            var point = spin.TranslatePoint(new Point(5, up ? 3 : spin.Bounds.Height - 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(4);
            recorder.Calls.Select(c => c.Name).Where(n => n is "UpDown1_Change" or "UpDown1_UpClick" or "UpDown1_DownClick")
                .Should().Equal(up ? "UpDown1_UpClick" : "UpDown1_DownClick");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(100)]
    public void Acceleration_is_stored_without_changing_steps(int acceleration)
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            spin.TrySetProperty("Max", new Vb6Value(10000L));
            spin.TrySetProperty("Acceleration", new Vb6Value((long)acceleration));
            recorder.Calls.Clear();
            var point = spin.TranslatePoint(new Point(5, 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(1);
            window.MouseDown(point, MouseButton.Left);
            var frame = new DispatcherFrame();
            using var stop = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(850));
            Dispatcher.UIThread.PushFrame(frame);
            spin.Value.Should().BeGreaterThan(2);
            spin.Value.Should().Be(recorder.Calls.Count(c => c.Name == "UpDown1_Change"));
            window.MouseUp(point, MouseButton.Left);
            spin.TryGetProperty("Acceleration", out var stored).Should().BeTrue();
            stored.Value.Should().Be((short)acceleration);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Label1", "Caption", "7", true, 8)]
    [InlineData("Label1", "Caption", "7", false, 6)]
    [InlineData("Label1", null, "7", true, 8)]
    [InlineData("Text1", "Tag", "3", true, 4)]
    [InlineData("Text1", "Tag", "abc", true, 1)]
    public async Task Click_reads_and_writes_the_selected_buddy_property(string buddy, string? property, string text, bool up, int expected)
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            await ContainerRuntimeHarness.Run(ctx, env, $"""
                UpDown1.BuddyControl = "{buddy}"
                UpDown1.SyncBuddy = True
                """ + (property is null ? "" : $"\r\nUpDown1.BuddyProperty = \"{property}\"") + $"""

                Text1.Text = "9"
                {buddy}.{property ?? "Caption"} = "{text}"
                UpDown1.Value = 2
                {buddy}.{property ?? "Caption"} = "{text}"
                """);
            recorder.Calls.Clear();
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            window.UpdateLayout();
            var point = spin.TranslatePoint(new Point(5, up ? 3 : spin.Bounds.Height - 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(expected);
            var printed = await ContainerRuntimeHarness.Run(ctx, env, $"Debug.Print {buddy}.{property ?? "Caption"}, Text1.Text");
            printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture)).Should().Equal(expected.ToString(CultureInfo.InvariantCulture), "9");
            recorder.Calls.Select(c => c.Name).Where(n => n.EndsWith("_Change", StringComparison.Ordinal))
                .Should().Equal(buddy == "Label1" ? ["Label1_Change", "UpDown1_Change"] : ["UpDown1_Change"]);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, 10, 10, true, true, 0)]
    [InlineData(0, 10, 1, false, true, 0)]
    [InlineData(20, 0, 10, true, false, 6)]
    [InlineData(20, 0, 10, false, false, 15)]
    [InlineData(0, 10, 10, true, false, 10)]
    public void Range_wrap_and_release_event_order(int min, int max, int value, bool up, bool wrap, int expected)
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            spin.TrySetProperty("Max", new Vb6Value((long)max));
            spin.TrySetProperty("Min", new Vb6Value((long)min));
            spin.TrySetProperty("Value", new Vb6Value((long)value));
            spin.TrySetProperty("Increment", new Vb6Value(3L));
            spin.TrySetProperty("Wrap", new Vb6Value(wrap));
            recorder.Calls.Clear();
            var point = spin.TranslatePoint(new Point(5, up ? 3 : spin.Bounds.Height - 3), window!)!.Value;
            window!.MouseDown(point, MouseButton.Left);
            spin.Value.Should().Be(expected);
            window.MouseUp(new Point(350, 250), MouseButton.Left);
            recorder.Calls.Select(c => c.Name).Where(n => !n.EndsWith("GotFocus", StringComparison.Ordinal) && !n.EndsWith("LostFocus", StringComparison.Ordinal))
                .Should().Equal("UpDown1_MouseDown", "UpDown1_Change", "UpDown1_MouseUp", up ? "UpDown1_UpClick" : "UpDown1_DownClick");
        }
        finally { window!.Close(); }
    }

    [AvaloniaFact]
    public void Horizontal_file_input_and_up_arrow_focus()
    {
        var form = Form.Replace("Begin MSComCtl2.UpDown UpDown1", "Begin MSComCtl2.UpDown UpDown1\n Orientation = 1")
            .Replace("Width = 255", "Width = 495").Replace("Height = 495", "Height = 255");
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(form);
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            var point = spin.TranslatePoint(new Point(spin.Bounds.Width - 3, 5), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(1);
            spin.Focus();
            window.KeyPress(Key.Up, RawInputModifiers.None, PhysicalKey.ArrowUp, null);
            ContainerRuntimeHarness.Child(canvas, "Text1").IsFocused.Should().BeTrue();
            spin.Value.Should().Be(1);
            spin.TrySetProperty("BuddyControl", new Vb6Value("Text1"));
            spin.Width.Should().Be(17);
            spin.Height.Should().Be(21);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Draws_two_triangles_and_disables_them_without_disabling_at_Max()
    {
        var (canvas, window, _, _) = ContainerRuntimeHarness.Laid(Form);
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            GeometryDrawing[] Arrows()
            {
                var group = new DrawingGroup();
                using (var context = group.Open()) spin.Render(context);
                var face = ((ISolidColorBrush)VBColor.FromSystemColor(VbSystemColor.Btnface).ToBrush()).Color;
                return group.Children.OfType<GeometryDrawing>()
                    .Where(d => d.Brush is ISolidColorBrush brush && brush.Color != face).ToArray();
            }
            Arrows().Should().HaveCount(2).And.OnlyContain(d => ((ISolidColorBrush)d.Brush!).Color == Colors.Black);
            spin.TrySetProperty("Value", new Vb6Value(10L));
            Arrows().Should().OnlyContain(d => ((ISolidColorBrush)d.Brush!).Color == Colors.Black);
            spin.IsEnabled = false;
            Arrows().Should().HaveCount(2).And.OnlyContain(d => ((ISolidColorBrush)d.Brush!).Color != Colors.Black);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Holding_repeats_Change_and_release_ends_the_repeat()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            var point = spin.TranslatePoint(new Point(5, 3), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            recorder.Calls.Clear();
            var frame = new DispatcherFrame();
            using var stop = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(850));
            Dispatcher.UIThread.PushFrame(frame);
            recorder.Calls.Count(c => c.Name == "UpDown1_Change").Should().BeGreaterThanOrEqualTo(1);
            recorder.Calls.Should().NotContain(c => c.Name == "UpDown1_UpClick");
            window.MouseUp(point, MouseButton.Left);
            recorder.Calls.Count(c => c.Name == "UpDown1_UpClick").Should().Be(1);
            var after = spin.Value;
            frame = new DispatcherFrame();
            using var stopAgain = DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(200));
            Dispatcher.UIThread.PushFrame(frame);
            spin.Value.Should().Be(after);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Right_click_only_raises_mouse_events_and_wheel_does_nothing()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var window = (Window)TopLevel.GetTopLevel(canvas)!;
        try
        {
            var spin = (VBUpDown)ContainerRuntimeHarness.Child(canvas, "UpDown1");
            var point = spin.TranslatePoint(new Point(5, 3), window)!.Value;
            recorder.Calls.Clear();
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            recorder.Calls.Select(c => c.Name).Should().Equal("UpDown1_MouseDown", "UpDown1_MouseUp");
            recorder.Calls.Clear();
            window.MouseWheel(point, new Vector(0, 1));
            recorder.Calls.Should().BeEmpty();
            spin.IsEnabled = false;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            spin.Value.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Buddy_currency_uses_the_interpreters_String_to_Long_conversion()
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-US"));
        (await Run("Dim n As Long\r\nn = \"$3\"\r\nDebug.Print n, CLng(\"$3\")")).Should().Equal("3", "3");
    }

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\n" + code);
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }


    [AvaloniaFact]
    public async Task Default_member_reads_and_writes_Value()
    {
        (await Run("UpDown1 = 4\r\nDim v As Variant\r\nv = UpDown1\r\nDebug.Print v, TypeName(v)")).Should().Equal("4", "Long");
    }

    [AvaloniaFact]
    public async Task Label_default_and_unbinding_Nothing()
    {
        (await Run("""
            Set UpDown1.BuddyControl = Label1
            UpDown1.SyncBuddy = True
            UpDown1.Value = 7
            Debug.Print Label1.Caption
            Set UpDown1.BuddyControl = Nothing
            Debug.Print UpDown1.BuddyControl, UpDown1.AutoBuddy
            """)).Should().Equal("7", "", "False");
    }

    [AvaloniaFact]
    public async Task Alignment_and_explicit_reattachment()
    {
        (await Run("""
            UpDown1.BuddyControl = "Text1"
            UpDown1.Alignment = 0
            Debug.Print UpDown1.Left, Text1.Left
            UpDown1.Alignment = 1
            Text1.Width = 2000
            Text1.Move 500, 600
            Debug.Print UpDown1.Left
            UpDown1.Alignment = 1
            Debug.Print UpDown1.Left, UpDown1.Top
            UpDown1.BuddyControl = ""
            Debug.Print Text1.Width
            """)).Should().Equal("120", "375", "1066", "2501", "600", "2255");
    }

    [AvaloniaFact]
    public async Task Invalid_buddy_property()
    {
        (await Run("UpDown1.AutoBuddy = True\r\nUpDown1.BuddyProperty = \"NoSuch\"\r\nDebug.Print Err.Number")).Should().Equal("380");
    }

    [AvaloniaFact]
    public async Task Defaults_and_types()
    {
        (await Run("""
            Debug.Print TypeName(UpDown1), UpDown1.Min, UpDown1.Max, UpDown1.Value, TypeName(UpDown1.Value)
            Debug.Print UpDown1.Increment, UpDown1.Wrap, UpDown1.AutoBuddy, UpDown1.SyncBuddy
            Debug.Print UpDown1.Orientation, UpDown1.Alignment, UpDown1.BuddyControl, UpDown1.BuddyProperty
            Debug.Print UpDown1.Enabled, UpDown1.TabStop, UpDown1.OLEDropMode, UpDown1.Acceleration, TypeName(UpDown1.Acceleration)
            """)).Should().Equal("UpDown", "0", "10", "0", "Long", "1", "False", "False", "False", "0", "1", "", "", "True", "True", "0", "0", "Integer");
    }

    [AvaloniaTheory]
    [InlineData("Value = 11", "380", "0")]
    [InlineData("Value = -1", "380", "0")]
    [InlineData("Value = 2.5", "0", "2")]
    [InlineData("Value = 3.5", "0", "4")]
    [InlineData("Value = \"x\"", "13", "0")]
    public async Task Value_validation(string assignment, string error, string value)
    {
        (await Run($"UpDown1.{assignment}\r\nDebug.Print Err.Number, UpDown1.Value")).Should().Equal(error, value);
    }

    [AvaloniaTheory]
    [InlineData("Increment", "0", "0", "0")]
    [InlineData("Increment", "100", "0", "100")]
    [InlineData("Increment", "-1", "380", "1")]
    [InlineData("Orientation", "1", "382", "0", "Property cannot be set at run time")]
    [InlineData("Alignment", "0", "0", "0")]
    [InlineData("Alignment", "2", "380", "1")]
    [InlineData("OLEDropMode", "1", "0", "1")]
    [InlineData("OLEDropMode", "-1", "380", "0")]
    [InlineData("Wrap", "2", "0", "True")]
    [InlineData("Enabled", "2", "0", "True")]
    [InlineData("Acceleration", "2", "0", "2")]
    public async Task Other_properties(string property, string assigned, string error, string value, string? description = null)
    {
        var printed = await Run($"UpDown1.{property} = {assigned}\r\nDebug.Print Err.Number, UpDown1.{property}"
            + (description is null ? "" : ", Err.Description"));
        string[] expected = description is null ? [error, value] : [error, value, description];
        printed.Should().Equal(expected);
    }

    [AvaloniaFact]
    public async Task Inverted_range_and_equal_endpoints()
    {
        (await Run("""
            UpDown1.Value = 5
            UpDown1.Max = 3
            Debug.Print UpDown1.Value
            UpDown1.Min = 3
            UpDown1.Min = 9
            UpDown1.Value = 5
            Debug.Print Err.Number, UpDown1.Value
            UpDown1.Min = -40000
            Debug.Print UpDown1.Value
            UpDown1.Max = 2147483647
            Debug.Print UpDown1.Max
            """)).Should().Equal("3", "0", "5", "3", "2147483647");
    }

    [AvaloniaFact]
    public async Task Buddy_binding_sync_and_restore()
    {
        (await Run("""
            Set UpDown1.BuddyControl = Text1
            Debug.Print UpDown1.BuddyControl, UpDown1.Left, UpDown1.Height, Text1.Width
            UpDown1.Value = 8
            UpDown1.SyncBuddy = True
            Debug.Print Text1.Text
            UpDown1.Value = 7
            Debug.Print Text1.Text
            Text1.Text = "abc"
            Debug.Print UpDown1.Value
            UpDown1.BuddyControl = "Text2"
            Debug.Print Text1.Width, UpDown1.SyncBuddy
            UpDown1.BuddyControl = ""
            Debug.Print UpDown1.Left, UpDown1.Top, UpDown1.Width, UpDown1.Height, Text2.Width
            """)).Should().Equal("Text1", "1066", "315", "945", "2", "7", "7", "1200", "True", "3000", "120", "255", "495", "1200");
    }

    [AvaloniaFact]
    public async Task Buddy_property_and_auto_buddy()
    {
        (await Run("""
            UpDown1.AutoBuddy = True
            Debug.Print UpDown1.BuddyControl, UpDown1.AutoBuddy
            UpDown1.BuddyProperty = "Tag"
            UpDown1.Value = 6
            Debug.Print Text1.Tag, UpDown1.BuddyProperty, UpDown1.SyncBuddy
            UpDown1.BuddyProperty = "text"
            UpDown1.Value = 4
            Debug.Print Text1.Text
            UpDown1.BuddyControl = "Text2"
            Debug.Print UpDown1.AutoBuddy
            UpDown1.AutoBuddy = True
            UpDown1.AutoBuddy = False
            Debug.Print UpDown1.BuddyControl, Text2.Width
            """)).Should().Equal("Text1", "True", "6", "", "True", "4", "False", "Text1", "1200");
    }

    [AvaloniaTheory]
    [InlineData("BuddyControl = \"NoSuch\"", "380")]
    [InlineData("BuddyControl = 5", "380")]
    [InlineData("SyncBuddy = True", "35754", "BuddyControl property must be set first")]
    [InlineData("BuddyProperty = \"Tag\"", "35754", "BuddyControl property must be set first")]
    [InlineData("hWnd = 1", "450")]
    public async Task Measured_errors(string assignment, string error, string? description = null)
    {
        var printed = await Run($"UpDown1.{assignment}\r\nDebug.Print Err.Number"
            + (description is null ? "" : ", Err.Description"));
        string[] expected = description is null ? [error] : [error, description];
        printed.Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("UpDown1.Value = Null")]
    [InlineData("UpDown1.Acceleration = 2.5")]
    [InlineData("Debug.Print UpDown1.hWnd")]
    [InlineData("UpDown1.OLEDrag")]
    [InlineData("UpDown1.AboutBox")]
    public async Task Unimplemented_members_and_unmeasured_conversions_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*UpDown*not implemented*");
    }

    [AvaloniaFact]
    public async Task SyncBuddy_off_stops_writes()
    {
        (await Run("""
            UpDown1.AutoBuddy = True
            UpDown1.SyncBuddy = True
            UpDown1.Value = 7
            UpDown1.SyncBuddy = False
            UpDown1.Value = 8
            Debug.Print Text1.Text
            """)).Should().Equal("7");
    }

    [AvaloniaFact]
    public async Task Code_events_fire_even_for_same_value_and_buddy_precedes_change()
    {
        var (_, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        try
        {
            recorder.Calls.Clear();
            await ContainerRuntimeHarness.Run(ctx, env, """
                UpDown1.Value = 5
                UpDown1.Value = 5
                UpDown1.Max = 3
                UpDown1.AutoBuddy = True
                UpDown1.SyncBuddy = True
                UpDown1.Value = 1
                """);
            recorder.Calls.Select(c => c.Name).Should().Equal("UpDown1_Change", "UpDown1_Change", "UpDown1_Change", "Text1_Change", "UpDown1_Change");
        }
        finally
        {
            (TopLevel.GetTopLevel(recorder) as Window)?.Close();
        }
    }
}
