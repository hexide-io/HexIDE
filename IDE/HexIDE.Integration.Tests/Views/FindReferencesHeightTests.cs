using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    [InlineData(0.111, 1, 40, true, 700)]
    [InlineData(0.111, 1.5, 40, true, 700)]
    [InlineData(0.111, 1.5, 120, false, 380)]
    [InlineData(0.111, 1.5, 120, true, 380)]
    [InlineData(0.111, 1.5, 40, false, 700, 18)]
    [InlineData(0.111, 1.5, 40, true, 700, 18)]
    [InlineData(0.111, 1.5, 40, true, 600, 18)]
    [InlineData(0.111, 1.5, 120, false, 380, 18)]
    [InlineData(0.111, 1.5, 120, true, 380, 18)]
    public async Task ResultsOpeningProvidesSixRowsAndPreservesSufficientHeight(double proportion, double scaling, double documentMinimum, bool hierarchy = false, double windowHeight = 700, double fontSize = 0)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var documents = Substitute.For<IDocumentDockService>();
        documents.OpenDocuments.Returns([]);
        var results = new FindReferencesToolViewModel(Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), projects, documents, localization, format: new Vb6ProjectFileFormat());
        var callClient = Substitute.For<ILspClient>();
        callClient.FindCallHierarchyAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(new CallHierarchyResult([], []));
        var calls = new CallHierarchyToolViewModel(callClient, Substitute.For<IEditorService>(), localization);
        Tool tool = hierarchy ? calls : results;
        var resized = 0;
        results.LayoutResized += () => resized++;
        calls.LayoutResized += () => resized++;
        var factory = new Factory();
        var document = new DocumentDock { Proportion = proportion == 0.4 ? 0.2 : 0.481 - proportion, MinHeight = documentMinimum,
            IsCollapsable = false, VisibleDockables = [] };
        var immediate = new Tool { Title = "Immediate", CanClose = true, CanPin = false };
        var slot = new ToolDock { Alignment = Alignment.Bottom, Proportion = proportion, VisibleDockables = [immediate], ActiveDockable = immediate };
        var locals = new ToolDock { MinHeight = 10, Proportion = 0.144, VisibleDockables = [new Tool { Title = "Locals" }] };
        var stack = new ToolDock { MinHeight = 20, Proportion = 0.144, VisibleDockables = [new Tool { Title = "Call Stack" }] };
        var watches = new ToolDock { MinHeight = 30, Proportion = 0.231, VisibleDockables = [new Tool { Title = "Watches" }] };
        var centre = new ProportionalDock { Orientation = Orientation.Vertical,
            VisibleDockables = [document, new ProportionalDockSplitter(), slot, new ProportionalDockSplitter(),
                locals, new ProportionalDockSplitter(), stack, new ProportionalDockSplitter(), watches] };
        var root = new RootDock { VisibleDockables = [centre], DefaultDockable = centre, ActiveDockable = centre };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root, Factory = factory };
        host.DataTemplates.Add(new FuncDataTemplate<FindReferencesToolViewModel>((_, _) => new FindReferencesToolView()));
        var window = new Window { Content = host, Width = 1000, Height = windowHeight };
        if (fontSize > 0) window.FontSize = fontSize;
        window.SetRenderScaling(scaling);
        void Layout()
        {
            var context = ExecutionContext.Capture();
            try
            {
                // Run the posted adjustment and the layout it causes.
                for (var i = 0; i < 10; i++)
                {
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                }
            }
            finally { ExecutionContext.Restore(context!); }
        }
        void Activate()
        {
            if (!slot.VisibleDockables!.Contains(tool)) factory.AddDockable(slot, tool);
            slot.ActiveDockable = tool;
            factory.SetFocusedDockable(slot, tool);
        }
        results.ShowRequested += Activate;
        calls.ShowRequested += Activate;
        host.DataTemplates.Add(new FuncDataTemplate<CallHierarchyToolViewModel>((_, _) => new CallHierarchyToolView()));
        async Task Show()
        {
            if (hierarchy)
            {
                await calls.SearchAsync("untitled:P/Module.bas", new Position(0, 0));
                calls.Roots.Clear();
                for (var i = 0; i < 12; i++) calls.Roots.Add(new CallHierarchyNode("Caller " + i));
            }
            else results.ShowLocations(Enumerable.Range(0, 12).Select(i =>
            new Location("untitled:P/Module.bas", new HexIDE.Lsp.Messages.Range(new Position(i, 0), new Position(i, 1)))));
        }
        try
        {
            window.Show();
            Layout();
            var before = slot.Proportion;
            var siblings = new IDockable[] { document, slot, locals, stack, watches };
            var initialProportions = siblings.ToDictionary(d => d, d => d.Proportion);
            await Show();
            Layout();
            var list = hierarchy
                ? (ItemsControl)host.GetVisualDescendants().OfType<TreeView>().Single()
                : host.GetVisualDescendants().OfType<ListBox>().Single();
            var row = list.ContainerFromIndex(0)!;
            if (documentMinimum == 180)
            {
                document.GetVisibleBounds(out _, out _, out _, out var remainingHeight);
                remainingHeight.Should().BeGreaterThanOrEqualTo(documentMinimum);
                slot.Proportion.Should().BeGreaterThan(before);
                return;
            }
            var viewport = list.GetVisualDescendants().OfType<ScrollViewer>().Single().Viewport.Height;
            document.GetVisibleBounds(out _, out _, out _, out var remainingHeightAfterShow);
            remainingHeightAfterShow.Should().BeGreaterThanOrEqualTo(Math.Max(120, documentMinimum));
            foreach (var sibling in new[] { locals, stack, watches })
                sibling.Proportion.Should().BeApproximately(initialProportions[sibling], 1 / (scaling * windowHeight));
            if (windowHeight == 380)
            {
                remainingHeightAfterShow.Should().BeApproximately(120, 1);
                viewport.Should().BeLessThan(6 * row.Bounds.Height);
                return;
            }
            if (viewport < 6 * row.Bounds.Height)
                remainingHeightAfterShow.Should().BeApproximately(Math.Max(120, documentMinimum), 1);
            if (proportion == 0.4)
            {
                slot.Proportion.Should().Be(before);
                resized.Should().Be(0);
            }
            else
            {
                slot.Proportion.Should().BeGreaterThan(before);
                resized.Should().Be(1);
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
                await Show();
                Layout();
                resized.Should().Be(2);
            }
        }
        finally { window.Close(); }
    }
}
