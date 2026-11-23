using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class SSTabRuntimeTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin TabDlg.SSTab SSTab1
              Left = 0
              Top = 0
              Width = 4995
              Height = 3195
              Begin VB.TextBox Text1
                 Left = 330
                 Top = 600
                 Width = 1215
                 Height = 300
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public async Task ProgramCanSwitchPagesAndRestorePositionsAndTabStop()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var tab = ContainerRuntimeHarness.Child(canvas, "SSTab1");
        var host = ((IVBContainerControl)tab).ChildHost!;
        host.Children.Should().HaveCount(1);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            SSTab1.Tab = 1
            Debug.Print Text1.Left
            Debug.Print Text1.TabStop
            SSTab1.Tab = 0
            Debug.Print Text1.Left
            Debug.Print Text1.TabStop
            Debug.Print Text1.Visible
            """);
        values.Select(v => v.Value).Should().Equal(-74670f, false, 330f, true, true);
    }

    [AvaloniaFact]
    public async Task DefaultCountsCaptionsAndIndexedMembersAreAvailable()
    {
        var library = ControlLibraries.All.Single(l => l.Name == "TabDlg");
        library.Controls["SSTab"].Should().Be(1090);
        library.TryBare("ssStylePropertyPage", out var style).Should().BeTrue();
        style.Value.Should().Be(1L);
        library.TryInContainer("TabOrientationConstants", "ssTabOrientationRight", out var orientation).Should().BeTrue();
        orientation.Value.Should().Be(3L);
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print SSTab1.Tabs
            Debug.Print SSTab1.Tab
            Debug.Print SSTab1.TabsPerRow
            Debug.Print SSTab1.Rows
            Debug.Print SSTab1.Caption
            SSTab1.TabCaption(1) = "&Second"
            SSTab1.TabEnabled(1) = False
            SSTab1.Tab = 1
            Debug.Print SSTab1.Caption
            Debug.Print SSTab1.TabEnabled(1)
            SSTab1.TabVisible(1) = False
            Debug.Print SSTab1.Tab
            SSTab1.Tabs = 5
            Debug.Print SSTab1.TabCaption(4)
            """);
        values.Select(v => v.Value).Should().Equal((short)3, (short)0, (short)3, (short)1, "Tab 0", "&Second", false, (short)0, "Tab 4");
    }

    [AvaloniaTheory]
    [InlineData("Tab = -1", 380)]
    [InlineData("Tab = 3", 380)]
    [InlineData("Tabs = 100", 380)]
    [InlineData("TabsPerRow = 0", 380)]
    [InlineData("Style = 2", 380)]
    [InlineData("TabOrientation = 4", 380)]
    [InlineData("TabHeight = 0", 380)]
    [InlineData("TabHeight = -1", 380)]
    [InlineData("TabMaxWidth = -1", 380)]
    [InlineData("Rows = 2", 383)]
    [InlineData("TabCaption(3) = \"x\"", 381)]
    public async Task MeasuredErrorsAreTrappable(string assignment, int error)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env,
            "On Error Resume Next\nSSTab1." + assignment + "\nDebug.Print Err.Number\n");
        values.Single().Value.Should().Be((long)error);
    }

    [AvaloniaFact]
    public async Task InertDefaultMemberDoesNotReplaceTheControl()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            SSTab1.Tab = 2
            Dim v
            v = SSTab1
            Debug.Print v
            SSTab1 = 1
            Debug.Print SSTab1.Tab
            """);
        values.Select(v => v.Value).Should().Equal((short)0, (short)2);
    }

    // Measured by #1090 in docs/vb6-fidelity-oracle/in-box-controls-the-sstab.md.
    [AvaloniaTheory]
    [InlineData("", "SSTab1.", "")]
    [InlineData("Dim o As Object\nSet o = SSTab1", "o.", "")]
    [InlineData("With SSTab1", ".", "End With")]
    public async Task ExplicitDefaultMemberIsInert(string setup, string receiver, string cleanup)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, $$"""
            SSTab1.Tab = 2
            {{setup}}
            Debug.Print {{receiver}}[_Tab]
            On Error Resume Next
            {{receiver}}[_Tab] = 1
            Debug.Print Err.Number
            {{cleanup}}
            Debug.Print SSTab1.Tab
            """);
        values.Select(v => v.Value).Should().Equal((short)0, 0L, (short)2);
    }

    [AvaloniaFact]
    public async Task UnknownBracketedMemberFailsOnlyWhenCalled()
    {
        const string procedure = """
            Sub ReadUnknown()
                Debug.Print Text1.[_Nothing]
            End Sub
            """;
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, procedure + "\nDebug.Print 1");
        values.Single().Value.Should().Be((short)1);

        Func<Task> call = async () => await ContainerRuntimeHarness.Run(ctx, env, procedure + "\nReadUnknown");
        await call.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [AvaloniaFact]
    public async Task DisabledContainerAffectsChildEnabledRead()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Laid(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            SSTab1.Enabled = False
            Debug.Print Text1.Enabled
            SSTab1.Enabled = True
            Debug.Print Text1.Enabled
            """);
        values.Select(v => v.Value).Should().Equal(false, true);
        Func<Task> unmeasured = async () => await ContainerRuntimeHarness.Run(ctx, env, "SSTab1.Enabled = Null");
        await unmeasured.Should().ThrowAsync<NotImplementedException>();
    }

    [AvaloniaFact]
    public async Task MeasuredPropertyDefaultsAndWrites()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print TypeName(SSTab1)
            Debug.Print SSTab1.Style
            Debug.Print SSTab1.TabOrientation
            Debug.Print SSTab1.TabHeight
            Debug.Print SSTab1.TabMaxWidth
            Debug.Print SSTab1.WordWrap
            Debug.Print SSTab1.ShowFocusRect
            SSTab1.TabsPerRow = 2
            Debug.Print SSTab1.Rows
            SSTab1.TabVisible(2) = False
            Debug.Print SSTab1.Rows
            SSTab1.Style = 1
            SSTab1.TabOrientation = 3
            SSTab1.TabHeight = 500
            SSTab1.TabMaxWidth = 1500
            SSTab1.WordWrap = False
            SSTab1.ShowFocusRect = False
            SSTab1.Caption = "&First"
            Debug.Print SSTab1.Style
            Debug.Print SSTab1.TabOrientation
            Debug.Print SSTab1.TabHeight
            Debug.Print SSTab1.TabMaxWidth
            Debug.Print SSTab1.WordWrap
            Debug.Print SSTab1.ShowFocusRect
            Debug.Print SSTab1.TabCaption(0)
            SSTab1.TabVisible(0) = False
            SSTab1.TabVisible(1) = False
            Debug.Print SSTab1.Rows
            On Error Resume Next
            SSTab1.Tab = 0
            Debug.Print Err.Number
            """);
        values.Select(v => v.Value).Should().Equal("SSTab", 0L, 0L, 295f, 0f, true, true,
            (short)2, (short)1, 1L, 3L, 500f, 1500f, false, false, "&First", (short)0, 380L);
        var (pixels, errors) = ControlsAddTests.Run("""
            Debug.Print CStr(SSTab1.TabHeight)
            SSTab1.TabMaxWidth = 20.5
            Debug.Print CStr(SSTab1.TabMaxWidth)
            ScaleMode = 1
            Debug.Print CStr(SSTab1.TabMaxWidth)
            """, "", Form.Replace("Begin VB.Form Form1", "Begin VB.Form Form1\n   ScaleMode = 3"));
        errors.Should().BeEmpty();
        pixels.Should().Equal("20", "20", "307");
    }

    // Assignment measurements: #4953's final comment, VB6 SP6 native and p-code.
    [AvaloniaTheory]
    [InlineData("TabHeight", 1, "300.4", 300, 20)]
    [InlineData("TabHeight", 1, "300.5", 300, 20)]
    [InlineData("TabHeight", 1, "300.6", 300, 20)]
    [InlineData("TabHeight", 1, "301", 301, 20)]
    [InlineData("TabHeight", 1, "308", 308, 21)]
    [InlineData("TabHeight", 1, "0.4", 1, 0)]
    [InlineData("TabHeight", 1, "0.6", 1, 0)]
    [InlineData("TabHeight", 3, "20.4", 306, 20)]
    [InlineData("TabHeight", 3, "20.5", 307, 20)]
    [InlineData("TabHeight", 3, "20.6", 309, 21)]
    [InlineData("TabHeight", 3, "1", 15, 1)]
    [InlineData("TabHeight", 3, "0.4", 6, 0)]
    [InlineData("TabMaxWidth", 1, "300.5", 300, 20)]
    [InlineData("TabMaxWidth", 1, "0.6", 1, 0)]
    [InlineData("TabMaxWidth", 3, "20.5", 307, 20)]
    public void AssignedDimensionsRoundInHimetric(string property, int mode, string assigned, int twips, int pixels)
    {
        var (values, errors) = ControlsAddTests.Run(
            $"ScaleMode = {mode}\nSSTab1.{property} = {assigned}\nScaleMode = 1\nDebug.Print CStr(SSTab1.{property})\nScaleMode = 3\nDebug.Print CStr(SSTab1.{property})", "", Form);
        errors.Should().BeEmpty();
        values.Should().Equal($"{twips}", $"{pixels}");
    }

    [AvaloniaTheory]
    [InlineData("TabHeight", "300.4")]
    [InlineData("TabHeight", "300.5")]
    [InlineData("TabMaxWidth", "300.5")]
    public void RecordedAssignedTwipReadsRemainUnchanged(string property, string assigned)
    {
        var (values, errors) = ControlsAddTests.Run(
            $"SSTab1.{property} = {assigned}\nDebug.Print CStr(SSTab1.{property})", "", Form);
        errors.Should().BeEmpty();
        values.Should().Equal("300");
    }

    // #4970 / oracle PR #5210: docs/vb6-fidelity-oracle/in-box-controls-the-sstab.md,
    // "Assigning in every ScaleMode, the edges, a flipped scale, and 1,100 reads" (SP6 native and p-code).
    [AvaloniaTheory]
    [InlineData("TabHeight", 2, "10", 353)]
    [InlineData("TabHeight", 2, "11", 388)]
    [InlineData("TabHeight", 2, "12", 423)]
    [InlineData("TabHeight", 2, "13", 459)]
    [InlineData("TabHeight", 2, "14", 494)]
    [InlineData("TabHeight", 2, "14.9", 526)]
    [InlineData("TabHeight", 2, "15", 529)]
    [InlineData("TabHeight", 2, "15.01", 530)]
    [InlineData("TabHeight", 2, "15.05", 531)]
    [InlineData("TabHeight", 2, "20", 706)]
    [InlineData("TabHeight", 4, "0.75", 159)]
    [InlineData("TabHeight", 4, "1", 212)]
    [InlineData("TabHeight", 4, "2", 423)]
    [InlineData("TabHeight", 4, "2.5", 529)]
    [InlineData("TabHeight", 5, "0.2", 508)]
    [InlineData("TabHeight", 5, "0.2083", 529)]
    [InlineData("TabHeight", 5, "0.5", 1270)]
    [InlineData("TabHeight", 6, "5.27", 527)]
    [InlineData("TabHeight", 6, "5.274", 527)]
    [InlineData("TabHeight", 6, "5.276", 528)]
    [InlineData("TabHeight", 7, "0.527", 527)]
    [InlineData("TabHeight", 7, "0.5274", 527)]
    [InlineData("TabHeight", 7, "0.5276", 528)]
    [InlineData("TabHeight", 0, "31.25", 529)]
    [InlineData("TabHeight", 0, "1", 17)]
    [InlineData("TabHeight", 0, "2.5", 42)]
    [InlineData("TabHeight", 0, "0.05", 1)]
    [InlineData("TabHeight", 1, "0.2", 0)]
    [InlineData("TabHeight", 1, "0.28", 0)]
    [InlineData("TabHeight", 6, "0.004", 0)]
    [InlineData("TabHeight", 6, "0.005", 0)]
    [InlineData("TabHeight", 1, "0.29", 1)]
    [InlineData("TabHeight", 4, "1.5", 317)]
    [InlineData("TabHeight", 1, "36", 63)]
    [InlineData("TabHeight", 1, "900", 1587)]
    [InlineData("TabMaxWidth", 2, "10", 353)]
    [InlineData("TabMaxWidth", 2, "11", 388)]
    [InlineData("TabMaxWidth", 2, "12", 423)]
    [InlineData("TabMaxWidth", 2, "13", 459)]
    [InlineData("TabMaxWidth", 2, "14", 494)]
    [InlineData("TabMaxWidth", 2, "14.9", 526)]
    [InlineData("TabMaxWidth", 2, "15", 529)]
    [InlineData("TabMaxWidth", 2, "15.01", 530)]
    [InlineData("TabMaxWidth", 2, "15.05", 531)]
    [InlineData("TabMaxWidth", 2, "20", 706)]
    [InlineData("TabMaxWidth", 4, "0.75", 159)]
    [InlineData("TabMaxWidth", 4, "1", 212)]
    [InlineData("TabMaxWidth", 4, "2", 423)]
    [InlineData("TabMaxWidth", 4, "2.5", 529)]
    [InlineData("TabMaxWidth", 5, "0.2", 508)]
    [InlineData("TabMaxWidth", 5, "0.2083", 529)]
    [InlineData("TabMaxWidth", 5, "0.5", 1270)]
    [InlineData("TabMaxWidth", 6, "5.27", 527)]
    [InlineData("TabMaxWidth", 6, "5.274", 527)]
    [InlineData("TabMaxWidth", 6, "5.276", 528)]
    [InlineData("TabMaxWidth", 7, "0.527", 527)]
    [InlineData("TabMaxWidth", 7, "0.5274", 527)]
    [InlineData("TabMaxWidth", 7, "0.5276", 528)]
    [InlineData("TabMaxWidth", 0, "31.25", 529)]
    [InlineData("TabMaxWidth", 0, "1", 17)]
    [InlineData("TabMaxWidth", 0, "2.5", 42)]
    [InlineData("TabMaxWidth", 0, "0.05", 1)]
    [InlineData("TabMaxWidth", 1, "0.2", 0)]
    [InlineData("TabMaxWidth", 1, "0.28", 0)]
    [InlineData("TabMaxWidth", 6, "0.004", 0)]
    [InlineData("TabMaxWidth", 6, "0.005", 0)]
    [InlineData("TabMaxWidth", 1, "0.29", 1)]
    [InlineData("TabMaxWidth", 4, "1.5", 317)]
    [InlineData("TabMaxWidth", 1, "36", 63)]
    [InlineData("TabMaxWidth", 1, "900", 1587)]
    public void AssignmentsInEveryScaleKeepWholeHimetric(string property, int mode, string assigned, int kept)
    {
        var designer = Form.Replace("Begin VB.Form Form1", "Begin VB.Form Form1\n   ClientWidth = 9600\n   ClientHeight = 7200");
        var setup = mode == 0 ? "ScaleWidth = 1000\nScaleHeight = 100" : $"ScaleMode = {mode}";
        double? stored = null;
        var (values, errors) = ControlsAddTests.Run($$"""
            {{setup}}
            On Error Resume Next
            SSTab1.{{property}} = {{assigned}}
            Debug.Print CStr(Err.Number)
            ScaleMode = 2
            Debug.Print SSTab1.{{property}} = CSng({{kept}}# * CDbl(CSng(1440# / 2540)) / 20)
            """, "", designer, inspect: window =>
            {
                var tab = window.GetVisualDescendants().OfType<VBSSTab>().SingleOrDefault();
                stored = property == "TabHeight" ? tab?.TabHeightHimetric : tab?.TabMaxWidthHimetric;
            });
        errors.Should().BeEmpty();
        stored.Should().Be(kept);
        double.IsNegative(stored!.Value).Should().BeFalse("a kept zero is +0, not the -0 Math.Ceiling gives");
        values.Should().Equal("0", "True");
    }

    // The 5.274 mm and 0.5274 cm rows above are the same 527.4 HIMETRIC in twips.
    [AvaloniaTheory]
    [InlineData("TabHeight")]
    [InlineData("TabMaxWidth")]
    public void EquivalentTwipAssignmentKeepsTheSameWholeHimetric(string property)
    {
        double? stored = null;
        var (_, errors) = ControlsAddTests.Run($"SSTab1.{property} = 5.274 * 1440 / 25.4", "", Form,
            inspect: window =>
            {
                var tab = window.GetVisualDescendants().OfType<VBSSTab>().SingleOrDefault();
                stored = property == "TabHeight" ? tab?.TabHeightHimetric : tab?.TabMaxWidthHimetric;
            });
        errors.Should().BeEmpty();
        stored.Should().Be(527);
    }

    [AvaloniaTheory]
    [InlineData("ScaleMode = 6\nDebug.Print SSTab1.TabHeight")]
    [InlineData("ScaleMode = 7\nDebug.Print SSTab1.TabMaxWidth")]
    [InlineData("ScaleWidth = 1000\nDebug.Print SSTab1.TabHeight")]
    [InlineData("ScaleWidth = -1000\nDebug.Print SSTab1.TabHeight")]
    [InlineData("ScaleWidth = -1000\nDebug.Print SSTab1.TabMaxWidth")]
    public void RemainingDimensionCasesNameFollowup(string code)
    {
        var (_, errors) = ControlsAddTests.Run(code, "", Form);
        errors.Should().ContainSingle().Which.Should().BeOfType<NotImplementedException>()
            .Which.Message.Should().Contain("#5439");
    }

    [AvaloniaFact]
    public async Task RefusedReductionSelectsZeroAndClickReportsPreviousTabAfterMovement()
    {
        var (canvas, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            SSTab1.Tab = 2
            Text1.Left = 330
            SSTab1.Tab = 0
            SSTab1.Tab = 2
            On Error Resume Next
            SSTab1.Tabs = 2
            Debug.Print Err.Number
            Debug.Print SSTab1.Tab
            Debug.Print SSTab1.Tabs
            """);
        values.Select(v => v.Value).Should().Equal(380L, (short)0, (short)3);
        recorder.Calls.Where(c => c.Name == "SSTab1_Click").Select(c => c.Args![0].Value)
            .Should().Equal((short)0, (short)2, (short)0, (short)2);
        await ContainerRuntimeHarness.Run(ctx, env, "SSTab1.Tab = 0");
        recorder.Calls.Count(c => c.Name == "SSTab1_Click").Should().Be(4);
        ContainerRuntimeHarness.Child(canvas, "SSTab1").Should().BeAssignableTo<IVBContainerControl>();
    }

    [AvaloniaTheory]
    [InlineData("Tab = 1.5")]
    [InlineData("TabEnabled(0) = 0")]
    [InlineData("Caption = 1")]
    [InlineData("Tabs = 0")]
    [InlineData("x = SSTab1.hWnd")]
    public async Task UnimplementedMembersAndUnmeasuredConversionsRefuse(string statement)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var code = statement.StartsWith("x", StringComparison.Ordinal) ? statement : "SSTab1." + statement;
        Func<Task> run = async () => await ContainerRuntimeHarness.Run(ctx, env, code);
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [AvaloniaFact]
    public async Task LayoutPropertiesMoveOnlyTheMeasuredChildren()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            SSTab1.Tab = 1
            SSTab1.Tabs = 5
            Debug.Print Text1.Top
            SSTab1.Tabs = 3
            Debug.Print Text1.Top
            SSTab1.TabOrientation = 1
            Debug.Print Text1.Top
            SSTab1.TabOrientation = 2
            Debug.Print Text1.Left
            Debug.Print Text1.Top
            SSTab1.TabOrientation = 0
            Debug.Print Text1.Left
            Debug.Print Text1.Top
            SSTab1.TabsPerRow = 1
            SSTab1.TabHeight = 600
            SSTab1.TabVisible(2) = False
            Debug.Print Text1.Top
            """);
        values.Select(v => v.Value).Should().Equal(900f, 600f, 300f, -74370f, 300f, -74670f, 600f, 600f);
    }

    [AvaloniaFact]
    public async Task FrameChildAlsoReadsEffectiveEnabled()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Laid(Form.Replace("TabDlg.SSTab SSTab1", "VB.Frame SSTab1"));
        var values = await ContainerRuntimeHarness.Run(ctx, env,
            "SSTab1.Enabled = False\nDebug.Print Text1.Enabled\nSSTab1.Enabled = True\nDebug.Print Text1.Enabled");
        values.Select(v => v.Value).Should().Equal(false, true);
    }

    [AvaloniaFact]
    public void MissingExtentBasisStillRefuses()
    {
        Action extent = () => _ = InBoxComponentClass.Implemented.Single(c => c.ControlClassName == "SSTab").DefaultExtent;
        extent.Should().Throw<NotImplementedException>().WithMessage("SSTab extents*");
    }

}
