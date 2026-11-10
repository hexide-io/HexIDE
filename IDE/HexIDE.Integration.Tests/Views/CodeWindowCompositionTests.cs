using HexIDE.Forms.ViewModels;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Language;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class CodeWindowCompositionTests
{
    // The code window's readers are optional, so leaving one out of the application's composition would still compile, open
    // and edit, and an #If would never be greyed, or a parenthesis coloured, again (#3559, #3572).
    [AvaloniaFact]
    public async Task The_code_window_the_application_builds_is_given_what_reads_its_text()
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
            var marks = editor.Brackets?.Brackets("(())");
            marks.Should().Equal(new BracketMark(0, 0), new BracketMark(1, 1), new BracketMark(2, 1), new BracketMark(3, 0));
        }
        finally
        {
            composition.ProjectManager.UnloadAllProjects();
            window.Close();
        }
    }
}
