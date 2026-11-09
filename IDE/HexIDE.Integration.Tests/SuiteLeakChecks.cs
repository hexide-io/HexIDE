using System.Collections.Generic;
using System.Linq;
using Xunit.Sdk;
using Xunit.v3;

[assembly: TestCollectionOrderer(typeof(HexIDE.Integration.Tests.SuiteLeakCheckOrderer))]

namespace HexIDE.Integration.Tests;

[CollectionDefinition]
public sealed class SuiteLeakCheckCollection;

// Ordinary test failures are reported once; assembly-fixture failures are repeated for every test (#3502).
[Collection(typeof(SuiteLeakCheckCollection))]
public class SuiteLeakChecks
{
    [Fact]
    public void Every_language_server_was_stopped() => LanguageServerLeakGuard.Check();

    [AvaloniaFact]
    public void Every_window_was_closed() => WindowLeakGuard.Check();
}

public sealed class SuiteLeakCheckOrderer : ITestCollectionOrderer
{
    public IReadOnlyCollection<TTestCollection> OrderTestCollections<TTestCollection>(
        IReadOnlyCollection<TTestCollection> testCollections) where TTestCollection : ITestCollection =>
        testCollections.OrderBy(collection =>
            collection.TestCollectionClassName == typeof(SuiteLeakCheckCollection).FullName ? 1 : 0).ToArray();
}
