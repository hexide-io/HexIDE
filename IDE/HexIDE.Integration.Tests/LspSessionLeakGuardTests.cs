using System;
using System.Linq;
using System.Reflection;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using Xunit.Sdk;
using Xunit.v3;

namespace HexIDE.TestGuards;

public class LspSessionLeakGuardTests
{
    private static LspDocumentSession Session(string uri) =>
        new(Substitute.For<ILspClient>(), new TextDocument("x"), uri, postToUiThread: work => work());

    private static ITestCollection Collection(string name)
    {
        var collection = Substitute.For<ITestCollection>();
        collection.TestCollectionDisplayName.Returns(name);
        collection.UniqueID.Returns(name);
        return collection;
    }

    [Fact]
    public void The_guard_names_an_open_session_and_the_test_that_started_it()
    {
        var session = Session("untitled:guard/Open.bas");
        session.Start();
        try
        {
            Action check = LspSessionLeakGuard.Check;
            check.Should().Throw<Exception>().WithMessage("*untitled:guard/Open.bas*")
                .Which.Message.Should().Contain(TestContext.Current.Test!.TestDisplayName);
        }
        finally
        {
            session.Dispose();
        }
        LspSessionLeakGuard.StillOpen().Should().NotContain(s => s.Session == session, "a disposed session is not a leak");
    }

    [Fact]
    public void A_session_that_was_never_started_is_not_a_leak()
    {
        using var session = Session("untitled:guard/NeverStarted.bas");

        LspSessionLeakGuard.StillOpen().Should().NotContain(s => s.Session == session);
    }

    [Fact]
    public void The_check_runs_after_every_other_collection()
    {
        var check = Collection(LspSessionLeakGuard.RunsLast);
        ITestCollection[] others = [Collection("A"), Collection("B"), Collection("C")];

        var ordered = new LeakCheckRunsLast().OrderTestCollections<ITestCollection>([others[0], check, others[1], others[2]]);

        ordered.Should().HaveCount(4).And.EndWith(check);
    }

    // Without the orderer the check would run somewhere in the middle, and miss whatever ran after it, in silence.
    [Fact]
    public void This_assembly_orders_its_collections_that_way() =>
        typeof(LspSessionLeakGuardTests).Assembly.GetCustomAttributes<TestCollectionOrdererAttribute>().Single()
            .OrdererType.Should().Be<LeakCheckRunsLast>();
}
