using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class AnimationInputTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComCtl2.Animation Animation1
              Left = 120
              Top = 120
              Width = 2400
              Height = 1200
              _Version = 393216
           End
           Begin VB.TextBox Text1
              Left = 120
              Top = 1800
              Width = 1200
              Height = 300
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public void Click_raises_MouseDown_then_focus_then_MouseUp_and_Click_in_client_twips()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var animation = ContainerRuntimeHarness.Child(canvas, "Animation1");
        var text = ContainerRuntimeHarness.Child(canvas, "Text1");
        text.Focus();
        recorder.Calls.Clear();
        var window = (Window)TopLevel.GetTopLevel(animation)!;
        var point = animation.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        recorder.Calls.Select(c => c.Name).Should().Equal("Animation1_MouseDown", "Text1_LostFocus",
            "Animation1_GotFocus", "Animation1_MouseUp", "Animation1_Click");
        animation.IsFocused.Should().BeTrue();
        recorder.Calls[0].Args!.Select(v => v.Value).Should().Equal(1, 0, 300f, 300f);
        recorder.Calls[3].Args!.Select(v => v.Value).Should().Equal(1, 0, 300f, 300f);
    }

    [AvaloniaFact]
    public void Double_click_adds_DblClick_without_second_mouse_events()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var animation = ContainerRuntimeHarness.Child(canvas, "Animation1");
        animation.Focus();
        recorder.Calls.Clear();
        var window = (Window)TopLevel.GetTopLevel(animation)!;
        var point = animation.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        recorder.Calls.Select(c => c.Name).Should().Equal("Animation1_MouseDown", "Animation1_MouseUp",
            "Animation1_Click", "Animation1_DblClick");
    }

    [AvaloniaFact]
    public void Move_uses_client_twips()
    {
        var (canvas, recorder, _, _) = ContainerRuntimeHarness.Recorded(Form);
        var animation = ContainerRuntimeHarness.Child(canvas, "Animation1");
        var window = (Window)TopLevel.GetTopLevel(animation)!;
        var point = animation.TranslatePoint(new Point(20, 20), window)!.Value;
        window.MouseMove(point);
        recorder.Calls.Select(c => c.Name).Should().Equal("Animation1_MouseMove");
        recorder.Calls[0].Args!.Select(v => v.Value).Should().Equal(0, 0, 300f, 300f);
    }
}
