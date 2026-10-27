using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class DesignerContainerSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Modifier_click_in_another_container_preserves_selection_and_reference(bool shift, bool insideFirst, bool multiple)
    {
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        using var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>())
            .Initialize(new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1"));
        vm.SpawnControlAt(FrameComponentClass.Instance, new Rect(130, 20, 200, 150));
        var frame = vm.SelectedComponent!;
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(150, 50, 60, 30), new Point(150, 50));
        var inside = vm.SelectedComponent!;
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(20, 200, 60, 30));
        var outside = vm.SelectedComponent!;
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(240, 100, 60, 30), new Point(240, 100));
        var insidePeer = vm.SelectedComponent!;
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        try
        {
            var controls = view.FindControl<ControlsContainer>("ControlsContainer")!;
            var first = insideFirst ? inside : outside;
            var other = insideFirst ? outside : inside;
            controls.SetSingleSelection(first);
            if (multiple)
            {
                var peer = insideFirst ? insidePeer : frame;
                controls.SetSingleSelection(peer);
                controls.Selection.Select(controls.Items.IndexOf(first));
            }
            var selection = vm.SelectedComponents.ToArray();
            var item = (ControlItem)controls.ContainerFromIndex(controls.Items.IndexOf(other))!;
            var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            var modifier = shift ? KeyModifiers.Shift : Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
            window.MouseDown(point, MouseButton.Left, (RawInputModifiers)modifier);
            window.MouseUp(point, MouseButton.Left, (RawInputModifiers)modifier);
            vm.SelectedComponents.Should().Equal(selection);
            vm.SelectedComponent.Should().BeSameAs(first);
            var lefts = vm.AllComponents.Select(c => c.Left).ToArray();
            var referenceLeft = first.Left;
            vm.AlignLefts();
            first.Left.Should().Be(referenceLeft);
            if (!multiple)
                vm.AllComponents.Select(c => c.Left).Should().Equal(lefts);
            else
                selection.Should().OnlyContain(c => c.Left == first.Left);

            window.UpdateLayout();
            point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            vm.SelectedComponents.Should().Equal(other);
        }
        finally { window.Close(); }
    }
}
