namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// What <c>set_file_content</c> tells a caller about the parts of a file it may not change (hexide-io/HexIDE#273
/// task 4.5b), read headlessly from the tool's declaration.
/// </summary>
/// <remarks>
/// A session that has loaded a tool keeps the description it loaded, through a rebuild and a reconnect, so a changed
/// description cannot be checked in the session that changed it (hexide-io/HexIDE#815). This reads the text the SDK
/// sends, from <c>HexIdeTools.cs</c>, as every description guard here does.
/// </remarks>
public class SetFileContentDescriptionTests
{
    private static ToolSource.Tool Tool => ToolSource.Tools.Single(t => t.Name == "set_file_content");

    [Fact]
    public void TheDescriptionSaysAMembersAttributeLinesAreTheIdesAndARefusalNamesTheMember()
    {
        Tool.Description.Should().Contain("A member's Attribute lines")
            .And.Contain("may move or be renamed with it and go with its declaration")
            .And.Contain("names each member whose lines are otherwise changed, added or dropped");
    }

    [Fact]
    public void TheDescriptionStillSaysAChangedHeaderIsRefused()
    {
        Tool.Description.Should().Contain("content that changes either is refused with nothing changed")
            .And.Contain("VERSION line and designer block");
    }

    [Fact]
    public void TheContentParameterSaysWhatToSend()
    {
        // Moved from the tool's description to the parameter's, which travels in the input schema, to keep the
        // description inside what a client delivers (ToolDescriptionLengthTests).
        Tool.Parameters.Single(p => p.Name == "content").Description
            .Should().Contain("What get_file_content returns, edited")
            .And.Contain("every member's Attribute lines as the document has them")
            .And.Contain("The code alone is accepted too");
    }
}
