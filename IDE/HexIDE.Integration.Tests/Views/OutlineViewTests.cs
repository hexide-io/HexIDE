using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using HexIDE.Tools;
using Range = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

public class OutlineViewTests
{
    [AvaloniaFact]
    public void ViewLocator_RendersNestedSymbolsAndTracksSelection()
    {
        using var vm = new OutlineToolViewModel(Substitute.For<ILocalizationService>(),
            Substitute.For<IDocumentDockService>());
        var range = new Range(new Position(0, 0), new Position(1, 0));
        vm.Roots = OutlineToolViewModel.Build([
            new DocumentSymbol("Module", SymbolKind.Module, range, range, Children: [
                new DocumentSymbol("Procedure", SymbolKind.Function, range, range)])], null);
        var view = new ViewLocator().Build(vm);
        view.Should().BeOfType<OutlineToolView>();
        var window = new Window { Content = view, Width = 320, Height = 300 };
        try
        {
            view!.DataContext = vm;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Select(item => ((OutlineNode)item.DataContext!).Name).Should().Equal("Module", "Procedure");
            vm.SelectedNode = vm.Roots[0].Children[0];
            Dispatcher.UIThread.RunJobs();
            tree.SelectedItem.Should().BeSameAs(vm.SelectedNode);
            tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(item => ReferenceEquals(item.DataContext, vm.SelectedNode)).IsSelected.Should().BeTrue();
        }
        finally { window.Close(); }
    }
}
