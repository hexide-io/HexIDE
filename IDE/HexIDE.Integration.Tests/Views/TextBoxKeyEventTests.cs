using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components;

namespace HexIDE.Integration.Tests.Views;

public class TextBoxKeyEventTests
{
    private sealed class Root : Window, IModuleExecutionRoot, System.IDisposable
    {
        public void Dispose() => Close();
        public List<Vb6Value> Arguments { get; } = [];
        public List<(string Name, int Code, int Shift)> Calls { get; } = [];
        public bool Handles(string name) => !name.EndsWith("_KeyPress", System.StringComparison.Ordinal);
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            args.Should().NotBeNull();
            Arguments.AddRange(args!);
            Calls.Add((name, (int)args![0].Value!, (int)args[1].Value!));
        }
    }

    // #2325: real keystrokes on VB6 SP6, native and p-code alike.
    [AvaloniaTheory]
    [InlineData(Key.K, KeyModifiers.None, 75, 0)]
    [InlineData(Key.K, KeyModifiers.Shift, 75, 1)]
    [InlineData(Key.A, KeyModifiers.Control, 65, 2)]
    [InlineData(Key.X, KeyModifiers.Alt, 88, 4)]
    [InlineData(Key.Back, KeyModifiers.None, 8, 0)]
    [InlineData(Key.Delete, KeyModifiers.None, 46, 0)]
    [InlineData(Key.Left, KeyModifiers.None, 37, 0)]
    [InlineData(Key.F2, KeyModifiers.None, 113, 0)]
    [InlineData(Key.LeftShift, KeyModifiers.Shift, 16, 1)]
    [InlineData(Key.LeftCtrl, KeyModifiers.Control, 17, 2)]
    [InlineData(Key.LeftAlt, KeyModifiers.Alt, 18, 4)]
    public void Measured_keys_deliver_down_and_up_even_when_Avalonia_handles_them(Key key, KeyModifiers modifiers, int code, int shift)
    {
        var box = new VBTextBox();
        VBProps.SetName(box, "Text1");
        using var root = new Root { Content = box };
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        var upModifiers = key is Key.LeftShift or Key.LeftCtrl or Key.LeftAlt ? KeyModifiers.None : modifiers;
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = upModifiers });
        root.Calls.Should().Equal(("Text1_KeyDown", code, shift),
            ("Text1_KeyUp", code, upModifiers == KeyModifiers.None ? 0 : shift));
    }
    [AvaloniaFact]
    public void Array_event_keeps_Index_before_KeyCode_and_Shift()
    {
        var box = new VBTextBox();
        VBProps.SetName(box, "Text1");
        VBProps.SetIndex(box, 3);
        using var root = new Root { Content = box };
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F2 });
        root.Arguments.ConvertAll(value => (int)value.Value!).Should().Equal(3, 113, 0);
    }

    [AvaloniaFact]
    public void TextBox_lists_the_events_it_now_raises()
    {
        TextBoxComponentClass.Instance.Events.Should().Contain(ComponentBaseClass.KeyDownEvent);
        TextBoxComponentClass.Instance.Events.Should().Contain(ComponentBaseClass.KeyUpEvent);
        TextBoxComponentClass.Instance.Events.Should().Contain(ComponentBaseClass.KeyPressEvent);
    }
}
