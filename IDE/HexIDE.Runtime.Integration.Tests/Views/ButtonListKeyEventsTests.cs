using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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

// VB6 SP6, native and p-code: docs/vb6-fidelity-oracle/key-events-keydown-keypress-and-keyup.md (#2325).
public class ButtonListKeyEventsTests
{
    public static IEnumerable<object[]> Controls =>
        from kind in new[] { "CommandButton", "CheckBox", "OptionButton", "ListBox", "Combo0", "Combo2" }
        from preview in new[] { false, true }
        select new object[] { kind, preview };

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Letters_raise_key_events_before_the_effect(string kind, bool preview)
    {
        using var run = new Run(kind, preview);
        var text = kind == "Combo2" ? "b" : "k";
        run.Key(text == "b" ? Key.B : Key.K, text);
        run.Log.Should().Equal(run.Events(text == "b" ? 66 : 75, text == "b" ? 98 : 107,
            kind is "ListBox" or "Combo2" ? "Click" : kind == "Combo0" ? "Change" : null));
        if (kind == "ListBox") ((VBListBox)run.Control!).SelectedIndex.Should().Be(2);
        if (kind == "Combo2") ((VBComboBox)run.Control!).SelectedIndex.Should().Be(1);
        if (kind == "Combo0") ((VBComboBox)run.Control!).Text.Should().Be("k");
    }

