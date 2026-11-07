using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A ListBox's Click when code sets ListIndex (#1374). Every expectation is vb6.exe's, native and p-code agreeing (the
/// oracle's "A ListBox's Click"): code that moves the selection raises Click, with ListIndex already the new value
/// inside it, and code that sets it again or only empties the list does not. The pointer and the keyboard are pinned in
/// <c>MouseEventTests</c>.
/// </summary>
public class ListBoxClickTests : WindowTestFixture
{
    private const string FormWithList = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.ListBox List1
              Left            =   120
              Top             =   120
              Width           =   2400
              Height          =   1200
           End
        End
        Attribute VB_Name = "Form1"
        """;

    /// <summary>Records each handler a control raises, with List1's ListIndex at that moment.</summary>
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public ListBox? List { get; set; }
        public List<string> Calls { get; } = new();
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            Calls.Add($"{name} li={List?.SelectedIndex}");
    }

    private static async Task<(Recorder Recorder, ModuleExecutionContext Ctx, ExecutionEnvironment Env)> Loaded(string setup)
    {
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithList);
        var recorder = new Recorder { Child = root, List = (ListBox)ContainerRuntimeHarness.Child(canvas, "List1") };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, setup);
        Dispatcher.UIThread.RunJobs();
        recorder.Calls.Clear();
        return (recorder, ctx, env);
    }

    private static async Task<List<string>> After(Recorder recorder, ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        await ContainerRuntimeHarness.Run(ctx, env, code);
        Dispatcher.UIThread.RunJobs();
        return recorder.Calls.Where(c => c.StartsWith("List1_", System.StringComparison.Ordinal)).ToList();
    }

    private const string ThreeItems = "List1.AddItem \"a\"\r\nList1.AddItem \"b\"\r\nList1.AddItem \"c\"\r\n";

    [AvaloniaTheory]
    [InlineData("List1.ListIndex = 2\r\n", "List1_Click li=2")]
    [InlineData("List1.ListIndex = -1\r\n", "List1_Click li=-1")]
    public async Task Code_that_moves_the_selection_raises_Click_with_the_new_ListIndex(string code, string expected)
    {
        var (recorder, ctx, env) = await Loaded(ThreeItems + "List1.ListIndex = 1\r\n");

        (await After(recorder, ctx, env, code)).Should().Equal(expected);
    }

    // Selected(i) and Text raise it too, pinned in ListBoxSelectedTests (#1395).
    [AvaloniaTheory]
    [InlineData("List1.ListIndex = 1\r\n", 1)]
    [InlineData("List1.AddItem \"d\"\r\n", 1)]
    [InlineData("List1.Clear\r\n", -1)]
    public async Task Code_that_sets_the_selection_again_or_only_empties_the_list_raises_none(string code, int listIndex)
    {
        var (recorder, ctx, env) = await Loaded(ThreeItems + "List1.ListIndex = 1\r\n");

        (await After(recorder, ctx, env, code)).Should().BeEmpty();
        recorder.List!.SelectedIndex.Should().Be(listIndex);
    }

    [AvaloniaFact]
    public async Task A_ListIndex_out_of_range_raises_380_and_no_Click()
    {
        var (recorder, ctx, env) = await Loaded(ThreeItems + "List1.ListIndex = 1\r\n");

        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\r\nList1.ListIndex = 5\r\nDebug.Print Err.Number\r\nDebug.Print List1.ListIndex\r\n");
        Dispatcher.UIThread.RunJobs();

        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("380", "1");
        recorder.Calls.Should().BeEmpty();
    }
}
