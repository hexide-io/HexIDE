namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class GetFileContentDescriptionTests
{
    [Fact]
    public void Unsaved_changes_describes_edits_and_allows_save_normalisation()
    {
        ToolSource.Tools.Single(t => t.Name == "get_file_content").Description.Should()
            .Contain("edits not yet saved").And.Contain("normalise")
            .And.Contain("missing module name line").And.NotContain("saving would change the file");
    }
}
