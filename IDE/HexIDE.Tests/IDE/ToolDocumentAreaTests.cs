using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace HexIDE.Tests.IDE;

public class ToolDocumentAreaTests : CompositionTestBase
{
    [AvaloniaTheory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void ToolOpensInFocusedAreaAndReopensWhereMoved(int kind, bool reopen)
    {
        var root = Composition.Root;
        var service = Composition.DocumentDockService;
        var factory = (MainViewViewModel.DockFactory)typeof(MainViewViewModel)
            .GetField("dockFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(root)!;
        var first = factory.DocumentDock!;
        var anchor = new Document { Title = "Anchor" };
        factory.AddDockable(first, new Document { Title = "Keep first area" });
        factory.AddDockable(first, anchor);
        var split = factory.CreateDocumentDock();
        split.VisibleDockables = factory.CreateList<IDockable>();
        factory.MoveDockable(first, split, anchor, null);
        factory.SplitToDock(first, split, DockOperation.Bottom);
        Action open = kind switch
        {
            0 => root.OpenObjectBrowserTool,
            1 => root.OpenProtocolInspector,
            2 => root.OpenLanguageServers,
            _ => root.OpenTranslationEditor
        };
        IDockable tool = kind switch
        {
            0 => root.ObjectBrowser,
            1 => root.ProtocolInspector,
            2 => root.LanguageServers,
            _ => root.TranslationEditor
        };
        if (reopen)
        {
            factory.SetFocusedDockable(first, first.VisibleDockables![0]);
            open();
            factory.MoveDockable(first, split, tool, null);
            factory.SetFocusedDockable(first, first.VisibleDockables![0]);
        }
        else factory.SetFocusedDockable(split, anchor);

        open();

        service.AllTabs.Count(t => ReferenceEquals(t, tool)).Should().Be(1);
        tool.Owner.Should().BeSameAs(split);
        split.ActiveDockable.Should().BeSameAs(tool);
        service.ActiveTab.Should().BeSameAs(tool);
        factory.CloseDockable(tool);
        service.AllTabs.Should().NotContain(tool);
    }
}
