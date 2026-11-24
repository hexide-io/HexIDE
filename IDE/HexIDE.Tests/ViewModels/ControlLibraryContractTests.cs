using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class ControlLibraryContractTests
{
    private sealed class Library : IControlLibrary
    {
        public string Name => "ContractControls";
        public string Description => "Contract controls";
        public string File => "ContractControls.ocx";
        public string Guid => "{A1234567-1234-1234-1234-123456789ABC}";
        public string Version => "1.0";
        public string ProjectLine => $"Object={Guid}#1.0#0; {File}";
        public string FormLine => $"Object = \"{Guid}#1.0#0\"; \"{File}\"";
        public string TypeName(string className) => $"{Name}.{className}";
        public bool IsNamedBy(ObjectReference reference) => reference.Names(Guid);
        public bool IsNamedBy(VbReference reference) =>
            Guid.Equals(reference.Guid, StringComparison.OrdinalIgnoreCase) && reference.Version == "1.0";
    }

    private static IComponentRegistry Registry(IControlLibrary library)
    {
        var registry = Substitute.For<IComponentRegistry>();
        registry.Libraries.Returns([library]);
        return registry;
    }

    [Fact]
    public void Components_lists_a_Core_control_library()
    {
        var library = new Library();
        var vm = new ComponentsViewModel(new ProjectDefinition(VBProjectType.EXE, "P"), Registry(library));

        var row = vm.Controls.Should().ContainSingle().Subject;
        row.Library.Should().BeSameAs(library);
        row.Name.Should().Be(library.Description);
        row.Location.Should().Be(library.File);
        row.IsSelected.Should().BeFalse();
    }

    [Fact]
    public void Components_checks_a_Core_library_named_by_the_projects_object_reference()
    {
        var library = new Library();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(library.ProjectLine);
        var vm = new ComponentsViewModel(project, Registry(library));

        vm.Controls.Should().ContainSingle().Subject.IsSelected.Should().BeTrue();
    }

    [AvaloniaFact]
    public void Toolbox_finds_a_referenced_Core_library()
    {
        var library = new Library();
        var registry = Registry(library);
        var component = Substitute.For<IComponentClass>();
        component.Name.Returns("ContractControl");
        registry.LibraryControls.Returns([new LibraryControl(component, library, "ContractControl",
            library.TypeName("ContractControl"), "Geo.Object")]);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(library.ProjectLine);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(), registry,
            Substitute.For<ILocalizationService>());

        toolbox.ShowProject(project);

        var tool = toolbox.Components.Single(c => c.Library is not null);
        tool.Library.Should().BeSameAs(library);
        tool.VBTypeName.Should().Be(library.TypeName("ContractControl"));
    }
}
