using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #3505: the designer asks for a picture and shows the pixels it gets back, so what draws a control can be elsewhere.
public class DesignerPictureTests
{
    [Fact]
    public void Rgba_pixels_become_Bgra() =>
        InProcessDesignerPictures.SwapRedAndBlue([1, 2, 3, 4, 5, 6, 7, 8]).Should().Equal(3, 2, 1, 4, 7, 6, 5, 8);
}
