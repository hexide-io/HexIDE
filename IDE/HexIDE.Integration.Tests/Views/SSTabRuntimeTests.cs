using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Integration.Tests.Views;

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
        var (_, _, pixelCtx, pixelEnv) = ContainerRuntimeHarness.Spawn(
            Form.Replace("Begin VB.Form Form1", "Begin VB.Form Form1\n   ScaleMode = 3"));
        Func<Task> read = async () => await ContainerRuntimeHarness.Run(pixelCtx, pixelEnv, "Debug.Print SSTab1.TabHeight");
        await read.Should().ThrowAsync<NotImplementedException>();
        Func<Task> write = async () => await ContainerRuntimeHarness.Run(pixelCtx, pixelEnv, "SSTab1.TabMaxWidth = 100");
        await write.Should().ThrowAsync<NotImplementedException>();
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
    [InlineData("x = SSTab1.TabPicture(0)")]
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
