using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #95 — <c>OptionButton.Value</c>, the whole interface of an option button, reachable from VB6 code.
///
/// Every expectation below is <c>vb6.exe</c> output, not reasoning about what VB6 probably does; the probe is
/// recorded under *Option buttons* in <c>docs/vb6-fidelity-oracle.md</c>. Three of its answers contradicted
/// the plausible guess:
///
/// <list type="bullet">
/// <item><c>Value</c> is a <b>Boolean</b>, not the check box's tri-state Integer — so <c>Value = 2</c> is
/// True by ordinary Boolean coercion, where <c>Check1.Value = 2</c> means <i>Grayed</i>.</item>
/// <item><c>Value = False</c> on the selected member is <b>honoured</b>, leaving the group with nothing
/// selected. It is not refused, and no sibling is promoted in its place.</item>
/// <item>A programmatic <c>Value = True</c> <b>does</b> raise Click — so the event cannot be raised from
/// <c>OnClick</c>, which only a user reaches.</item>
/// </list>
///
/// These need real headless Avalonia, because the group behaviour under test IS Avalonia's radio-button
/// grouping and the dispatch under test travels the visual tree.
/// </summary>
public class OptionButtonValueTests
{
    // Option2 carries the designer's `Value = -1  'True` — the boolean spelling VB6 writes, which is also
    // what the .frm round-trip test in the runtime suite pins. Check1 is here so the sibling control's
    // tri-state Value can be asserted to have stayed tri-state.
    private const string ThreeOptions = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CheckBox Check1
              Caption         =   "Check1"
              Left            =   300
              Top             =   1800
              Width           =   1500
              Height          =   300
           End
           Begin VB.OptionButton Option3
              Caption         =   "Option3"
              Left            =   300
              Top             =   1200
              Width           =   1500
              Height          =   300
           End
           Begin VB.OptionButton Option2
              Caption         =   "Option2"
              Left            =   300
              Top             =   800
              Value           =   -1  'True
              Width           =   1500
              Height          =   300
           End
           Begin VB.OptionButton Option1
              Caption         =   "Option1"
              Left            =   300
              Top             =   400
              Width           =   1500
              Height          =   300
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static (Canvas canvas, ContainerRuntimeHarness.RecordingRoot recorder, ModuleExecutionContext ctx, ExecutionEnvironment env) Recorded()
        => ContainerRuntimeHarness.Recorded(ThreeOptions);

    private static Task<List<Vb6Value>> Run(ModuleExecutionContext ctx, ExecutionEnvironment env, string code)
        => ContainerRuntimeHarness.Run(ctx, env, code);

    private static VBOptionButton Option(Canvas canvas, string name)
        => (VBOptionButton)ContainerRuntimeHarness.Child(canvas, name);

    private static IEnumerable<string> ClickedNames(ContainerRuntimeHarness.RecordingRoot recorder)
        => recorder.Calls.Where(c => c.Name.EndsWith("_Click", StringComparison.Ordinal)).Select(c => c.Name);

    // ── reading (oracle: load.o1=False|Boolean, load.o2=True|Boolean) ────────────────────────────

    [AvaloniaFact]
    public async Task Value_IsReadable_AndIsABoolean()
    {
        var (_, _, ctx, env) = Recorded();

        var debug = await Run(ctx, env,
            "Debug.Print Option1.Value\r\n" +
            "Debug.Print Option2.Value\r\n" +
            "Debug.Print TypeName(Option1.Value)\r\n" +
            "Debug.Print TypeName(Check1.Value)\r\n");

        debug[0].Value.Should().Be(false);
        debug[1].Value.Should().Be(true);       // the designer's Value = -1 'True survived the load
        // The distinction the whole change turns on: siblings in every tutorial, different types in VB6.
        debug[2].Value.Should().Be("Boolean");
        debug[3].Value.Should().Be("Integer");
    }

    // ── writing True (oracle: set1.o1=True, set1.o2=False, set1.clicks=C1;) ──────────────────────

