namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public sealed class BreakpointConditionDescriptionTests
{
    [Theory]
    [InlineData("set_breakpoints")]
    [InlineData("get_breakpoints")]
    public void Conditions_explain_scope_frequency_and_pause_rule(string name)
    {
        ToolSource.Tools.Single(tool => tool.Name == name).Description.Should()
            .Contain("VB expression").And.Contain("procedure").And.Contain("each time")
            .And.Contain("True");
    }

    [Fact]
    public void Setter_explains_preservation_and_explicit_clearing()
    {
        ToolSource.Tools.Single(tool => tool.Name == "set_breakpoints").Description.Should()
            .Contain("Omitted keys keep").And.Contain("explicit blank");
    }
}
