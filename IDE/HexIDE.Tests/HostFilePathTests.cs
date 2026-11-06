using System.IO;
using HexIDE.IO;

namespace HexIDE.Tests;

public class HostFilePathTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Case_follows_the_selected_mode(bool ignoreCase, bool expected)
    {
        HostFilePath.Equals(Path.Combine("Folder", "A.vbp"), Path.Combine("folder", "a.vbp"), ignoreCase)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Relative_segments_are_normalized(bool ignoreCase)
    {
        HostFilePath.Equals(Path.Combine("Folder", "..", "A.vbp"), Path.GetFullPath("A.vbp"), ignoreCase)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Different_files_stay_distinct(bool ignoreCase)
    {
        HostFilePath.Equals("A.vbp", "B.vbp", ignoreCase).Should().BeFalse();
    }

    [Fact]
    public void Default_mode_follows_the_host()
    {
        HostFilePath.Equals("A.vbp", "a.vbp").Should().Be(OperatingSystem.IsWindows());
        HostFilePath.Comparer.Equals("A.vbp", "a.vbp").Should().Be(OperatingSystem.IsWindows());
    }
}
