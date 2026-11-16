using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Forms.Views;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

// #3364 (decided 2026-11-15): HexIDE's logo is a brand image, exempt from the icon system's vector rule. It ships
// rendered from its SVG at 256 pixels, and About and New Project scale it down smoothly, so it stays sharp at 150% and
// 200%. A 64-pixel image stretched there looked soft, and a 256-pixel one scaled down without smoothing looked stepped.
public class BrandLogoTests
{
    [AvaloniaFact]
    public void The_stock_logo_is_large_enough_to_scale_down_at_every_supported_scaling()
    {
        var logo = BrandAssets.Logo;

        logo.PixelSize.Width.Should().BeGreaterThanOrEqualTo(256);
        logo.PixelSize.Height.Should().Be(logo.PixelSize.Width);
    }

    [AvaloniaFact]
    public void About_scales_the_logo_down_smoothly()
    {
        var image = new AboutDialog().GetLogicalDescendants().OfType<Image>().Single(i => i.Width == 48);

        RenderOptions.GetBitmapInterpolationMode(image).Should().Be(BitmapInterpolationMode.HighQuality);
    }

    [AvaloniaFact]
    public void New_Project_scales_the_logo_down_smoothly()
    {
        var image = new NewProjectView().GetLogicalDescendants().OfType<Image>().Single(i => i.Width == 46);

        RenderOptions.GetBitmapInterpolationMode(image).Should().Be(BitmapInterpolationMode.HighQuality);
    }
}
