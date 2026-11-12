using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Integration.Tests.Views;

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

    // Keyboard sweep: #2325 issuecomment-6454480155, native and p-code, 372 steps.
    public static IEnumerable<object[]> KeyboardSweep()
    {
        (Key Key, int Code)[] keys =
        [
            (Key.B, 66), (Key.C, 67), (Key.D, 68), (Key.E, 69), (Key.F, 70), (Key.G, 71),
            (Key.H, 72), (Key.I, 73), (Key.J, 74), (Key.L, 76), (Key.M, 77), (Key.N, 78),
            (Key.O, 79), (Key.P, 80), (Key.Q, 81), (Key.R, 82), (Key.S, 83), (Key.T, 84),
            (Key.U, 85), (Key.V, 86), (Key.W, 87), (Key.Y, 89), (Key.Z, 90),
            (Key.D0, 48), (Key.D1, 49), (Key.D2, 50), (Key.D3, 51), (Key.D4, 52),
            (Key.D5, 53), (Key.D6, 54), (Key.D7, 55), (Key.D8, 56), (Key.D9, 57),
            (Key.NumPad0, 96), (Key.NumPad1, 97), (Key.NumPad2, 98), (Key.NumPad3, 99),
            (Key.NumPad4, 100), (Key.NumPad5, 101), (Key.NumPad6, 102), (Key.NumPad7, 103),
            (Key.NumPad8, 104), (Key.NumPad9, 105),
            (Key.F1, 112), (Key.F3, 114), (Key.F4, 115), (Key.F5, 116), (Key.F6, 117),
            (Key.F7, 118), (Key.F8, 119), (Key.F9, 120), (Key.F11, 122), (Key.F12, 123),
            (Key.Space, 32), (Key.Insert, 45),
            (Key.Home, 36), (Key.End, 35), (Key.PageUp, 33), (Key.PageDown, 34),
            (Key.Up, 38), (Key.Right, 39), (Key.Down, 40),
            (Key.Clear, 12), (Key.Decimal, 110), (Key.Add, 107), (Key.Subtract, 109),
            (Key.Multiply, 106), (Key.Divide, 111), (Key.NumLock, 144),
        ];
        foreach (var (key, code) in keys)
            foreach (var modifiers in new[] { KeyModifiers.None, KeyModifiers.Shift, KeyModifiers.Control, KeyModifiers.Alt })
                if (!(key == Key.F4 && modifiers == KeyModifiers.Alt)
                    && !(key == Key.Space && modifiers == KeyModifiers.Alt)
                    && !(key >= Key.NumPad0 && key <= Key.NumPad9 && modifiers != KeyModifiers.None)
                    && !(key is Key.NumLock or Key.Decimal && modifiers != KeyModifiers.None)
                    && !(key is Key.Add or Key.Subtract or Key.Multiply or Key.Divide
                        && modifiers is KeyModifiers.Control or KeyModifiers.Alt))
                    yield return [key, modifiers, code];
    }

    [AvaloniaTheory]
    [MemberData(nameof(KeyboardSweep))]
    public void Keyboard_sweep_delivers_measured_codes(Key key, KeyModifiers modifiers, int code)
    {
        var box = new VBTextBox();
        VBProps.SetName(box, "Text1");
        using var root = new Root { Content = box };
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
        var shift = (modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
            | (modifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
            | (modifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        root.Calls.Should().Equal(("Text1_KeyDown", code, shift), ("Text1_KeyUp", code, shift));
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
