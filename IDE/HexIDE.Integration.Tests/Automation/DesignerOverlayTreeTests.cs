using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Automation;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// The designer's draw preview, its rubber band and a selected control's grab handles are in the control view (#2231).
/// The dump folded the first three away as unnamed wrappers, and the handles sit in an adorner layer far from the control
/// they belong to, so a resize had to guess where to press and the preview's bounds could not be read.
/// </summary>
/// <remarks>
/// The handles themselves come from the adorner's template, in a dictionary this harness cannot load (see
/// DesignerClientSizeTests), so here the adorner is drawn without them. Their ids and a drag on one are checked live.
/// </remarks>
public class DesignerOverlayTreeTests
{
    private sealed record Designer(Window Window, FormEditView View, FormEditViewModel Model) : System.IDisposable
    {
        public void Dispose()
        {
            Window.Close();
            Model.Dispose();
        }
    }

    private static Designer Shown()
    {
        var toolbox = new ToolBoxToolViewModel(
            Substitute.For<IDocumentDockService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ILocalizationService>());
        toolbox.SelectedComponent = toolbox.Components.First(c => c.BaseClass == CommandButtonComponentClass.Instance);
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(),
                Substitute.For<IProjectService>(), null!, null!, Substitute.For<ISettingsService>(),
                Substitute.For<ILocalizationService>())
            .Initialize(new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1"));
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Settle(window);
        return new Designer(window, view, vm);
    }

    private static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static IEnumerable<UiNode> Flatten(UiNode node) => new[] { node }.Concat(node.Children.SelectMany(Flatten));

    private static UiNode[] Dumped(Window window) =>
        [.. Flatten(UiAutomationDriver.Dump(window, "Window", maxDepth: 60, interactiveOnly: false))];

    [AvaloniaFact]
    public void The_draw_preview_is_in_the_tree_and_can_be_read_while_the_button_is_down()
    {
        using var designer = Shown();
        var nodes = Dumped(designer.Window);
        var canvas = nodes.Single(n => n.AutomationId == "NewControlSpawner");
        var phantom = nodes.Single(n => n.AutomationId == "NewControlSpawnerPhantom");
        phantom.Path.Should().StartWith(canvas.Path + "/");
        phantom.IsHidden.Should().BeTrue();
        nodes.Should().ContainSingle(n => n.AutomationId == "RubberBandRect");

        // The press and the held moves, without the release: what a held drag will leave (#2231's second half).
        var spawner = designer.View.FindControl<Canvas>("NewControlSpawner")!;
        var pressed = UiAutomationDriver.Interact(spawner, "click", "20,20 to 100,60");
        pressed.Success.Should().BeTrue(pressed.Error);
        foreach (var move in pressed.HeldMoves!)
        {
            move();
            Dispatcher.UIThread.RunJobs();
        }
        Settle(designer.Window);
        try
        {
            var (resolved, error) = UiAutomationDriver.Resolve(designer.Window, phantom.Path);
            error.Should().BeNull();
            UiAutomationDriver.Inspect(resolved!, phantom.Path, designer.Window).IsHidden.Should().NotBe(true);
            resolved!.Bounds.Width.Should().BeGreaterThan(0);
            resolved.Bounds.Height.Should().BeGreaterThan(0);
        }
        finally { pressed.Release!(); }
    }

    [AvaloniaFact]
    public async Task A_selected_control_s_resize_adorner_is_under_the_control_and_its_path_round_trips()
    {
        using var designer = Shown();
        var spawner = designer.View.FindControl<Canvas>("NewControlSpawner")!;
        var drawn = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(spawner, "click", "20,20 to 100,60"));
        drawn.Success.Should().BeTrue(drawn.Error);
        Settle(designer.Window);

        var nodes = Dumped(designer.Window);
        var item = nodes.Single(n => n.ControlType == "ListItem" && n.Name == "Command1");
        var adorner = nodes.Should().ContainSingle(n => n.AutomationId == "ResizeAdorner").Subject;
        adorner.Path.Should().StartWith(item.Path + "/").And.EndWith("[ResizeAdorner]");
        adorner.ClassName.Should().Be("ResizeAdorner");

        var (resolved, error) = UiAutomationDriver.Resolve(designer.Window, adorner.Path);
        error.Should().BeNull();
        resolved.Should().BeSameAs(AdornerLayer.GetAdorner(UiAutomationDriver.Resolve(designer.Window, item.Path).control!));
        UiAutomationDriver.PathWithin(designer.Window, "Window", resolved!).Should().Be(adorner.Path);
    }

    // The graft is general: an adorner with no AutomationId folds away as any wrapper does, so a focus adorner or Dock's
    // drop targets add nothing, and one with an id appears under the element it adorns and nowhere else.
    [AvaloniaFact]
    public void An_adorner_is_grafted_under_the_element_it_adorns_only_when_it_has_an_id()
    {
        var adorned = new Border { Width = 100, Height = 40 };
        Avalonia.Automation.AutomationProperties.SetAutomationId(adorned, "Adorned");
        var marked = new Button { Content = "Grip" };
        Avalonia.Automation.AutomationProperties.SetAutomationId(marked, "Grip");
        var window = new Window { Width = 300, Height = 200, Content = new StackPanel { Children = { adorned } } };
        window.Show();
        try
        {
            Settle(window);
            AdornerLayer.SetAdorner(adorned, new Panel { Children = { new Border() } });
            Settle(window);
            Dumped(window).Where(n => n.Path.Contains("Adorned")).Should().ContainSingle();

            AdornerLayer.SetAdorner(adorned, new Panel { Children = { marked } });
            Settle(window);
            var grip = Dumped(window).Should().ContainSingle(n => n.AutomationId == "Grip").Subject;
            grip.Path.Should().EndWith("[Adorned]/Button[Grip]");
            UiAutomationDriver.Resolve(window, grip.Path).control.Should().BeSameAs(marked);
            UiAutomationDriver.PathWithin(window, "Window", marked).Should().Be(grip.Path);
        }
        finally { window.Close(); }
    }
}
