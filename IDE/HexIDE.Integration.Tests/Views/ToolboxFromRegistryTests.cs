using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>The toolbox offers what the registry says, with the icons it names, and writes no list of its own (#3601).</summary>
public class ToolboxFromRegistryTests
{
    private static ToolBoxToolViewModel Toolbox(IComponentRegistry registry) =>
        new(Substitute.For<IDocumentDockService>(), registry, Substitute.For<ILocalizationService>());

    [AvaloniaFact]
    public void The_real_registry_gives_the_toolbox_its_controls_in_their_order()
    {
        var toolbox = Toolbox(new ComponentRegistry());

        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse",
            "Picture", "Label", "Text", "Frame", "Command", "Check", "Option", "Combo", "List",
            "HScroll", "VScroll", "Timer", "Shape");
    }

    [AvaloniaFact]
    public void A_registry_s_toolbox_entry_appears_with_its_own_icon_and_a_class_it_does_not_place_does_not()
    {
        var placed = Named("Placed");
        var onlyListed = Named("OnlyListed");
        var registry = Substitute.For<IComponentRegistry>();
        registry.Components.Returns([placed, onlyListed]);
        registry.Toolbox.Returns([new ToolboxEntry(placed, "Geo.Timer")]);

        var toolbox = Toolbox(registry);

        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse", "Placed");
        toolbox.Components[1].IconKey.Should().Be("Geo.Timer");
    }

    [AvaloniaFact]
    public void A_library_s_controls_follow_the_registry_s_whichever_class_comes_last()
    {
        var first = Named("First");
        var last = Named("Last");
        var registry = Substitute.For<IComponentRegistry>();
        registry.Toolbox.Returns([new ToolboxEntry(first, "Geo.Timer"), new ToolboxEntry(last, "Geo.Shape")]);
        var toolbox = Toolbox(registry);
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P");
        project.UnknownPreSectionLines.Add((1, HexIDE.Runtime.Components.InBox.ControlLibraries.MSComctlLib.ProjectLine));

        toolbox.ShowProject(project);

        toolbox.Components.Select(tool => tool.Name).Take(3).Should().Equal("Mouse", "First", "Last");
        toolbox.Components[3].Library.Should().BeSameAs(HexIDE.Runtime.Components.InBox.ControlLibraries.MSComctlLib);
    }

    private static IComponentClass Named(string name)
    {
        var component = Substitute.For<IComponentClass>();
        component.Name.Returns(name);
        return component;
    }
}