    // #1424: an Extended list's letter search follows KeyPress, so KeyAscii = 0 cancels it. Measured on a single-select
    // list (above and below); reasoned, not measured, with MultiSelect, since Windows' list box searches on the character.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_multi_select_list_searches_after_KeyPress_which_can_cancel_it(bool preview)
    {
        using var run = new Run("ListBoxMulti", preview, filtering: true);
        var list = (VBListBox)run.Control!;
        run.Key(Key.K, "k");
        run.Log.Should().Equal(run.Events(75, 107, "Click"));
        list.ListIndex.Should().Be(2);
        list.Selection.SelectedIndexes.Should().Equal(2);
        run.Log.Clear();
        run.Key(Key.Q, "q");
        run.Log.Should().Equal(preview
            ? run.Event("D", 81).Concat(new[] { "FP113" }).Concat(run.Event("U", 81))
            : run.Events(81, 113));
        list.ListIndex.Should().Be(2);
        list.Selection.SelectedIndexes.Should().Equal(2);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Modifiers_report_the_state_after_each_event(string kind, bool preview)
    {
        using var run = new Run(kind, preview);
        foreach (var (modifier, mask, key, text, ascii) in new[]
        {
            (Key.LeftShift, KeyModifiers.Shift, Key.K, "K", 75),
            (Key.LeftCtrl, KeyModifiers.Control, Key.A, (string?)null, 1),
            (Key.LeftAlt, KeyModifiers.Alt, Key.X, (string?)null, 0),
        })
        {
            run.Log.Clear();
            run.Down(modifier, mask);
            run.Key(key, text, mask);
            run.Up(modifier);
            var code = modifier == Key.LeftShift ? 16 : modifier == Key.LeftCtrl ? 17 : 18;
            var shift = modifier == Key.LeftShift ? 1 : modifier == Key.LeftCtrl ? 2 : 4;
            var expected = run.Event("D", code, shift)
                .Concat(run.Event("D", key == Key.K ? 75 : key == Key.A ? 65 : 88, shift));
            if (ascii != 0) expected = expected.Concat(run.Event("P", ascii));
            // The effect of Ctrl+A and Alt+X is not pinned; Shift+K types only in the editable combo.
            if (kind == "Combo0" && text is not null) expected = expected.Append("Change");
            if (kind == "ListBox" && text is not null) expected = expected.Append("Click");
            expected = expected.Concat(run.Event("U", key == Key.K ? 75 : key == Key.A ? 65 : 88, shift))
                .Concat(run.Event("U", code, 0));
            run.Log.Should().Equal(expected);
        }
    }

    [AvaloniaTheory]
    [InlineData("CommandButton", false, "Click")]
    [InlineData("CommandButton", true, "Click")]
    [InlineData("CheckBox", false, "Click")]
    [InlineData("CheckBox", true, "Click")]
    [InlineData("OptionButton", false, null)]
    [InlineData("OptionButton", true, null)]
    public void Space_clicks_only_after_KeyUp(string kind, bool preview, string? effect)
    {
        using var run = new Run(kind, preview);
        run.Key(Key.Space, " ");
        run.Log.Should().Equal(run.Events(32, 32).Concat(effect is null ? [] : new[] { effect }));
        if (kind == "CheckBox") ((VBCheckBox)run.Control!).Value.Should().Be(BuiltinTypes.VBCheckValue.Checked);
    }

    [AvaloniaTheory]
    [InlineData("CommandButton", false)]
    [InlineData("CommandButton", true)]
    [InlineData("CheckBox", false)]
    [InlineData("CheckBox", true)]
    [InlineData("OptionButton", false)]
    [InlineData("OptionButton", true)]
    public void KeyAscii_zero_filters_button_Space_before_its_effect(string kind, bool preview)
    {
        using var run = new Run(kind, preview, swallowSpace: true);
        run.Key(Key.Space, " ");
        run.Log.Should().Equal(preview
            ? new[] { "FD32/0", "D32/0", "FP32", "FU32/0", "U32/0" }
            : new[] { "D32/0", "P32", "U32/0" });
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Preview_and_control_can_filter_or_replace_the_character(string kind, bool preview)
    {
        using var run = new Run(kind, preview, filtering: true);
        run.Key(Key.X, "x");
        run.Log.Should().Equal(preview
            ? run.Event("D", 88).Take(1).Concat(run.Event("P", 120)).Concat(run.Event("U", 88))
            : run.Events(88, 120));
        run.Log.Clear();
        run.Key(Key.Q, "q");
        run.Log.Should().Equal(preview
            ? run.Event("D", 81).Concat(new[] { "FP113" }).Concat(run.Event("U", 81))
            : run.Events(81, 113));
        run.Log.Clear();
        run.Key(Key.E, "e");
        var expected = run.Event("D", 69).Concat(preview ? new[] { "FP101", "P69" } : new[] { "P101" });
        if (kind == "Combo0") expected = expected.Append("Change");
        run.Log.Should().Equal(expected.Concat(run.Event("U", 69)));
        if (kind == "Combo0") ((VBComboBox)run.Control!).Text.Should().Be(preview ? "E" : "e");
    }

    [AvaloniaTheory]
    [InlineData("ListBox", false)]
    [InlineData("ListBox", true)]
    [InlineData("Combo0", false)]
    [InlineData("Combo0", true)]
    [InlineData("Combo2", false)]
    [InlineData("Combo2", true)]
    public void Down_selects_between_the_key_events(string kind, bool preview)
    {
        using var run = new Run(kind, preview);
        if (kind == "Combo2") ((VBComboBox)run.Control!).SelectedIndex = 1;
        run.Log.Clear();
        run.Key(Key.Down);
        run.Log.Should().Equal(run.Events(40, null, "Click"));
        ((Avalonia.Controls.Primitives.SelectingItemsControl)run.Control!).SelectedIndex.Should().Be(kind == "Combo2" ? 2 : 0);
        if (kind == "Combo0") ((VBComboBox)run.Control!).Text.Should().Be("apple");
    }

    [AvaloniaTheory]
    [InlineData("Combo0", false)]
    [InlineData("Combo0", true)]
    [InlineData("Combo2", false)]
    [InlineData("Combo2", true)]
    public void Alt_Down_opens_and_open_list_Down_selects_until_the_last_item(string kind, bool preview)
    {
        using var run = new Run(kind, preview);
        ((VBComboBox)run.Control!).SelectedIndex = 0;
        run.Log.Clear();
        run.Down(Key.LeftAlt, KeyModifiers.Alt);
        run.Key(Key.Down, modifiers: KeyModifiers.Alt);
        run.Up(Key.LeftAlt);
        run.Log.Should().Equal(run.Event("D", 18, 4).Concat(run.Event("D", 40, 4)).Append("DropDown")
            .Concat(run.Event("U", 40, 4)).Concat(run.Event("U", 18)));
        var combo = (VBComboBox)run.Control!;
        combo.IsDropDownOpen.Should().BeTrue();
        for (var i = 1; i < 4; i++)
        {
            run.Log.Clear();
            run.Key(Key.Down);
            run.Log.Should().Equal(run.Events(40, null, i < 3 ? "Click" : null));
            combo.SelectedIndex.Should().Be(Math.Min(i, 2));
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void F4_opens_then_closes_without_another_DropDown(bool preview)
    {
        using var run = new Run("Combo0", preview);
        run.Key(Key.F4);
        run.Log.Should().Equal(run.Events(115, null, "DropDown"));
        ((VBComboBox)run.Control!).IsDropDownOpen.Should().BeTrue();
        run.Log.Clear();
        run.Key(Key.F4);
        run.Log.Should().Equal(run.Events(115, null));
        ((VBComboBox)run.Control!).IsDropDownOpen.Should().BeFalse();
    }

    // #4418: VB6 SP6 measured these keys without a holder, with KeyPreview off and on. Enter on a focused button is its Click.
    public static IEnumerable<object[]> DialogKeyCases =>
        from kind in new[] { "CheckBox", "OptionButton", "ListBox", "Combo0", "Combo2", "CommandButton" }
        from key in new[] { Key.Enter, Key.Escape }
        where !(kind == "CommandButton" && key == Key.Enter)
        from preview in new[] { false, true }
        select new object[] { kind, key, preview };

    [AvaloniaTheory]
    [MemberData(nameof(DialogKeyCases))]
    public void Dialog_keys_without_holders_raise_events(string kind, Key key, bool preview)
    {
        using var run = new Run(kind, preview);
        run.Key(key);
        var code = key == Key.Enter ? 13 : 27;
        run.Log.Should().Equal(run.Events(code, code));
    }

    // The one-holder Style 2 cases were measured in oracle PR #4486.
    public static IEnumerable<object[]> OpenComboCases =>
        from style in new[] { 0, 2 }
        from holders in (style == 0 ? new[] { 0, 3 } : new[] { 0, 1, 2, 3 })
        from key in new[] { Key.Enter, Key.Escape }
        select new object[] { style, holders, key };

    [AvaloniaTheory]
    [MemberData(nameof(OpenComboCases))]
    public async System.Threading.Tasks.Task Open_combo_closes_and_delivers_dialog_keys(int style, int holders, Key key)
    {
        var blocks = UserControlKeyTests.Block("VB.ComboBox", "Text1", 0, extra: $"Style = {style}")
            + UserControlKeyTests.Block("VB.CommandButton", "Command1", 1, extra: (holders & 1) != 0 ? "Default = -1" : "")
            + UserControlKeyTests.Block("VB.CommandButton", "Command2", 2, extra: (holders & 2) != 0 ? "Cancel = -1" : "");
        var code = "Private Sub Text1_KeyDown(KeyCode As Integer, Shift As Integer)\nDebug.Print \"D\" & KeyCode\nEnd Sub\n"
            + "Private Sub Text1_KeyPress(KeyAscii As Integer)\nDebug.Print \"P\" & KeyAscii\nEnd Sub\n"
            + "Private Sub Text1_KeyUp(KeyCode As Integer, Shift As Integer)\nDebug.Print \"U\" & KeyCode\nEnd Sub\n"
            + "Private Sub Command1_Click()\nDebug.Print \"Click\"\nEnd Sub\n"
            + "Private Sub Command2_Click()\nDebug.Print \"Click\"\nEnd Sub\n";
        using var run = new UserControlKeyTests.Run("", formCode: code, blocks: blocks);
        await run.Code("Text1.AddItem \"apple\"");
        var combo = (VBComboBox)run.Named("Text1");
        if (style == 0) combo.GetVisualDescendants().OfType<TextBox>().First().Focus();
        else combo.Focus().Should().BeTrue();
        combo.IsDropDownOpen = true;
        run.Key(key);
        var value = key == Key.Enter ? 13 : 27;
        var expected = new List<string> { $"D{value}", $"P{value}" };
        if ((holders & (key == Key.Enter ? 1 : 2)) == 0) expected.Add($"U{value}");
        run.Lib.Printed.Select(v => v.Value).Should().Equal(expected);
        combo.IsDropDownOpen.Should().BeFalse();
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Non_ASCII_KeyAscii_replacement_refuses_instead_of_guessing(string kind, bool preview)
    {
        using var run = new Run(kind, preview, unsupported: true);
        foreach (var key in new[] { Key.K, Key.Space, Key.A })
        {
            run.Faults.Clear();
            var modifiers = key == Key.A ? KeyModifiers.Control : KeyModifiers.None;
            run.Down(key, modifiers);
            if (key == Key.K) run.Window.KeyTextInput("k");
            if (key == Key.Space) run.Window.KeyTextInput(" ");
            run.Up(key, modifiers);
            Dispatcher.UIThread.RunJobs();
            run.Faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
                .Which.Message.Should().Be("Non-ASCII KeyAscii conversion is not implemented (#2325)");
        }
    }

    public static IEnumerable<object[]> SynthesizedReplacements =>
        from row in Controls
        from key in (string)row[0] is "CommandButton" or "CheckBox" or "OptionButton" ? new[] { Key.A, Key.Space } : new[] { Key.A }
        select new[] { row[0], row[1], key };

    [AvaloniaTheory]
    [MemberData(nameof(SynthesizedReplacements))]
    public void Unmeasured_synthesized_KeyAscii_replacement_effect_is_refused(string kind, bool preview, Key key)
    {
        using var run = new Run(kind, preview, substituteControlCharacter: true);
        var modifiers = key == Key.A ? KeyModifiers.Control : KeyModifiers.None;
        run.Down(key, modifiers);
        if (key == Key.Space) run.Window.KeyTextInput(" ");
        run.Up(key, modifiers);
        Dispatcher.UIThread.RunJobs();
        run.Faults.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Be("Changing KeyAscii for a synthesized Space or Ctrl+A is not implemented (#2325)");
        run.Log.Should().NotContain("Click");
    }

    [AvaloniaTheory]
    [InlineData("Combo0")]
    [InlineData("Combo1")]
    public void Unmeasured_combo_input_keeps_raising_no_key_events(string kind)
    {
        using var run = new Run(kind, true);
        run.Key(Key.K, "k");
        run.Log.Clear();
        run.Key(Key.Down);
        run.Log.Where(value => value.Any(char.IsDigit)).Should().BeEmpty();
        if (kind == "Combo1")
        {
            run.Log.Clear();
            run.Key(Key.B, "b");
            run.Log.Where(value => value.Any(char.IsDigit)).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("CommandButton")]
    [InlineData("CheckBox")]
    [InlineData("OptionButton")]
    [InlineData("ListBox")]
    [InlineData("ComboBox")]
    public void Component_lists_offer_all_three_keyboard_handlers(string kind)
    {
        ComponentBaseClass component = kind switch
        {
            "CommandButton" => CommandButtonComponentClass.Instance,
            "CheckBox" => CheckBoxComponentClass.Instance,
            "OptionButton" => OptionButtonComponentClass.Instance,
            "ListBox" => ListBoxComponentClass.Instance,
            _ => ComboBoxComponentClass.Instance,
        };
        // FormEditView opens the first event on a designer double-click: preserve main's default.
        component.Events.First().Name.Should().Be(kind == "ComboBox" ? "Change" : "Click");
        component.Events.Select(e => e.Name).Should().Contain(new[] { "KeyDown", "KeyPress", "KeyUp" });
    }

    [AvaloniaTheory]
    [MemberData(nameof(Controls))]
    public void Tab_moves_focus_without_key_events_even_with_preview(string kind, bool preview)
    {
        using var run = new Run(kind, preview, optionPair: false);
        run.Key(Key.Tab);
        run.Log.Should().Equal("NextFocus");
    }

    [AvaloniaTheory]
    [InlineData("CommandButton", false)]
    [InlineData("CommandButton", true)]
    [InlineData("CheckBox", false)]
    [InlineData("CheckBox", true)]
    [InlineData("OptionButton", false)]
    [InlineData("OptionButton", true)]
    public void Arrows_move_focus_without_key_events_on_buttons(string kind, bool preview)
    {
        using var run = new Run(kind, preview);
        run.Key(Key.Down);
        run.Log.Should().Equal(kind == "CheckBox" ? (preview ? new[] { "NextFocus", "FU40/0", "NU40/0" } : new[] { "NextFocus", "NU40/0" })
            : kind == "OptionButton" ? new[] { "NextFocus", "NextClick" } : new[] { "NextFocus" });
        if (kind == "OptionButton")
        {
            run.Log.Clear();
            run.Key(Key.Up);
            run.Log.Should().Equal("Focus", "Click");
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.K, "k", 75, 107)]
    [InlineData(Key.Enter, null, 13, 13)]
    [InlineData(Key.Escape, null, 27, 27)]
    [InlineData(Key.Space, " ", 32, 32)]
    [InlineData(Key.Left, null, 37, 0)]
    [InlineData(Key.F2, null, 113, 0)]
    [InlineData(Key.Tab, null, 9, 9)]
    public void Empty_form_receives_its_own_keys(Key key, string? text, int code, int ascii)
    {
        using var run = new Run("Empty", false);
        run.Key(key, text);
        var expected = new List<string> { $"FD{code}/0" };
        if (ascii != 0) expected.Add($"FP{ascii}");
        if (key != Key.Tab) expected.Add($"FU{code}/0");
        run.Log.Should().Equal(expected);
    }

    // These chords were not measured on the empty form; preserve main's lack of key events.
    [AvaloniaTheory]
    [InlineData(Key.Enter, KeyModifiers.Control)]
    [InlineData(Key.Enter, KeyModifiers.Shift)]
    [InlineData(Key.Escape, KeyModifiers.Control)]
    [InlineData(Key.Escape, KeyModifiers.Shift)]
    [InlineData(Key.Tab, KeyModifiers.Control)]
    [InlineData(Key.Tab, KeyModifiers.Shift)]
    public void Empty_form_does_not_infer_modified_special_keys(Key key, KeyModifiers modifiers)
    {
        using var run = new Run("Empty", false);
        run.Key(key, modifiers: modifiers);
        run.Log.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Empty_form_receives_modifier_chords()
    {
        using var run = new Run("Empty", false);
        run.Down(Key.LeftCtrl, KeyModifiers.Control);
        run.Key(Key.A, modifiers: KeyModifiers.Control);
        run.Up(Key.LeftCtrl);
        run.Down(Key.LeftAlt, KeyModifiers.Alt);
        run.Key(Key.X, modifiers: KeyModifiers.Alt);
        run.Up(Key.LeftAlt);
        run.Log.Should().Equal("FD17/2", "FD65/2", "FP1", "FU65/2", "FU17/0",
            "FD18/4", "FD88/4", "FU88/4", "FU18/0");
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new Exception(message);
    }

    private sealed class Run : IDisposable
    {
        public readonly List<string> Log = [];
        public readonly List<Exception> Faults = [];
        public readonly VBFormRuntime Window;
        public readonly Control? Control;
        private readonly bool preview;

        public Run(string kind, bool preview, bool filtering = false, bool optionPair = true, bool unsupported = false, bool swallowSpace = false, bool substituteControlCharacter = false)
        {
            this.preview = preview;
            var type = kind.StartsWith("Combo", StringComparison.Ordinal) ? "ComboBox" : kind == "ListBoxMulti" ? "ListBox" : kind;
            var extra = kind == "Combo1" ? "Style = 1" : kind == "Combo2" ? "Style = 2" : kind == "OptionButton" ? "Value = -1"
                : kind == "ListBoxMulti" ? "MultiSelect = 2" : "";
            var nextType = kind == "CheckBox" ? "ComboBox" : kind == "OptionButton" && optionPair ? "OptionButton" : "CommandButton";
            var controls = kind == "Empty" ? "" : $"""
                Begin VB.{type} Target
                    Height = 600
                    Width = 1800
                    TabIndex = 0
                    {extra}
                End
                Begin VB.{nextType} NextControl
                    Top = 700
                    Height = 400
                    Width = 1800
                    TabIndex = 1
                End
                """;
            var code = $"""
                VERSION 5.00
                Begin VB.Form Form1
                    ClientHeight = 1800
                    ClientWidth = 3000
                    KeyPreview = {(preview ? -1 : 0)}
                    {controls}
                End
                Attribute VB_Name = "Form1"
                Private Sub Form_KeyDown(KeyCode As Integer, Shift As Integer)
                    Debug.Print "FD" & KeyCode & "/" & Shift
                    {(filtering ? "If KeyCode = 88 Then KeyCode = 0" : "")}
                End Sub
                Private Sub Form_KeyPress(KeyAscii As Integer)
                    Debug.Print "FP" & KeyAscii
                    {(substituteControlCharacter && preview ? "If KeyAscii = 1 Or KeyAscii = 32 Then KeyAscii = 98" : "")}
                    {(swallowSpace && preview ? "If KeyAscii = 32 Then KeyAscii = 0" : "")}
                    {(filtering ? "If KeyAscii = 113 Then KeyAscii = 0\r\nIf KeyAscii = 101 Then KeyAscii = 69" : "")}
                End Sub
                Private Sub Form_KeyUp(KeyCode As Integer, Shift As Integer)
                    Debug.Print "FU" & KeyCode & "/" & Shift
                End Sub
                Private Sub Target_KeyDown(KeyCode As Integer, Shift As Integer)
                    Debug.Print "D" & KeyCode & "/" & Shift
                    {(filtering ? "KeyCode = 0" : "")}
                End Sub
                Private Sub Target_KeyPress(KeyAscii As Integer)
                    Debug.Print "P" & KeyAscii
                    {(swallowSpace && !preview ? "If KeyAscii = 32 Then KeyAscii = 0" : "")}
                    {(unsupported ? "KeyAscii = 128" : substituteControlCharacter && !preview ? "KeyAscii = 98" : filtering ? "If KeyAscii = 113 Or KeyAscii = 120 Then KeyAscii = 0" : "")}
                End Sub
                Private Sub Target_KeyUp(KeyCode As Integer, Shift As Integer)
                    Debug.Print "U" & KeyCode & "/" & Shift
                End Sub
                Private Sub Target_Click()
                    Debug.Print "Click"
                End Sub
                Private Sub Target_Change()
                    Debug.Print "Change"
                End Sub
                Private Sub Target_DropDown()
                    Debug.Print "DropDown"
                End Sub
                Private Sub Target_GotFocus()
                    Debug.Print "Focus"
                End Sub
                Private Sub NextControl_GotFocus()
                    Debug.Print "NextFocus"
                End Sub
                Private Sub NextControl_Click()
                    Debug.Print "NextClick"
                End Sub
                Private Sub NextControl_KeyUp(KeyCode As Integer, Shift As Integer)
                    Debug.Print "NU" & KeyCode & "/" & Shift
                End Sub
                """;
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            var form = new FormDeserializer().Deserialize(project, code.ReplaceLineEndings("\r\n"), new Sink())!;
            project.AddForm(form);
            VBDebugConsole.Output += Log.Add;
            VBWindowContext.Faulted += Faulted;
            VBLoader.RunForm(form, CancellationToken.None, out var window, null);
            Window = window!;
            Dispatcher.UIThread.RunJobs();
            Control = Window.GetVisualDescendants().OfType<Control>().SingleOrDefault(c => VBProps.GetName(c) == "Target");
            if (Control is Avalonia.Controls.Primitives.SelectingItemsControl list)
                foreach (var item in kind == "ListBox" ? new[] { "apple", "banana", "kiwi", "pear" }
                    : kind == "ListBoxMulti" ? new[] { "apple", "banana", "kiwi", "pear", "quince" } : new[] { "apple", "banana", "cherry" })
                    list.Items.Add(item);
            Control?.Focus();
            Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Log.Clear();
        }

        private void Faulted(VBWindowContext _, Exception error) => Faults.Add(error);
        public IEnumerable<string> Event(string name, int code, int shift = 0)
        {
            var value = name == "P" ? $"{code}" : $"{code}/{shift}";
            if (preview) yield return $"F{name}{value}";
            yield return $"{name}{value}";
        }
        public IEnumerable<string> Events(int code, int? ascii, string? effect = null) =>
            Event("D", code).Concat(ascii is { } a ? Event("P", a) : [])
                .Concat(effect is null ? [] : new[] { effect }).Concat(Event("U", code));
        public void Down(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            Window.KeyPress(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
        public void Up(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
            Window.KeyRelease(key, (RawInputModifiers)modifiers, PhysicalKey.None, null);
        public void Key(Key key, string? text = null, KeyModifiers modifiers = KeyModifiers.None)
        {
            Down(key, modifiers);
            if (text is not null) Window.KeyTextInput(text);
            Up(key, modifiers);
            Dispatcher.UIThread.RunJobs();
            Faults.Should().BeEmpty();
        }
        public void Dispose()
        {
            Window.Close();
            VBDebugConsole.Output -= Log.Add;
            VBWindowContext.Faulted -= Faulted;
        }
    }
}
