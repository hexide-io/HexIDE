using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ActivationSlotTests : BaseVBTestFixture
{
    private async Task<int> SlotsAfter(string code)
    {
        var state = new ModuleExecutionContext();
        var vb = new BasicInterpreter(new MockStdLib(debug), state, new ExecutionEnvironment(), code);
        await vb.Execute();
        return state.State.memory.Count;
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(100000)]
    public async Task RepeatedCalls_LeaveOnlyPersistentSlots(int calls)
    {
        string Program(int n) => $$"""
            Dim i As Long
            For i = 1 To {{n}}
                Work i
            Next
            Sub Work(ByVal n As Long)
                Dim a As Long, b As Long
            End Sub
            """;
        (await SlotsAfter(Program(calls))).Should().Be(await SlotsAfter(Program(0)));
    }

    [Theory]
    [InlineData("Exit Sub")]
    [InlineData("Err.Raise 5")]
    public async Task EarlyAndErrorReturns_ReclaimSlots(string exit)
    {
        string Program(int n) => $$"""
            Dim i As Long
            On Error Resume Next
            For i = 1 To {{n}}
                Work i
            Next
            Sub Work(ByVal n As Long)
                Dim a As Long
                Const k = 1
                implicit = n
                {{exit}}
            End Sub
            """;
        (await SlotsAfter(Program(10000))).Should().Be(await SlotsAfter(Program(0)));
    }

    [Fact]
    public async Task FunctionAndRefArguments_ReclaimOnlyTheirOwnSlots()
    {
        string Program(int n) => $$"""
            Type T
                n As Long
            End Type
            Dim u As T, a(0) As Long, i As Long, result As Long
            For i = 1 To {{n}}
                result = Work(u.n)
                result = Work(a(0))
            Next
            Function Work(ByRef n As Long) As Long
                Inner n
                Work = n
            End Function
            Sub Inner(ByRef n As Long)
                n = n + 1
            End Sub
            """;
        (await SlotsAfter(Program(1000))).Should().Be(await SlotsAfter(Program(0)));
    }

    [Fact]
    public async Task FailedParameterBinding_ReclaimsPartialActivation()
    {
        string Program(int n) => $$"""
            Dim i As Long
            On Error Resume Next
            For i = 1 To {{n}}
                Work 1, "abc"
            Next
            Sub Work(ByVal a As Long, ByVal b As Boolean)
            End Sub
            """;
        (await SlotsAfter(Program(1000))).Should().Be(await SlotsAfter(Program(0)));
    }
    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.SetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    [Fact]
    public async Task Recursion_GrowsWithDepthAndShrinksOnUnwind()
    {
        var (vb, dbg) = NewDebuggable("Work 1000\nDebug.Print 0\nSub Work(ByVal n As Long)\nDim a As Long, b As Long\nDebug.Print n\nIf n > 0 Then Work n - 1\nDebug.Print n\nEnd Sub\n");
        dbg.SetBreakpoints("Module1", [2, 5, 7]);
        var stop = NextStop(dbg);
        var run = vb.Execute();
        int first = 0;
        for (int depth = 0; depth <= 1000; depth++)
        {
            (await stop.Guarded()).Line.Should().Be(5);
            int count = context.State.memory.Count;
            if (depth == 0) first = count;
            count.Should().Be(first + 3 * depth);
            stop = NextStop(dbg);
            dbg.Continue();
        }
        for (int depth = 1000; depth >= 0; depth--)
        {
            (await stop.Guarded()).Line.Should().Be(7);
            context.State.memory.Count.Should().Be(first + 3 * depth);
            stop = NextStop(dbg);
            dbg.Continue();
        }
        (await stop.Guarded()).Line.Should().Be(2);
        context.State.memory.Count.Should().Be(first - 3);
        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public async Task StepOut_StaleFrameLocalsAndWatchAreSafe()
    {
        var (vb, dbg) = NewDebuggable("Outer\nSub Outer()\nWork\nDebug.Print 0\nEnd Sub\nSub Work()\nDim local As Long\nlocal = 1\nEnd Sub\n");
        dbg.SetBreakpoints("Module1", [8]);
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();
        IDebugFrame stale = vb.ActivationStack.Last();
        stale.GetLocals().Locals.Should().Contain(n => n.Name == "local");
        stop = NextStop(dbg);
        dbg.StepOut();
        (await stop.Guarded()).Line.Should().Be(4);
        stale.GetLocals().Locals.Should().NotContain(n => n.Name == "local");
        var watch = await stale.EvaluateTypedAsync("local");
        watch.Ok.Should().BeFalse();
        watch.Display.Should().Contain("out of scope");
        dbg.Continue();
        await run.Guarded();
    }

    [Fact]
    public void FreedSlots_RejectWritesAndAreNeverReused()
    {
        var state = new ExecutionState();
        int slot = state.Alloc(1);
        state.Free(slot);
        state.TryRead(slot, out _).Should().BeFalse();
        Action write = () => state[slot] = 2;
        write.Should().Throw<InvalidOperationException>().WithMessage($"*{slot}*out of scope*");
        state.Alloc(3).Should().BeGreaterThan(slot);
        state.memory.Should().NotContainKey(slot);
    }

    [Fact]
    public void CounterExhaustion_RefusesAllocationWithoutWrapping()
    {
        var state = new ExecutionState();
        int live = state.Alloc(1);
        typeof(ExecutionState).GetField("nextFreeLocation", System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance)!.SetValue(state, int.MaxValue);
        Action allocate = () => state.Alloc(2);
        allocate.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(7);
        state[live].Should().Be(new Vb6Value(1));
        state.memory.Should().ContainSingle();
        allocate.Should().Throw<VBRunTimeException>();
    }

    [Fact]
    public void Free_RemovesAllSlotMetadataAndUnlocksRefTarget()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "");
        var module = vb.PrimaryModule;
        var instance = new VbObject(module, new ExecutionEnvironment());
        var state = context.State;
        var env = new ExecutionEnvironment { AllocatedSlots = [] };
        var target = new RefTarget();
        int slot = state.AllocRef(target, env);
        env.AllocatedSlots.Should().Equal(slot);
        target.Locked.Should().BeTrue();
        state.MarkReadOnly(slot);
        state.MarkConstant(slot);
        state.MarkDeclaredArray(slot);
        state.MarkFixedArray(slot);
        state.MarkVariant(slot);
        state.MarkAutoNew(slot, "C1", "o");
        state.MarkPendingControl(slot, instance);
        state.MarkDefaultForm(slot, module);
        int typed = state.Alloc(1, Vb6Value.ValueType.Long, "C1");
        state.Free(slot);
        state.Free(typed);
        target.Locked.Should().BeFalse();
        state.HasRefSlots.Should().BeFalse();
        state.DeclaredTypeOf(typed).Should().BeNull();
        state.DeclaredClassOf(typed).Should().BeNull();
        state.IsReadOnly(slot).Should().BeFalse();
        state.IsConstant(slot).Should().BeFalse();
        state.IsDeclaredArray(slot).Should().BeFalse();
        state.IsFixedArray(slot).Should().BeFalse();
        state.IsVariant(slot).Should().BeFalse();
        state.AutoNewOf(slot).Should().BeNull();
        state.PendingControlOf(slot).Should().BeNull();
        state.DefaultFormOf(slot).Should().BeNull();
        state.TryRead(slot, out _).Should().BeFalse();
    }

    private sealed class RefTarget : IByRefTarget
    {
        public bool Locked { get; private set; }
        public Vb6Value.ValueType? DeclaredType => Vb6Value.ValueType.Long;
        public bool IsVariant => true;
        public Vb6Value Read() => 1;
        public void Write(Vb6Value value) { }
        public void Lock() => Locked = true;
        public void Unlock() => Locked = false;
    }

    [Fact]
    public async Task Methods_ReclaimMeAndLocalsButKeepFieldsAndStatics()
    {
        string Program(int n) => $$"""
            Dim o As C1, i As Long
            Set o = New C1
            o.Bump
            For i = 1 To {{n}}
                o.Bump
            Next
            """;
        const string cls = "Public field As Long\nSub Bump()\nStatic calls As Long\nDim local As Long\ncalls = calls + 1\nfield = calls\nEnd Sub\n";
        async Task<int> Count(int n)
        {
            var state = new ModuleExecutionContext();
            var vb = new BasicInterpreter(new MockStdLib(debug), state, new ExecutionEnvironment(), Program(n),
                "Module1", null, [("C1", cls)]);
            await vb.Execute();
            return state.State.memory.Count;
        }
        (await Count(1000)).Should().Be(await Count(0));
    }

}
