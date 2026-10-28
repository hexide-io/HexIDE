using System;
using System.Linq;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using NSubstitute;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class SSTabDesignerTests : WindowTestFixture
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    private static VBSSTab Preview(string properties)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Probe"), $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin TabDlg.SSTab SSTab1
                  Width = 4995
                  Height = 3195
                  {{properties}}
               End
            End
            Attribute VB_Name = "Form1"
            """, new Sink())!;
        var instance = form.Components.Single(c => c.BaseClass is SSTabComponentClass);
        return (VBSSTab)SSTabComponentClass.Instance.InstantiateForDesigner(instance);
    }

    private static FormEditViewModel Designer()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Probe"), """
            VERSION 5.00
            Begin VB.Form Form1
               ClientWidth = 6000
               ClientHeight = 4000
               Begin TabDlg.SSTab SSTab1
                  Width = 4995
                  Height = 3195
                  _ExtentX = 8811
                  _ExtentY = 5636
                  Tab = 1
                  Tab(0).ControlEnabled = 0
                  Tab(0).Control(0) = "Command1"
                  Tab(0).ControlCount = 1
                  Tab(1).ControlEnabled = -1
                  Tab(1).Control(0) = "Text1"
                  Tab(1).ControlCount = 1
                  Begin VB.CommandButton Command1
                     Left = -74670
                     Top = 630
                     Width = 1515
                     Height = 495
                  End
                  Begin VB.TextBox Text1
                     Left = 480
                     Top = 1230
                     Width = 1215
                     Height = 390
                  End
               End
            End
            Attribute VB_Name = "Form1"
            """, new Sink())!;
        var toolbox = new ToolBoxToolViewModel(Substitute.For<IDocumentDockService>(),
            Substitute.For<IComponentRegistry>(), Substitute.For<ILocalizationService>());
        return new FormEditViewModel(toolbox, Substitute.For<IEventBus>(), null!, null!, null!,
            Substitute.For<ISettingsService>(), Substitute.For<ILocalizationService>()).Initialize(form);
    }

    [AvaloniaFact]
    public void CaptionClickSwitchesDesignerPageAndSavesMeasuredPositionsWithoutUndo()
    {
        var vm = Designer();
        vm.IsReadOnly.Should().BeFalse();
        vm.SwitchSSTabPage(new Point(40, 8)).Should().BeTrue();
        vm.AllComponents.Single(c => c.Name == "Command1").RelativeLeft.Should().Be(22);
        vm.AllComponents.Single(c => c.Name == "Text1").RelativeLeft.Should().Be(-4968);
        vm.CanUndo.Should().BeFalse();
        var (text, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
        text.ReplaceLineEndings("\n").Should().Contain("Left            =   330")
            .And.Contain("Left            =   -74520");
        vm.SwitchSSTabPage(new Point(150, 8)).Should().BeTrue();
        vm.AllComponents.Single(c => c.Name == "Text1").RelativeLeft.Should().Be(32);
    }

    [AvaloniaFact]
    public void DrawnChildJoinsCurrentPageAndSurvivesSaveReload()
    {
        var vm = Designer();
        vm.ToolsBoxToolViewModel.SelectedComponent = vm.ToolsBoxToolViewModel.Components
            .Single(c => c.BaseClass is CommandButtonComponentClass);
        vm.SpawnControlDrawn(new Point(60, 60), new Point(140, 90));
        vm.SwitchSSTabPage(new Point(40, 8)).Should().BeTrue();
        vm.FlushComponentsToModel();
        var added = vm.AllComponents.Single(c => c.Name == "Command2");
        added.RelativeLeft.Should().Be(-4940);
        var (text, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Reload"), text, new Sink())!;
        var tab = form.Components.Single(c => c.BaseClass is SSTabComponentClass);
        tab.GetPropertyOrDefault(SSTabSavedState.Property)!.Number("Tab(1).ControlCount", -1).Should().Be(2);
        tab.UnknownRawPropertyLines.Should().Contain(l => l.Contains("Tab(1).Control(1)") && l.Contains("Command2"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrawnSSTabPropertiesUseCorpusIndentationAtTheirNestingDepth(bool nested)
    {
        var vm = Designer();
        vm.SelectedComponent = vm.AllComponents.Single(c => c.Instance.BaseClass is SSTabComponentClass);
        vm.DeleteSelected();
        if (nested)
        {
            vm.ToolsBoxToolViewModel.SelectedComponent = vm.ToolsBoxToolViewModel.Components
                .Single(c => c.BaseClass is FrameComponentClass);
            vm.SpawnControlDrawn(new Point(0, 0), new Point(350, 250));
        }
        vm.ToolsBoxToolViewModel.SelectedComponent = new ComponentToolViewModel(vm.ToolsBoxToolViewModel,
            SSTabComponentClass.Instance, vm.ToolsBoxToolViewModel.Arrow.Icon);
        vm.SpawnControlDrawn(new Point(20, 30), new Point(300, 220));
        vm.SwitchSSTabPage(new Point(160, 38)).Should().BeTrue();
        vm.ToolsBoxToolViewModel.SelectedComponent = vm.ToolsBoxToolViewModel.Components
            .Single(c => c.BaseClass is CommandButtonComponentClass);
        vm.SpawnControlDrawn(new Point(60, 90), new Point(140, 120));
        vm.FlushComponentsToModel();

        var (text, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "corpus")))
            root = root.Parent;
        root.Should().NotBeNull();
        var corpus = File.ReadAllText(Path.Combine(root!.FullName, "corpus", "inbox-controls", "pending",
            "sstab", "Form1.frm")).ReplaceLineEndings("\n").Split('\n');
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var ownLines = lines.Where(l => l.TrimStart().StartsWith("Tab", StringComparison.Ordinal)
            && !l.TrimStart().StartsWith("TabIndex", StringComparison.Ordinal)).ToArray();
        ownLines.Should().Contain(l => l.TrimStart().StartsWith("Tab ", StringComparison.Ordinal));
        ownLines.Should().Contain(l => l.Contains("Tab(1).Control(0)") && l.Contains("Command1"));
        foreach (var line in ownLines)
        {
            var name = line.TrimStart().Split('=')[0].TrimEnd();
            var measured = corpus.Single(l => l.TrimStart().Split('=')[0].TrimEnd() == name);
            var indentation = measured.Length - measured.TrimStart().Length;
            line.Length.Should().Be(line.TrimStart().Length + indentation + (nested ? 3 : 0), name);
        }
    }

    [AvaloniaFact]
    public void CutAndPasteMovesChildToCurrentPage()
    {
        var vm = Designer();
        vm.SelectedComponent = vm.AllComponents.Single(c => c.Name == "Text1");
        vm.CutSelectedControls();
        vm.SwitchSSTabPage(new Point(40, 8)).Should().BeTrue();
        vm.PasteControls();
        vm.SwitchSSTabPage(new Point(150, 8)).Should().BeTrue();
        var child = vm.AllComponents.Single(c => c.Name == "Text1");
        child.RelativeLeft.Should().Be(-4968);
        vm.FlushComponentsToModel();
        var (text, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
        text.Should().Contain("Tab(0).Control(1)").And.Contain("Tab(1).ControlCount");
        vm.SwitchSSTabPage(new Point(40, 8)).Should().BeTrue();
        child.RelativeLeft.Should().Be(32);
    }

    [AvaloniaFact]
    public void PointerClickReachesCaptionThroughDesignerBitmap()
    {
        var vm = Designer();
        vm.ToolsBoxToolViewModel.SelectedComponent = vm.ToolsBoxToolViewModel.Arrow;
        var view = new FormEditView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var canvas = view.FindControl<HexIDE.Controls.ControlsContainer>("ControlsContainer")!;
            var point = canvas.TranslatePoint(new Point(40, 8), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            vm.AllComponents.Single(c => c.Name == "Command1").RelativeLeft.Should().Be(22);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RenamedHiddenChildAndUndoOfDeletionKeepMembership()
    {
        var vm = Designer();
        var child = vm.AllComponents.Single(c => c.Name == "Command1");
        child.Instance.SetProperty(VBProperties.NameProperty, "Renamed");
        vm.SelectedComponent = child;
        vm.DeleteSelected();
        vm.FlushComponentsToModel();
        vm.UndoStack.Undo();
        vm.FlushComponentsToModel();
        var (text, _) = new FormSerializer().Serialize(vm.FormDefinition!, "Form1.frm");
        text.Should().Contain("\"Renamed\"").And.NotContain("\"Command1\"");
        vm.SwitchSSTabPage(new Point(40, 8)).Should().BeTrue();
        child.RelativeLeft.Should().Be(22);
    }

    [AvaloniaFact]
    public void PreviewUsesSavedCaptionsAndShowingTabWithoutRuntimeTabZeroLoadQuirk()
    {
        var tab = Preview("""
            Tab = 1
            TabCaption(0) = "First"
            TabCaption(1) = "Second"
            TabCaption(2) = "Third"
            Tab(0).ControlEnabled = -1
            """);

        tab.Tab.Should().Be(1);
        Enumerable.Range(0, tab.Tabs).Select(tab.Caption).Should().Equal("First", "Second", "Third");
        tab.ChildHost.Children.Should().BeEmpty();
    }

    [AvaloniaFact]
    public void PreviewUsesSavedStripLayout()
    {
        var tab = Preview("""
            Tabs = 5
            TabsPerRow = 2
            Style = 1
            TabOrientation = 2
            TabHeight = 520
            TabMaxWidth = 1500
            WordWrap = 0
            ShowFocusRect = 0
            """);

        tab.Tabs.Should().Be(5);
        tab.TabsPerRow.Should().Be(2);
        tab.Rows.Should().Be(3);
        tab.VbStyle.Should().Be(1);
        tab.TabOrientation.Should().Be(2);
        tab.TabHeight.Should().Be(295d / 15);
        tab.TabMaxWidth.Should().Be(850d / 15);
        tab.WordWrap.Should().BeFalse();
        tab.ShowFocusRect.Should().BeFalse();
    }
}
