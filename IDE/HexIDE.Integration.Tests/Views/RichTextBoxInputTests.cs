using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.RichText;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class RichTextBoxInputTests
{
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public List<string> Calls { get; } = new();
        public List<Vb6Value> KeyPressCodes { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            Calls.Add(name);
            if (name.EndsWith("_KeyPress", StringComparison.Ordinal)) KeyPressCodes.Add(args![0]);
        }
    }

    private static (Window Window, VBRichTextBox Box, Recorder Events) Open(string text)
    {
        var box = new VBRichTextBox();
        box.SetText(text);
        VBProps.SetName(box, "r");
        var recorder = new Recorder { Child = box };
        var window = new Window { Content = recorder, Width = 300, Height = 100 };
        window.Show();
        window.UpdateLayout();
        box.Focus();
        recorder.Calls.Clear();
        return (window, box, recorder);
    }

    [AvaloniaFact]
    public void Typing_replaces_the_selection_keeps_its_format_and_raises_selection_before_change()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            box.Select(6, 4);
            box.FormatSelection(f => f with { Bold = true, Color = 255 });
            events.Calls.Clear();
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            window.KeyTextInput("x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            box.Document.Text.Should().Be("alpha x gamma");
            box.Start.Should().Be(7);
            box.Length.Should().Be(0);
            box.Document.Slice(6, 1).Single().Format.Should().BeEquivalentTo(new RtfCharacterFormat(Bold: true, Color: 255));
            events.Calls.Should().Equal("r_KeyDown", "r_KeyPress", "r_SelChange", "r_Change", "r_KeyUp");
            Action unmeasuredKey = () => window.KeyTextInput("€");
            // Refused, and sent to the form: Input_refusals_reach_the_program_as_its_run_time_error.
            unmeasuredKey.Should().NotThrow();
            box.Document.Text.Should().Be("alpha x gamma");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Enter_inserts_CRLF_and_Backspace_removes_both_before_KeyPress()
    {
        var (window, box, events) = Open("a");
        try
        {
            box.Select(1, 0);
            events.Calls.Clear();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            box.Document.Text.Should().Be("a\r\n");
            box.Start.Should().Be(3);
            events.Calls.Should().Equal("r_KeyDown", "r_KeyPress", "r_SelChange", "r_Change", "r_KeyUp");
            events.Calls.Clear();
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            box.Document.Text.Should().Be("a");
            box.Start.Should().Be(1);
            events.Calls.Should().Equal("r_KeyDown", "r_SelChange", "r_Change", "r_KeyPress", "r_KeyUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Protected_text_drops_typing_without_change_events()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            box.Select(6, 4);
            box.FormatSelection(f => f with { Protected = true });
            events.Calls.Clear();
            window.KeyTextInput("x");
            box.Document.Text.Should().Be("alpha beta gamma");
            events.Calls.Should().Equal("r_KeyPress");
            box.Select(8, 0);
            events.Calls.Clear();
            window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            window.KeyRelease(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
            box.Document.Text.Should().Be("alpha beta gamma");
            box.Start.Should().Be(8);
            box.Length.Should().Be(0);
            events.Calls.Should().Equal("r_KeyDown", "r_KeyPress", "r_KeyUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("ReadOnly", -1, "abc")]
    [InlineData("MaxLength", 5, "abcxy")]
    public void Saved_limits_apply_to_real_input(string property, int value, string expected)
    {
        var instance = new HexIDE.Runtime.Components.ComponentInstance(HexIDE.Runtime.Components.RichTextBoxComponentClass.Instance, "r");
        instance.UnknownRawProperties.Add(new("TextRTF", ["TextRTF = \"abc\"" ]));
        instance.UnknownRawProperties.Add(new(property, [$"{property} = {value}"]));
        var box = (VBRichTextBox)HexIDE.Runtime.Components.RichTextBoxComponentClass.Instance.Instantiate(instance);
        VBProps.SetName(box, "r");
        var recorder = new Recorder { Child = box };
        var window = new Window { Content = recorder, Width = 300, Height = 100 };
        try
        {
            window.Show();
            box.Focus();
            box.Select(3, 0);
            window.KeyTextInput("xyz");
            box.Document.Text.Should().Be(expected);
            recorder.Calls.Should().Contain("r_KeyPress");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Locked")]
    [InlineData("SelProtected")]
    public void Runtime_protection_drops_real_input(string member)
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            box.Select(6, 4);
            RichTextBoxMembers.TrySet(box, member, new Vb6Value(true));
            events.Calls.Clear();
            window.KeyPress(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            window.KeyTextInput("x");
            window.KeyRelease(Key.X, RawInputModifiers.None, PhysicalKey.X, null);
            box.Document.Text.Should().Be("alpha beta gamma");
            events.Calls.Should().Equal("r_KeyDown", "r_KeyPress", "r_KeyUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Runtime_MaxLength_drops_keys_after_the_measured_twenty_character_limit()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            RichTextBoxMembers.TrySet(box, "MaxLength", new Vb6Value(20));
            box.Select(16, 0);
            events.Calls.Clear();
            window.KeyTextInput("abcdefgh");
            box.Document.Text.Should().Be("alpha beta gammaabcd");
            events.Calls.FindAll(c => c == "r_Change").Count.Should().Be(4);
            events.Calls.FindAll(c => c == "r_KeyPress").Count.Should().Be(8);
        }
        finally { window.Close(); }
    }

    private static KeyModifiers CommandModifiers =>
        Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;

    private static void Shortcut(Window window, Key key) =>
        window.KeyPress(key, (RawInputModifiers)CommandModifiers, PhysicalKey.None, null);

    [AvaloniaFact]
    public void Select_all_tab_and_paragraph_shortcuts_follow_the_recorded_edits_and_undo()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            Shortcut(window, Key.A);
            box.Start.Should().Be(0);
            box.Length.Should().Be(16);
            events.KeyPressCodes.Should().Equal(new Vb6Value((short)1));
            Shortcut(window, Key.I);
            events.KeyPressCodes.Should().Equal(new Vb6Value((short)1), new Vb6Value((short)9));
            box.Document.Text.Should().Be("\t");
            Shortcut(window, Key.Z);
            box.Document.Text.Should().Be("alpha beta gamma");
            box.Select(0, 0);
            foreach (var (key, alignment) in new[] { (Key.E, "qc"), (Key.R, "qr"), (Key.L, "ql") })
            {
                events.Calls.Clear();
                Shortcut(window, key);
                box.Document.Runs.First().Paragraph.Alignment.Should().Be(alignment);
                events.Calls.Should().Contain("r_Change");
            }
            events.Calls.Clear();
            Shortcut(window, Key.Z);
            box.Document.Runs.First().Paragraph.Alignment.Should().Be("qr");
            events.Calls.Where(c => c is "r_SelChange" or "r_Change").Should().Equal("r_SelChange", "r_Change");
            box.SetText("assigned from code");
            Action undo = () => Shortcut(window, Key.Z);
            undo.Should().NotThrow();
            box.Document.Text.Should().Be("assigned from code");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(Key.B)]
    [InlineData(Key.U)]
    public void Bold_and_underline_shortcuts_only_raise_keys(Key key)
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            box.Select(0, 5);
            var before = box.WriteRtf();
            events.Calls.Clear();
            Shortcut(window, key);
            box.WriteRtf().Should().Be(before);
            events.Calls.Should().Equal("r_KeyDown", "r_KeyPress");
            events.KeyPressCodes.Should().Equal(new Vb6Value((short)(key == Key.B ? 2 : 21)));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_the_recorded_plain_selection_and_undo_preserve_the_document()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            box.Select(6, 4);
            Shortcut(window, Key.C);
            (await window.Clipboard!.TryGetTextAsync()).Should().Be("beta");
            box.Select(16, 0);
            events.Calls.Clear();
            Shortcut(window, Key.V);
            // Headless clipboard tasks complete synchronously; the assertion observes the actual key's edit.
            box.Document.Text.Should().Be("alpha beta gammabeta");
            events.Calls.Where(c => c is "r_SelChange" or "r_Change").Should().Equal("r_SelChange", "r_Change");
            Shortcut(window, Key.Z);
            box.Document.Text.Should().Be("alpha beta gamma");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Formatted_copy_refuses_the_unmeasured_transfer_instead_of_flattening()
    {
        var (window, box, _) = Open("alpha beta gamma");
        try
        {
            box.Select(6, 4);
            box.FormatSelection(f => f with { Bold = true });
            Action copy = () => Shortcut(window, Key.C);
            copy.Should().NotThrow();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Non_text_clipboard_formats_refuse_before_pasting_the_plain_fallback()
    {
        var (window, box, _) = Open("alpha beta gamma");
        try
        {
            var data = new DataTransfer();
            var item = DataTransferItem.CreateText("beta");
            item.Set(DataFormat.CreateStringApplicationFormat("rich-text-test"), "unmodelled formatting");
            data.Add(item);
            await window.Clipboard!.SetDataAsync(data);
            box.Select(16, 0);
            Func<Task> paste = () => box.PasteInput();
            await paste.Should().ThrowAsync<NotImplementedException>().WithMessage("*formatted clipboard*#1100*");
            box.Document.Text.Should().Be("alpha beta gamma");
        }
        finally { await window.Clipboard!.ClearAsync(); window.Close(); }
    }

    private const string KeyboardProbe = "Hello, world. Foo-bar baz! it's 3.14 (x)\r\n\r\nSecond line, here.\r\nEnd";

    [AvaloniaTheory]
    [InlineData(0, "Right", 1)]
    [InlineData(5, "Left", 4)]
    [InlineData(40, "Right", 42)]
    [InlineData(44, "Left", 42)]
    [InlineData(9, "Home", 0)]
    [InlineData(9, "End", 42)]
    [InlineData(3, "Down", 44)]
    [InlineData(3, "Up", 0)]
    [InlineData(66, "Down", 69)]
    [InlineData(67, "Right", 69)]
    [InlineData(67, "Home", 64)]
    public void Plain_navigation_moves_to_the_recorded_Shift_endpoint(int start, string key, int expected)
    {
        var (window, box, _) = Open(KeyboardProbe);
        try
        {
            window.Width = 1000;
            window.UpdateLayout();
            box.Select(start, 0);
            window.KeyPress(Enum.Parse<Key>(key), RawInputModifiers.None, PhysicalKey.None, null);
            box.Start.Should().Be(expected);
            box.Length.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("Up")]
    [InlineData("Down")]
    public void Plain_navigation_with_a_selection_refuses_unmeasured_non_arrow_collapse(string key)
    {
        var (window, box, _) = Open(KeyboardProbe);
        try
        {
            box.Select(7, 5);
            Action navigate = () => window.KeyPress(Enum.Parse<Key>(key), RawInputModifiers.None, PhysicalKey.None, null);
            navigate.Should().NotThrow();
            box.Start.Should().Be(7);
            box.Length.Should().Be(5);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("a\nb", "Right")]
    [InlineData("a\nb", "Left")]
    [InlineData("a\nb", "Home")]
    [InlineData("a\nb", "End")]
    [InlineData("a\nb", "Up")]
    [InlineData("a\nb", "Down")]
    [InlineData("wrapped", "Home")]
    [InlineData("wrapped", "End")]
    [InlineData("wrapped", "Up")]
    [InlineData("wrapped", "Down")]
    public void Plain_navigation_refuses_unmeasured_lone_break_and_wrapped_line_endpoints(string text, string key)
    {
        var (window, box, _) = Open(text == "wrapped" ? new string('a', 300) : text);
        try
        {
            Action navigate = () => window.KeyPress(Enum.Parse<Key>(key), RawInputModifiers.None, PhysicalKey.None, null);
            navigate.Should().NotThrow();
            box.Start.Should().Be(0);
            box.Length.Should().Be(0);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, "Right,Left", "0+1,0+0")]
    [InlineData(5, "Left,Left", "4+1,3+2")]
    [InlineData(40, "Right,Right,Right", "40+2,40+4,40+5")]
    [InlineData(44, "Left,Left", "42+2,40+4")]
    [InlineData(9, "Home", "0+9")]
    [InlineData(9, "End,End", "9+33,9+33")]
    [InlineData(9, "Home,End", "0+9,9+33")]
    [InlineData(3, "Down,Down,Down", "3+41,3+61,3+66")]
    [InlineData(3, "Down,Up", "3+41,0+3")]
    [InlineData(3, "Up", "0+3")]
    [InlineData(66, "Down", "66+3")]
    [InlineData(67, "Right", "67+2")]
    [InlineData(67, "Home", "64+3")]
    public void Shift_navigation_matches_the_recorded_paragraph_endpoints(int start, string keys, string expected)
    {
        var (window, box, _) = Open(KeyboardProbe);
        try
        {
            window.Width = 1000;
            window.UpdateLayout();
            box.Document.Text.Length.Should().Be(67);
            box.Select(start, 0);
            var results = new List<string>();
            foreach (var key in keys.Split(','))
            {
                window.KeyPress(Enum.Parse<Key>(key), RawInputModifiers.Shift, PhysicalKey.None, null);
                results.Add($"{box.Start}+{box.Length}");
            }
            string.Join(',', results).Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, "Left", false, false, "7+0")]
    [InlineData(false, "Right", false, false, "12+0")]
    [InlineData(true, "Left", false, false, "7+0")]
    [InlineData(false, "Right", true, false, "12+0")]
    [InlineData(false, "Right", true, true, "7+7")]
    [InlineData(false, "Left", false, true, "7+4")]
    [InlineData(false, "Right", false, true, "7+6")]
    [InlineData(true, "Right", false, true, "8+4")]
    [InlineData(true, "Left", false, true, "6+6")]
    public void Navigation_moves_the_active_end_or_collapses_to_the_pressed_side(
        bool backwards, string key, bool command, bool shift, string expected)
    {
        var (window, box, _) = Open(KeyboardProbe);
        try
        {
            box.Select(backwards ? 12 : 7, backwards ? 0 : 5);
            if (backwards)
                for (var i = 0; i < 5; i++)
                    window.KeyPress(Key.Left, RawInputModifiers.Shift, PhysicalKey.None, null);
            box.Start.Should().Be(7);
            box.Length.Should().Be(5);
            var modifiers = (command ? (RawInputModifiers)CommandModifiers : RawInputModifiers.None)
                | (shift ? RawInputModifiers.Shift : RawInputModifiers.None);
            window.KeyPress(Enum.Parse<Key>(key), modifiers, PhysicalKey.None, null);
            $"{box.Start}+{box.Length}".Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(0, true, false, "5,7,12,14,17,18,22,25,27,32,33,34,37,38,39,40,42,44,51,55,57,61,62,64,67")]
    [InlineData(67, false, false, "64,62,61,57,55,51,44,42,40,39,38,37,34,33,32,27,25,22,18,17,14,12,7,5,0")]
    [InlineData(16, true, false, "17,18,22,25,27,32,33,34,37,38,39,40,42,44,51,55,57,61,62,64,67")]
    [InlineData(0, true, true, "0+5,0+7,0+12")]
    [InlineData(40, false, true, "39+1,38+2,37+3")]
    public void Command_arrows_match_the_recorded_word_stops(int start, bool right, bool shift, string expected)
    {
        var (window, box, _) = Open(KeyboardProbe);
        try
        {
            box.Select(start, 0);
            var results = new List<string>();
            foreach (var _ in expected.Split(','))
            {
                window.KeyPress(right ? Key.Right : Key.Left,
                    (RawInputModifiers)CommandModifiers | (shift ? RawInputModifiers.Shift : RawInputModifiers.None),
                    PhysicalKey.None, null);
                results.Add(shift ? $"{box.Start}+{box.Length}" : box.Start.ToString());
            }
            string.Join(',', results).Should().Be(expected);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("wrapped")]
    [InlineData("lone-break")]
    [InlineData("non-ASCII")]
    [InlineData("collapse")]
    [InlineData("hidden-break-collapse")]
    public void Unmeasured_keyboard_endpoints_refuse(string edge)
    {
        var (window, box, _) = Open(edge == "wrapped" ? new string('a', 300)
            : edge == "lone-break" ? "a\nb" : "café");
        try
        {
            if (edge == "collapse") box.Select(0, 2);
            if (edge == "hidden-break-collapse") box.Select(box.Document.Text.Length, 2);
            Action navigate = () => window.KeyPress(edge == "collapse" ? Key.Left : edge is "non-ASCII" or "hidden-break-collapse" ? Key.Right : Key.Down,
                edge is "non-ASCII" or "collapse" ? (RawInputModifiers)CommandModifiers
                    : edge == "hidden-break-collapse" ? RawInputModifiers.None : RawInputModifiers.Shift,
                PhysicalKey.None, null);
            navigate.Should().NotThrow();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Shift_down_scrolls_to_the_moving_end_and_mouse_hit_testing_follows_it()
    {
        var (window, box, _) = Open(string.Join("\r\n", Enumerable.Repeat("alpha beta gamma", 40)));
        try
        {
            for (var i = 0; i < 39; i++)
                window.KeyPress(Key.Down, RawInputModifiers.Shift, PhysicalKey.None, null);
            box.Start.Should().Be(0);
            window.MouseDown(new Point(2, 4), MouseButton.Left);
            window.MouseUp(new Point(2, 4), MouseButton.Left);
            box.Start.Should().BeGreaterThan(0);
        }
        finally { window.Close(); }
    }

    private static Point AtCharacter(VBRichTextBox box, int character)
    {
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(box.Document.Text,
            new Typeface("MS Sans Serif"), 11, Brushes.Black, maxWidth: box.Bounds.Width - 4);
        var caret = layout.HitTestTextPosition(character);
        return new Point(caret.X + 2, caret.Y + 4);
    }

    [AvaloniaFact]
    public void Click_and_double_click_select_after_Click_and_DblClick()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            var point = AtCharacter(box, 9);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            box.Start.Should().Be(9);
            events.Calls.Should().Equal("r_MouseDown", "r_MouseUp", "r_Click", "r_SelChange");
            events.Calls.Clear();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            box.Start.Should().Be(6);
            box.Length.Should().Be(5);
            events.Calls.Should().Equal("r_DblClick", "r_SelChange", "r_MouseUp");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("modified")]
    [InlineData("drag")]
    [InlineData("punctuation")]
    public void Unmeasured_pointer_selection_edges_refuse(string gesture)
    {
        var (window, box, _) = Open(gesture == "punctuation" ? "alpha-beta" : "alpha beta gamma");
        try
        {
            var point = AtCharacter(box, 4);
            Action select = () =>
            {
                if (gesture == "modified") window.MouseDown(point, MouseButton.Left, RawInputModifiers.Shift);
                else
                {
                    window.MouseDown(point, MouseButton.Left);
                    window.MouseUp(gesture == "drag" ? AtCharacter(box, 10) : point, MouseButton.Left);
                    if (gesture == "punctuation") window.MouseDown(point, MouseButton.Left);
                }
            };
            select.Should().NotThrow();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Right_click_raises_Click()
    {
        var (window, box, events) = Open("alpha beta gamma");
        try
        {
            var point = AtCharacter(box, 9);
            window.MouseDown(point, MouseButton.Right);
            window.MouseUp(point, MouseButton.Right);
            events.Calls.Should().ContainInOrder("r_MouseDown", "r_MouseUp", "r_Click");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Editing_at_the_end_scrolls_the_caret_into_view_and_mouse_hit_testing_uses_that_offset()
    {
        var (window, box, _) = Open(string.Join("\r\n", Enumerable.Repeat("alpha beta gamma", 40)));
        try
        {
            box.Select(box.Document.Text.Length, 0);
            window.KeyTextInput("x");
            // The last line's x must be visible, rather than the first line's alpha.
            box.Select(box.Document.Text.Length - 1, 1);
            box.FormatSelection(f => f with { Color = 255 });
            box.Select(box.Document.Text.Length, 0);
            var atEnd = Pixels(box);
            Enumerable.Range(0, atEnd.Length / 4).Count(i => atEnd[i * 4 + 2] > 150
                && atEnd[i * 4 + 1] < 100 && atEnd[i * 4] < 100).Should().BeGreaterThan(0);
            box.Select(0, 0);
            Pixels(box).Should().NotEqual(atEnd);
            box.Select(box.Document.Text.Length, 0);
            window.MouseDown(new Point(2, 4), MouseButton.Left);
            window.MouseUp(new Point(2, 4), MouseButton.Left);
            box.Start.Should().BeGreaterThan(0);
            box.Start.Should().BeLessThan(box.Document.Text.Length);
        }
        finally { window.Close(); }
    }

    private static VBRichTextBox SavedBox(params string[] properties)
    {
        var type = HexIDE.Runtime.Components.RichTextBoxComponentClass.Instance;
        var instance = new HexIDE.Runtime.Components.ComponentInstance(type, "r");
        foreach (var property in properties)
            instance.UnknownRawProperties.Add(new(property.Split('=')[0].Trim(), [property]));
        return (VBRichTextBox)type.Instantiate(instance);
    }

    [AvaloniaFact]
    public void Saved_background_fills_the_box()
    {
        var box = SavedBox("BackColor = &H000000FF&");
        var window = new Window { Content = box, Width = 300, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var pixels = Pixels(box);
            pixels.Skip((50 * 300 + 150) * 4).Take(4).Should().Equal(0, 0, 255, 255);

        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Disabled_drawing_uses_button_face_and_grey_text_then_restores_the_background_and_colour()
    {
        var box = SavedBox("BackColor = &H000000FF&", "Enabled = 0");
        box.SetText("MMMM");
        box.Select(0, 4);
        box.FormatSelection(f => f with { Color = 16711680 });
        box.Select(4, 0);
        var window = new Window { Content = box, Width = 300, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var disabled = Pixels(box);
            var face = ((ISolidColorBrush)HexIDE.Runtime.BuiltinTypes.VBColorExtensions.ToBrush(
                HexIDE.Runtime.BuiltinTypes.VBColor.FromSystemColor(HexIDE.Runtime.BuiltinTypes.VbSystemColor.Btnface))).Color;
            disabled.Skip((50 * 300 + 150) * 4).Take(4).Should().Equal(face.B, face.G, face.R, face.A);
            Enumerable.Range(0, disabled.Length / 4).Count(i => disabled[i * 4 + 2] > 150
                && disabled[i * 4 + 1] < 100 && disabled[i * 4] < 100).Should().Be(0);
            var grey = ((ISolidColorBrush)HexIDE.Runtime.BuiltinTypes.VBColorExtensions.ToBrush(
                HexIDE.Runtime.BuiltinTypes.VBColor.FromSystemColor(HexIDE.Runtime.BuiltinTypes.VbSystemColor.Graytext))).Color;
            using var layout = (TextLayout)typeof(VBRichTextBox)
                .GetMethod("CreateLayout", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, null)!;
            var runs = layout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().ToArray();
            runs.Should().NotBeEmpty();
            runs.Select(run => ((ISolidColorBrush)run.Properties.ForegroundBrush!).Color).Should().OnlyContain(color => color == grey);
            // Anti-aliased glyphs blend with the face; exclude the border and the unchanged face.
            Enumerable.Range(4, 40).Sum(y => Enumerable.Range(4, 100).Count(x =>
            {
                var i = (y * 300 + x) * 4;
                var b = disabled[i];
                var g = disabled[i + 1];
                var r = disabled[i + 2];
                return Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 3
                    && r < face.R - 3 && g < face.G - 3 && b < face.B - 3;
            })).Should().BeGreaterThan(0);
            RichTextBoxMembers.TrySet(box, "Enabled", new Vb6Value(true)).Should().BeTrue();
            Pixels(box).Should().NotEqual(disabled);
            Pixels(box).Skip((50 * 300 + 150) * 4).Take(4).Should().Equal(0, 0, 255, 255);
            var enabledPixels = Pixels(box);
            Enumerable.Range(0, enabledPixels.Length / 4).Count(i => enabledPixels[i * 4] > 150
                && enabledPixels[i * 4 + 1] < 100 && enabledPixels[i * 4 + 2] < 100).Should().BeGreaterThan(0);
            RichTextBoxMembers.TryGet(box, "Enabled", out var enabled).Should().BeTrue();
            enabled.Should().Be(new Vb6Value(true));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    public void Saved_border_and_appearance_draw_a_border_that_borderless_omits(int appearance, int border)
    {
        var box = SavedBox($"Appearance = {appearance}", $"BorderStyle = {border}");
        var borderless = SavedBox("BorderStyle = 0");
        var panel = new Grid { Children = { box, borderless } };
        var window = new Window { Content = panel, Width = 300, Height = 100 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var withBorder = Pixels(box);
            var withoutBorder = Pixels(borderless);
            withBorder.Take(300 * 4).Should().NotEqual(withoutBorder.Take(300 * 4));
            // Appearance changes the edge treatment, rather than only storing the number.
            var other = SavedBox($"Appearance = {1 - appearance}", "BorderStyle = 1");
            panel.Children.Add(other);
            window.UpdateLayout();
            withBorder.Should().NotEqual(Pixels(other));
        }
        finally { window.Close(); }
    }

    // Blue, green, red, alpha on every host. CopyPixels copies the render target's own layout, which is RGBA on macOS,
    // and every read in this file indexes BGRA.
    private static byte[] Pixels(VBRichTextBox box)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(300, 100));
        bitmap.Render(box);
        var bytes = new byte[300 * 4 * 100];
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 300, 100), handle.AddrOfPinnedObject(), bytes.Length, 300 * 4); }
        finally { handle.Free(); }
        if (bitmap.Format == Avalonia.Platform.PixelFormat.Rgba8888)
            for (var i = 0; i < bytes.Length; i += 4)
                (bytes[i], bytes[i + 2]) = (bytes[i + 2], bytes[i]);
        return bytes;
    }

    [AvaloniaTheory]
    [InlineData("SelBold")]
    [InlineData("SelItalic")]
    [InlineData("SelUnderline")]
    [InlineData("SelStrikeThru")]
    [InlineData("SelFontName")]
    public void Character_formatting_changes_the_rendered_glyphs(string member)
    {
        var (window, box, _) = Open("MMMM abcdef");
        try
        {
            box.Select(11, 0);
            var plain = Pixels(box);
            box.Select(0, 11);
            // Use an installed face so this exercises font selection even where Windows fonts are absent.
            var face = FontManager.Current.SystemFonts.First(f => f.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase)
                || f.Name.Contains("Courier", StringComparison.OrdinalIgnoreCase)
                || f.Name.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
                || f.Name.Contains("Menlo", StringComparison.OrdinalIgnoreCase));
            RichTextBoxMembers.TrySet(box, member, member == "SelFontName" ? new Vb6Value(face.Name) : new Vb6Value(true));
            box.Select(11, 0);
            Pixels(box).Should().NotEqual(plain);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Selection_is_black_and_hides_when_focus_leaves()
    {
        var (window, box, recorder) = Open("alpha beta gamma");
        try
        {
            var unselected = Pixels(box);
            box.Select(6, 5);
            var selected = Pixels(box);
            selected.Should().NotEqual(unselected);
            // Pure black background and white glyphs, rather than a platform highlight brush.
            Enumerable.Range(0, selected.Length / 4).Count(i => selected[i * 4] == 0 && selected[i * 4 + 1] == 0
                && selected[i * 4 + 2] == 0 && selected[i * 4 + 3] == 255).Should().BeGreaterThan(
                Enumerable.Range(0, unselected.Length / 4).Count(i => unselected[i * 4] == 0 && unselected[i * 4 + 1] == 0
                    && unselected[i * 4 + 2] == 0 && unselected[i * 4 + 3] == 255));
            var button = new Button();
            recorder.Child = null;
            recorder.Child = new Grid { Children = { box, button } };
            window.UpdateLayout();
            button.Focus();
            var hidden = Pixels(box);
            box.Select(0, 0);
            Pixels(box).Should().Equal(hidden);
        }
        finally { window.Close(); }
    }

    // A rendered red glyph, then larger red glyphs after a code edit: no host-font pixel metrics are pinned.
    [AvaloniaFact]
    public void Character_colour_and_size_are_drawn_and_code_edits_repaint()
    {
        var (window, box, _) = Open("MMMM");
        try
        {
            box.Select(0, 4);
            RichTextBoxMembers.TrySet(box, "SelColor", new Vb6Value(255));
            int RedPixels()
            {
                var bytes = Pixels(box);
                return Enumerable.Range(0, bytes.Length / 4).Count(i => bytes[i * 4 + 2] > 150 && bytes[i * 4 + 1] < 100 && bytes[i * 4] < 100);
            }
            box.Select(4, 0);
            var small = RedPixels();
            small.Should().BeGreaterThan(0);
            box.Select(0, 4);
            RichTextBoxMembers.TrySet(box, "SelFontSize", new Vb6Value(24));
            box.Select(4, 0);
            RedPixels().Should().BeGreaterThan(small);
        }
        finally { window.Close(); }
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    // A refusal met while handling input is the program's run-time error. Thrown out of the input handler it would reach
    // the UI thread, and end the IDE (#643).
    [AvaloniaTheory]
    [InlineData("navigation", "*endpoints*#1100*")]
    [InlineData("undo", "*Undo after a code edit*#1100*")]
    [InlineData("copy", "*formatted clipboard*#1100*")]
    [InlineData("non-ASCII", "*non-ASCII KeyPress*#1100*")]
    [InlineData("modified-press", "*modified mouse selection*#1100*")]
    [InlineData("drag", "*drag selection*#1100*")]
    public void Input_refusals_reach_the_program_as_its_run_time_error(string input, string message)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var source = "VERSION 5.00\r\nBegin VB.Form Form1\r\nClientWidth = 4500\r\nClientHeight = 3000\r\n"
            + "Begin RichTextLib.RichTextBox r\r\nWidth = 3000\r\nHeight = 1500\r\nEnd\r\nEnd\r\n"
            + "Attribute VB_Name = \"Form1\"\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n";
        project.AddForm(new FormDeserializer().Deserialize(project, source, new NullSink())!);
        using var cancel = new CancellationTokenSource();
        var context = ExecutionContext.Capture();
        VBFormRuntime? window = null;
        var faults = new List<Exception>();
        void Faulted(VBWindowContext faulted, Exception error)
        {
            if (window != null && ReferenceEquals(faulted, ((IFormView)window).Context))
                faults.Add(error);
        }
        VBWindowContext.Faulted += Faulted;
        try
        {
            _ = VBLoader.RunForm(project.StartupForm!, cancel.Token, out window);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var box = window.GetVisualDescendants().OfType<VBRichTextBox>().Single();
            box.SetText(input == "navigation" ? KeyboardProbe : "alpha beta gamma");
            box.Focus();
            Point At(int character) => box.TranslatePoint(AtCharacter(box, character), window)!.Value;
            Action act = input switch
            {
                "navigation" => () =>
                {
                    box.Select(7, 5);
                    window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.None, null);
                },
                "undo" => () =>
                {
                    box.Select(16, 0);
                    window.KeyTextInput("x");
                    box.SetText("assigned from code");
                    Shortcut(window, Key.Z);
                },
                "copy" => () =>
                {
                    box.Select(6, 4);
                    box.FormatSelection(f => f with { Bold = true });
                    Shortcut(window, Key.C);
                },
                "non-ASCII" => () => window.KeyTextInput("€"),
                "modified-press" => () => window.MouseDown(At(4), MouseButton.Left, RawInputModifiers.Shift),
                _ => () =>
                {
                    window.MouseDown(At(4), MouseButton.Left);
                    window.MouseUp(At(10), MouseButton.Left);
                },
            };
            act.Should().NotThrow();
            for (var i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs();
            faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
                .Which.Message.Should().Match(message);
        }
        finally
        {
            VBWindowContext.Faulted -= Faulted;
            cancel.Cancel();
            window?.Close();
            Dispatcher.UIThread.RunJobs();
            if (context != null) ExecutionContext.Restore(context);
        }
    }
}