    [AvaloniaFact]
    public async Task SettingValueTrue_SelectsIt_AndClearsTheSibling()
    {
        var (canvas, _, ctx, env) = Recorded();

        await Run(ctx, env, "Option1.Value = True\r\n");
        Dispatcher.UIThread.RunJobs();

        Option(canvas, "Option1").Value.Should().BeTrue();
        Option(canvas, "Option2").Value.Should().BeFalse();
        // The control's own checked state moved with it, not just the VB6-facing property.
        Option(canvas, "Option1").IsChecked.Should().BeTrue();
        Option(canvas, "Option2").IsChecked.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task SettingValueTrue_RaisesClick_OnTheNewlySelectedOneOnly()
    {
        var (_, recorder, ctx, env) = Recorded();
        recorder.Calls.Clear();

        await Run(ctx, env, "Option1.Value = True\r\n");
        Dispatcher.UIThread.RunJobs();

        // Option2 was deselected by the same assignment and fires NOTHING — one Click per switch, not two.
        ClickedNames(recorder).Should().Equal("Option1_Click");
    }

    [AvaloniaFact]
    public async Task ReSelectingTheSelectedOne_RaisesNoClick()
    {
        var (_, recorder, ctx, env) = Recorded();
        recorder.Calls.Clear();

        await Run(ctx, env, "Option2.Value = True\r\n");   // Option2 is already the selected one
        Dispatcher.UIThread.RunJobs();

        ClickedNames(recorder).Should().BeEmpty();
    }

    // ── writing False (oracle: clr1.o1/o2/o3 all False, clr1.clicks empty) ───────────────────────

    [AvaloniaFact]
    public async Task SettingValueFalse_OnTheSelectedMember_LeavesTheGroupEmpty()
    {
        var (canvas, _, ctx, env) = Recorded();

        await Run(ctx, env, "Option2.Value = False\r\n");
        Dispatcher.UIThread.RunJobs();

        // Not refused, and nothing is promoted in its place. A group with no selection is a state only code
        // can reach — clicking cannot clear one — and VB6 lets code reach it.
        Option(canvas, "Option1").Value.Should().BeFalse();
        Option(canvas, "Option2").Value.Should().BeFalse();
        Option(canvas, "Option3").Value.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task SettingValueFalse_RaisesNoClick()
    {
        var (_, recorder, ctx, env) = Recorded();
        recorder.Calls.Clear();

        await Run(ctx, env, "Option2.Value = False\r\n");
        Dispatcher.UIThread.RunJobs();

        ClickedNames(recorder).Should().BeEmpty();
    }

    [AvaloniaFact]
    public async Task SettingValueFalse_OnAnUnselectedMember_DisturbsNothing()
    {
        var (canvas, recorder, ctx, env) = Recorded();
        recorder.Calls.Clear();

        await Run(ctx, env, "Option1.Value = False\r\n");   // Option1 was already False
        Dispatcher.UIThread.RunJobs();

        Option(canvas, "Option2").Value.Should().BeTrue();  // the selected one is untouched
        ClickedNames(recorder).Should().BeEmpty();
    }

    // ── the designer's own value (oracle: load.clicks empty) ─────────────────────────────────────

    [AvaloniaFact]
    public void ADesignerSetValue_RaisesNoClick_AtLoad()
    {
        // Option2 loads selected. Nothing may have fired: in VB6 the designer's value is the control's
        // starting state, not an event. This holds because VBLoader finishes building the tree before an
        // execution root is above it — Recorded() reproduces that order deliberately, and this test is what
        // notices if the two ever swap.
        var (_, recorder, _, _) = Recorded();

        recorder.Calls.Should().BeEmpty();
    }

    // ── coercion (oracle: num1=True, num0=False, num2=True, numS=True, all with Err 0) ───────────

    [AvaloniaFact]
    public async Task NumericAndStringAssignments_CoerceLikeAnyOtherVB6Boolean()
    {
        var (canvas, _, ctx, env) = Recorded();
        var option1 = Option(canvas, "Option1");

        await Run(ctx, env, "Option1.Value = 1\r\n");
        option1.Value.Should().BeTrue();

        await Run(ctx, env, "Option1.Value = 0\r\n");
        option1.Value.Should().BeFalse();

        // The one that would be wrong if Value were the check box's tri-state: 2 means Grayed there, and
        // here it is just a non-zero number, so it is True.
        await Run(ctx, env, "Option1.Value = 2\r\n");
        option1.Value.Should().BeTrue();

        await Run(ctx, env, "Option1.Value = 0\r\n");
        await Run(ctx, env, "Option1.Value = \"True\"\r\n");
        option1.Value.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task NumericAssignment_RaisesNoError()
    {
        var (_, _, ctx, env) = Recorded();

        var debug = await Run(ctx, env,
            "On Error Resume Next\r\n" +
            "Option1.Value = 1\r\n" +
            "Debug.Print Err.Number\r\n");

        Convert.ToInt64(debug[0].Value).Should().Be(0);
    }

    // ── the same crossing on every OTHER boolean property ────────────────────────────────────────

    [AvaloniaFact]
    public async Task NumberIntoAnyBooleanProperty_IsOrdinaryVB6Assignment()
    {
        // Found while fixing #95 and NOT specific to it: `Command1.Enabled = 0` threw a bare CLR exception —
        // not a VB6 error, so `On Error Resume Next` could not even catch it — for every boolean property on
        // every control. The option button just happened to be the one with an issue open.
        var (canvas, _, ctx, env) = Recorded();
        var option1 = Option(canvas, "Option1");

        await Run(ctx, env, "Option1.Enabled = 0\r\n");
        option1.IsEnabled.Should().BeFalse();

        await Run(ctx, env, "Option1.Enabled = 7\r\n");     // any non-zero, not just 1
        option1.IsEnabled.Should().BeTrue();

        await Run(ctx, env, "Option1.Enabled = 0.4\r\n");   // non-zero → True; it does NOT round to 0
        option1.IsEnabled.Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AnUnparseableStringIntoABoolean_IsATrappableTypeMismatch()
    {
        var (canvas, _, ctx, env) = Recorded();

        var debug = await Run(ctx, env,
            "On Error Resume Next\r\n" +
            "Option1.Enabled = \"banana\"\r\n" +
            "Debug.Print Err.Number\r\n");

        Convert.ToInt64(debug[0].Value).Should().Be(13);            // type mismatch, and trappable
        Option(canvas, "Option1").IsEnabled.Should().BeTrue();      // the failed assignment changed nothing
    }

    [AvaloniaFact]
    public async Task ABooleanIntoANumericProperty_IsMinusOne()
    {
        // The reverse crossing, which VB6 also allows: True widens to -1.
        var (canvas, _, ctx, env) = Recorded();

        await Run(ctx, env, "Option1.Left = True\r\n");

        Canvas.GetLeft(Option(canvas, "Option1")).Should().Be(-1);
    }

    // ── Appearance, the option button's other unreachable property ───────────────────────────────

    [AvaloniaFact]
    public async Task Appearance_IsReadableAndSettableAtRunTime()
    {
        // Registered alongside Value because it was missing for the same reason and raised the same 461.
        // Settable is measured, not assumed: several VB6 appearance-ish properties are design-time only and
        // raise Err 382/383, and this one is not — it reads 1 and takes 0 with no error.
        var (canvas, _, ctx, env) = Recorded();

        var debug = await Run(ctx, env,
            "Debug.Print Option1.Appearance\r\n" +
            "Option1.Appearance = 0\r\n" +
            "Debug.Print Option1.Appearance\r\n");

        Convert.ToInt64(debug[0].Value).Should().Be(1);   // 3D by default
        Convert.ToInt64(debug[1].Value).Should().Be(0);   // Flat
        Option(canvas, "Option1").Appearance.Should().Be(HexIDE.Runtime.BuiltinTypes.VBAppearance.Flat);
    }

    // ── the sibling control is unaffected ────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task CheckBoxValue_IsStillTheTriState()
    {
        var (_, _, ctx, env) = Recorded();

        // Modelling the option button separately must not have flattened the check box with it. 2 is Grayed
        // on a CheckBox — the very value that means True on an OptionButton.
        var debug = await Run(ctx, env,
            "Check1.Value = 2\r\n" +
            "Debug.Print Check1.Value\r\n");

        Convert.ToInt64(debug[0].Value).Should().Be(2);
    }

    // ── a user click travels the same path as an assignment ──────────────────────────────────────

    [AvaloniaFact]
    public void AUserClick_RaisesExactlyOneClick_ForTheNewSelection()
    {
        var (canvas, recorder, _, _) = Recorded();
        recorder.Calls.Clear();

        // Both halves of the event now come from the same IsChecked transition, so a click must not be
        // counted twice — which is the regression an OnClick override plus a property handler would make.
        Option(canvas, "Option3").IsChecked = true;
        Dispatcher.UIThread.RunJobs();

        ClickedNames(recorder).Should().Equal("Option3_Click");
        Option(canvas, "Option3").Value.Should().BeTrue();   // Value follows a click, not only an assignment
        Option(canvas, "Option2").Value.Should().BeFalse();
    }
}
