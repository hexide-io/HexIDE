using System;
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
    private const int Budget = HexIdeTools.TreeCharacterBudget;

    private static IEnumerable<UiNode> Flat(UiNode node) =>
        new[] { node }.Concat(node.Children.SelectMany(Flat));

    // What a fitted reply keeps of a branch: every node not below a hidden one, as hidden nodes' children go first.
    private static IEnumerable<UiNode> Showing(UiNode node) =>
        new[] { node }.Concat(node.IsHidden == true ? [] : node.Children.SelectMany(Showing));

    private sealed record Scene(UiNode Tree, string Path, int Size);

    // The whole IDE, as its first dump sees it, with a designer holding this many buttons open in front.
    private static Scene Dumped(int buttons, bool interactiveOnly)
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
        form.UpdateComponents([.. form.Components, .. Enumerable.Range(1, buttons).Select(i =>
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
            var path = HexIdeTools.ActiveDocumentPath(window, "Window", designer);
            path.Should().NotBeNull();
            return new Scene(tree, path!, JsonSerializer.Serialize(tree, ReplyJson.Options).Length);
        }
        finally
        {
            composition.DocumentDockService.CloseAll();
            window.Close();
        }
    }

    private static int SizeOf(UiNode node) => JsonSerializer.Serialize(node, ReplyJson.Options).Length;

    private static UiNode DocumentOf(Scene scene) => Flat(scene.Tree).Single(n => n.Path == scene.Path);

    // Sized to the IDE as it stands, not a fixed count of buttons. The active document is kept whole when a tree is
    // fitted, so a fixed count left room that every node added to the IDE ate into: 26 buttons sat about 330 characters
    // under the budget, and the designer's own hidden draw preview and rubber band took them (#2231). Here the designer
    // is made to fill half the budget, whatever a button costs. The rest of the IDE, cut as far as it must be, has to fit
    // in the other half; when it no longer does, that is the regression this reports.
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Whole_IDE_dump_keeps_the_open_designers_controls(bool interactiveOnly)
    {
        // A button's cost is measured between two counts above zero: the first one also brings what only an occupied
        // designer has, so measuring from an empty one overestimates it. Then a few secant steps close on the target.
        const int target = Budget / 2;
        var one = SizeOf(DocumentOf(Dumped(1, interactiveOnly)));
        var perButton = (SizeOf(DocumentOf(Dumped(11, interactiveOnly))) - one) / 10;
        perButton.Should().BePositive();
        var buttons = Math.Max(1, 1 + (target - one) / perButton);
        var scene = Dumped(buttons, interactiveOnly);
        for (var round = 0; round < 4 && Math.Abs(SizeOf(DocumentOf(scene)) - target) > perButton; round++)
        {
            buttons = Math.Max(1, buttons + (target - SizeOf(DocumentOf(scene))) / perButton);
            scene = Dumped(buttons, interactiveOnly);
        }

        scene.Size.Should().BeGreaterThan(Budget, "the whole IDE is more than a reply takes, so fitting has to cut");
        SizeOf(DocumentOf(scene)).Should().BeInRange(target - perButton, target + perButton,
            $"{buttons} buttons at about {perButton} characters each should fill half the budget");

        var (fitted, note) = HexIdeTools.FitToReply(scene.Tree, scene.Path);

        SizeOf(fitted).Should().BeLessThanOrEqualTo(Budget,
            $"a designer of {buttons} buttons fills half the budget, and the rest of the IDE must fit in the other half");
        note.Should().Contain("active document");
        var kept = Flat(fitted).Single(n => n.Path == scene.Path);
        Showing(kept).Select(n => n.Path).Should().Equal(Showing(DocumentOf(scene)).Select(n => n.Path),
            "the active document is whole, but for what is below a hidden node");
        foreach (var i in Enumerable.Range(1, buttons))
            Flat(kept).Should().Contain(n => n.Name == "Command" + i);
    }
}
