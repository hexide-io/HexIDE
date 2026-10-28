using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class TabStripSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private static string CorpusPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls"))) dir = dir.Parent;
            var root = Path.Join(dir!.FullName, "corpus", "inbox-controls");
            return Path.Join(root, "saved", "tabstrip", "Form1.frm");
        }
    }

    private static FormDefinition? Read(string source, bool companion = false) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
        companion ? FrxDeserializer.Read(File.ReadAllBytes(Path.ChangeExtension(CorpusPath, ".frx")), source) : null);

    [Fact]
    public void The_measured_save_and_its_ImageList_companion_round_trip()
    {
        var source = Vb6TextFile.ReadAllText(CorpusPath);
        var form = Read(source, true)!;
        form.CanSaveFaithfully.Should().BeTrue();
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        text.ReplaceLineEndings("\n").Should().Be(source.ReplaceLineEndings("\n"));
        companion.Should().Equal(File.ReadAllBytes(Path.ChangeExtension(CorpusPath, ".frx")));
    }

    private const string Head = "VERSION 5.00\nBegin VB.Form Form1 \n   Begin MSComctlLib.TabStrip ts \n      Height = 1500\n      Width = 3000\n      _ExtentX = 5292\n      _ExtentY = 2646\n";
    private const string Tail = "   End\nEnd\nAttribute VB_Name = \"Form1\"\n";

    [Fact]
    public void A_minimal_block_gets_one_empty_tab_and_always_writes_ImageVarType()
    {
        var form = Read(Head + Tail)!;
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().Contain("      _Version        =   393216\n      BeginProperty Tabs {1EFB6598-857C-11D1-B16A-00C0F0283628} \n         NumTabs         =   1\n         BeginProperty Tab1 {1EFB659A-857C-11D1-B16A-00C0F0283628} \n            ImageVarType    =   2\n         EndProperty\n      EndProperty\n");
    }

    [Fact]
    public void Scalar_properties_are_ordered_before_Version_and_defaults_are_omitted()
    {
        var form = Read(Head + "      _Version = 393216\n      TabStyle = 1\n      MultiSelect = -1\n      ShowTips = 0\n      Style = 1\n      MultiRow = -1\n      TabWidthStyle = 2\n      Placement = 0\n      TabFixedWidth = 0\n      HotTracking = 0\n      TabMinWidth = 1000\n" + Tail)!;
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().Contain("      TabWidthStyle   =   2\n      MultiRow        =   -1  'True\n      Style           =   1\n      ShowTips        =   0   'False\n      MultiSelect     =   -1  'True\n      TabStyle        =   1\n      _Version        =   393216\n");
        text.Should().NotContain("TabFixedWidth").And.NotContain("Placement").And.NotContain("HotTracking").And.NotContain("TabMinWidth");
    }

    [Fact]
    public void An_unmeasured_tab_field_keeps_the_designer_read_only_and_refuses_writing()
    {
        var form = Read(Head + "      BeginProperty Tabs\n         NumTabs = 1\n         BeginProperty Tab1\n            Unknown = 1\n         EndProperty\n      EndProperty\n" + Tail)!;
        form.CanSaveFaithfully.Should().BeFalse();
        Action save = () => new FormSerializer().Serialize(form, "Form1.frm");
        save.Should().Throw<NotImplementedException>();
    }

    [Fact]
    public void An_image_key_without_ImageKey_refuses_the_whole_form()
    {
        var sink = new Sink();
        var source = Head + "      BeginProperty Tabs\n         NumTabs = 1\n         BeginProperty Tab1\n            ImageVarType = 8\n            Image = \"blue\"\n         EndProperty\n      EndProperty\n" + Tail;
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, sink).Should().BeNull();
        sink.Errors.Should().ContainSingle().Which.Should().Contain("Type mismatch");
    }

    [Fact]
    public async Task Selection_and_wrong_image_names_are_ignored_and_sizes_load_from_HIMETRIC()
    {
        var form = Read(Head + "      TabFixedWidth = 1200\n      TabFixedHeight = 400\n      TabMinWidth = 600\n      BeginProperty Tabs\n         NumTabs = 2\n         BeginProperty Tab1\n            Caption = \"One\"\n            Key = \"k\"\n            Object.Tag = \"tag\"\n            Object.ToolTipText = \"tip\"\n            Image = 1\n         EndProperty\n         BeginProperty Tab2\n            Selected = -1\n         EndProperty\n      EndProperty\n" + Tail)!;
        var strip = (HexIDE.Runtime.BuiltinControls.VBTabStrip)TabStripComponentClass.Instance.Instantiate(form.Components.Single(c => c.BaseClass is TabStripComponentClass));
        strip.Model.SelectedItem!.Index.Should().Be(1);
        strip.Model.Tabs[0].Caption.Should().Be("One");
        strip.Model.Tabs[0].Key.Should().Be("k");
        (await strip.Tabs.Wrap(strip.Model.Tabs[0]).CallAsync("Tag", []))!.Value.Value.Should().Be("tag");
        (await strip.Tabs.Wrap(strip.Model.Tabs[0]).CallAsync("ToolTipText", []))!.Value.Value.Should().Be("tip");
        strip.Values["TabFixedWidth"].Value.Should().Be(680);
        strip.Values["TabFixedHeight"].Value.Should().Be(226);
        strip.Values["TabMinWidth"].Value.Should().BeOfType<float>().Which.Should().BeApproximately(340.1575f, .0001f);
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        text.Should().NotContain("Selected").And.NotContain("Image           =");
    }
}
