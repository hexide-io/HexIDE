using Avalonia;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Commands;

namespace HexIDE.Tests.ViewModels;

public class ComponentRoleTests
{
    private sealed class StubClass : IComponentClass
    {
        public string Name => "Stub";
        public string VBTypeName => Role == ComponentRole.Form ? "VB.Form" : "Test.Stub";
        public string ClassName => "Stub";
        public bool IsVisual => true;
        public ComponentRole Role { get; init; }
        public bool IsContainer { get; init; }
        public string? DesignerDoubleClickEvent => "Open";
        public IReadOnlyList<PropertyClass> Properties => TextBoxComponentClass.Instance.Properties;
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName => TextBoxComponentClass.Instance.PropertiesByName;
        public IReadOnlyList<EventClass> Events => [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
    }

    private static FormEditViewModel Open(params ComponentInstance[] components)
    {
        var project = TestHelpers.CreateProject();
        var form = new FormDefinition(project, components, "");
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            InBoxRegistry.Create(), Substitute.For<ILocalizationService>());
        return new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    [AvaloniaFact]
    public void Form_role_selects_a_root_without_a_provider_form_class()
    {
        var root = new ComponentInstance(new StubClass { Role = ComponentRole.Form }, "Root1");
        using var designer = Open(root);
        designer.Form.Instance.Should().BeSameAs(root);
        designer.RootDoubleClickProcedure.Should().Be("Stub_Open");
        designer.Form.BaseClassName.Should().Be(designer.RootClassName);
        var resize = new MoveResizeCommand([(designer.Form, new Rect(0, 0, 100, 100), new Rect(0, 0, 120, 100))]);
        resize.Description.Should().Be("Resize Form");
    }

    [AvaloniaFact]
    public void A_control_dropped_inside_a_stub_container_becomes_its_child()
    {
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        var container = new ComponentInstance(new StubClass { IsContainer = true }, "Container1")
            .SetProperty(VBProperties.LeftProperty, 20d).SetProperty(VBProperties.TopProperty, 20d)
            .SetProperty(VBProperties.WidthProperty, 200d).SetProperty(VBProperties.HeightProperty, 150d);
        using var designer = Open(root, container);
        designer.SpawnControlAt(TextBoxComponentClass.Instance, new Rect(40, 40, 60, 20), new Point(40, 40));
        var child = designer.AllComponents.Single(c => c.Instance != root && c.Instance != container);
        child.Instance.Container.Should().BeSameAs(container);
        container.ContainedControls.Should().ContainSingle().Which.Should().BeSameAs(child.Instance);
    }

    [AvaloniaFact]
    public void Line_role_selects_line_geometry_without_a_provider_line_class()
    {
        var root = new ComponentInstance(FormComponentClass.Instance, "Form1");
        var line = new ComponentInstance(new StubClass { Role = ComponentRole.Line }, "Line1");
        using var designer = Open(root, line);
        designer.AllComponents.Single(c => c.Instance == line).IsLine.Should().BeTrue();
    }
}
