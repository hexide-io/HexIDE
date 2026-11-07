using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

public class DesignerVisibilityTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Visibility_changes_dim_only_the_preview_and_follow_the_theme(bool initiallyVisible)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.CommandButton Command1
                  Width = 1200
                  Height = 450
               End
            End
            Attribute VB_Name = "Form1"
            """, new NullSink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var component = vm.AllComponents.Single(c => c.Name == "Command1");
        component.Instance.SetProperty(VBProperties.VisibleProperty, initiallyVisible);
        var view = new FormEditView { DataContext = vm };
        var dimming = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        view.Resources["DesignerHiddenControlMaskBrush"] = dimming;
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var renderer = view.GetVisualDescendants().OfType<ControlRenderer>().Single();
            var item = renderer.FindAncestorOfType<ControlItem>()!;
            renderer.OpacityMask.Should().BeSameAs(initiallyVisible ? null : dimming);
            component.Instance.SetProperty(VBProperties.VisibleProperty, false);
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeSameAs(dimming);
            var replacement = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            view.Resources["DesignerHiddenControlMaskBrush"] = replacement;
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeSameAs(replacement);
            item.IsVisible.Should().BeTrue();
            item.IsEnabled.Should().BeTrue();
            item.IsHitTestVisible.Should().BeTrue();
            item.Opacity.Should().Be(1);
            vm.SelectedComponent = component;
            component.Width = 100;
            vm.SelectedComponent.Should().BeSameAs(component);
            component.Instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(100);
            component.Instance.SetProperty(VBProperties.VisibleProperty, true);
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeNull();
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_button_inside_a_hidden_frame_dims_and_remains_editable(bool initiallyVisible)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), """
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.Frame Frame1
                  Width = 3000
                  Height = 2000
                  Begin VB.CommandButton Command1
                     Width = 1200
                     Height = 450
                  End
               End
            End
            Attribute VB_Name = "Form1"
            """, new NullSink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var frame = vm.AllComponents.Single(c => c.Name == "Frame1");
        var button = vm.AllComponents.Single(c => c.Name == "Command1");
        frame.Instance.SetProperty(VBProperties.VisibleProperty, initiallyVisible);
        var view = new FormEditView { DataContext = vm };
        var dimming = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        view.Resources["DesignerHiddenControlMaskBrush"] = dimming;
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var renderer = view.GetVisualDescendants().OfType<ControlRenderer>().Single(r => r.Component == button);
            var item = renderer.FindAncestorOfType<ControlItem>()!;
            renderer.OpacityMask.Should().BeSameAs(initiallyVisible ? null : dimming);
            frame.Instance.SetProperty(VBProperties.VisibleProperty, false);
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeSameAs(dimming);
            item.IsVisible.Should().BeTrue();
            item.IsEnabled.Should().BeTrue();
            item.IsHitTestVisible.Should().BeTrue();
            vm.SelectedComponent = button;
            button.Left += 10;
            button.Width = 100;
            vm.SelectedComponent.Should().BeSameAs(button);
            button.Instance.GetPropertyOrDefault(VBProperties.LeftProperty).Should().Be(10);
            button.Instance.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(100);
            frame.Instance.SetProperty(VBProperties.VisibleProperty, true);
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeNull();
            button.Instance.SetProperty(VBProperties.VisibleProperty, false);
            frame.Instance.SetProperty(VBProperties.VisibleProperty, false);
            frame.Instance.SetProperty(VBProperties.VisibleProperty, true);
            Dispatcher.UIThread.RunJobs();
            renderer.OpacityMask.Should().BeSameAs(dimming);
        }
        finally { window.Close(); }
    }

}
