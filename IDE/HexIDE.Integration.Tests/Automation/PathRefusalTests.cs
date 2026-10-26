using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Automation;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// A target that is no path at all says where paths come from (#2080). A caller who guessed one from a snapshot, as
/// "Editor/General" from the Options dialog's tree, was told only that it must start with 'Window'.
/// </summary>
public class PathRefusalTests
{
    // The suggested call names the window the path was looked for in, so it can be sent as it stands.
    [AvaloniaFact]
    public void A_path_not_starting_with_Window_names_dump_visual_tree_and_a_match_for_its_last_segment()
    {
        var window = new Window { Title = "Options" };

        var (control, error) = UiAutomationDriver.Resolve(window, "Editor/General");

        control.Should().BeNull();
        error.Should().Be("path must start with 'Window' (got 'Editor'); a path comes from dump_visual_tree, whose every node "
                          + "carries its 'path', and its 'match' finds a control by name, as dump_visual_tree "
                          + "{\"window\":\"Options\",\"match\":\"General\"}");
    }

    [AvaloniaFact]
    public void A_bracketed_segment_is_matched_by_the_text_in_its_brackets()
    {
        var (_, error) = UiAutomationDriver.Resolve(new Window { Title = "Options" }, "Tree/TreeItem[Editor]/TreeItem[General]");

        error.Should().EndWith("as dump_visual_tree {\"window\":\"Options\",\"match\":\"General\"}");
    }

    [AvaloniaFact]
    public void An_empty_path_names_dump_visual_tree()
    {
        var (_, error) = UiAutomationDriver.Resolve(new Window(), " ");

        error.Should().Be("empty target path; a path comes from dump_visual_tree, whose every node carries its 'path', and "
                          + "its 'match' finds a control by name");
    }
}
