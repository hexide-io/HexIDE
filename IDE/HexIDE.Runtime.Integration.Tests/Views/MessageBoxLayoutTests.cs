using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using MessageBox = HexIDE.Runtime.Dialogs.MessageBox;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class MessageBoxLayoutTests
{
    // #3973; docs/vb6-fidelity-oracle/rgb-qbcolor-and-msgboxs-style.md, measuring PR #3958.
    [AvaloniaTheory]
    [InlineData(MessageBoxLayout.None, false)]
    [InlineData(MessageBoxLayout.Right, false)]
    [InlineData(MessageBoxLayout.RtlReading, true)]
    [InlineData(MessageBoxLayout.Right | MessageBoxLayout.RtlReading, true)]
    public void Windows_program_window_styles_follow_RtlReading(MessageBoxLayout layout, bool mirrored)
    {
        var box = new MessageBox { IsProgramDialog = true, Layout = layout };
        var callback = box.WindowStylesCallback(true);
        if (!mirrored)
        {
            callback.Should().BeNull();
            return;
        }
        callback.Should().NotBeNull();
        callback!(0x12345678, 0x00010101).Should().Be((0x12345678u, 0x00410101u));
        callback(0, 0x00400000).Should().Be((0u, 0x00400000u));
    }

    [AvaloniaFact]
    public void Other_platforms_and_IDE_boxes_have_no_window_style_callback()
    {
        var program = new MessageBox { IsProgramDialog = true, Layout = MessageBoxLayout.RtlReading };
        program.WindowStylesCallback(false).Should().BeNull();
        if (!System.OperatingSystem.IsWindows())
            program.WindowStylesCallback(System.OperatingSystem.IsWindows()).Should().BeNull();
        var ide = new MessageBox { Layout = MessageBoxLayout.RtlReading };
        ide.WindowStylesCallback(true).Should().BeNull();
    }

    [AvaloniaTheory]
    [InlineData(0x0200)]
    [InlineData(0x0201)]
    [InlineData(0x0202)]
    [InlineData(0x0203)]
    [InlineData(0x0204)]
    [InlineData(0x0205)]
    [InlineData(0x0206)]
    [InlineData(0x0207)]
    [InlineData(0x0208)]
    [InlineData(0x0209)]
    [InlineData(0x020B)]
    [InlineData(0x020C)]
    [InlineData(0x020D)]
    public void Mirrored_mouse_messages_are_resent_once_in_client_pixels(uint message)
    {
        var box = new MessageBox { IsProgramDialog = true, Layout = MessageBoxLayout.RtlReading };
        Avalonia.Controls.Win32Properties.CustomWndProcHookCallback? hook = null;
        var sends = 0;
        // The review's 399-pixel client, at 150%: x 324 must reach Avalonia as 74, without DIP conversion.
        hook = box.MouseHookCallback(true, _ => 399, (hwnd, msg, wParam, lParam) =>
        {
            sends++;
            hwnd.Should().Be((System.IntPtr)123);
            msg.Should().Be(message);
            wParam.Should().Be((System.IntPtr)1);
            lParam.Should().Be((System.IntPtr)((27 << 16) | 74));
            var nestedHandled = false;
            hook!(hwnd, msg, wParam, lParam, ref nestedHandled).Should().Be(System.IntPtr.Zero);
            nestedHandled.Should().BeFalse("the re-sent message must reach Avalonia");
            return (System.IntPtr)42;
        });
        var handled = false;
        hook!((System.IntPtr)123, message, (System.IntPtr)1, (System.IntPtr)((27 << 16) | 324), ref handled)
            .Should().Be((System.IntPtr)42);
        sends.Should().Be(1);
        handled.Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(0x020A)] // wheel
    [InlineData(0x020E)] // horizontal wheel
    [InlineData(0x007B)] // context menu
    [InlineData(0x0084)] // non-client hit test
    [InlineData(0x0100)] // key
    public void Screen_coordinate_and_non_mouse_messages_pass_through(uint message)
    {
        var box = new MessageBox { IsProgramDialog = true, Layout = MessageBoxLayout.RtlReading };
        var hook = box.MouseHookCallback(true, _ => throw new System.Exception("Must not read client width"));
        var handled = false;
        hook!(System.IntPtr.Zero, message, System.IntPtr.Zero, (System.IntPtr)(-1), ref handled)
            .Should().Be(System.IntPtr.Zero);
        handled.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false, true, MessageBoxLayout.RtlReading)]
    [InlineData(true, false, MessageBoxLayout.RtlReading)]
    [InlineData(true, true, MessageBoxLayout.None)]
    [InlineData(true, true, MessageBoxLayout.Right)]
    public void Mouse_hook_is_absent_without_native_mirroring(bool windows, bool program, MessageBoxLayout layout)
    {
        var box = new MessageBox { IsProgramDialog = program, Layout = layout };
        box.MouseHookCallback(windows).Should().BeNull();
        if (!System.OperatingSystem.IsWindows())
            box.MouseHookCallback(System.OperatingSystem.IsWindows()).Should().BeNull();
    }

    // VB6 SP6 native and p-code, supplied in #3014 by measuring PR #3958, style 35 and the two layout flags.
    [AvaloniaTheory]
    [InlineData(MessageBoxLayout.None, false, false)]
    [InlineData(MessageBoxLayout.Right, false, true)]
    [InlineData(MessageBoxLayout.RtlReading, true, false)]
    [InlineData(MessageBoxLayout.Right | MessageBoxLayout.RtlReading, true, true)]
    public async Task The_programs_layout_flags_position_the_icon_text_and_buttons(
        MessageBoxLayout layout, bool mirrored, bool rightAligned)
    {
        var owner = new Window();
        owner.Show();
        var answer = new StandaloneStandardLib(owner).MsgBox("probe", "Layout probe",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, layout);
        var window = owner.OwnedWindows.Single();
        var box = (MessageBox)window.Content!;
        window.Width = 420;
        window.Height = 180;
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var prompt = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "probe");
            var icon = box.GetVisualDescendants().OfType<Border>().Single(b => b.Child is TextBlock { Text: "?" });
            var promptLeft = prompt.PointToScreen(default).X;
            var iconLeft = icon.PointToScreen(default).X;
            (iconLeft > promptLeft).Should().Be(mirrored);
            var line = prompt.TextLayout.TextLines.Single();
            (line.Start > 1).Should().Be(rightAligned, "the laid-out glyphs align within the prompt's available width");
            var buttons = box.GetVisualDescendants().OfType<Button>()
                .OrderBy(b => b.PointToScreen(default).X).ToArray();
            buttons.Select(b => b.Content).Should().Equal(mirrored
                ? new[] { "Cancel", "No", "Yes" } : new[] { "Yes", "No", "Cancel" });
            buttons.Single(b => Equals(b.Content, "No")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await answer).Should().Be(MessageBoxResult.No);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }
}
