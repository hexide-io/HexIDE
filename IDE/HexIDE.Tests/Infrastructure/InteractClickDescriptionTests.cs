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
    public void Click_describes_a_drag_and_its_release_coordinates()
    {
        Interact.Parameters.Single(p => p.Name == "action").Description.Should().Contain("to drags");
        Interact.Parameters.Single(p => p.Name == "value").Description.Should()
            .Contain("to <path>:x,y").And.Contain("dump_visual_tree path, including spaces")
            .And.Contain("to cannot accompany double");
    }

    [Fact]
    public void The_hold_the_description_states_is_the_one_click_uses()
    {
        Interact.Description.Should().Contain($"releasing {UiAutomationDriver.ClickHoldMs} ms later");
    }

    [Fact]
    public void Expand_and_collapse_say_they_reach_a_controls_own_popup()
    {
        // Without it, expandCollapse on a TextBox read as a phantom and eight toolbar commands were recorded as
        // unreachable (#949). Both descriptions sit near the length cap, so a trim could drop it unseen.
        Interact.Parameters.Single(p => p.Name == "action").Description.Should().Contain("or else the context menu or flyout the control owns");
        ToolSource.Tools.Single(t => t.Name == "dump_visual_tree").Description.Should()
            .Contain("expandCollapse → expand/collapse (also a context menu or flyout the control owns)");
    }

    [Fact]
    public void The_action_description_says_only_click_presses()
    {
        Interact.Parameters.Single(p => p.Name == "action").Description.Should().Contain("the only verb that presses");
    }

    [Fact]
    public void Value_stays_optional_on_the_wire()
    {
        // click with no value is a left click at the centre, so the parameter must not become required.
        Interact.Parameters.Single(p => p.Name == "value").Optional.Should().BeTrue();
    }
}
