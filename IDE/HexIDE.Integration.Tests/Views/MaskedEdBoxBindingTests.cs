using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class MaskedEdBoxBindingTests
{
    private static TextBox Create()
    {
        FormDeserializer.TryGetComponentClass("MSMask.MaskEdBox", out var component).Should().BeTrue();
        return (TextBox)component!.Instantiate(new ComponentInstance(component, "Mask1"));
    }

    [AvaloniaTheory]
    [InlineData(Key.A, KeyModifiers.Alt)]
    [InlineData(Key.Left, KeyModifiers.Shift)]
    public void Unmeasured_modified_input_refuses(Key key, KeyModifiers modifiers)
    {
        var box = Create();
        Action input = () => box.RaiseEvent(new KeyEventArgs
            { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        input.Should().Throw<NotImplementedException>().WithMessage("*#1098*");
    }

    [AvaloniaFact]
    public void Unmeasured_navigation_with_a_selection_refuses()
    {
        var box = Create();
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("SelLength", new(3));
        Action input = () => box.RaiseEvent(new KeyEventArgs
            { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
        input.Should().Throw<NotImplementedException>().WithMessage("*#1098*");
    }

    private sealed class RewritingRoot : Decorator, IModuleExecutionRoot
    {
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) { }
        public void ExecuteKeyboardEvent(string name, IReadOnlyList<Vb6Value> args, int writable, Action<int> completed)
            => completed(0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unmeasured_key_argument_modifications_refuse(bool textInput)
    {
        var box = Create();
        var root = new RewritingRoot { Child = box };
        Action input = textInput
            ? () => box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a" })
            : () => box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        input.Should().Throw<NotImplementedException>().WithMessage("*#1098*");
        GC.KeepAlive(root);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Non_ASCII_and_unfinished_AutoTab_input_refuse(bool autoTab)
    {
        var box = Create();
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("AutoTab", new(autoTab));
        Action input = () => box.RaiseEvent(new TextInputEventArgs
            { RoutedEvent = InputElement.TextInputEvent, Text = autoTab ? "a" : "é" });
        input.Should().Throw<NotImplementedException>().WithMessage("*#1098*");
    }

    [AvaloniaFact]
    public void Refused_Text_assignment_still_dispatches_Change_and_keeps_the_display()
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("Text", new("12-34"));
        root.Calls.Clear();
        Action assign = () => bag.TrySetProperty("Text", new("12-3"));
        assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
        root.Calls.Select(c => c.Name).Should().Equal("Mask1_Change");
        box.Text.Should().Be("12-34");
    }

    [AvaloniaFact]
    public void PromptInclude_changes_the_reading_while_selection_and_prompts_stay_visible()
    {
        var box = Create();
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("PromptInclude", new(false));
        bag.TrySetProperty("Text", new("13"));
        bag.TrySetProperty("SelStart", new(1));
        bag.TrySetProperty("SelLength", new(3));
        bag.TryGetProperty("Text", out var text).Should().BeTrue();
        text.Value.Should().Be("13");
        box.Text.Should().Be("13-__");
        box.SelectionStart.Should().Be(1);
        box.SelectionEnd.Should().Be(4);
    }
    [AvaloniaFact]
    public void Case_directives_transform_input_and_refuse_keys_at_the_end()
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new(">?<?"));
        root.Calls.Clear();
        foreach (var character in "aBcD")
            box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = character.ToString() });
        box.Text.Should().Be("Ab");
        box.CaretIndex.Should().Be(2);
        root.Calls.Select(c => c.Name).Should().Equal("Mask1_KeyPress", "Mask1_Change",
            "Mask1_KeyPress", "Mask1_Change", "Mask1_KeyPress", "Mask1_ValidationError",
            "Mask1_KeyPress", "Mask1_ValidationError");
        foreach (var call in root.Calls.Where(c => c.Name == "Mask1_ValidationError"))
            call.Args!.Select(v => v.Value).Should().Equal("Ab", 2L);
    }

    [AvaloniaTheory]
    [InlineData("12___", 0, 0, "9", "912__", 1)]
    [InlineData("1_3__", 0, 0, "9", "913__", 1)]
    [InlineData("12345", 0, 0, "9", "12345", 0)]
    [InlineData("12345", 1, 3, "9", "195__", 2)]
    public void TextInput_uses_the_model_and_synchronizes_selection(string before, int start, int length,
        string input, string expected, int caret)
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("#####"));
        bag.TrySetProperty("Text", new(before));
        bag.TrySetProperty("SelStart", new(start));
        bag.TrySetProperty("SelLength", new(length));
        root.Calls.Clear();
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = input });
        root.Calls.Select(c => c.Name).Should().Equal("Mask1_KeyPress",
            before == expected ? "Mask1_ValidationError" : "Mask1_Change");
        box.Text.Should().Be(expected);
        box.CaretIndex.Should().Be(caret);
        bag.TryGetProperty("SelStart", out var reading);
        reading.Value.Should().Be((long)caret);
    }

    [AvaloniaTheory]
    [InlineData(Key.Delete, "23-4_", 0, "Change,KeyDown,KeyUp")]
    [InlineData(Key.Back, "12-3_", 5, "KeyDown,KeyPress,Change,KeyUp")]
    public void Removal_events_follow_the_measured_order(Key key, string expected, int caret, string events)
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("Text", new("12-34"));
        bag.TrySetProperty("SelStart", new(caret));
        root.Calls.Clear();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        box.Text.Should().Be(expected);
        root.Calls.Select(c => c.Name).Should().Equal(events.Split(',').Select(name => "Mask1_" + name));
    }

    [AvaloniaFact]
    public void Refused_character_raises_KeyPress_then_ValidationError_with_arguments()
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        root.Calls.Clear();
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A });
        box.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "a" });
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.A });
        box.Text.Should().Be("__-__");
        root.Calls.Select(c => c.Name).Should().Equal("Mask1_KeyDown", "Mask1_KeyPress", "Mask1_ValidationError", "Mask1_KeyUp");
        root.Calls[2].Args!.Select(v => v.Value).Should().Equal("a_-__", 0L);
    }

    // #2489, the MaskedEdBox oracle's SelText tables (including the property probe).
    [AvaloniaTheory]
    [InlineData(0, 0, "9", "91-23")]
    [InlineData(3, 0, "99", "12-99")]
    [InlineData(1, 3, "a", "14-__")]
    [InlineData(0, 5, "9876", "98-76")]
    [InlineData(0, 5, "98-76", "98-76")]
    [InlineData(0, 5, "", "__-__")]
    [InlineData(1, 3, "123456", "11-23")]
    [InlineData(1, 3, "99", "19-94")]
    public void SelText_writes_insert_drop_refused_characters_and_cut_overflow(int start, int length,
        string input, string expected)
    {
        var box = Create();
        var root = new ContainerRuntimeHarness.RecordingRoot { Child = box };
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("Text", new("12-34"));
        bag.TrySetProperty("SelStart", new(start));
        bag.TrySetProperty("SelLength", new(length));
        root.Calls.Clear();
        bag.TrySetProperty("SelText", new(input)).Should().BeTrue();
        box.Text.Should().Be(expected);
        bag.TryGetProperty("Text", out var reading).Should().BeTrue();
        reading.Value.Should().Be(expected);
        bag.TryGetProperty("SelStart", out var caret).Should().BeTrue();
        caret.Value.Should().Be(0L);
        box.CaretIndex.Should().Be(0);
        root.Calls.Should().NotContain(c => c.Name == "Mask1_ValidationError");
    }

    [AvaloniaFact]
    public void Navigation_keeps_literal_positions_and_uses_the_mask_end()
    {
        var box = Create();
        var bag = (ICSharpPropertyBag)box;
        bag.TrySetProperty("Mask", new("##-##"));
        bag.TrySetProperty("Text", new("1_-__"));
        foreach (var key in new[] { Key.Right, Key.Right })
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        box.CaretIndex.Should().Be(2);
        bag.TrySetProperty("SelStart", new(4));
        foreach (var key in new[] { Key.Left, Key.Left })
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
        box.CaretIndex.Should().Be(2);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.End });
        box.CaretIndex.Should().Be(5);
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Home });
        box.CaretIndex.Should().Be(0);
    }
}
