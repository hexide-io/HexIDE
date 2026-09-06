using HexIDE.IDE;

namespace HexIDE.Tests;

/// <summary>
/// The assertion that would have caught hexide-io/HexIDE#280.
///
/// <para>
/// Ten call sites built per-user paths from
/// <c>Environment.GetFolderPath(SpecialFolder.ApplicationData)</c>, which returns an <b>empty string</b>
/// on Unix when <c>XDG_CONFIG_HOME</c> is unset — as it is on most distributions, because applications
/// are expected to default to <c>~/.config</c> themselves. <c>Path.Combine("", "HexIDE", "settings.json")</c>
/// is a <b>relative</b> path, so every per-user file landed wherever the process was started from.
/// </para>
///
/// <para>
/// Nothing caught it, because nothing asserted where those files go — the paths were built from a folder
/// API assumed to work, and `build-ide` runs on Linux where it does not. These tests run on every platform
/// and say nothing about which one they are on: the invariant is the same everywhere, and an assertion
/// that branches on the platform is one that cannot fail on the platform that broke.
/// </para>
/// </summary>
public class UserDataPathTests
{
    [Fact]
    public void TheDirectoryIsAbsolute()
    {
        // The whole bug in one assertion. A relative path here means settings that do not persist,
        // consent re-asked, and an add-in revocation that cannot be found.
        Path.IsPathRooted(UserDataPath.Directory).Should().BeTrue(
            $"per-user files must not depend on the working directory, but got "
          + $"'{UserDataPath.Directory}'");
    }

    [Fact]
    public void AFileInsideItIsAbsoluteToo()
    {
        Path.IsPathRooted(UserDataPath.For("settings.json")).Should().BeTrue();
    }

    [Fact]
    public void TheDirectoryIsNamedForHexIde()
    {
        // Guards against a fix that returns *an* absolute path but drops the application segment, which
        // would put HexIDE's files loose in the user's configuration root.
        UserDataPath.Directory.Should().EndWith("HexIDE");
    }

    [Fact]
    public void AFileSitsDirectlyInsideTheDirectory()
    {
        Path.GetDirectoryName(UserDataPath.For("settings.json"))
            .Should().Be(UserDataPath.Directory);
    }

    [Fact]
    public void RepeatedCallsAgree()
    {
        // Cheap, but the failure it guards is nasty: a base that varied between calls would write a file
        // in one place and look for it in another.
        UserDataPath.For("settings.json").Should().Be(UserDataPath.For("settings.json"));
        UserDataPath.Directory.Should().Be(UserDataPath.Directory);
    }

    [WindowsOnlyFact]
    public void OnWindowsItIsUnderAppData()
    {
        // Pinned so the Unix fix cannot quietly change where existing Windows users' files live.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        UserDataPath.Directory.Should().Be(Path.Combine(appData, "HexIDE"));
    }
}
