using System.IO;

namespace HexIDE.Tests.Recovery;

/// <summary>
/// Where the backup lives (hexide-io/HexIDE#499): local and never roaming, the XDG state directory on Unix, and under
/// <c>--user-data-dir</c> when a session names one.
/// </summary>
public class RecoveryPathTests
{
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "hexide-recovery-path");

    [Fact]
    public void Windows_keeps_it_in_local_not_roaming_app_data()
    {
        var local = Path.Combine(Base, "Local");

        RecoveryPath.For(null, windows: true, local, xdgStateHome: null, home: Base)
            .Should().Be(Path.Combine(local, "HexIDE", "Recovery"));
    }

    [Fact]
    public void Unix_keeps_it_in_the_XDG_state_directory()
    {
        var state = Path.Combine(Base, "state");

        RecoveryPath.For(null, windows: false, localAppData: "", state, home: Base)
            .Should().Be(Path.Combine(state, "HexIDE", "Recovery"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/state")]
    public void Unix_without_a_usable_XDG_state_directory_uses_the_one_under_home(string? xdgStateHome)
    {
        RecoveryPath.For(null, windows: false, localAppData: "", xdgStateHome, home: Base)
            .Should().Be(Path.Combine(Base, ".local", "state", "HexIDE", "Recovery"),
                "XDG has a relative path in its variables ignored, not used");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_session_s_user_data_directory_holds_it_instead(bool windows)
    {
        var session = Path.Combine(Base, "sessions", "probe");

        RecoveryPath.For(session, windows, Path.Combine(Base, "Local"), Path.Combine(Base, "state"), Base)
            .Should().Be(Path.Combine(session, "Recovery"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_root_that_is_not_absolute_is_refused_rather_than_used(bool windows)
    {
        var resolve = () => RecoveryPath.For(null, windows, localAppData: "", xdgStateHome: null, home: "");

        resolve.Should().Throw<InvalidOperationException>("a relative path would hide the backup from the next start");
    }
}
