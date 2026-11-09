using HexIDE.Forms.ViewModels;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ConditionalCodeCompositionTests
{
    // The code window's reader is optional, so leaving it out of the application's composition would still compile, open and
    // edit, and an #If would just never be greyed again (#3559).
    [AvaloniaFact]
    public async Task The_code_window_the_application_builds_can_say_which_lines_an_If_leaves_out()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var window = ToolbarAutomationNameTests.ShowMainView();
        var view = (MainView)window.Content!;
        view.DataTemplates.Add(new ViewLocator());
        view.DataContext = composition.Root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        project.AddModule(module);
        composition.ProjectManager.AddProject(project);
        try
        {
            composition.EditorService.EditCode(module);
            var editor = composition.DocumentDockService.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            var lines = editor.ConditionalCode?.InactiveLines("#If 0 Then\nDebug.Print 1\n#End If\n",
                new ConditionalConstants(ConditionalConstants.NoArguments));
            lines.Should().Equal(new LineRange(2, 2));
        }
        finally
        {
            composition.ProjectManager.UnloadAllProjects();
            window.Close();
        }
    }
}
