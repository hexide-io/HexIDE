using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Dock.Avalonia.Controls;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
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
    public async Task ChoosingControls_ActivatesDesignerSelectsControlAndReusesDocument()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        var root = composition.Root;
        var dock = composition.DocumentDockService;
        var localization = new LocalizationService();
        localization.Apply("en");
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        var command = new ComponentInstance(CommandButtonComponentClass.Instance, "Command1");
        var higher = new ComponentInstance(OptionButtonComponentClass.Instance, "Option1")
            .SetProperty(VBProperties.DesignIndexProperty, "2");
        var lower = new ComponentInstance(OptionButtonComponentClass.Instance, "Option1")
            .SetProperty(VBProperties.DesignIndexProperty, "0");
        form.UpdateComponents([..form.Components, command, higher, lower]);
        form.UpdateCode("Sub Run()\nEnd Sub\n");
        var manager = Substitute.For<IProjectManager>();
        manager.LoadedProjects.Returns([project]);
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var bus = new EventBus();
        EditorService editors = null!;
        CodeEditorViewModel NewEditor() => new(Substitute.For<IWindowManager>(), editors,
            Substitute.For<IProjectService>(), bus, lsp, settings, Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(), localization);
        var created = 0;
        FormEditViewModel NewDesigner()
        {
            created++;
            return new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(), editors,
                Substitute.For<IWindowManager>(), settings, localization);
        }
        editors = new EditorService(Substitute.For<HexIDE.IDE.IWindowManager>(), dock, NewEditor, () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), NewDesigner, () => manager, localization);
        using var outline = new OutlineToolViewModel(localization, dock);
        var view = new OutlineToolView { DataContext = outline };
        var host = new DockControl { Layout = root.Layout };
        host.DataTemplates.Add(new ViewLocator());
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*") };
        Grid.SetColumn(host, 1);
        grid.Children.Add(view);
        grid.Children.Add(host);
        var window = new Window { Content = grid, Width = 1000, Height = 700 };
        var range = new Range(new Position(0, 0), new Position(0, 0));
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([
            new DocumentSymbol("Command1", SymbolKind.Property, range, range),
            new DocumentSymbol("option1", SymbolKind.Property, range, range)]);
        try
        {
            window.Show();
            editors.EditCode(form);
            var code = dock.OpenDocuments.OfType<CodeEditorViewModel>().Single();
            code.CaretOffset = 5;
            await code.RefreshSymbolsAsync();
            await Settle();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            outline.SelectedNode = outline.Roots[0];
            await Settle();
            tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(item => ReferenceEquals(item.DataContext, outline.SelectedNode)).Focus().Should().BeTrue();

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Settle();

            var designer = dock.OpenDocuments.OfType<FormEditViewModel>().Should().ContainSingle().Which;
            dock.ActiveDocument.Should().BeSameAs(designer);
            dock.ActiveTab.Should().BeSameAs(designer);
            designer.SelectedComponent!.Instance.Should().BeSameAs(command);
            root.Properties.SelectedComponentProxy.Should().BeSameAs(designer.SelectedComponent);
            code.CaretOffset.Should().Be(5);

            editors.EditCode(form);
            await code.RefreshSymbolsAsync();
            await Settle();
            outline.SelectedNode = outline.Roots[1];
            await Settle();
            tree.GetVisualDescendants().OfType<TreeViewItem>()
                .Single(item => ReferenceEquals(item.DataContext, outline.SelectedNode)).Focus().Should().BeTrue();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Settle();

            dock.OpenDocuments.OfType<FormEditViewModel>().Should().ContainSingle().Which.Should().BeSameAs(designer);
            created.Should().Be(1);
            dock.ActiveDocument.Should().BeSameAs(designer);
            dock.ActiveTab.Should().BeSameAs(designer);
            designer.SelectedComponent!.Instance.Should().BeSameAs(lower);
            root.Properties.SelectedComponentProxy.Should().BeSameAs(designer.SelectedComponent);
            code.CaretOffset.Should().Be(5);
        }
        finally
        {
            dock.CloseAll();
            window.Close();
        }
    }

    private static async Task Settle()
    {
        for (var i = 0; i < 3; i++)
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

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
