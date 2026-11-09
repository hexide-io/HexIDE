using Avalonia.Controls;
using HexIDE.Automation;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Forms.Views;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;
using HexIDE.VisualDesigner.Views;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class WindowShortcutFocusTests
{
    // The destinations requested by #3393; no additional VB6 focus ordering is asserted.
    [AvaloniaTheory]
    [InlineData("code")]
    [InlineData("properties")]
    [InlineData("project")]
    [InlineData("immediate")]
    [InlineData("browser")]
    public async Task Shortcut_focuses_the_window_on_first_and_repeated_open(string destination)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var window = ToolbarAutomationNameTests.ShowMainView();
        var view = (MainView)window.Content!;
        view.DataTemplates.Add(new ViewLocator());
        view.DataContext = root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        composition.ProjectManager.AddProject(project);
        var command = destination switch
        {
            "code" => ApplicationCommands.ViewCodeCommand,
            "properties" => ApplicationCommands.OpenPropertiesCommand,
            "project" => ApplicationCommands.OpenProjectExplorerCommand,
            "immediate" => ApplicationCommands.OpenImmediateCommand,
            _ => ApplicationCommands.OpenObjectBrowserCommand,
        };
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                composition.EditorService.EditForm(form);
                await Settle();
                var designer = view.GetVisualDescendants().OfType<FormEditView>().Single(v => v.IsEffectivelyVisible);
                var source = designer.FindControl<Control>("FormContainer")!;
                source.Focus().Should().BeTrue();
                var gesture = command.Gestures.Single();
                var reply = UiAutomationDriver.PressKey(source, gesture.Key.ToString(), gesture.KeyModifiers == KeyModifiers.None ? null : gesture.KeyModifiers.ToString());
                reply.Success.Should().BeTrue(reply.Error);
                await Settle();
                var focused = window.FocusManager!.GetFocusedElement() as Control;
                focused.Should().NotBeNull(destination);
                var ancestors = focused!.GetVisualAncestors().Prepend(focused).Select(c => c.GetType()).ToList();
                switch (destination)
                {
                    case "code":
                        ancestors.Should().Contain(t => typeof(CodeEditorView).IsAssignableFrom(t));
                        ancestors.Should().Contain(t => typeof(TextEditor).IsAssignableFrom(t));
                        break;
                    case "properties":
                        ancestors.Should().Contain(t => typeof(PropertiesToolView).IsAssignableFrom(t));
                        ancestors.Should().Contain(t => typeof(ListBox).IsAssignableFrom(t));
                        break;
                    case "project":
                        ancestors.Should().Contain(t => typeof(ProjectToolView).IsAssignableFrom(t));
                        ancestors.Should().Contain(t => typeof(TreeView).IsAssignableFrom(t));
                        break;
                    case "immediate":
                        ancestors.Should().Contain(t => typeof(ImmediateToolView).IsAssignableFrom(t));
                        ancestors.Should().Contain(t => typeof(TextEditor).IsAssignableFrom(t));
                        break;
                    case "browser":
                        ancestors.Should().Contain(t => typeof(HexIDE.Tools.ObjectBrowser.ObjectBrowserToolView).IsAssignableFrom(t));
                        ancestors.Should().Contain(t => typeof(TextBox).IsAssignableFrom(t));
                        break;
                }
            }
        }
        finally { composition.ProjectManager.UnloadAllProjects(); window.Close(); }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }
}
