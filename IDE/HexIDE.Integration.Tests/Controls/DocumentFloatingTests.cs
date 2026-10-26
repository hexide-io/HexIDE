using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Bookmarks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Controls;

public class DocumentFloatingTests
{
    private static BaseEditorWindowViewModel CreateDocument(bool designer) => designer
        ? new FormEditViewModel()
        : new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(),
            Substitute.For<HexIDE.Debugging.IBreakpointService>(), Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>());

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Document_tab_menu_offers_no_float(bool designer)
    {
        using var document = CreateDocument(designer);
        document.Title = "Document";
        var factory = new Factory();
        var documents = new DocumentDock
        {
            CanFloat = false,
            VisibleDockables = factory.CreateList<IDockable>(document), ActiveDockable = document,
        };
        var root = new RootDock
        {
            VisibleDockables = factory.CreateList<IDockable>(documents), ActiveDockable = documents,
        };
        factory.InitLayout(root);
        var host = new DockControl { Layout = root };
        var window = new Window { Content = host, Width = 800, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tab = host.GetVisualDescendants().OfType<DocumentTabStripItem>().Single();
            var menu = tab.DocumentContextMenu;
            menu.Should().NotBeNull();
            menu!.Open();
            Dispatcher.UIThread.RunJobs();
            var items = menu.Items.OfType<MenuItem>().ToArray();
            items.Should().NotBeEmpty();
            var floatItems = items.Where(i => i.Header?.ToString()?.Replace("_", "").ToLowerInvariant() is "float" or "float all").ToArray();
            floatItems.Should().HaveCount(2);
            floatItems.Should().OnlyContain(i => !i.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Float_attempts_leave_document_in_tab_list(bool designer)
    {
        using var document = CreateDocument(designer);
        var factory = new Factory();
        var documents = new DocumentDock
        {
            CanFloat = false,
            VisibleDockables = factory.CreateList<IDockable>(document), ActiveDockable = document,
        };
        var root = new RootDock
        {
            VisibleDockables = factory.CreateList<IDockable>(documents), ActiveDockable = documents,
        };
        factory.InitLayout(root);

        factory.FloatDockable(document);
        factory.FloatAllDockables(document);

        documents.VisibleDockables.Should().Contain(document);
        document.Owner.Should().BeSameAs(documents);
        root.Windows.Should().BeNullOrEmpty();
    }
}
