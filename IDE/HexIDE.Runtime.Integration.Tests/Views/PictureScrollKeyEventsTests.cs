using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6: #4575's tables, measured in oracle PR #4574 (PictureBox and scroll bars).
public class PictureScrollKeyEventsTests
{
    public static IEnumerable<object[]> Keys =>
        from kind in new[] { "PictureBox", "HScrollBar", "VScrollBar" }
        from row in new (Key Key, string? Text, int Code, int? Ascii)[]
        {
            (Key.A, "a", 65, 97), (Key.Space, " ", 32, 32),
            (Key.Enter, null, 13, 13), (Key.Escape, null, 27, 27),
        }
        select new object[] { kind, row.Key, row.Text!, row.Code, row.Ascii! };

    [AvaloniaTheory]
    [MemberData(nameof(Keys))]
    public void Focused_control_raises_all_three_events(string kind, Key key, string? text, int code, int ascii)
    {
        using var run = new Run(kind);
        run.Key(key, text);
        run.Log.Should().Equal($"D{code}", $"P{ascii}", $"U{code}");
        run.Target.IsFocused.Should().BeTrue();
        if (run.Target is VBScrollBar bar) bar.VbValue.Should().Be(50);
    }

    [AvaloniaFact]
    public void PictureBox_Right_keeps_focus_and_raises_down_and_up()
    {
        using var run = new Run("PictureBox");
        run.Key(Key.Right);
        run.Log.Should().Equal("D39", "U39");
        run.Target.IsFocused.Should().BeTrue();
    }

    public static IEnumerable<object[]> Moves =>
        from kind in new[] { "HScrollBar", "VScrollBar" }
        from row in new[]
        {
            (Key.Left, 50, 49), (Key.Up, 50, 49), (Key.Right, 50, 51), (Key.Down, 50, 51),
            (Key.PageUp, 100, 90), (Key.PageDown, 51, 61), (Key.Home, 49, 0), (Key.End, 0, 100),
        }
        select new object[] { kind, row.Item1, row.Item2, row.Item3 };

    [AvaloniaTheory]
    [MemberData(nameof(Moves))]
    public void Bar_moves_between_KeyDown_and_KeyUp_without_Scroll(string kind, Key key, int start, int end)
    {
        using var run = new Run(kind, value: start);
        run.Key(key);
        run.Bar.VbValue.Should().Be(end);
        run.Log.Should().Equal($"D{Code(key)}", "Change", $"U{Code(key)}");
    }

    public static IEnumerable<object[]> ReversedMoves =>
        from kind in new[] { "HScrollBar", "VScrollBar" }
        from row in new[]
        {
            (Key.Left, 50, 51), (Key.Up, 50, 51), (Key.Right, 51, 50), (Key.Down, 51, 50),
            (Key.Home, 50, 0), (Key.End, 0, 100), (Key.PageUp, 100, 100), (Key.PageDown, 100, 90),
        }
        select new object[] { kind, row.Item1, row.Item2, row.Item3 };

    [AvaloniaTheory]
    [MemberData(nameof(ReversedMoves))]
    public void Reversed_bar_uses_VB_values_and_does_not_Change_at_the_end(string kind, Key key, int start, int end)
    {
        using var run = new Run(kind, value: start, reversed: true);
        run.Key(key);
        run.Bar.VbValue.Should().Be(end);
        run.Log.Should().Equal(start == end
            ? new[] { $"D{Code(key)}", $"U{Code(key)}" }
            : new[] { $"D{Code(key)}", "Change", $"U{Code(key)}" });
    }

    [AvaloniaTheory]
    [MemberData(nameof(Moves))]
    public void Controls_KeyCode_zero_stops_each_move(string kind, Key key, int start, int end)
    {
        using var run = new Run(kind, value: start, zero: "Target");
        run.Key(key);
        run.Bar.VbValue.Should().Be(start).And.NotBe(end);
        run.Log.Should().Equal($"D{Code(key)}", $"U{Code(key)}");
    }

    public static IEnumerable<object[]> PreviewMoves =>
        from kind in new[] { "HScrollBar", "VScrollBar" }
        from key in new[] { Key.Right, Key.PageDown, Key.Home, Key.Down, Key.End }
        select new object[] { kind, key };

