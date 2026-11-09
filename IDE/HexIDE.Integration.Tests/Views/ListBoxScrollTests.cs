using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>TopIndex assignment measurements from #3275, native and p-code agreeing; measuring PR #3286.</summary>
public class ListBoxScrollTests : WindowTestFixture
{
    [Fact]
    public void ListBox_declares_the_measured_Scroll_event()
    {
        HexIDE.Runtime.Components.ListBoxComponentClass.Instance.Events.Should().Contain(e => e.Name == "Scroll");
    }

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

    /// <summary>Records each handler a control raises, with List1's TopIndex at that moment.</summary>
    private sealed class Recorder : Decorator, IModuleExecutionRoot
    {
        public VBListBox? List { get; set; }
        public List<string> Calls { get; } = new();
        private int Top() => ListMembers.TryGet(List!, "TopIndex", out var value) ? (int)value.Value! : -999;
        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) =>
            Calls.Add(name is "List1_Scroll" or "List1_Click" ? $"{name} top={Top()}" : name);
    }

    private static async Task<(Recorder Recorder, ModuleExecutionContext Ctx, ExecutionEnvironment Env)> Loaded(string setup)
    {
        var (root, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithList);
        var recorder = new Recorder { Child = root, List = (VBListBox)ContainerRuntimeHarness.Child(canvas, "List1") };
        var window = new Window { Content = recorder, Width = 400, Height = 300 };
        ContainerRuntimeHarness.MergeAppResources(window);
        window.Show();
        await ContainerRuntimeHarness.Run(ctx, env, setup);
        Dispatcher.UIThread.RunJobs();
        var list = recorder.List!;
        var rowHeight = list.ContainerFromIndex(0)!.Bounds.Height;
        list.Height += 11 * rowHeight - list.Scroll!.Viewport.Height;
        list.UpdateLayout();
        list.Scroll.Viewport.Height.Should().BeApproximately(11 * rowHeight, 0.01);
        recorder.Calls.Clear();
        return (recorder, ctx, env);
    }

    private static async Task<List<string>> After(Recorder recorder, ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
    {
        await ContainerRuntimeHarness.Run(ctx, env, code);
        Dispatcher.UIThread.RunJobs();
        return recorder.Calls.Where(c => c.StartsWith("List1_", System.StringComparison.Ordinal)).ToList();
    }

    private const string SixtyItems = "For i = 0 To 59\r\nList1.AddItem CStr(i)\r\nNext\r\n";

    [AvaloniaTheory]
    [InlineData(0, 5, 5, true)]
    [InlineData(5, 0, 0, true)]
    [InlineData(5, 5, 5, false)]
    public async Task TopIndex_assignment_source_changes_raise_once_and_same_value_raises_none(int initial, int target, int expected, bool raises)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        await After(recorder, ctx, env, $"List1.TopIndex = {initial}\r\n");
        recorder.Calls.Clear();
        var calls = await After(recorder, ctx, env, $"List1.TopIndex = {target}\r\nDebug.Print List1.TopIndex\r\n");
        calls.Should().Equal(raises ? new[] { $"List1_Scroll top={expected}" } : System.Array.Empty<string>());
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "Debug.Print List1.TopIndex\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal(expected.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(60)]
    public async Task TopIndex_assignment_source_outside_the_list_raises_380_without_Scroll(int target)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            $"On Error Resume Next\r\nList1.TopIndex = {target}\r\nDebug.Print Err.Number\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("380");
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task TopIndex_assignment_source_last_item_clamps_to_last_page_and_repeat_raises_none()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var calls = await After(recorder, ctx, env, "List1.TopIndex = 59\r\n");
        calls.Should().Equal("List1_Scroll top=49");
        recorder.Calls.Clear();
        (await After(recorder, ctx, env, "List1.TopIndex = 59\r\n")).Should().BeEmpty();
    }
    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(-2)]
    public async Task Wheel_source_moves_the_list_without_Scroll(int notches)
    {
        var (recorder, _, _) = await Loaded(SixtyItems);
        var list = recorder.List!;
        var window = (Window)TopLevel.GetTopLevel(list)!;
        var before = list.Scroll!.Offset.Y;
        window.MouseWheel(list.TranslatePoint(new Point(20, 20), window)!.Value, new Vector(0, notches));
        Dispatcher.UIThread.RunJobs();
        list.Scroll.Offset.Y.Should().BeGreaterThan(before);
        recorder.Calls.Should().NotContain(c => c.StartsWith("List1_Scroll", System.StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task ListIndex_assignment_source_scrolls_with_Click_only()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var before = recorder.List!.Scroll!.Offset.Y;
        var calls = await After(recorder, ctx, env, "List1.ListIndex = 50\r\n");
        calls.Should().ContainSingle().Which.Should().StartWith("List1_Click");
        recorder.List.Scroll.Offset.Y.Should().BeGreaterThan(before);
    }

    [AvaloniaTheory]
    [InlineData("Dim target As Long\r\ntarget = 5")]
    [InlineData("Dim target As Variant\r\ntarget = 5&")]
    public async Task TopIndex_assignment_uses_Integer_store_coercion(string declaration)
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var calls = await After(recorder, ctx, env, declaration + "\r\nList1.TopIndex = target\r\n");
        calls.Should().Equal("List1_Scroll top=5");
    }

    [AvaloniaFact]
    public async Task TopIndex_assignment_Integer_store_overflow_raises_6_without_Scroll()
    {
        var (recorder, ctx, env) = await Loaded(SixtyItems);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\r\nList1.TopIndex = 32768&\r\nDebug.Print Err.Number\r\nDebug.Print List1.TopIndex\r\n");
        printed.Select(v => VbDisplay.ToText(v, System.Globalization.CultureInfo.InvariantCulture)).Should().Equal("6", "0");
        recorder.Calls.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void Unmeasured_MultiSelect_TopIndex_read_refuses_instead_of_guessing()
    {
        var list = new VBListBox { MultiSelect = HexIDE.Runtime.BuiltinTypes.VBMultiSelect.Simple };
        var read = () => ListMembers.TryGet(list, "TopIndex", out _);
        read.Should().Throw<System.NotImplementedException>();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopIndex_before_layout_refuses_instead_of_guessing(bool write)
    {
        var list = new VBListBox();
        list.Items.Add("a");
        System.Action access = () =>
        {
            if (write)
                ListMembers.TrySet(list, "TopIndex", new Vb6Value(0));
            else
                ListMembers.TryGet(list, "TopIndex", out _);
        };
        access.Should().Throw<System.NotImplementedException>();
    }

}
