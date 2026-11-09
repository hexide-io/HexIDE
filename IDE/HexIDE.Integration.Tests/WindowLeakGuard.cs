using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;

namespace HexIDE.Integration.Tests;

// The final collection checks after other collections' fixtures have been disposed.
public static class WindowLeakGuard
{
    internal static readonly Dictionary<Window, string> Open = new();

    internal static void Track()
    {
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
            Open.TryAdd(window, TestContext.Current.Test?.TestDisplayName ?? "outside a test"));
        Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => Open.Remove(window));
    }

    internal static void Check() => Open.Count.Should().Be(0,
        "every test must close every window it opened, including hidden windows:{0}{1}",
        Environment.NewLine, string.Join(Environment.NewLine, Open.Select(pair =>
            $"'{pair.Key.Title}' ({pair.Key.GetType().Name}), opened by {pair.Value}")));
}