    [AvaloniaTheory]
    [MemberData(nameof(PreviewMoves))]
    public void Forms_KeyCode_zero_skips_the_controls_KeyDown_and_move(string kind, Key key)
    {
        using var run = new Run(kind, preview: true, zero: "Form");
        run.Key(key);
        run.Bar.VbValue.Should().Be(50);
        run.Log.Should().Equal($"FD{Code(key)}", $"FU{Code(key)}", $"U{Code(key)}");
    }

    [AvaloniaTheory]
    [InlineData("HScrollBar")]
    [InlineData("VScrollBar")]
    public void Preview_delivers_form_then_control_before_the_move(string kind)
    {
        using var run = new Run(kind, preview: true);
        run.Key(Key.Right);
        run.Bar.VbValue.Should().Be(51);
        run.Log.Should().Equal("FD39", "D39", "Change", "FU39", "U39");
    }

    [AvaloniaTheory]
    [InlineData("HScrollBar", Key.A, "a", 65, 97)]
    [InlineData("VScrollBar", Key.A, "a", 65, 97)]
    [InlineData("HScrollBar", Key.Space, " ", 32, 32)]
    [InlineData("VScrollBar", Key.Space, " ", 32, 32)]
    public void Forms_KeyCode_zero_leaves_KeyPress(string kind, Key key, string text, int code, int ascii)
    {
        using var run = new Run(kind, preview: true, zero: "Form");
        run.Key(key, text);
        run.Log.Should().Equal($"FD{code}", $"FP{ascii}", $"P{ascii}", $"FU{code}", $"U{code}");
        run.Bar.VbValue.Should().Be(50);
    }

    public static IEnumerable<object[]> HolderKeys =>
        from kind in new[] { "PictureBox", "HScrollBar", "VScrollBar" }
        from key in new[] { Key.Enter, Key.Escape, Key.Space }
        select new object[] { kind, key };

    [AvaloniaTheory]
    [MemberData(nameof(HolderKeys))]
    public void Default_and_Cancel_take_dialog_keys_but_leave_Space(string kind, Key key)
    {
        using var run = new Run(kind, holders: true);
        run.Key(key, key == Key.Space ? " " : null);
        run.Log.Should().Equal(key == Key.Space ? new[] { "D32", "P32", "U32" }
            : new[] { key == Key.Enter ? "Default" : "Cancel" });
    }

    [AvaloniaTheory]
    [InlineData("PictureBox")]
    [InlineData("HScrollBar")]
    [InlineData("VScrollBar")]
    public void Tab_moves_focus_without_control_key_events(string kind)
    {
        using var run = new Run(kind);
        run.Key(Key.Tab);
        run.Log.Should().BeEmpty();
        run.Next.IsFocused.Should().BeTrue();
    }

