namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// inspect_element's selectionItems read as the list's items, and a dropdown holding two looked like one holding the
/// one selected (#1263). The description says what it holds, and names itemCount, so the wording is not dropped later.
/// </summary>
public class InspectElementDescriptionTests
{
    [Fact]
    public void The_description_says_selectionItems_is_the_selection_and_names_itemCount()
    {
        ToolSource.Tools.Single(t => t.Name == "inspect_element").Description.Should()
            .Contain("'selectionItems' (what is selected, not the list's items").And.Contain("'itemCount'");
    }
}
