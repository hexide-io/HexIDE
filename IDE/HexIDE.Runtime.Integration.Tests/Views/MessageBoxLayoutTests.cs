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
        var box = new MessageBox { Layout = layout };
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
    public void Other_platforms_have_no_window_style_callback()
    {
        var program = new MessageBox { Layout = MessageBoxLayout.RtlReading };
        program.WindowStylesCallback(false).Should().BeNull();
        if (!System.OperatingSystem.IsWindows())
            program.WindowStylesCallback(System.OperatingSystem.IsWindows()).Should().BeNull();
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
        var box = new MessageBox { Layout = MessageBoxLayout.RtlReading };
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
        var box = new MessageBox { Layout = MessageBoxLayout.RtlReading };
        var hook = box.MouseHookCallback(true, _ => throw new System.Exception("Must not read client width"));
        var handled = false;
        hook!(System.IntPtr.Zero, message, System.IntPtr.Zero, (System.IntPtr)(-1), ref handled)
            .Should().Be(System.IntPtr.Zero);
        handled.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false, MessageBoxLayout.RtlReading)]
    [InlineData(true, MessageBoxLayout.None)]
    [InlineData(true, MessageBoxLayout.Right)]
    public void Mouse_hook_is_absent_without_native_mirroring(bool windows, MessageBoxLayout layout)
    {
        var box = new MessageBox { Layout = layout };
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
        var answer = new StandaloneStandardLib(owner, HexIDE.IDE.Distribution.DisplayName).MsgBox("probe", "Layout probe",
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
            // #4250: captions (including access keys) belong to the platform; this test pins their order.
            var yes = HexIDE.Runtime.Dialogs.ProgramButtonCaptions.Get(MessageBoxResult.Yes, "Yes");
            var no = HexIDE.Runtime.Dialogs.ProgramButtonCaptions.Get(MessageBoxResult.No, "No");
            var cancel = HexIDE.Runtime.Dialogs.ProgramButtonCaptions.Get(MessageBoxResult.Cancel, "Cancel");
            buttons.Select(b => b.Content).Should().Equal(mirrored
                ? new[] { cancel, no, yes } : new[] { yes, no, cancel });
            buttons.Single(b => Equals(b.Content, no)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            (await answer).Should().Be(MessageBoxResult.No);
        }
        finally
        {
            window.Close();
            owner.Close();
        }
    }
}
