using HexIDE.Runtime.Debugging;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The Locals window's Type column as VB6 writes it (#2700). hexide-int measured VB6's on #2700 (SP6, 6.00.9782; the oracle's
/// #2702): a Standard EXE paused at Stop in Sub Main, with a Class1 holding Value and mName. Each row here is a row of that
/// table. A procedure's own locals keep their declaration order.
/// </summary>
public class LocalsTypeTests : BaseVBTestFixture
{
    private static Task<StoppedInfo> NextStop(DebugController dbg)
    {
        var tcs = new TaskCompletionSource<StoppedInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(StoppedInfo info) { dbg.Stopped -= Handler; tcs.TrySetResult(info); }
        dbg.Stopped += Handler;
        return tcs.Task;
    }

    private const string Main =
        "Main\n"
        + "Sub Main()\n"
        + "    Dim objC As Class1\n"
        + "    Dim objO As Object\n"
        + "    Dim varO As Variant\n"
        + "    Dim nilC As Class1\n"
        + "    Dim nilO As Object\n"
        + "    Dim nilV As Variant\n"
        + "    Dim empV As Variant\n"
        + "    Dim arrC(1 To 2) As Class1\n"
        + "    Dim arrO(1 To 2) As Object\n"
        + "    Dim colC As Collection\n"
        + "    Dim colO As Object\n"
        + "    Set objC = New Class1\n"
        + "    Set objO = objC\n"
        + "    Set varO = objC\n"
        + "    Set nilV = Nothing\n"
        + "    Set arrC(1) = objC\n"
        + "    Set arrO(1) = objC\n"
        + "    Set colC = New Collection\n"
        + "    colC.Add objC\n"
        + "    Set colO = colC\n"
        + "    Stop\n"
        + "End Sub\n";

    private const string Class1 =
        "Public Value As Long\n"
        + "Private mName As String\n"
        + "Private Sub Class_Initialize()\n"
        + "    mName = \"c1\"\n"
        + "    Value = 3\n"
        + "End Sub\n";

    private async Task<(DebugScope Scope, Func<Task> Finish)> PausedAtStop()
    {
        var (vb, dbg) = NewDebuggable(Main, "Module1", ("Class1", Class1));
        var stop = NextStop(dbg);
        var run = vb.Execute();
        await stop.Guarded();
        var scope = dbg.GetLocals()!;
        return (scope, async () => { dbg.Continue(); await run.Guarded(); });
    }

    private static (string Name, string Value, string Type) Row(DebugNode n) => (n.Name, n.Value, n.TypeName);

    [Fact]
    public async Task Each_local_is_typed_as_VB6s_Locals_types_it_in_declaration_order()
    {
        var (scope, finish) = await PausedAtStop();
        try
        {
            var locals = scope.Locals.Where(n => n.Name != "Module1").ToDictionary(n => n.Name);

            scope.Locals.Select(n => n.Name).Should().Equal("Module1", "objC", "objO", "varO", "nilC", "nilO", "nilV",
                "empV", "arrC", "arrO", "colC", "colO");
            locals["objC"].TypeName.Should().Be("Class1/Class1");
            locals["objO"].TypeName.Should().Be("Object/Class1");
            locals["varO"].TypeName.Should().Be("Variant/Object/Class1");
            Row(locals["nilC"]).Should().Be(("nilC", "Nothing", "Class1"));
            Row(locals["nilO"]).Should().Be(("nilO", "Nothing", "Object"));
            Row(locals["nilV"]).Should().Be(("nilV", "Nothing", "Variant/Object"));
            Row(locals["empV"]).Should().Be(("empV", "Empty", "Variant/Empty"));
            locals["colC"].TypeName.Should().Be("Collection/Collection");
            locals["colO"].TypeName.Should().Be("Object/Collection");
        }
        finally { await finish(); }
    }

    [Fact]
    public async Task An_array_is_its_element_type_and_bounds_and_each_element_follows_the_same_rules()
    {
        var (scope, finish) = await PausedAtStop();
        try
        {
            var arrC = scope.Locals.Single(n => n.Name == "arrC");
            var arrO = scope.Locals.Single(n => n.Name == "arrO");

            arrC.TypeName.Should().Be("Class1(1 to 2)");
            arrO.TypeName.Should().Be("Object(1 to 2)");
            arrC.Expand().Select(n => (n.Name, n.TypeName)).Should().Equal(("arrC(1)", "Class1/Class1"), ("arrC(2)", "Class1"));
            arrO.Expand().Select(n => (n.Name, n.TypeName)).Should().Equal(("arrO(1)", "Object/Class1"), ("arrO(2)", "Object"));
        }
        finally { await finish(); }
    }

    [Fact]
    public async Task A_class_instance_lists_its_variables_sorted_and_a_Collection_its_items_as_Variants()
    {
        var (scope, finish) = await PausedAtStop();
        try
        {
            scope.Locals.Single(n => n.Name == "objC").Expand().Select(Row).Should().Equal(
                ("mName", "\"c1\"", "String"),
                ("Value", "3", "Long"));
            scope.Locals.Single(n => n.Name == "colC").Expand().Select(n => (n.Name, n.TypeName))
                .Should().Equal(("Item 1", "Variant/Object/Class1"));
        }
        finally { await finish(); }
    }

    [Fact]
    public async Task A_module_with_no_variables_is_listed_by_its_name_with_one_No_Variables_row()
    {
        var (scope, finish) = await PausedAtStop();
        try
        {
            var module = scope.Locals[0];

            Row(module).Should().Be(("Module1", "", "Module1/Module1"));
            module.Expand().Select(n => n.Name).Should().Equal("<No Variables>");
        }
        finally { await finish(); }
    }
}
