using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace HexIDE.Integration.Tests;

// A fixture owns only its test's windows, even when other classes are queued on the shared dispatcher.
public abstract class WindowTestFixture : IDisposable
{
    private readonly List<Window> windows = new();
    private readonly IDisposable opening;

    protected WindowTestFixture()
    {
        var testId = TestContext.Current.Test?.UniqueID;
        opening = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (TestContext.Current.Test?.UniqueID == testId)
                windows.Add(window);
        });
    }

    public void Dispose()
    {
        opening.Dispose();
        // Owners open first; close their dialogs first, including windows a test hid.
        foreach (var window in windows.AsEnumerable().Reverse())
            window.Close();
        windows.Clear();
    }
}
