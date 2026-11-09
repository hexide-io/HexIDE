using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Forms.ViewModels;
using Xunit.Sdk;
using Xunit.v3;

[assembly: AssemblyFixture(typeof(HexIDE.TestGuards.LspSessionLeakGuard))]
[assembly: TestCollectionOrderer(typeof(HexIDE.TestGuards.LeakCheckRunsLast))]

namespace HexIDE.TestGuards;

/// <summary>
/// Records each language-server document session a test starts, so that the run's last test can fail on any still open
/// and name the test that started it (#3476). Compiled into both test projects that open editors.
/// </summary>
/// <remarks>
/// An open session is work outliving its test: it holds a subscription to its client, and a debounced change can still
/// be on its way. #2530's third hang was one of these, posting during a later test's setup, and only a dump could say
/// which test had left it. Dispose the editor, or for a window closed while its project stays open, unload the project.
/// <para>
/// The check is a test, not this fixture's disposal, because xunit's Microsoft Testing Platform runner reports a failure
/// in an assembly fixture's disposal once against every test in the assembly, with the whole message each time. Measured
/// on this suite: nearly 5,000 reports and 65 MB of log, and the run hadn't finished after 45 minutes.
/// </para>
/// </remarks>
public sealed class LspSessionLeakGuard : IDisposable
{
    internal const string RunsLast = "Runs after every other test";

    internal sealed record OpenSession(LspDocumentSession Session, string Test);

    private static readonly ConcurrentQueue<OpenSession> Started = new();

    public LspSessionLeakGuard() => LspDocumentSession.Started += Note;

    // Called on the starting thread, inside Start. It must not throw.
    internal static void Note(LspDocumentSession session) =>
        Started.Enqueue(new OpenSession(session, TestContext.Current.Test?.TestDisplayName ?? "outside a test"));

    internal static IReadOnlyList<OpenSession> StillOpen() => [.. Started.Where(s => !s.Session.IsDisposed)];

    internal static void Check()
    {
        var open = StillOpen();
        open.Count.Should().Be(0,
            "every test must dispose the language-server sessions it opened, or their work outlives it:{0}{1}",
            Environment.NewLine, string.Join(Environment.NewLine, open.Select(s => $"{s.Session.Uri}, opened by {s.Test}")));
    }

    public void Dispose() => LspDocumentSession.Started -= Note;
}

/// <summary>Leaves every collection in xunit's default order, and moves the leak check after them all.</summary>
public sealed class LeakCheckRunsLast : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : notnull, ITestCollection =>
        DefaultTestCollectionOrderer.Instance.OrderTestCollections(testCollections)
            .OrderBy(collection => collection.TestCollectionDisplayName == LspSessionLeakGuard.RunsLast)
            .ToArray();
}

// Every other test has finished and disposed its fixtures by the time this runs, so a session still open has been left.
[Collection(LspSessionLeakGuard.RunsLast)]
public class LspSessionLeakCheck
{
    [Fact]
    public void No_test_left_a_language_server_session_open() => LspSessionLeakGuard.Check();
}
