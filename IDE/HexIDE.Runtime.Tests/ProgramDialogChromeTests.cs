using HexIDE.Runtime.Dialogs;

namespace HexIDE.Runtime.Tests;

public class ProgramDialogChromeTests
{
    // GetWindowLong measurements in hexide-io/HexIDE#4099: no min/max boxes, WS_EX_DLGMODALFRAME.
    [Theory]
    [InlineData(0xffffffffu, 0xfffffffeu, 0xfffcffffu, 0xffffffffu)]
    [InlineData(0x94c801c5u, 0x00010100u, 0x94c801c5u, 0x00010101u)]
    public void Only_the_measured_caption_bits_change(uint style, uint exStyle, uint expectedStyle, uint expectedExStyle)
    {
        ProgramDialogChrome.WindowsStyles(style, exStyle).Should().Be((expectedStyle, expectedExStyle));
    }

    // The system message box has no icon (WM_GETICON returns none, #4099), and Avalonia would set its default one.
    [Theory]
    [InlineData(0x007Fu, true)]
    [InlineData(0x0080u, true)]
    [InlineData(0x0200u, false)]
    [InlineData(0x0081u, false)]
    public void Only_the_icon_messages_are_answered_and_with_no_icon(uint message, bool answered)
    {
        var handled = false;

        var result = ProgramDialogChrome.NoIcon(1, message, 1, 1, ref handled);

        handled.Should().Be(answered);
        result.Should().Be(IntPtr.Zero);
    }
}
