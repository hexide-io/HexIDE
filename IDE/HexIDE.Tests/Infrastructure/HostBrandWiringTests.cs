namespace HexIDE.Tests.Infrastructure;

public class HostBrandWiringTests
{
    [Theory]
    [InlineData("HexIDE.Core/IDE/UserDataPath.cs")]
    [InlineData("HexIDE.Core/IDE/UserDataRoot.cs")]
    [InlineData("HexIDE.Core/IDE/RecoveryPath.cs")]
    public void Path_diagnostics_name_the_installation(string file)
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", file));
        source.Should().Contain("{Distribution.DisplayName}");
        source.Should().NotContain("Cannot locate HexIDE's").And.NotContain("directory HexIDE");
    }

    [Fact]
    public void Unsupported_template_caption_uses_the_installations_name()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE", "Forms", "ViewModels", "NewProjectViewModel.cs"));
        source.Should().Contain("SelectedTemplate.DisplayName), Distribution.DisplayName, MessageBoxButtons.Ok");
    }

    [Fact]
    public void Automation_log_hint_uses_the_distributions_state_folder()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "ToolFailures.cs"));
        source.Should().Contain("Distribution.Current.StateDirectoryName");
        source.Should().NotContain(@"%LOCALAPPDATA%\HexIDE\logs\ide")
            .And.NotContain("Application Support/HexIDE/logs/ide").And.NotContain("share/HexIDE/logs/ide");
    }
}
