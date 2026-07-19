using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Addins;

namespace HexIDE.Integration.Tests.Views;

public class AddinToolWindowReHostTests
{
    [AvaloniaFact]
    public void AddinToolWindowContent_HostedByTwoViews_DoesNotCrash()
    {
        // Repro of the addin tool-window dock-move crash. Dock's DeferredContentControl re-materialises a
        // tool's view when the window is dragged to a new dock, so the SAME cached content control is
        // briefly hosted by two AddinToolWindowViews at once. A control can have only one visual parent, so
        // before the fix the second view's ContentPresenter.UpdateChild threw
        // InvalidOperationException ("already has a visual parent") during the layout pass and crashed the
        // process. The fix makes AddinToolWindowView detach the control from any prior parent before hosting.
        var content = new TextBlock { Text = "addin content" };
        var vm = new AddinToolWindowViewModel("id", "Title", () => content);

        var view1 = new AddinToolWindowView { DataContext = vm };
        var view2 = new AddinToolWindowView { DataContext = vm };
        var window = new Window
        {
            Content = new StackPanel { Children = { view1, view2 } },
            Width = 400,
            Height = 300,
        };

        window.Show();
        Dispatcher.UIThread.RunJobs(); // pre-fix: throws during the layout pass

        // The single content control ends up hosted in exactly one of the two views — no crash.
        content.GetVisualParent().Should().NotBeNull();
    }
}
