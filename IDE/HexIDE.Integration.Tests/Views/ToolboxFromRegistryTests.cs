using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>The toolbox offers what the registry says, with the icons it names, and writes no list of its own (#3601).</summary>
public class ToolboxFromRegistryTests
{
    private static readonly RuntimeLibrary StandIn = Library("Stand", "{11111111-2222-3333-4444-555555555555}", "STAND.OCX");

    private static ToolBoxToolViewModel Toolbox(IComponentRegistry registry) =>
        new(Substitute.For<IDocumentDockService>(), registry, Substitute.For<ILocalizationService>());

    [AvaloniaFact]
    public void The_real_registry_gives_the_toolbox_its_controls_in_their_order()
    {
        var toolbox = Toolbox(InBoxRegistry.Create());

        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse",
            "Picture", "Label", "Text", "Frame", "Command", "Check", "Option", "Combo", "List",
            "HScroll", "VScroll", "Timer", "Shape", "Image");
        toolbox.Components.Skip(1).Select(tool => tool.IconKey).Should().Equal(
            "Geo.Picture", "Geo.Label", "Geo.Textbox", "Geo.Groupbox", "Geo.Button", "Geo.Checkbox", "Geo.Radio", "Geo.Combo", "Geo.Listbox",
            "Geo.Hscroll", "Geo.Vscroll", "Geo.Timer", "Geo.Shape", "Geo.Image");
    }

    // The IDE builds the toolbox before the Basic provider registers, so the provider's items arrive as an add-in's do, and
    // the toolbox ends as it always was (#5594).
    [AvaloniaFact]
    public void A_toolbox_built_before_the_provider_registers_ends_in_its_order_with_the_libraries_after()
    {
        var registry = new ComponentRegistry();
        var toolbox = Toolbox(registry);
        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse");

        InBoxRegistry.Fill(registry);
        toolbox.ShowProject(Referencing(ControlLibraries.MSComctlLib));

        string[] inBox = ["Mouse", "Picture", "Label", "Text", "Frame", "Command", "Check", "Option", "Combo", "List",
            "HScroll", "VScroll", "Timer", "Shape", "Image"];
        toolbox.Components.Take(inBox.Length).Select(tool => tool.Name).Should().Equal(inBox);
        toolbox.Components.Skip(1).Take(inBox.Length - 1).Select(tool => tool.IconKey).Should().Equal(
            BasicProvider.Toolbox.Select(item => item.IconKey));
        toolbox.Components.Skip(inBox.Length).Should().NotBeEmpty()
            .And.OnlyContain(tool => ReferenceEquals(tool.Library, ControlLibraries.MSComctlLib));
    }

    [AvaloniaFact]
    public void An_add_in_s_item_joins_after_the_registered_items_and_before_the_libraries_and_leaves_with_its_class()
    {
        var registry = InBoxRegistry.Create();
        var toolbox = Toolbox(registry);
        toolbox.ShowProject(Referencing(ControlLibraries.MSComctlLib));
        var before = toolbox.Components.Select(tool => tool.Name).ToList();
        var owner = new HexIDE.Addins.AddinOrigin("probe", "Probe");
        var gauge = Named("Gauge");
        gauge.VBTypeName.Returns("Probe.Gauge");

        var registration = registry.Register(gauge, owner);
        registry.RegisterToolboxItem(gauge, null, owner);

        toolbox.Components.Select(tool => tool.Name).Should().Equal([.. before.Take(15), "Gauge", .. before.Skip(15)]);
        toolbox.Components[15].IconKey.Should().Be("Geo.Object");
        toolbox.SelectedComponent = toolbox.Components[15];

        registration.Dispose();

        toolbox.Components.Select(tool => tool.Name).Should().Equal(before);
        toolbox.IsCursorSelected.Should().BeTrue();
        toolbox.ShowProject(Referencing(ControlLibraries.MSComctlLib));
        toolbox.Components.Select(tool => tool.Name).Should().Equal(before);
    }

    [AvaloniaFact]
    public void Every_registered_control_except_the_form_is_in_the_toolbox()
    {
        var registry = InBoxRegistry.Create();
        var toolbox = Toolbox(registry);
        toolbox.Components.Skip(1).Select(tool => tool.BaseClass).Should().BeEquivalentTo(
            registry.Components.Where(component => component != FormComponentClass.Instance));
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
    public void A_registry_s_library_offers_its_controls_after_the_registry_s_whichever_class_comes_last()
    {
        var first = Named("First");
        var last = Named("Last");
        var thing = Named("Thing");
        var registry = Substitute.For<IComponentRegistry>();
        registry.Toolbox.Returns([new ToolboxEntry(first, "Geo.Timer"), new ToolboxEntry(last, "Geo.Shape")]);
        registry.Libraries.Returns([StandIn]);
        registry.LibraryControls.Returns([new LibraryControl(thing, StandIn, "Thing", "Stand.Thing", "Geo.Label")]);
        var toolbox = Toolbox(registry);

        toolbox.ShowProject(Referencing(StandIn));

        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse", "First", "Last", "Thing");
        toolbox.Components[3].Library.Should().BeSameAs(StandIn);
        toolbox.Components[3].VBTypeName.Should().Be("Stand.Thing");
        toolbox.Components[3].IconKey.Should().Be("Geo.Label");
    }

    [AvaloniaFact]
    public void A_library_the_registry_does_not_list_offers_the_toolbox_nothing()
    {
        var thing = Named("Thing");
        var registry = Substitute.For<IComponentRegistry>();
        registry.Libraries.Returns([StandIn]);
        registry.LibraryControls.Returns([new LibraryControl(thing, StandIn, "Thing", "Stand.Thing", "Geo.Label")]);
        var toolbox = Toolbox(registry);

        toolbox.ShowProject(Referencing(ControlLibraries.MSComctlLib));

        toolbox.Components.Select(tool => tool.Name).Should().Equal("Mouse");
    }

    [AvaloniaFact]
    public void A_control_is_found_by_its_name_or_a_type_name_with_every_library_that_offers_it()
    {
        var thing = Named("Thing");
        var other = Named("Other");
        var second = Library("Second", "{66666666-7777-8888-9999-000000000000}", "SECOND.OCX");
        var registry = Substitute.For<IComponentRegistry>();
        registry.LibraryControls.Returns([
            new LibraryControl(thing, StandIn, "Thing", "Stand.Thing", "Geo.Label"),
            new LibraryControl(other, StandIn, "Other", "Stand.Other", "Geo.Label"),
            new LibraryControl(thing, second, "Thing", "Second.Thing", "Geo.Label")]);
        var toolbox = Toolbox(registry);

        toolbox.LibrariesOffering("thing").Select(c => c.Library.File).Should().Equal("STAND.OCX", "SECOND.OCX");
        toolbox.LibrariesOffering("Second.Thing").Select(c => c.Library.File).Should().Equal("STAND.OCX", "SECOND.OCX");
        toolbox.LibrariesOffering("Stand.Other").Select(c => c.Library.File).Should().Equal("STAND.OCX");
        toolbox.LibrariesOffering("Nothing").Should().BeEmpty();
    }

    [AvaloniaFact]
    public void The_real_registry_says_where_a_control_comes_from()
    {
        var toolbox = Toolbox(InBoxRegistry.Create());

        toolbox.LibrariesOffering("ProgressBar").Select(c => c.Library.File).Should().Equal("MSCOMCTL.OCX", "COMCTL32.OCX");
        toolbox.LibrariesOffering("ComctlLib.ProgressBar").Select(c => c.Library.File).Should().Equal("MSCOMCTL.OCX", "COMCTL32.OCX");
        toolbox.LibrariesOffering("ListView").Select(c => c.Library.File).Should().Equal("MSCOMCTL.OCX");
        toolbox.LibrariesOffering("ProgressBar")[0].ControlName.Should().Be("ProgressBar");
    }

    private static ProjectDefinition Referencing(RuntimeLibrary library)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.UnknownPreSectionLines.Add((1, library.ProjectLine));
        return project;
    }

    private static RuntimeLibrary Library(string name, string guid, string file) =>
        new(name, "A stand-in library", guid, "1.0", file, 1,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["Thing"] = 1 }, null);

    private static IComponentClass Named(string name)
    {
        var component = Substitute.For<IComponentClass>();
        component.Name.Returns(name);
        return component;
    }
}
