namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public sealed class ArchiveFileToolDescriptionTests
{
    [Theory]
    [InlineData("get_project_info")]
    [InlineData("open_file")]
    public void Archive_addressing_is_discoverable(string name)
    {
        var description = ToolSource.Tools.Single(tool => tool.Name == name).Description;
        description.Should().Contain("archive").And.Contain("entries").And.Contain("project")
            .And.Contain("percent");
        description.Length.Should().BeLessThanOrEqualTo(2048);
    }

    [Theory]
    [InlineData("get_document_tabs")]
    [InlineData("activate_document_tab")]
    [InlineData("close_document_tab")]
    public void Archive_tab_type_is_discoverable(string name)
    {
        var description = ToolSource.Tools.Single(tool => tool.Name == name).Description;
        description.Should().Contain("'archive'").And.Contain("read-only");
        description.Length.Should().BeLessThanOrEqualTo(1900);
    }
}
