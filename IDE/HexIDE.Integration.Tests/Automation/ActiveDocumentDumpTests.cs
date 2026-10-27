using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Automation;

public class ActiveDocumentDumpTests
{
    private static IEnumerable<UiNode> Flat(UiNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flat));

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Whole_IDE_dump_keeps_the_open_designers_controls(bool interactiveOnly)
    {
        var composition = new DISetup();
        var window = ToolbarAutomationNameTests.ShowMainView();
        var app = new App();
        app.LoadMarkup();
        var resources = app.Resources;
        app.Resources = new ResourceDictionary();
        window.Resources.MergedDictionaries.Add(resources);
        while (app.Styles.Count > 0)
        {
            var style = app.Styles[0];
            app.Styles.RemoveAt(0);
            window.Styles.Add(style);
        }
        window.DataTemplates.Add(new ViewLocator());
        var main = (MainView)window.Content!;
        main.DataContext = composition.Root;
        var form = new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1");
        // Enough that the whole tree is over the budget, with room left once it is fitted: the active document is kept
        // whole, so every node added to the IDE eats into that room. 26 left about 330 characters, and the designer's own
        // hidden draw preview and rubber band took them (#2231).
        form.UpdateComponents([.. form.Components, .. Enumerable.Range(1, 20).Select(i =>
            new ComponentInstance(CommandButtonComponentClass.Instance, "Command" + i)
                .SetProperty(VBProperties.WidthProperty, 100d)
                .SetProperty(VBProperties.HeightProperty, 30d))]);
        using var designer = new FormEditViewModel(composition.ToolBoxViewModel, composition.EventBus,
            Substitute.For<IProjectService>(), null!, null!, Substitute.For<ISettingsService>(),
            Substitute.For<ILocalizationService>()).Initialize(form);
        try
        {
            composition.DocumentDockService.OpenDocument(designer);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var tree = UiAutomationDriver.Dump(window, "Window", 20, interactiveOnly);
            JsonSerializer.Serialize(tree, ReplyJson.Options).Length.Should().BeGreaterThan(HexIdeTools.TreeCharacterBudget);
            var path = HexIdeTools.ActiveDocumentPath(window, "Window", designer);
            path.Should().NotBeNull();
            var (fitted, note) = HexIdeTools.FitToReply(tree, path);
            var json = JsonSerializer.Serialize(fitted, ReplyJson.Options);
            foreach (var i in Enumerable.Range(1, 20))
                Flat(fitted).Should().Contain(n => n.Name == "Command" + i);
            json.Length.Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
            note.Should().Contain("active document");
        }
        finally
        {
            composition.DocumentDockService.CloseAll();
            window.Close();
        }
    }
}
