using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;

[assembly: AssemblyFixture(typeof(HexIDE.Integration.Tests.WindowLeakGuard))]

namespace HexIDE.Integration.Tests;

// Check after fixture disposal, so tests may close their windows in Dispose as well as finally.
public sealed class WindowLeakGuard : IAsyncDisposable
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

    public async ValueTask DisposeAsync() =>
        await HeadlessUnitTestSession.GetOrStartForAssembly(typeof(TestApp).Assembly)
            .Dispatch(Check, CancellationToken.None);
}
