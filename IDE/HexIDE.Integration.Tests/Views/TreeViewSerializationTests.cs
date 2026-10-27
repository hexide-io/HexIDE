using System;
using System.IO;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class TreeViewSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) { }
    }

    private static string CorpusDirectory()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        var corpus = Path.Join(root!.FullName, "corpus", "inbox-controls");
        return Path.Join(corpus, "saved", "treeview");
    }

    private static FormDefinition Load(string source, byte[]? companion = null) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "Project1"), source, new Sink(),
            companion is null ? null : FrxDeserializer.Read(companion, source))!;

    [AvaloniaFact]
    public void Vb6_saved_treeviews_and_their_companion_round_trip_byte_for_byte()
    {
        var directory = CorpusDirectory();
        var source = File.ReadAllText(Path.Join(directory, "Form1.frm"));
        var bytes = File.ReadAllBytes(Path.Join(directory, "Form1.frx"));
        var form = Load(source, bytes);
        form.CanSaveFaithfully.Should().BeTrue();
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().Be(source);
        companion.Should().Equal(bytes);
        // A second load/save must be stable too.
        new FormSerializer().Serialize(Load(text, companion), "Form1.frm").frmText.Should().Be(text);
    }

    [AvaloniaFact]
    public void New_tree_writes_style_seven_appearance_and_version_after_extents()
    {
        var form = Load("Begin VB.Form Form1\r\nEnd\r\n");
        var tree = new ComponentInstance(TreeViewComponentClass.Instance, "TreeView1");
        tree.SetProperty(VBProperties.WidthProperty, 100d);
        tree.SetProperty(VBProperties.HeightProperty, 50d);
        form.UpdateComponents([.. form.Components, tree]);
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().Contain("      _ExtentX        =   2646\n      _ExtentY        =   1323\n      _Version        =   393217\n      Style           =   7\n      Appearance      =   1\n");
    }

    [AvaloniaFact]
    public void Loaded_defaults_are_omitted_and_owned_lines_use_the_measured_order()
    {
        var form = Load("""
            Begin VB.Form Form1
               Begin MSComctlLib.TreeView TreeView1
                  Width = 1500
                  Height = 750
                  OLEDropMode = 1
                  OLEDragMode = 1
                  Style = 0
                  PathSeparator = "\"
                  Indentation = 1000
                  HideSelection = -1
                  LineStyle = 0
                  LabelEdit = 0
                  Scroll = -1
                  BorderStyle = 0
                  _Version = 393217
                  _ExtentX = 2646
                  _ExtentY = 1323
               End
            End
            """);
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().NotContain("Indentation").And.NotContain("PathSeparator").And.NotContain("Style");
        text.Should().Contain("      Appearance      =   1\n      OLEDragMode = 1\n      OLEDropMode = 1\n");
    }

    [AvaloniaFact]
    public void Resizing_changes_extents_without_reordering_or_dropping_own_properties()
    {
        var directory = CorpusDirectory();
        var source = File.ReadAllText(Path.Join(directory, "Form1.frm"));
        var form = Load(source, File.ReadAllBytes(Path.Join(directory, "Form1.frx")));
        var tree = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "TreeView1");
        tree.SetProperty(VBProperties.WidthProperty, 100d);
        var text = new FormSerializer().Serialize(form, "Form1.frm").frmText.ReplaceLineEndings("\n");
        text.Should().Contain("      _ExtentX        =   2646\n      _ExtentY        =   4419\n      _Version        =   393217\n      HideSelection");
        text.Should().Contain("      Indentation     =   529");
        text.Split("OLEDragMode").Length.Should().Be(2);
        // Own properties have no designer editor until a faithful value writer exists.
        tree.BaseClass.PropertiesByName.Should().NotContainKey("ImageList");
        tree.BaseClass.PropertiesByName.Should().NotContainKey("Indentation");
    }
}
