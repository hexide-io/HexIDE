using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Controls;

public class PreservedProjectItemTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Preserved_items_render_distinct_status_labels_and_overlays(bool unsupported, bool exists)
    {
        var anchor = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-tree-" + Guid.NewGuid());
        Directory.CreateDirectory(anchor);
        try
        {
            var definition = new ProjectDefinition(VBProjectType.EXE, "P")
            {
                AbsolutePath = System.IO.Path.Combine(anchor, "P.vbp"),
            };
            var line = unsupported ? "UserDocument=Doc; Doc.dob" : "Form=Lost.frm";
            if (exists) File.WriteAllText(System.IO.Path.Combine(anchor, "Doc.dob"), "");
            var tool = new ProjectToolViewModel(Substitute.For<IProjectManager>(), Substitute.For<IEventBus>(),
                Substitute.For<IProjectService>(), Substitute.For<IEditorService>(), Substitute.For<ILocalizationService>());
            using var project = new ProjectViewModel(tool, definition);
            var node = new PreservedItemViewModel(project, line);
            var view = new ProjectToolView();
            view.Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
            {
                Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml"),
            });
            view.Resources["Str.Tool.Project.MissingFile"] = "missing";
            view.Resources["Str.Tool.Project.Unsupported"] = "unsupported";
            var tree = view.GetLogicalDescendants().OfType<TreeView>().Single();
            var template = tree.DataTemplates.SingleOrDefault(t => t.Match(node));
            template.Should().NotBeNull("preserved project items need their own visible tree template");
            tool.SelectedItem = node;
            tool.ViewCodeCommand.CanExecute(null).Should().BeFalse();
            tool.ViewObjectCommand.CanExecute(null).Should().BeFalse();
            var content = template!.Build(node)!;
            content.DataContext = node;
            tree.ItemsSource = new[] { node };
            var window = new Window { Content = view, Width = 600, Height = 200 };
            window.Show();
            try
            {
                // Build under the view so StaticResource and DynamicResource use the same scope as the tree.
                view.Content = content;
                Dispatcher.UIThread.RunJobs();
                var labels = content.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsVisible).Select(t => t.Text).ToArray();
                labels.Should().Contain(line);
                labels.Contains("missing").Should().Be(!exists);
                labels.Contains("unsupported").Should().Be(unsupported);
                content.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Count(p => p.IsVisible)
                    .Should().Be(1 + (exists ? 0 : 1) + (unsupported ? 1 : 0));
            }
            finally { window.Close(); }
        }
        finally { Directory.Delete(anchor, true); }
    }
}
