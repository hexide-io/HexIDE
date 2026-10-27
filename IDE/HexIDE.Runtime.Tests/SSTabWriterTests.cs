using System;
using System.IO;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class SSTabWriterTests
{
    [Theory]
    [InlineData("sstab")]
    [InlineData("sstab-resaved")]
    public void MeasuredCorpusWritesBackExactlyIncludingCompanion(string directory)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "corpus")))
            root = root.Parent;
        root.Should().NotBeNull();
        var path = Path.Combine(root!.FullName, "corpus", "inbox-controls", "pending", directory, "Form1.frm");
        var source = File.ReadAllText(path);
        var companion = File.ReadAllBytes(Path.ChangeExtension(path, ".frx"));
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Probe"),
            source, new Sink(), FrxDeserializer.Read(companion, source))!;

        var (text, bytes) = new FormSerializer().Serialize(form, "Form1.frm");

        text.ReplaceLineEndings().Should().Be(source.ReplaceLineEndings());
        bytes.Should().Equal(companion);
    }

    [Fact]
    public void EditedExtenderKeepsPageMetadataInMeasuredOrderAndAddsRequiredHeight()
    {
        var source = Form(Lists + "\n" + Flags + """

            _ExtentX = 8811
            _ExtentY = 5636
            _Version = 393216
            TabCaption(0) = "First"
            TabCaption(1) = "Second"
            TabMaxWidth = 1500
            """).Replace("TabHeight = 520", "TabEnabled(0) = 0\nTabVisible(1) = 0");
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Probe"),
            source, new Sink())!;
        var tab = form.Components.Single(c => c.BaseClass is SSTabComponentClass);
        tab.SetProperty(VBProperties.LeftProperty, 20d);

        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        var lines = text.ReplaceLineEndings("\n");
        lines.Should().Contain("Left            =   300");
        lines.Should().Contain("TabHeight       =   520");
        lines.Should().Contain("TabMaxWidth = 1500");
        lines.Should().NotContain("TabEnabled(").And.NotContain("TabVisible(");
        lines.IndexOf("_Version", StringComparison.Ordinal).Should().BeLessThan(lines.IndexOf("TabHeight", StringComparison.Ordinal));
        lines.IndexOf("TabHeight", StringComparison.Ordinal).Should().BeLessThan(lines.IndexOf("TabCaption(0)", StringComparison.Ordinal));
        lines.IndexOf("TabCaption(0)", StringComparison.Ordinal).Should().BeLessThan(lines.IndexOf("Tab(0).ControlEnabled", StringComparison.Ordinal));
        lines.IndexOf("Tab(0).ControlCount", StringComparison.Ordinal).Should().BeLessThan(lines.IndexOf("TabCaption(1)", StringComparison.Ordinal));
        // Membership-changing designer edits are still refused until the designer can maintain these lists.
        form.CanSaveFaithfully.Should().BeFalse();
    }

    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    private static string Form(string metadata) => $$"""
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
                 Left = -74670
                 Top = 630
                 Width = 1515
                 Height = 495
              End
              Begin VB.TextBox Txt
                 Left = 480
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
}
