using HexIDE.Automation;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// <c>interact</c>'s description states what <c>click</c> takes in the same words its refusals use (#1033). The two are
/// written in different projects, and only the description reaches a caller before the first call, so a grammar that
/// changed in one and not the other would be learned from a refusal.
/// </summary>
public class InteractClickDescriptionTests
{
    private static ToolSource.Tool Interact => ToolSource.Tools.Single(t => t.Name == "interact");

    [Fact]
    public void The_value_description_gives_the_grammar_the_refusals_quote()
    {
        Interact.Parameters.Single(p => p.Name == "value").Description.Should().Contain(UiAutomationDriver.ClickValue);
    }

    [Fact]
    public void The_action_description_lists_click()
    {
        Interact.Parameters.Single(p => p.Name == "action").Description.Should().Contain("click (press and release a pointer");
    }

    [Fact]
    public void Value_stays_optional_on_the_wire()
    {
        // click with no value is a left click at the centre, so the parameter must not become required.
        Interact.Parameters.Single(p => p.Name == "value").Optional.Should().BeTrue();
    }
}
