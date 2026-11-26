using HexIDE.Integration.Tests;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Integration.Tests.Views;

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
        tab.TabHeight.Should().Be(520d * 96 / 2540);
        tab.TabMaxWidth.Should().Be(1500d * 96 / 2540);
        tab.WordWrap.Should().BeFalse();
        tab.ShowFocusRect.Should().BeFalse();
    }
}
