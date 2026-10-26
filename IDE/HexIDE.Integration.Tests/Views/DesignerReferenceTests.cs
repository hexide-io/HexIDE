using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class DesignerReferenceTests
{
    [AvaloniaTheory]
    [InlineData("add")]
    [InlineData("reselect")]
    [InlineData("already selected")]
    public void The_last_selected_control_is_the_reference(string gesture)
    {
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        var vm = new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>())
            .Initialize(new FormDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), FormComponentClass.Instance, "Form1"));
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(20, 20, 60, 30));
        var first = vm.SelectedComponent!;
        vm.SpawnControlAt(CommandButtonComponentClass.Instance, new Rect(100, 100, 90, 50));
        var second = vm.SelectedComponent!;
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var controls = view.FindControl<ControlsContainer>("ControlsContainer")!;
            var a = controls.Items.IndexOf(first);
            var b = controls.Items.IndexOf(second);
            controls.SetSingleSelection(first);
            controls.Selection.Select(b); // The same selection path as add_to_selection and Ctrl+click.
            if (gesture != "add")
            {
                controls.Selection.Select(a);
                if (gesture == "reselect")
                    controls.Selection.Deselect(b);
                controls.Selection.Select(b);
            }

            vm.SelectedComponent.Should().BeSameAs(second);
            ((ControlItem)controls.ContainerFromIndex(b)!).IsPrimary.Should().BeTrue();
            ((ControlItem)controls.ContainerFromIndex(a)!).IsPrimary.Should().BeFalse();
            vm.SelectedComponents.Should().HaveCount(2);
            var top = second.Top;
            vm.AlignTops();
            second.Top.Should().Be(top);
            first.Top.Should().Be(top);
            var width = second.Width;
            vm.MakeSameWidth();
            second.Width.Should().Be(width);
            first.Width.Should().Be(width);

            controls.Selection.Deselect(b);
            vm.SelectedComponent.Should().BeSameAs(first, "a deselected anchor falls back to the remaining selection");
            ((ControlItem)controls.ContainerFromIndex(a)!).IsPrimary.Should().BeTrue();
            controls.Selection.Clear();
            vm.SelectedComponent.Should().BeSameAs(vm.Form);
        }
        finally { window.Close(); }
    }
}
