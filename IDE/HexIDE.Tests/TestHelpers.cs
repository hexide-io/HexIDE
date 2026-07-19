using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests;

/// <summary>
/// Factory methods for domain objects used across ViewModel tests.
/// </summary>
public static class TestHelpers
{
    public static ProjectDefinition CreateProject(string name = "TestProject")
    {
        return new ProjectDefinition(VBProjectType.EXE, name);
    }

    public static ProjectDefinition CreateProjectWithForm(
        string projectName = "TestProject",
        string formName = "Form1")
    {
        var project = new ProjectDefinition(VBProjectType.EXE, projectName);
        var form = new FormDefinition(project, FormComponentClass.Instance, formName);
        project.AddForm(form);
        return project;
    }

    public static ProjectDefinition CreateProjectWithModule(
        string projectName = "TestProject",
        string moduleName = "Module1",
        ModuleKind kind = ModuleKind.StandardModule)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, projectName);
        var module = new ModuleDefinition(project, moduleName, kind);
        project.AddModule(module);
        return project;
    }

    public static FormDefinition CreateForm(
        ProjectDefinition? owner = null,
        string name = "Form1")
    {
        owner ??= CreateProject();
        return new FormDefinition(owner, FormComponentClass.Instance, name);
    }

    public static ModuleDefinition CreateModule(
        ProjectDefinition? owner = null,
        string name = "Module1",
        ModuleKind kind = ModuleKind.StandardModule)
    {
        owner ??= CreateProject();
        return new ModuleDefinition(owner, name, kind);
    }
}
