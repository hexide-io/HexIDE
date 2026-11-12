namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public sealed class OpenDocumentAreaDescriptionTests
{
    [Theory]
    [InlineData("open_file")]
    [InlineData("view_designer")]
    public void OpeningDescribesTheAreaReturned(string name)
    {
        ToolSource.Tools.Single(t => t.Name == name).Description.Should()
            .Contain("'area'").And.Contain("0").And.Contain("layout order");
    }
}
