using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Interpreter diagnostics requested by #4375; these force stale references, not VB6 semantics.
public class TerminatedMemberAccessTests : BaseVBTestFixture
{
    private BasicInterpreter Create(string code = "", string cls = "Public field As Long\nPublic Sub Ping()\nEnd Sub\n") =>
        new(new MockStdLib(debug), context, rootEnv, code, "Module1", null, [("C1", cls)]);

    [Fact]
    public async Task ForcedMethodAccess_IsCountedOnce()
    {
        var vb = Create();
        var instance = await vb.NewObject("C1");
        await vb.FireTerminate(instance);
        await vb.RunProcedure(instance.ClassDef, instance.ClassDef.PrePass.Procedures["Ping"], [],
            instance.InstanceEnv, Vb6Value.NewObject(instance));
        vb.TerminatedMemberAccessCount.Should().Be(1);
        vb.TerminatedMemberAccesses.Should().ContainSingle().Which.Member.Should().Be("Ping");
    }

    [Theory]
    [InlineData("Debug.Print stale.field", "field", 1)]
    [InlineData("stale.field = 3", "field", 1)]
    [InlineData("stale.Ping", "Ping", 1)]
    [InlineData("Call stale.Ping()", "Ping", 1)]
    public async Task ForcedAccess_RecordsTheSite(string code, string member, int line)
    {
        var vb = Create(code);
        var instance = await vb.NewObject("C1");
        await vb.FireTerminate(instance);
        context.AllocVariable(rootEnv, "stale", Vb6Value.NewObject(instance));
        await vb.Execute();
        vb.TerminatedMemberAccessCount.Should().Be(1);
        var hit = vb.TerminatedMemberAccesses.Should().ContainSingle().Subject;
        hit.Member.Should().Be(member);
        if (line != 0)
        {
            hit.Module.Should().Be("Module1");
            hit.Line.Should().Be(line);
        }
    }

    [Theory]
    [InlineData("Set stale.child = Nothing", "Public child As Object", "child")]
    [InlineData("Debug.Print stale.P", "Public Property Get P() As Long\nP = 1\nEnd Property", "P")]
    [InlineData("stale.P = 2", "Public Property Let P(ByVal n As Long)\nEnd Property", "P")]
    [InlineData("Set stale.P = Nothing", "Public Property Set P(ByVal o As Object)\nEnd Property", "P")]
    [InlineData("Debug.Print CallByName(stale, \"field\", 2)", "Public field As Long", "field")]
    public async Task OtherMemberRoutes_AreCountedOnce(string code, string cls, string member)
    {
        var vb = Create(code, cls);
        var instance = await vb.NewObject("C1");
        await vb.FireTerminate(instance);
        context.AllocVariable(rootEnv, "stale", Vb6Value.NewObject(instance));
        await vb.Execute();
        vb.TerminatedMemberAccessCount.Should().Be(1);
        vb.TerminatedMemberAccesses.Should().ContainSingle().Which.Member.Should().Be(member);
    }

    [Fact]
    public async Task ImplicitFieldRead_AndWrite_AreCountedAfterAStaleMethodCall()
    {
        var vb = Create("stale.Ping", "Public field As Long\nSub Ping()\nDebug.Print field\nfield = 3\nEnd Sub");
        var instance = await vb.NewObject("C1");
        await vb.FireTerminate(instance);
        context.AllocVariable(rootEnv, "stale", Vb6Value.NewObject(instance));
        await vb.Execute();
        vb.TerminatedMemberAccessCount.Should().Be(3);
        vb.TerminatedMemberAccesses.Should().Contain(hit => hit.Module == "C1" && hit.Member == "field" && hit.Line == 3);
        vb.TerminatedMemberAccesses.Should().Contain(hit => hit.Module == "C1" && hit.Member == "field" && hit.Line == 4);
    }

    [Fact]
    public async Task NormalRun_AndTerminationHookAccesses_AreNotCounted()
    {
        var vb = Create("Dim o As C1\nSet o = New C1\no.Ping\no.field = 2\nDebug.Print o.field\nSet o = Nothing",
            "Public field As Long\nSub Ping()\nfield = field + 1\nEnd Sub\nPrivate Sub Class_Terminate()\nMe.Ping\nDebug.Print Me.field\nEnd Sub");
        await vb.Execute();
        vb.TerminatedMemberAccessCount.Should().Be(0);
        vb.TerminatedMemberAccesses.Should().BeEmpty();
    }

    [Fact]
    public async Task FormTerminate_IsExempt_ButALaterCallOnThatInstanceIsCounted()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv,
            "Private Sub Form_Terminate()\nMe.Ping\nEnd Sub\nPublic Sub Ping()\nEnd Sub",
            primaryName: "Form1", primaryKind: InterpreterModuleKind.Form);
        var form = vb.NewFormInstance(vb.PrimaryModule);
        await vb.FireTerminate(form);
        vb.TerminatedMemberAccessCount.Should().Be(0);
        await vb.RunProcedure(form.ClassDef, form.ClassDef.PrePass.Procedures["Ping"], [],
            form.InstanceEnv, Vb6Value.NewObject(form));
        vb.TerminatedMemberAccessCount.Should().Be(1);
    }

    [Fact]
    public async Task RetainsOnlyTheFirstTwentySites_ButCountsEveryHit()
    {
        var vb = Create();
        var instance = await vb.NewObject("C1");
        await vb.FireTerminate(instance);
        for (int i = 0; i < 25; i++)
            vb.NoteMemberAccess(instance, $"member{i}", "Caller", i + 1);
        vb.TerminatedMemberAccessCount.Should().Be(25);
        vb.TerminatedMemberAccesses.Should().HaveCount(20);
        vb.TerminatedMemberAccesses[^1].Should().Be(new TerminatedMemberAccess("Caller", "member19", 20));
    }
}
