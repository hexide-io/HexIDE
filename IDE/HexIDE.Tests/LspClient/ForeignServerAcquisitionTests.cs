namespace HexIDE.Tests.LspClient;

public class ForeignServerAcquisitionTests
{
    [Theory]
    [InlineData("markdown")]
    [InlineData("latex")]
    [InlineData("cpp")]
    [InlineData("python")]
    public void WindowsArmDeliberatelyUsesThePinnedX64Archive(string key)
    {
        var source = new[]
        {
            ForeignServerAcquisition.Markdown, ForeignServerAcquisition.Latex,
            ForeignServerAcquisition.Cpp, ForeignServerAcquisition.Python,
        }.Single(server => server.Key == key);
        var x64 = source.Assets.Single(asset => asset.Rid == "win-x64");
        var arm = source.Assets.Where(asset => asset.Rid == "win-arm64").Should().ContainSingle().Subject;

        arm.Should().Be(x64 with { Rid = "win-arm64" },
            "#3139 deliberately runs these existing x64 pins under Windows on Arm emulation");
    }
}
