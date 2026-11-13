using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.VisualDesigner;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Integration.Tests.Views;

public class ToolReactivationFocusTests
{
    [AvaloniaTheory]
    [InlineData("servers", false)]
    [InlineData("diagnostics", false)]
    [InlineData("protocol", false)]
    [InlineData("browser", false)]
    [InlineData("servers", true)]
    [InlineData("diagnostics", true)]
    [InlineData("protocol", true)]
    [InlineData("browser", true)]
    public async Task Reactivation_moves_keyboard_focus_to_the_other_area(string kind, bool automation)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        // Another case can have saved this tool as an open document. Start from the default layout
        // so the first command below exercises a fresh open rather than a restored tab's reactivation.
        root.ResetWindowLayout();
        var window = ToolbarAutomationNameTests.ShowMainView();
        var view = (MainView)window.Content!;
        view.DataTemplates.Add(new ViewLocator());
        view.DataContext = root;
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        project.AddForm(form);
        composition.ProjectManager.AddProject(project);
        try
        {
            composition.EditorService.EditForm(form);
            await Settle();
            var factory = (MainViewViewModel.DockFactory)root.Layout.Factory!;
            var first = factory.DocumentDock!;
            IDockable tool = kind switch
            {
                "servers" => root.LanguageServers,
                "diagnostics" => root.Diagnostics,
                "protocol" => root.ProtocolInspector,
                _ => root.ObjectBrowser,
            };
            Action open = kind switch
            {
                "servers" => () => root.OpenLanguageServersCommand.Execute(null),
                "diagnostics" => () => root.OpenDiagnosticsCommand.Execute(null),
                "protocol" => () => root.OpenProtocolInspectorCommand.Execute(null),
                _ => root.OpenObjectBrowserTool,
            };
            composition.DocumentDockService.AllTabs.Should().NotContain(tool);
            open();
            await Settle();
            if (kind == "servers")
                window.FocusManager!.GetFocusedElement().Should().BeNull("a fresh Language & Debug Servers open keeps #3393's focus policy");
            var split = factory.CreateDocumentDock();
            split.VisibleDockables = factory.CreateList<IDockable>();
            factory.MoveDockable((IDock)tool.Owner!, split, tool, null);
            factory.SplitToDock(first, split, DockOperation.Bottom);
            composition.EditorService.EditForm(form);
            await Settle();
            var designer = view.GetVisualDescendants().OfType<FormEditView>().Single(v => v.IsEffectivelyVisible);
            var source = designer.FindControl<Control>("FormContainer")!;
            source.Focus().Should().BeTrue();

            // activate_document_tab uses this same service entry point.
            if (automation) composition.DocumentDockService.TryActivateAny(t => ReferenceEquals(t, tool)).Should().BeTrue();
            else open();
            await Settle();

            split.ActiveDockable.Should().BeSameAs(tool);
            designer.IsKeyboardFocusWithin.Should().BeFalse();
            var content = view.GetVisualDescendants().OfType<UserControl>()
                .First(c => ReferenceEquals(c.DataContext, tool) && c.IsEffectivelyVisible);
            content.IsKeyboardFocusWithin.Should().BeTrue();
            if (kind == "browser")
            {
                content.Should().BeOfType<ObjectBrowserToolView>();
                window.FocusManager!.GetFocusedElement().Should().BeAssignableTo<TextBox>();
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
