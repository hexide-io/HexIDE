using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Controls;

// Runs App.Initialize, which pins the process-wide culture defaults (#2500).
[Collection(typeof(ProcessCultureCollection))]
public class DockOpacityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_pane_switches_keep_documents_and_tools_opaque(bool switchTools)
    {
        var duration = DeferredContentPresentationSettings.RevealDuration;
        var culture = CultureInfo.DefaultThreadCurrentCulture;
        var uiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        var locale = VbLocale.Default;
        var window = new Window { Width = 800, Height = 600 };
        try
        {
            // Exercise the production startup setting, rather than copying it into TestApp.
            new App().Initialize();
            var factory = new Factory();
            var first = new Document { Id = "first", Title = "First" };
            var second = new Document { Id = "second", Title = "Second" };
            var tool = new Tool { Id = "tool", Title = "Tool" };
            var otherTool = new Tool { Id = "otherTool", Title = "Other tool" };
            var documents = new DocumentDock
            {
                VisibleDockables = factory.CreateList<IDockable>(first, second), ActiveDockable = first,
            };
            var tools = new ToolDock
            {
                VisibleDockables = factory.CreateList<IDockable>(tool, otherTool), ActiveDockable = tool,
            };
            var split = new ProportionalDock
            {
                VisibleDockables = factory.CreateList<IDockable>(documents, tools), ActiveDockable = documents,
            };
            var root = new RootDock
            {
                VisibleDockables = factory.CreateList<IDockable>(split), ActiveDockable = split,
            };
            factory.InitLayout(root);
            var host = new DockControl { Layout = root };
            host.DataTemplates.Add(new FuncDataTemplate<Document>((d, _) => new TextBlock { Text = d?.Title }));
            host.DataTemplates.Add(new FuncDataTemplate<Tool>((t, _) => new TextBlock { Text = t?.Title }));
            window.Content = host;
            window.Show();
            await Settle();

            for (var i = 0; i < 2; i++)
            {
                if (switchTools)
                    tools.ActiveDockable = i == 0 ? otherTool : tool;
                else
                    documents.ActiveDockable = i == 0 ? second : first;
                await Settle();
                AssertOpaque(host, typeof(DocumentControl));
                AssertOpaque(host, typeof(ToolControl));
            }
        }
        finally
        {
            window.Close();
            DeferredContentPresentationSettings.RevealDuration = duration;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
            VbLocale.Default = locale;
        }
    }

    private static async Task Settle()
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertOpaque(Control host, Type paneType)
    {
        var panes = host.GetVisualDescendants().OfType<Control>()
            .Where(c => c.GetType() == paneType && c.IsEffectivelyVisible).ToArray();
        panes.Should().NotBeEmpty();
        foreach (var pane in panes)
        {
            var reveals = pane.GetVisualDescendants().Prepend(pane)
                .Where(v => v is DeferredContentControl or DeferredContentPresenter).ToArray();
            reveals.OfType<DeferredContentControl>().Should().NotBeEmpty();
            reveals.OfType<DeferredContentPresenter>().Should().NotBeEmpty();
            foreach (var reveal in reveals)
                reveal.Opacity.Should().Be(1, $"{paneType.Name}'s {reveal.GetType().Name} must remain opaque after each switch");
        }
    }
}
