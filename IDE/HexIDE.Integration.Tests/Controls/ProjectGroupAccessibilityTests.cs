using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Controls;

public class ProjectGroupAccessibilityTests
{
    [AvaloniaFact]
    public void GroupTreeItem_HasTheLocalizedDisplayNameAndFollowsRename()
    {
        var group = new ProjectGroupDefinition("Group1");
        var manager = Substitute.For<IProjectManager>();
        manager.CurrentGroup.Returns(group);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Tool.Project.TitleGroup").Returns("Projektgruppe - {0}");
        var tool = new ProjectToolViewModel(manager, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), localization);
        var view = new ProjectToolView { DataContext = tool };
        var window = new Window { Content = view, Width = 600, Height = 200 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
            var item = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(i => i.DataContext is ProjectGroupViewModel);
            AutomationProperties.GetName(item).Should().Be("Projektgruppe - Group1");

            group.Name = "Renamed";
            Dispatcher.UIThread.RunJobs();

            AutomationProperties.GetName(item).Should().Be("Projektgruppe - Renamed");
        }
        finally { window.Close(); }
    }
}
