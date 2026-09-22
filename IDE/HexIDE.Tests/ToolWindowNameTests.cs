namespace HexIDE.Tests;

/// <summary>
/// set_tool_window_visible takes a panel's name as the View menu spells it as well as its own (#586). The menu's
/// names were refused for six of the eight panels, so a caller who read one off the menu was told it did not exist.
/// </summary>
public class ToolWindowNameTests
{
    [Theory]
    [InlineData("Immediate Window", "Immediate")]
    [InlineData("Locals Window", "Locals")]
    [InlineData("Watch Window", "Watches")]
    [InlineData("Call Stack...", "CallStack")]
    [InlineData("Project Explorer", "ProjectGroup")]
    [InlineData("Properties Window", "Properties")]
    [InlineData("Form Layout Window", "FormLayout")]
    [InlineData("Toolbox", "Toolbox")]
    public void The_View_menus_name_for_a_panel_is_its_name(string menu, string expected)
    {
        MainViewViewModel.CanonicalToolWindowName(menu).Should().Be(expected);
    }

    [Fact]
    public void Every_documented_name_is_its_own_name_whatever_the_case()
    {
        foreach (var name in MainViewViewModel.ToolWindowNames)
        {
            MainViewViewModel.CanonicalToolWindowName(name).Should().Be(name);
            MainViewViewModel.CanonicalToolWindowName(name.ToUpperInvariant()).Should().Be(name);
        }
    }

    [Theory]
    [InlineData("Object Browser")]
    [InlineData("Window")]
    [InlineData("")]
    public void Anything_else_is_not_a_tool_window(string name)
    {
        MainViewViewModel.CanonicalToolWindowName(name).Should().BeNull();
    }
}
