using System.Collections.Generic;
using System.Linq;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(HexIDE.Runtime.Integration.Tests.LeakChecksRunLast))]

namespace HexIDE.Runtime.Integration.Tests;

/// <summary>Runs <see cref="SuiteLeakChecks"/> after every other collection, as HexIDE.Integration.Tests does.</summary>
public sealed class LeakChecksRunLast : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(IReadOnlyCollection<TTestCollection> testCollections)
        where TTestCollection : notnull, ITestCollection =>
        DefaultTestCollectionOrderer.Instance.OrderTestCollections(testCollections)
            .OrderBy(collection => collection.TestCollectionDisplayName == SuiteLeakChecks.RunsLast)
            .ToArray();
}

// Every other test has finished by the time this runs, so a window still open was left open by one of them.
[Collection(RunsLast)]
public class SuiteLeakChecks
{
    internal const string RunsLast = "Runs after every other test";

    [AvaloniaFact]
    public void Every_window_was_closed() => HexIDE.Integration.Tests.WindowLeakGuard.Check();
}
