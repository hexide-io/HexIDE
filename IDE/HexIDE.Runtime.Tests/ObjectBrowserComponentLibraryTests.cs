using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

public class ObjectBrowserComponentLibraryTests
{
    [Fact]
    public void Every_declared_control_has_a_component_model_or_an_explicit_gap()
    {
        string[] notYetModelled = ["ImageCombo", "Animation"];
        foreach (var library in ControlLibraries.All)
        foreach (var name in library.Controls.Keys)
        {
            var component = library.FindComponentClass(name);
            if (notYetModelled.Contains(name))
                component.Should().BeNull($"{library.Name}.{name} has no component model yet");
            else
                component.Should().NotBeNull($"{library.Name}.{name} needs a component model or an explicit gap");
        }
    }
}
