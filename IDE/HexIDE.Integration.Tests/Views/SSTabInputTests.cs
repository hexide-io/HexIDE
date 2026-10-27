using System.Linq;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

public class SSTabInputTests : WindowTestFixture
{
    private sealed class InputRoot(VBSSTab tab) : Decorator, IModuleExecutionRoot
    {
        public readonly List<(string Name, IReadOnlyList<Vb6Value>? Args, int Tab)> Calls = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Calls.Add((name, args, tab.Tab));
    }

    private static (VBSSTab Tab, Window Window, InputRoot Root) Open()
    {
        var tab = new VBSSTab(new Canvas());
        VBProps.SetName(tab, "SSTab1");
        var root = new InputRoot(tab) { Child = tab };
        var window = new Window { Content = root, Width = 360, Height = 240 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        root.Calls.Clear();
        return (tab, window, root);
    }

    private static RawInputModifiers Command =>
        Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers.HasFlag(KeyModifiers.Meta)
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Key(Window window, Avalonia.Input.Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CaptionPressSwitchesBetweenMouseDownAndMouseUpAndTakesFocus(int orientation)
    {
        var (tab, window, root) = Open();
        tab.TabOrientation = orientation;
        Dispatcher.UIThread.RunJobs();
        var header = tab.GetVisualDescendants().OfType<TextBlock>().ElementAt(1);
        var point = ((Control)header.Parent!).TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        tab.Tab.Should().Be(1);
        tab.IsFocused.Should().BeTrue();
        window.MouseUp(point, MouseButton.Left);
        root.Calls.Where(c => c.Name is "SSTab1_MouseDown" or "SSTab1_Click" or "SSTab1_MouseUp")
            .Select(c => c.Name).Should().Equal("SSTab1_MouseDown", "SSTab1_Click", "SSTab1_MouseUp");
        root.Calls.Single(c => c.Name == "SSTab1_Click").Args![0].Value.Should().Be((short)0);
        root.Calls.Single(c => c.Name == "SSTab1_MouseDown").Tab.Should().Be(0);
        root.Calls.Single(c => c.Name == "SSTab1_MouseUp").Tab.Should().Be(1);
    }

    [AvaloniaFact]
    public void DisabledCaptionAndPageDoNotClickButDisabledCaptionTakesFocus()
    {
        var (tab, window, root) = Open();
        tab.TabEnabled(1, false);
        Dispatcher.UIThread.RunJobs();
        var caption = tab.GetVisualDescendants().OfType<TextBlock>().ElementAt(1);
        var point = caption.TranslatePoint(new Point(5, 5), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        tab.IsFocused.Should().BeTrue();
        window.MouseDown(new Point(20, 100), MouseButton.Left);
        window.MouseUp(new Point(20, 100), MouseButton.Left);
        tab.Tab.Should().Be(0);
        root.Calls.Should().NotContain(c => c.Name == "SSTab1_Click");
        root.Calls.Count(c => c.Name == "SSTab1_MouseDown").Should().Be(2);
    }

    [AvaloniaFact]
    public void DoubleClickRaisesDblClickWithoutASecondMouseDownOrClick()
    {
        var (tab, window, root) = Open();
        var pointer = new Pointer(1, PointerType.Mouse, true);
        var point = new Point(130, 5);
        void Press(int count) => tab.RaiseEvent(new PointerPressedEventArgs(tab, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None, count));
        void Release() => tab.RaiseEvent(new PointerReleasedEventArgs(tab, pointer, window, point, 0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
        Press(1);
        Release();
        Press(2);
        Release();
        root.Calls.Where(c => c.Name != "SSTab1_GotFocus").Select(c => c.Name).Should().Equal(
            "SSTab1_MouseDown", "SSTab1_Click", "SSTab1_MouseUp", "SSTab1_DblClick", "SSTab1_MouseUp");
        var mouse = root.Calls.First(c => c.Name == "SSTab1_MouseDown").Args!;
        mouse[2].Value.Should().Be(1950f);
        mouse[3].Value.Should().Be(75f);
    }

    [AvaloniaFact]
    public void RightClickAndShowingCaptionDoNotSwitchOrClick()
    {
        var (tab, window, root) = Open();
        window.MouseDown(new Point(130, 5), MouseButton.Right);
        window.MouseUp(new Point(130, 5), MouseButton.Right);
        window.MouseDown(new Point(5, 5), MouseButton.Left);
        window.MouseUp(new Point(5, 5), MouseButton.Left);
        root.Calls.Should().NotContain(c => c.Name == "SSTab1_Click");
        root.Calls.Count(c => c.Name == "SSTab1_MouseUp").Should().Be(2);
        tab.Tab.Should().Be(0);
    }

    [AvaloniaFact]
    public void ArrowsAndControlTabWrapAndStopAtDisabledTabs()
    {
        var (tab, window, _) = Open();
        tab.Focus();
        Key(window, Avalonia.Input.Key.Left);
        tab.Tab.Should().Be(2);
        Key(window, Avalonia.Input.Key.Right);
        tab.Tab.Should().Be(0);
        Key(window, Avalonia.Input.Key.Tab, Command);
        tab.Tab.Should().Be(1);
        Key(window, Avalonia.Input.Key.Tab, Command | RawInputModifiers.Shift);
        tab.Tab.Should().Be(0);
        tab.TabEnabled(1, false);
        Key(window, Avalonia.Input.Key.Right);
        tab.Tab.Should().Be(0);
        tab.TabEnabled(1, true);
        tab.Tab = 1;
        tab.TabEnabled(0, false);
        Key(window, Avalonia.Input.Key.Left);
        tab.Tab.Should().Be(1);
        tab.TabEnabled(0, true);
        tab.Tab = 2;
        Key(window, Avalonia.Input.Key.Tab, Command);
        tab.Tab.Should().Be(0);
        Key(window, Avalonia.Input.Key.Home);
        Key(window, Avalonia.Input.Key.End);
        Key(window, Avalonia.Input.Key.Up);
        Key(window, Avalonia.Input.Key.Down);
        Key(window, Avalonia.Input.Key.T);
        tab.Tab.Should().Be(0);
    }

    [AvaloniaFact]
    public void ChildControlTabDoesNothingAndAltMnemonicSelectsAndFocusesSSTab()
    {
        var (tab, window, root) = Open();
        var text = new TextBox { Width = 80, Height = 20 };
        VBProps.SetName(text, "Text1");
        Canvas.SetTop(text, 50);
        tab.ChildHost.Children.Add(text);
        tab.Caption(2, "&Third");
        text.Focus();
        Key(window, Avalonia.Input.Key.Tab, Command);
        Key(window, Avalonia.Input.Key.Tab, Command | RawInputModifiers.Shift);
        Key(window, Avalonia.Input.Key.PageDown, Command);
        tab.Tab.Should().Be(0);
        text.IsFocused.Should().BeTrue();
        root.Calls.Clear();
        Key(window, Avalonia.Input.Key.T, RawInputModifiers.Alt);
        tab.Tab.Should().Be(2);
        tab.IsFocused.Should().BeTrue();
        root.Calls.Select(c => c.Name).Should().Equal("SSTab1_Click", "SSTab1_GotFocus");
    }
}
