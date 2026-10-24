namespace HexIDE.Tests.Infrastructure;

public class SaveRewriteReplyTests
{
    [Theory]
    [InlineData("set_file_content")]
    [InlineData("set_control_property")]
    [InlineData("add_control")]
    public void SuccessfulReplyIncludesTheSavesRewriteReport(string name)
    {
        // The executable's tools are checked as source, as ToolDocumentNamingTests does.
        var body = ToolSource.Tools.Single(t => t.Name == name).Body;
        body.Should().Contain("rewrites: rewrites");
        body.Should().Contain("+ rewrites.Note");
    }
}
