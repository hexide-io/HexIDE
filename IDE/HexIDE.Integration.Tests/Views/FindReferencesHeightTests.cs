using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Location = HexIDE.Lsp.Messages.Location;
using HexIDE.Projects;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class FindReferencesHeightTests
{
    [AvaloniaTheory]
    [InlineData(0.111, 1, 40)]
    [InlineData(0.111, 1.5, 40)]
    [InlineData(0.4, 1, 40)]
    [InlineData(0.4, 1.5, 40)]
    [InlineData(0.111, 1.5, 180)]
    public void ResultsOpeningProvidesSixRowsAndPreservesSufficientHeight(double proportion, double scaling, double documentMinimum)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var results = new FindReferencesToolViewModel(Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), projects, documents, localization);
        var resized = 0;
        results.LayoutResized += () => resized++;
        var factory = new Factory();
        var document = new DocumentDock { Proportion = 0.481 - proportion, MinHeight = documentMinimum,
            IsCollapsable = false, VisibleDockables = [] };
        var immediate = new Tool { Title = "Immediate", CanClose = true, CanPin = false };
        var slot = new ToolDock { Alignment = Alignment.Bottom, Proportion = proportion, VisibleDockables = [immediate], ActiveDockable = immediate };
        var locals = new ToolDock { Proportion = 0.144, VisibleDockables = [new Tool { Title = "Locals" }] };
        var stack = new ToolDock { Proportion = 0.144, VisibleDockables = [new Tool { Title = "Call Stack" }] };
        var watches = new ToolDock { Proportion = 0.231, VisibleDockables = [new Tool { Title = "Watches" }] };
        var centre = new ProportionalDock { Orientation = Orientation.Vertical,
            VisibleDockables = [document, new ProportionalDockSplitter(), slot, new ProportionalDockSplitter(),
                locals, new ProportionalDockSplitter(), stack, new ProportionalDockSplitter(), watches] };
        var root = new RootDock { VisibleDockables = [centre], DefaultDockable = centre, ActiveDockable = centre };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root, Factory = factory };
        host.DataTemplates.Add(new FuncDataTemplate<FindReferencesToolViewModel>((_, _) => new FindReferencesToolView()));
        var window = new Window { Content = host, Width = 1000, Height = 700 };
        window.SetRenderScaling(scaling);
        void Layout()
        {
            var context = ExecutionContext.Capture();
            try
            {
                for (var i = 0; i < 5; i++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
            }
            finally { ExecutionContext.Restore(context!); }
        }
        results.ShowRequested += () =>
        {
            if (!slot.VisibleDockables!.Contains(results)) factory.AddDockable(slot, results);
            slot.ActiveDockable = results;
            factory.SetFocusedDockable(slot, results);
        };
        void Show() => results.ShowLocations(Enumerable.Range(0, 12).Select(i =>
            new Location("untitled:P/Module.bas", new HexIDE.Lsp.Messages.Range(new Position(i, 0), new Position(i, 1)))));
        try
        {
            window.Show();
            Layout();
            var before = slot.Proportion;
            Show();
            Layout();
            var view = host.GetVisualDescendants().OfType<FindReferencesToolView>().Single();
            var list = (ListBox)view.Content!;
            var row = (ListBoxItem)list.ContainerFromIndex(0)!;
            if (documentMinimum == 180)
            {
                document.GetVisibleBounds(out _, out _, out _, out var remainingHeight);
                remainingHeight.Should().BeGreaterThanOrEqualTo(documentMinimum);
                slot.Proportion.Should().BeGreaterThan(before);
                return;
            }
            list.GetVisualDescendants().OfType<ScrollViewer>().Single().Viewport.Height
                .Should().BeGreaterThanOrEqualTo(6 * row.Bounds.Height, $"the list height is {list.Bounds.Height} DIP");
            if (proportion == 0.4)
            {
                slot.Proportion.Should().Be(before);
                resized.Should().Be(0);
            }
            else
            {
                slot.Proportion.Should().BeGreaterThan(before);
                resized.Should().Be(1);
                locals.Proportion.Should().BeApproximately(0.144, 0.0001);
                stack.Proportion.Should().BeApproximately(0.144, 0.0001);
                watches.Proportion.Should().BeApproximately(0.231, 0.0001);
                // Bringing the same pane forward after another user resize must grow it again.
                slot.Proportion = 0.111;
                document.Proportion = 0.370;
                foreach (var panel in host.GetVisualDescendants().OfType<ProportionalStackPanel>())
                    foreach (var child in panel.Children)
                        if (ReferenceEquals(child.DataContext, slot))
                            child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, 0.111);
                        else if (ReferenceEquals(child.DataContext, document))
                            child.SetCurrentValue(ProportionalStackPanel.ProportionProperty, 0.370);
                Layout();
                list.GetVisualDescendants().OfType<ScrollViewer>().Single().Viewport.Height
                    .Should().BeLessThan(6 * row.Bounds.Height);
                Show();
                Layout();
                list.GetVisualDescendants().OfType<ScrollViewer>().Single().Viewport.Height
                    .Should().BeGreaterThanOrEqualTo(6 * row.Bounds.Height);
            }
        }
        finally { window.Close(); }
    }
}
