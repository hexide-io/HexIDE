namespace HexIDE.Tests.Infrastructure;

public class UnavailableTestGuardTests
{
    [Theory]
    [InlineData(typeof(Tools.ProjectTreeBuilderTests), "CrossDriveMember_RendersAtRoot")]
    [InlineData(typeof(Tools.ProjectTreeBuilderTests), "ForwardSlashPath_NormalizesAndNests")]
    [InlineData(typeof(Tools.ProjectTreeBuilderTests), "AMemberWithNoRelativeForm_CaptionsItsAbsoluteLocation")]
    [InlineData(typeof(Addins.PackageVerificationTests), "Locked_signature_file_yields_Untrusted_not_an_exception")]
    public void Windows_gates_report_a_skip(Type type, string method)
    {
        var attribute = type.GetMethod(method)!.GetCustomAttributes(typeof(FactAttribute), false)
            .Cast<FactAttribute>().Single();
        attribute.Should().BeOfType<WindowsOnlyFactAttribute>();
        attribute.Skip.Should().Be(OperatingSystem.IsWindows() ? null : new WindowsOnlyFactAttribute().Skip);
    }
}
