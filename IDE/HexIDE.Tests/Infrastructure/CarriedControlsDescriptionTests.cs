namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class CarriedControlsDescriptionTests
{
    [Fact]
    public void Description_explains_carried_rows_and_named_lookup()
    {
        var text = ToolSource.Tools.Single(t => t.Name == "get_form_controls").Description;
        text.Should().Contain("carried: true").And.Contain("cannot be moved, sized or set")
            .And.Contain("controlName also finds these rows")
            .And.Contain("zOrder").And.Contain("bottom").And.Contain("container");
    }
}
