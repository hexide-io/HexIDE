using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Views;

public class FormPositionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Manual_position_is_the_outer_origin_before_and_after_Show(bool pixels)
    {
        var instance = new ComponentInstance(FormComponentClass.Instance, "Form1");
        instance.SetProperty(VBProperties.LeftProperty, 133d);
        instance.SetProperty(VBProperties.TopProperty, 100d);
        var form = FormComponentClass.Instance.InstantiateWindow(instance);
        try
        {
            if (pixels) VBProps.SetScaleMode(form, Runtime.BuiltinTypes.VBScaleMode.Pixel);
            Read(form, VBProperties.LeftProperty).Should().Be(1875f);
            Read(form, VBProperties.TopProperty).Should().Be(1035f);
            form.ShowForm();
            Read(form, VBProperties.LeftProperty).Should().Be(1875f);
            Read(form, VBProperties.TopProperty).Should().Be(1035f);
        }
        finally { form.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1200, 900)]
    [InlineData(1207, 900)]
    [InlineData(600, 900)]
    [InlineData(300, 450)]
    [InlineData(-3000, 900)]
    public void Assigned_twips_move_the_window_and_read_back_exactly(int left, int top)
    {
        var form = new VBFormRuntime();
        try
        {
            VBProps.SetScaleMode(form, Runtime.BuiltinTypes.VBScaleMode.Pixel);
            AvaloniaInteroperability.TrySet(form, VBProperties.LeftProperty, new Runtime.Interpreter.Vb6Value(left)).Should().BeTrue();
            AvaloniaInteroperability.TrySet(form, VBProperties.TopProperty, new Runtime.Interpreter.Vb6Value(top)).Should().BeTrue();
            form.Position.Should().Be(new PixelPoint((int)System.Math.Round(left / 15d), (int)System.Math.Round(top / 15d)));
            Read(form, VBProperties.LeftProperty).Should().Be(left);
            Read(form, VBProperties.TopProperty).Should().Be(top);
        }
        finally { form.Close(); }
    }

    [AvaloniaFact]
    public void CenterScreen_keeps_the_Manual_origin_until_Show()
    {
        var instance = new ComponentInstance(FormComponentClass.Instance, "Form1");
        instance.SetProperty(VBProperties.LeftProperty, 133d);
        instance.SetProperty(VBProperties.TopProperty, 100d);
        instance.SetProperty(VBProperties.WidthProperty, 300d);
        instance.SetProperty(VBProperties.HeightProperty, 200d);
        instance.SetProperty(VBProperties.StartUpPositionProperty, Runtime.BuiltinTypes.VBStartupPosition.StartUpScreen);
        var form = FormComponentClass.Instance.InstantiateWindow(instance);
        try
        {
            Read(form, VBProperties.LeftProperty).Should().Be(1875f);
            Read(form, VBProperties.TopProperty).Should().Be(1035f);
            form.ShowForm();
            var screen = form.Screens.Primary!.Bounds;
            Read(form, VBProperties.LeftProperty).Should().Be((float)((screen.X + System.Math.Floor((screen.Width - 316d) / 2)) * 15));
            Read(form, VBProperties.TopProperty).Should().Be((float)((screen.Y + System.Math.Floor((screen.Height - 239d) / 2)) * 15));
        }
        finally { form.Close(); }
    }

    private static float Read(Window form, PropertyClass property)
    {
        AvaloniaInteroperability.TryGet(form, property, out var value).Should().BeTrue();
        value.Value.Should().BeOfType<float>();
        return (float)value.Value!;
    }
}
