using HexIDE.Automation;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The XAML compiler's record of which methods are bound as a Command, read from HexIDE's own assembly (#836 review).
/// Its names are Avalonia's, so an upgrade that changes them fails here rather than hiding every method.
/// </summary>
public class XamlCommandBindingsTests
{
    [Fact]
    public void The_trampolines_in_HexIDE_s_assembly_are_read()
    {
        XamlCommandBindings.In(typeof(ProjectToolViewModel).Assembly).Should().HaveCountGreaterThan(50)
            .And.Contain("HexIDE.Tools.ProjectToolViewModel+DeleteForm_0");
    }

    [Fact]
    public void The_Project_Explorer_s_Remove_is_reachable()
    {
        UiAutomationDriver.CommandMethods(typeof(ProjectToolViewModel)).Select(m => m.Name).Should().Contain("DeleteForm");
    }

    [Fact]
    public void Plumbing_no_XAML_binds_is_not_reachable()
    {
        // OnInitialized reruns startup; MakeProject skips the guard on the MakeProjectCommand the menu binds.
        UiAutomationDriver.CommandMethods(typeof(MainViewViewModel)).Select(m => m.Name)
            .Should().NotContain(["OnInitialized", "SaveLayout", "MakeProject"]).And.Contain("SaveProjectAs");
    }

    [Fact]
    public void A_method_bound_with_a_CommandParameter_is_known_by_its_arity()
    {
        XamlCommandBindings.Arity(typeof(MainViewViewModel), "AddProject").Should().Be(1);
        XamlCommandBindings.Arity(typeof(MainViewViewModel), "OnInitialized").Should().BeNull();
    }
}
