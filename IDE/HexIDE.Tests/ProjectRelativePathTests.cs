using System.IO;
using HexIDE.Projects;

namespace HexIDE.Tests;

/// <summary>
/// Guards <see cref="ProjectService.ToLocalRelativePath"/> — the fix for Windows-authored .vbp/.frm
/// projects silently dropping files on macOS/Linux (a raw 'Forms\Main.frm' is one literal filename there).
/// The Be(expected) oracle is built by a different route than the implementation, and is meaningful on the
/// Linux CI runner (where the backslash actually bites).
/// </summary>
public class ProjectRelativePathTests
{
    [Theory]
    [InlineData("Forms\\Main.frm")]
    [InlineData("Modules\\Shared\\Util.bas")]
    [InlineData("Main.frm")]
    [InlineData("Sub/Mixed\\Sep.cls")]
    public void ToLocalRelativePath_normalizes_either_separator_to_the_platform_one(string vbRelativePath)
    {
        var expected = string.Join(Path.DirectorySeparatorChar, vbRelativePath.Split('\\', '/'));

        ProjectService.ToLocalRelativePath(vbRelativePath).Should().Be(expected);
    }

    [Fact]
    public void ToLocalRelativePath_leaves_no_backslash_on_a_unix_separator_platform()
    {
        var result = ProjectService.ToLocalRelativePath("Forms\\Main.frm");

        if (Path.DirectorySeparatorChar == '/')
            result.Should().Be("Forms/Main.frm").And.NotContain("\\");
    }
}
