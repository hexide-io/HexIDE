using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Automation;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class DesignerModifiedMarkerTests
{
    [AvaloniaFact]
    public async Task Delete_from_a_focused_child_shows_the_docks_modified_marker()
    {
        var dir = Path.Join(Path.GetTempPath(), "hexide-marker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var bus = new EventBus();
        var service = new ProjectService(null!, Substitute.For<IWindowManager>(), bus,
            Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(),
            new FileBaselineStore(), Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1")
        {
            AbsolutePath = Path.Join(dir, "Form1.frm")
        };
        form.UpdateComponents([..form.Components,
            new ComponentInstance(CommandButtonComponentClass.Instance, "Command1")
                .SetProperty(VBProperties.WidthProperty, 100d)
                .SetProperty(VBProperties.HeightProperty, 30d)]);
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        using var vm = new FormEditViewModel(toolbox, bus, service, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var dock = new DocumentDock
        {
            VisibleDockables = new ObservableCollection<IDockable> { vm }, ActiveDockable = vm
        };
        var tabs = new DocumentControl { DataContext = dock };
        var window = new Window
        {
            Width = 1000, Height = 800, Content = new StackPanel { Children = { tabs, view } }
        };
        try
        {
            (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var marker = tabs.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "*");
            marker.IsEffectivelyVisible.Should().BeFalse();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single();
            var click = await UiAutomationDriver.FinishAsync(UiAutomationDriver.Interact(item, "click", null));
            click.Success.Should().BeTrue(click.Error);
            vm.SelectedComponent.Should().Be(vm.Components.Single());
            item.Focus().Should().BeTrue();
            UiAutomationDriver.PressKey(item, "Delete", null).Success.Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            vm.Components.Should().BeEmpty("the key bubbled from the focused child and deleted the selection");
            marker.IsEffectivelyVisible.Should().BeTrue("Dock must display the designer's dirty state");
            vm.UndoStack.Undo();
            Dispatcher.UIThread.RunJobs();
            vm.Components.Should().ContainSingle();
            marker.IsEffectivelyVisible.Should().BeFalse();
            vm.UndoStack.Redo();
            Dispatcher.UIThread.RunJobs();
            marker.IsEffectivelyVisible.Should().BeTrue();
            (await service.SaveForm(form, saveAs: false, interactive: false)).Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            marker.IsEffectivelyVisible.Should().BeFalse();
        }
        finally
        {
            window.Close();
            Directory.Delete(dir, recursive: true);
        }
    }
}
