using System;
using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using RecordingRoot = HexIDE.Integration.Tests.Views.ContainerRuntimeHarness.RecordingRoot;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// VB6 control arrays — N controls sharing one Name with distinct integer indices (Command1(0), Command1(1)),
/// indexed as Command1(i), the array-name object's .Count/.LBound/.UBound, and shared event handlers that receive
/// the fired element's Index. Needs real (headless) Avalonia controls spawned through the runtime
/// <see cref="VBLoader.SpawnComponents"/> grouping path, so it lives here. Behaviour is oracle-pinned against
/// vb6.exe (see docs/vb6-fidelity-oracle.md → Control arrays).
/// </summary>
public class ControlArrayTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "MyProject");

    private class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private class CaptureLib(List<Vb6Value> debug) : IBasicStandardLibrary
    {
        public Task<MessageBoxResult> MsgBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => Task.FromResult<MessageBoxResult>(default);
        public Task<string?> InputBox(string prompt, string title, string defaultText)
            => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value value) => debug.Add(value);
    }

    // A form with a 2-element CommandButton array Command1(0)="A", Command1(1)="B".
    private const string TwoButtonArray = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CommandButton Command1
              Caption         =   "A"
              Index           =   0
           End
           Begin VB.CommandButton Command1
              Caption         =   "B"
              Index           =   1
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Sparse indices 0 and 2 (1 deleted at design time) — proves the .frm Index is actually parsed, not assigned
    // positionally.
    private const string SparseArray = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.CommandButton Command1
              Caption         =   "zero"
              Index           =   0
           End
           Begin VB.CommandButton Command1
              Caption         =   "two"
              Index           =   2
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (ModuleExecutionContext ctx, ExecutionEnvironment env) Spawn(string frm)
    {
        var form = new FormDeserializer().Deserialize(Project, frm, NullSink.Instance)!;
        var ctx = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        VBLoader.SpawnComponents(form, ctx, env);
        return (ctx, env);
    }

    private static async Task<List<Vb6Value>> Run(string frm, string code)
    {
        var (ctx, env) = Spawn(frm);
        var debug = new List<Vb6Value>();
        await new BasicInterpreter(new CaptureLib(debug), ctx, env, code).Execute();
        return debug;
    }

    [AvaloniaFact]
    public async Task IndexedElement_ReadsItsProperty()
    {
        var debug = await Run(TwoButtonArray, "Debug.Print Command1(1).Caption\r\n");
        debug.Should().ContainSingle();
        debug[0].Value.Should().Be("B");
    }

    [AvaloniaFact]
    public async Task IndexedElement_WritesThenReadsItsProperty()
    {
        var debug = await Run(TwoButtonArray,
            "Command1(0).Caption = \"Hi\"\r\n" +
            "Debug.Print Command1(0).Caption\r\n");
        debug.Should().ContainSingle();
        debug[0].Value.Should().Be("Hi");
    }

    [AvaloniaFact]
    public async Task ArrayName_ReportsCountAndBounds()
    {
        var debug = await Run(TwoButtonArray,
            "Debug.Print Command1.Count\r\n" +
            "Debug.Print Command1.LBound\r\n" +
            "Debug.Print Command1.UBound\r\n");
        debug.Should().HaveCount(3);
        Convert.ToInt64(debug[0].Value).Should().Be(2);   // Count
        Convert.ToInt64(debug[1].Value).Should().Be(0);   // LBound
        Convert.ToInt64(debug[2].Value).Should().Be(1);   // UBound
    }

    [AvaloniaFact]
    public async Task MissingElement_Read_Yields340()
    {
        // Oracle: Command1(9).Caption on a missing element → Err 340 (Control array element doesn't exist).
        var debug = await Run(TwoButtonArray,
            "On Error Resume Next\r\n" +
            "Dim s\r\n" +
            "s = Command1(9).Caption\r\n" +
            "Debug.Print Err.Number\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(340);
    }

    [AvaloniaFact]
    public async Task MissingElement_Write_Yields340()
    {
        var debug = await Run(TwoButtonArray,
            "On Error Resume Next\r\n" +
            "Command1(9).Caption = \"x\"\r\n" +
            "Debug.Print Err.Number\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(340);
    }

    [AvaloniaFact]
    public async Task SparseIndices_AreParsedNotPositional()
    {
        // Command1(2) exists (its Index is really parsed), Command1(1) does not (→ 340), UBound is 2.
        var debug = await Run(SparseArray,
            "On Error Resume Next\r\n" +
            "Debug.Print Command1(2).Caption\r\n" +
            "Debug.Print Command1.UBound\r\n" +
            "Dim s\r\n" +
            "s = Command1(1).Caption\r\n" +
            "Debug.Print Err.Number\r\n");
        debug[0].Value.Should().Be("two");
        Convert.ToInt64(debug[1].Value).Should().Be(2);   // UBound = highest index present
        Convert.ToInt64(debug[2].Value).Should().Be(340); // element 1 doesn't exist
    }

    [AvaloniaFact]
    public void SharedEvent_PassesElementIndexAsLeadingArg()
    {
        // A control-array element stamped with its Index dispatches `Command1_Click(Index)` — Index passed as the arg.
        var button = new Button();
        VBProps.SetName(button, "Command1");
        VBProps.SetIndex(button, 1);
        var root = new RecordingRoot { Child = button };

        button.ExecuteSub(ComponentBaseClass.ClickEvent);

        root.Calls.Should().ContainSingle();
        root.Calls[0].Name.Should().Be("Command1_Click");
        root.Calls[0].Args.Should().NotBeNull();
        root.Calls[0].Args!.Should().ContainSingle();
        Convert.ToInt64(root.Calls[0].Args![0].Value).Should().Be(1);
    }

    [AvaloniaFact]
    public void StandaloneControl_DispatchesWithNoArgs()
    {
        // A standalone control (no Index stamp) dispatches a parameterless handler — no Index arg.
        var button = new Button();
        VBProps.SetName(button, "Text1");
        var root = new RecordingRoot { Child = button };

        button.ExecuteSub(ComponentBaseClass.ClickEvent);

        root.Calls.Should().ContainSingle();
        root.Calls[0].Name.Should().Be("Text1_Click");
        root.Calls[0].Args.Should().BeNull();
    }

    // ── Phase 2: runtime Load / Unload (oracle-pinned) ──────────────────────────────────

    [AvaloniaFact]
    public async Task Load_NewIndex_AddsElement_ClonedFromTemplate_Hidden()
    {
        // Oracle: Load Command1(5) → a new element cloning the lowest index's props (Caption "A"), forced Visible=False.
        var debug = await Run(TwoButtonArray,
            "Load Command1(5)\r\n" +
            "Debug.Print Command1.Count\r\n" +
            "Debug.Print Command1(5).Caption\r\n" +
            "Debug.Print Command1(5).Visible\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(3);      // Count grew
        debug[1].Value.Should().Be("A");                     // cloned the lowest-index element's Caption
        debug[2].Value.Should().Be(false);                   // loaded elements start hidden
    }

    [AvaloniaFact]
    public async Task Load_ExistingIndex_Yields360()
    {
        var debug = await Run(TwoButtonArray,
            "On Error Resume Next\r\n" +
            "Load Command1(0)\r\n" +
            "Debug.Print Err.Number\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(360);
    }

    [AvaloniaFact]
    public async Task Unload_LoadedElement_RemovesIt()
    {
        var debug = await Run(TwoButtonArray,
            "Load Command1(5)\r\n" +
            "Unload Command1(5)\r\n" +
            "Debug.Print Command1.Count\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(2);      // back to the two design-time elements
    }

    [AvaloniaFact]
    public async Task Unload_DesignTimeElement_Yields362()
    {
        var debug = await Run(TwoButtonArray,
            "On Error Resume Next\r\n" +
            "Unload Command1(0)\r\n" +
            "Debug.Print Err.Number\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(362);    // can't unload a design-time element
    }

    [AvaloniaFact]
    public async Task Unload_MissingElement_Yields340()
    {
        var debug = await Run(TwoButtonArray,
            "On Error Resume Next\r\n" +
            "Unload Command1(9)\r\n" +
            "Debug.Print Err.Number\r\n");
        Convert.ToInt64(debug[0].Value).Should().Be(340);
    }

    [AvaloniaFact]
    public async Task LoadedElement_CanBeIndexedAndRaisesSharedEvent()
    {
        // A loaded element is a full array member: indexable, and after Load its Index is stamped so a click would
        // dispatch Command1_Click(5). Here we verify indexing + that it took the new index.
        var debug = await Run(TwoButtonArray,
            "Load Command1(5)\r\n" +
            "Command1(5).Caption = \"loaded\"\r\n" +
            "Debug.Print Command1(5).Caption\r\n" +
            "Debug.Print Command1.UBound\r\n");
        debug[0].Value.Should().Be("loaded");
        Convert.ToInt64(debug[1].Value).Should().Be(5);      // UBound is now the loaded index
    }
}
