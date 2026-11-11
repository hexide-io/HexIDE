using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HexIDE.Integration.Tests;

// The check behind each project's WindowLifetimeTests: a WindowTestFixture, disposed after one of its tests, has
// closed every window that test opened. A theory can name only the types its own project compiles, so each project
// lists its own fixtures and both call this.
internal static class WindowLifetime
{
    public static async Task Disposing_a_test_closes_every_window_it_opened(Type type, string method)
    {
        var opened = new List<Window>();
        var open = new HashSet<Window>();
        using var opening = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            opened.Add(window);
            open.Add(window);
        });
        using var closing = Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => open.Remove(window));
        try
        {
            var tests = Activator.CreateInstance(type)!;
            try
            {
                if (type.GetMethod(method)!.Invoke(tests, null) is Task task)
                    await task;
            }
            finally { (tests as IDisposable)?.Dispose(); }
            Dispatcher.UIThread.RunJobs();

            opened.Should().NotBeEmpty();
            open.Should().BeEmpty("{0}.{1} must close its windows when disposed", type.Name, method);
        }
        finally
        {
            foreach (var window in opened.AsEnumerable().Reverse())
                window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
