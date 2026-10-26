using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class DesignerTabOrderTests
{
    private const string Frm = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           ClientHeight    =   7200
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   9600
           Begin VB.TextBox Text1
              Left = 600
              Top = 600
              TabIndex = 7
              TabStop = 0
           End
           Begin VB.Label Label1
              Left = 1800
              Top = 600
              TabIndex = 4
           End
           Begin VB.Timer Timer1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static (FormEditViewModel vm, FormEditView view, Window window) Designed()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, new NullSink())!;
        var vm = new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, view, window);
    }

    [AvaloniaFact]
    public void Toggle_shows_and_hides_existing_indexes_without_changing_the_form()
    {
        var (vm, view, window) = Designed();
        try
        {
            var toggle = view.FindControl<CheckBox>("ShowTabOrderToggle");
            toggle.Should().NotBeNull();
            var overlay = view.FindControl<ItemsControl>("TabOrderOverlay")!;
            overlay.IsVisible.Should().BeFalse();
            overlay.IsHitTestVisible.Should().BeFalse();
            var index = vm.Components.Single(c => c.Name == "Text1").Instance.GetPropertyOrDefault(VBProperties.TabIndexProperty);
            toggle!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            overlay.IsVisible.Should().BeTrue();
            var badges = overlay.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Classes.Contains("tabIndexBadge") && b.IsVisible).ToArray();
            badges.Should().HaveCount(2);
            var text = badges.Single(b => b.DataContext is ComponentInstanceViewModel { Name: "Text1" })
                .GetVisualDescendants().OfType<TextBlock>().Single();
            var label = badges.Single(b => b.DataContext is ComponentInstanceViewModel { Name: "Label1" })
                .GetVisualDescendants().OfType<TextBlock>().Single();
            label.Text.Should().Be("4");
            label.TextDecorations.Should().BeNullOrEmpty();
            text.Text.Should().Be("7");
            text.TextDecorations.Should().Contain(Avalonia.Media.TextDecorations.Strikethrough[0]);
            toggle.IsChecked = false;
            overlay.IsVisible.Should().BeFalse();
            vm.Components.Single(c => c.Name == "Text1").Instance.GetPropertyOrDefault(VBProperties.TabIndexProperty)
                .Should().Be(index);
            vm.Components.Single(c => c.Name == "Text1").Instance.GetPropertyOrDefault(VBProperties.TabStopProperty)
                .Should().BeFalse();
            vm.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void Badges_follow_property_edits_and_control_positions()
    {
        var (vm, view, window) = Designed();
        try
        {
            var toggle = view.FindControl<CheckBox>("ShowTabOrderToggle");
            toggle.Should().NotBeNull();
            toggle!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            var overlay = view.FindControl<ItemsControl>("TabOrderOverlay")!;
            var badge = overlay.GetVisualDescendants().OfType<Border>()
                .Single(b => b.Classes.Contains("tabIndexBadge") && b.DataContext is ComponentInstanceViewModel { Name: "Text1" });
            var text = badge.GetVisualDescendants().OfType<TextBlock>().Single();
            var component = vm.Components.Single(c => c.Name == "Text1");
            component.Instance.SetProperty(VBProperties.TabIndexProperty, 12);
            component.Instance.SetProperty(VBProperties.TabStopProperty, true);
            component.Left = 80;
            component.Top = 90;
            Dispatcher.UIThread.RunJobs();
            text.Text.Should().Be("12");
            text.TextDecorations.Should().BeNullOrEmpty();
            window.UpdateLayout();
            var position = badge.TranslatePoint(default, overlay);
            position.Should().Be(new Point(80, 90));
        }
        finally { window.Close(); }
    }

}
