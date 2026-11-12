using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Controls;

public sealed class ProjectGroupExpansionTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-group-expansion-" + Guid.NewGuid().ToString("N"));
    private readonly ILocalizationService strings = Substitute.For<ILocalizationService>();
    private readonly ProjectService service;
    private readonly ProjectToolViewModel explorer;

    public ProjectGroupExpansionTests()
    {
        Directory.CreateDirectory(directory);
        var events = Substitute.For<IEventBus>();
        var editors = Substitute.For<IEditorService>();
        var manager = new ProjectManager(editors, events);
        strings.GetString("Str.Tool.Project.TitleGroup").Returns("Project Group - {0}");
        service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(),
            Substitute.For<IWindowManager>(), events, manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            strings, Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
        explorer = new ProjectToolViewModel(manager, events, service, editors, strings);
    }

    private string WriteProject(string name)
    {
        var path = Path.Join(directory, name + ".vbp");
        File.WriteAllText(path, $"Type=Exe\r\nName=\"{name}\"\r\n");
        return path;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewGroup_ShowsBothProjectItems(bool openGroup)
    {
        var first = WriteProject("First");
        var second = WriteProject("Second");
        var view = new ProjectToolView { DataContext = explorer };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            if (openGroup)
            {
                var path = Path.Join(directory, "Outer.vbg");
                File.WriteAllText(path, "VBGROUP 5.00\r\nProject=First.vbp\r\nProject=Second.vbp\r\nStartupProject=First.vbp\r\n");
                await service.OpenProject(path);
            }
            else
            {
                await service.OpenProject(first);
                await service.OpenProject(second);
            }
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
            var items = tree.GetVisualDescendants().OfType<TreeViewItem>().ToArray();
            items.Single(i => i.DataContext is ProjectGroupViewModel).IsExpanded.Should().BeTrue();
            items.Where(i => i.DataContext is ProjectViewModel)
                .Select(i => ((ProjectViewModel)i.DataContext!).Name).Should().BeEquivalentTo("First", "Second");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LanguageSwitch_KeepsTheUsersCollapsedGroup()
    {
        await service.OpenProject(WriteProject("First"));
        await service.OpenProject(WriteProject("Second"));
        var view = new ProjectToolView { DataContext = explorer };
        var window = new Window { Content = view, Width = 600, Height = 400 };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
            var item = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(i => i.DataContext is ProjectGroupViewModel);
            item.IsExpanded = false;
            Dispatcher.UIThread.RunJobs();
            ((ProjectGroupViewModel)item.DataContext!).IsExpanded.Should().BeFalse();

            strings.LanguageChanged += Raise.Event<Action>();
            Dispatcher.UIThread.RunJobs();

            var rebuilt = tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(i => i.DataContext is ProjectGroupViewModel);
            rebuilt.Should().NotBeSameAs(item);
            rebuilt.IsExpanded.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