    [AvaloniaFact]
    public void TabStop_false_skips_PictureBox()
    {
        using var run = new Run("PictureBox", tabStop: false);
        run.Next.IsFocused.Should().BeTrue();
        run.Key(Key.Tab);
        run.Target.IsFocused.Should().BeFalse();
        run.Log.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Click_focuses_PictureBox_with_TabStop_false_and_keys_reach_it()
    {
        using var run = new Run("PictureBox", tabStop: false);
        var point = run.Target.TranslatePoint(new Point(10, 10), run.Window)!.Value;
        run.Window.MouseDown(point, MouseButton.Left);
        run.Window.MouseUp(point, MouseButton.Left);
        run.Target.IsFocused.Should().BeTrue();
        run.Key(Key.A, "a");
        run.Log.Should().Equal("D65", "P97", "U65");
    }

    [AvaloniaFact]
    public void Guard_child_receives_its_own_keys_without_PictureBox_events()
    {
        // Not a VB6 measurement: preserve main's routing for contained controls (#4575).
        using var run = new Run("PictureBox", child: true);
        run.Named("Inner").Focus().Should().BeTrue();
        run.Key(Key.A, "a");
        run.Log.Should().Equal("ID65", "IP97", "IU65");
    }

    [Theory]
    [InlineData("PictureBox")]
    [InlineData("HScrollBar")]
    [InlineData("VScrollBar")]
    public void Component_offers_keyboard_handlers_and_keeps_its_default_event(string kind)
    {
        var component = kind == "PictureBox" ? PictureBoxComponentClass.Instance
            : kind == "HScrollBar" ? HScrollBarComponentClass.Instance : VScrollBarComponentClass.Instance;
        component.Events.First().Name.Should().Be(kind == "PictureBox" ? "Click" : "Change");
        component.Events.Select(e => e.Name).Should().Contain(new[] { "KeyDown", "KeyPress", "KeyUp" });
        // #991 declares both measured PictureBox events, without changing its default Click.
        if (kind == "PictureBox")
            component.Events.Select(e => e.Name).Should().Contain(new[] { "Paint", "Resize" });
    }

    private static int Code(Key key) => key switch
    {
        Key.Left => 37, Key.Up => 38, Key.Right => 39, Key.Down => 40,
        Key.PageUp => 33, Key.PageDown => 34, Key.Home => 36, Key.End => 35,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private sealed class Run : IDisposable
    {
        public readonly List<string> Log = [];
        private readonly List<Exception> faults = [];
        public readonly VBFormRuntime Window;
        public readonly Control Target;
        public Control Next => Named("NextControl");
        public VBScrollBar Bar => (VBScrollBar)Target;
        public Control Named(string name) => Window.GetVisualDescendants().OfType<Control>().Single(c => VBProps.GetName(c) == name);

        public Run(string kind, int value = 50, bool reversed = false, bool preview = false,
            string zero = "", bool holders = false, bool tabStop = true, bool child = false)
        {
            var blocks = UserControlKeyTests.Block("VB." + kind, "Target", 0,
                child ? UserControlKeyTests.Block("VB.TextBox", "Inner", 3) : "",
                extra: $"TabStop = {(tabStop ? -1 : 0)}\n" + (kind == "PictureBox" ? "" :
                    $"Min = {(reversed ? 100 : 0)}\nMax = {(reversed ? 0 : 100)}\nValue = {value}\nSmallChange = 1\nLargeChange = 10"))
                + UserControlKeyTests.Block("VB.CommandButton", "NextControl", 1, extra: holders ? "Default = -1" : "")
                + UserControlKeyTests.Block("VB.CommandButton", "LastControl", 2, extra: holders ? "Cancel = -1" : "");
            var code = "";
            foreach (var (name, prefix) in new[] { ("Form", "F"), ("Target", ""), ("Inner", "I") })
                foreach (var (ev, tag, arg) in new[] { ("KeyDown", "D", "KeyCode"), ("KeyPress", "P", "KeyAscii"), ("KeyUp", "U", "KeyCode") })
                    code += $"Private Sub {name}_{ev}({arg} As Integer{(ev == "KeyPress" ? "" : ", Shift As Integer")})\n"
                        + $"Debug.Print \"{prefix}{tag}\" & {arg}\n"
                        + (zero == name && ev == "KeyDown" ? "KeyCode = 0\n" : "") + "End Sub\n";
            code += "Private Sub Target_Change()\nDebug.Print \"Change\"\nEnd Sub\n"
                + "Private Sub Target_Scroll()\nDebug.Print \"Scroll\"\nEnd Sub\n"
                + "Private Sub NextControl_Click()\nDebug.Print \"Default\"\nEnd Sub\n"
                + "Private Sub LastControl_Click()\nDebug.Print \"Cancel\"\nEnd Sub\n";
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project,
                ($"VERSION 5.00\nBegin VB.Form Form1\nClientHeight = 4000\nClientWidth = 6000\nKeyPreview = {(preview ? -1 : 0)}\n"
                + blocks + "End\nAttribute VB_Name = \"Form1\"\n" + code).ReplaceLineEndings("\r\n"), new UserControlKeyTests.Sink())!;
            project.AddForm(form);
            VBDebugConsole.Output += Log.Add;
            VBWindowContext.Faulted += Faulted;
            VBLoader.RunForm(form, CancellationToken.None, out var window, null);
            Window = window!;
            Dispatcher.UIThread.RunJobs();
            Target = Named("Target");
            if (tabStop) Target.Focus().Should().BeTrue();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Log.Clear();
        }

        private void Faulted(VBWindowContext _, Exception error) => faults.Add(error);
        public void Key(Key key, string? text = null)
        {
            Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            if (text is not null) Window.KeyTextInput(text);
            Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            faults.Should().BeEmpty();
        }
        public void Dispose()
        {
            Window.Close();
            VBDebugConsole.Output -= Log.Add;
            VBWindowContext.Faulted -= Faulted;
        }
    }
}
