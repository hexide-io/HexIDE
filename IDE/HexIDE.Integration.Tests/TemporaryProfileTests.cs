using HexIDE.IDE;

namespace HexIDE.Integration.Tests;

public class TemporaryProfileTests
{
    [Fact]
    public void UserDataPath_is_redirected_under_the_temp_directory()
    {
        UserDataPath.IsRedirected.Should().BeTrue();
        var relative = Path.GetRelativePath(Path.GetTempPath(), UserDataPath.Directory);
        Path.IsPathRooted(relative).Should().BeFalse();
        relative.Should().NotBe("..");
        relative.Should().NotStartWith(".." + Path.DirectorySeparatorChar);
        relative.Should().NotBe(".");
    }
}
