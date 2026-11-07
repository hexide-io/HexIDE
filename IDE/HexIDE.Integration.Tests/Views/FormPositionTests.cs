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

    // A window's Position is in the screen's pixels, and a twip is a fifteenth of a device-independent pixel, so at
    // 150% Left = 1200 is 80 device-independent pixels and 120 of the screen's. The headless platform scales by 1, where
    // the two units agree, so the conversions are checked here at the scalings a desktop has.
    [Theory]
    [InlineData(80d, 1d, 80)]
    [InlineData(80d, 1.5d, 120)]
    [InlineData(200d, 1.25d, 250)]
    [InlineData(-200d, 1.5d, -300)]
    public void A_position_goes_to_the_screen_pixel_its_scaling_gives(double pixels, double scaling, int screenPixel)
    {
        FormPosition.ScreenPixel(pixels, scaling).Should().Be(screenPixel);
        FormPosition.Twips(screenPixel, scaling).Should().Be(pixels * 15);
    }

    [Theory]
    [InlineData(1024, 1d, 4740d, 5310d)]
    [InlineData(768, 1d, 3585d, 3960d)]
    [InlineData(1920, 1.5d, 4740d, 7230d)]
    [InlineData(1080, 1.5d, 3585d, 3600d)]
    public void CenterScreen_centres_in_the_units_Screen_reports(int screenExtent, double scaling, double outerTwips, double twips)
    {
        // 5310 and 3960 are vb6.exe's on a 1024 x 768 console (#852). At 150%, Screen.Width is 1920 / 1.5 * 15 = 19200
        // twips, and (19200 - 4740) / 2 is 7230.
        (FormPosition.Centred(0, screenExtent, outerTwips, scaling) * 15).Should().Be(twips);
    }

    private static float Read(Window form, PropertyClass property)
    {
        AvaloniaInteroperability.TryGet(form, property, out var value).Should().BeTrue();
        value.Value.Should().BeOfType<float>();
        return (float)value.Value!;
    }
}
