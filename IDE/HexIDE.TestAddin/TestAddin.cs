using System.Diagnostics;
using Avalonia.Controls;
using HexIDE.Addins;

[assembly: AddinTitle("TestAddin")]
[assembly: AddinDescription("Phase 4 verification add-in")]
[assembly: AddinVersion("4.0.0")]
[assembly: AddinAuthor("HexIDE Team")]

namespace HexIDE.TestAddin;

public sealed class TestAddin : IAddin
{
    private IDisposable? _toolsItem;
    private IDisposable? _addInsItem;
    private IDisposable? _toolWindow;
    private IDisposable? _optionsPage;

    public void Initialize(IHexIdeHost host)
    {
        _toolsItem  = host.Menus.Add("Tools",           "Hello from TestAddin", null, () => { });
        _addInsItem = host.Menus.Add("AddIns/TestAddin", "Do Something",         null, () => { });
        _toolWindow = host.ToolWindows.Register(
            "testAddinPanel",
            "Test Addin Panel",
            null,
            () => new TextBlock { Text = "Hello from TestAddin tool window!" });

        // Contribute a settings control to this add-in's Tools->Options page (Phase 5 seam demo).
        _optionsPage = host.Options.RegisterOptionsPage(() => new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "TestAddin custom settings" },
                new CheckBox { Content = "Enable the frobnicator" },
            },
        });

        host.Events.ProjectLoaded   += e => Debug.WriteLine($"[TestAddin] ProjectLoaded: {e.ProjectName}");
        host.Events.ProjectUnloaded += e => Debug.WriteLine($"[TestAddin] ProjectUnloaded: {e.ProjectName}");
        host.Events.FileOpened      += e => Debug.WriteLine($"[TestAddin] FileOpened: {e.FileName}");
        host.Events.FileClosed      += e => Debug.WriteLine($"[TestAddin] FileClosed: {e.FileName}");
        host.Events.RunStarted      += () => Debug.WriteLine("[TestAddin] RunStarted");
        host.Events.RunStopped      += () => Debug.WriteLine("[TestAddin] RunStopped");
    }

    public void Dispose()
    {
        _toolsItem?.Dispose();
        _addInsItem?.Dispose();
        _toolWindow?.Dispose();
        _optionsPage?.Dispose();
    }
}
