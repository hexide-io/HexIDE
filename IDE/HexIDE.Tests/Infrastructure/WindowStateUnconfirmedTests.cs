using Avalonia.Controls;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// With no window manager on an X11 display, set_window_state's unconfirmed minimize or maximize says nothing will
/// apply it (#2185). It said the window manager had not confirmed and to re-read, and re-reading showed Normal forever.
/// </summary>
public class WindowStateUnconfirmedTests
{
    [Theory]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Maximized)]
    public void With_no_window_manager_a_minimize_or_maximize_says_nothing_will_apply_it(WindowState target)
    {
        var (error, note) = HexIdeTools.Unconfirmed(target, windowManagerRunning: false);

        error.Should().Be($"No window manager is running on this display, so nothing will apply {target}.");
        note.Should().Contain("_NET_SUPPORTING_WM_CHECK").And.Contain("however long you wait")
            .And.Contain("Normal with x, y, width and height").And.NotContain("Re-read");
    }

    // A window manager that is there but slow, a display that couldn't be read, another platform, and a restore, which
    // the X server applies itself: none of these is known never to come, so each keeps the advice to re-read.
    [Theory]
    [InlineData(WindowState.Minimized, true)]
    [InlineData(WindowState.Maximized, null)]
    [InlineData(WindowState.Normal, false)]
    public void Otherwise_the_reply_says_the_change_is_unconfirmed_and_to_re_read(WindowState target, bool? windowManagerRunning)
    {
        var (error, note) = HexIdeTools.Unconfirmed(target, windowManagerRunning);

        error.Should().Be("The window manager has not confirmed the requested change.");
        note.Should().EndWith("Re-read with get_window_state.");
    }

    // Off X11 there is nothing to read, and the probe must say so rather than guess.
    [WindowsOnlyFact]
    public void On_Windows_the_probe_does_not_guess() => X11WindowManager.IsRunning().Should().BeNull();
}
