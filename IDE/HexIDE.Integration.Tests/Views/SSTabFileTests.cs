using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class SSTabFileTests : WindowTestFixture
{
    private static string Form(string metadata, int commandLeft = -74670, int textLeft = 480) => $$"""
        VERSION 5.00
        Begin VB.Form Form1
           Begin TabDlg.SSTab SSTab1
              Width = 4995
              Height = 3195
              Tabs = 2
              Tab = 1
              TabHeight = 520
              {{metadata}}
              Begin VB.CommandButton Cmd
                 Left = {{commandLeft}}
                 Top = 630
                 Width = 1515
                 Height = 495
              End
              Begin VB.TextBox Txt
                 Left = {{textLeft}}
                 Top = 1230
                 Width = 1215
                 Height = 390
              End
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string Lists = """
        Tab(0).Control(0) = "Cmd"
        Tab(0).ControlCount = 1
        Tab(1).Control(0) = "Txt"
        Tab(1).ControlCount = 1
        """;
    private const string Flags = """
        Tab(0).ControlEnabled = 0
        Tab(1).ControlEnabled = -1
        """;

    [AvaloniaFact]
    public async Task SavedListsRestorePagesAndTabStops()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(Lists + "\n" + Flags));
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print SSTab1.Tab
            Debug.Print Cmd.Left
            Debug.Print Cmd.TabStop
            Debug.Print Txt.Left
            Debug.Print Txt.TabStop
            SSTab1.Tab = 0
            Debug.Print Cmd.Left
            Debug.Print Cmd.TabStop
            Debug.Print Txt.Left
            Debug.Print Txt.TabStop
            """);
        values.Select(v => v.Value).Should().Equal((short)1, -74670f, false, 480f, true, 330f, true, -74520f, false);
    }

    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("Tab(0).ControlEnabled = -1\nTab(1).ControlEnabled = 0")]
    public async Task MissingOrWrongFlagsLeaveZeroWithoutRestoringShowingPage(string flags)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(Lists + "\n" + flags));
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print Cmd.Left
            Debug.Print Cmd.TabStop
            Debug.Print Txt.Left
            Debug.Print Txt.TabStop
            SSTab1.Tab = 0
            Debug.Print Cmd.Left
            Debug.Print Txt.Left
            Debug.Print Txt.TabStop
            SSTab1.Tab = 1
            Debug.Print Txt.Left
            """);
        values.Select(v => v.Value).Should().Equal(-74670f, true, -74520f, false, -74670f, 480f, true, -74520f);
    }

    [AvaloniaFact]
    public async Task NoListDoesNotInferMembershipFromPositions()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(Flags));
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print Cmd.TabStop
            SSTab1.Tab = 0
            Debug.Print Cmd.Left
            SSTab1.Tab = 1
            Debug.Print Cmd.Left
            Debug.Print Txt.Left
            """);
        values.Select(v => v.Value).Should().Equal(true, -74670f, -74670f, 480f);
    }

    [AvaloniaFact]
    public async Task SavedPositionsAreTrustedAndControlEnabledSuffixIsIgnored()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(Lists + "\n" + Flags +
            "\nTab(0).Control(0).Enabled = -1", commandLeft: 330, textLeft: -74520));
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print Cmd.Left
            Debug.Print Cmd.TabStop
            Debug.Print Txt.Left
            SSTab1.Tab = 0
            Debug.Print Cmd.Left
            Debug.Print Txt.Left
            SSTab1.Tab = 1
            Debug.Print Cmd.Left
            Debug.Print Txt.Left
            """);
        values.Select(v => v.Value).Should().Equal(330f, false, -74520f, 330f, -74520f, 330f, -74520f);
    }

    [AvaloniaFact]
    public async Task SavedDimensionsAreHimetricAndRuntimeOnlyFlagsAreIgnored()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form(Flags + """

            TabsPerRow = 1
            Style = 1
            TabOrientation = 2
            TabHeight = 900
            TabMaxWidth = 1500
            WordWrap = 0
            ShowFocusRect = 0
            TabCaption(1) = "&Second"
            TabEnabled(1) = 0
            TabVisible(1) = 0
            """));
        var values = await ContainerRuntimeHarness.Run(ctx, env, """
            Debug.Print SSTab1.Rows
            Debug.Print SSTab1.Style
            Debug.Print SSTab1.TabOrientation
            Debug.Print SSTab1.TabHeight
            Debug.Print SSTab1.TabMaxWidth
            Debug.Print SSTab1.WordWrap
            Debug.Print SSTab1.ShowFocusRect
            Debug.Print SSTab1.Caption
            Debug.Print SSTab1.TabEnabled(1)
            Debug.Print SSTab1.TabVisible(1)
            Debug.Print Txt.Top
            """);
        values.Select(v => v.Value).Should().Equal((short)2, 1L, 2L, 510f, 850f, false, false, "&Second", true, true, 1230f);
    }
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    [AvaloniaTheory]
    [InlineData("sstab")]
    [InlineData("sstab-resaved")]
    public async Task MeasuredCorpusLoadsAndSwitchesButDesignerRemainsReadOnly(string directory)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "corpus")))
            root = root.Parent;
        root.Should().NotBeNull();
        var path = Path.Combine(root!.FullName, "corpus", "inbox-controls", "pending", directory, "Form1.frm");
        var source = File.ReadAllText(path);
        var records = FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(path, ".frx")), source);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Probe"),
            source, new Sink(), records)!;
        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.SSTabPageMetadata);
        var ctx = new ModuleExecutionContext();
        var env = new ExecutionEnvironment();
        var canvas = ((DockPanel)VBLoader.SpawnComponents(form, ctx, env)).Children.OfType<Canvas>().Single();
        if (directory == "sstab")
        {
            var values = await ContainerRuntimeHarness.Run(ctx, env, """
                Debug.Print Command1.Left
                Debug.Print Text1.Left
                Debug.Print Check1.Left
                Debug.Print SSTab1.TabHeight
                SSTab1.Tab = 0
                Debug.Print Command1.Left
                SSTab1.Tab = 2
                Debug.Print Check1.Left
                """);
            values.Select(v => v.Value).Should().Equal(-74670f, 480f, -74370f, 295f, 330f, 630f);
        }
        else
        {
            var tab = (VBSSTab)ContainerRuntimeHarness.Child(canvas, "SSTab3");
            var text = ContainerRuntimeHarness.Child(tab.ChildHost, "Text2");
            Canvas.GetLeft(text).Should().Be(-74520d / 15);
            tab.Tab = 0;
            Canvas.GetLeft(text).Should().Be(480d / 15);
            tab.Tab = 1;
            Canvas.GetLeft(text).Should().Be(-74520d / 15);
        }
    }

}
