using System;
using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class CommonDialogComponentTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new System.Exception(message);
    }

    [Fact]
    public void A_loaded_dialog_is_a_component_and_instantiates_its_saved_properties()
    {
        var source = """
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComDlg.CommonDialog dlg
                  Left            =   120
                  Top             =   240
                  _ExtentX        =   847
                  _ExtentY        =   847
                  _Version        =   393216
                  Copies          =   3
                  FileName        =   "keep.txt"
                  FontSize        =   12
                  MaxFileSize     =   1024
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        form.UnknownChildSubtreeTexts.Should().BeEmpty();
        var component = form.Components.Single(c => c.BaseClass.VBTypeName == "MSComDlg.CommonDialog");
        component.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(32);
        component.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(32);
        var inBox = (InBoxComponentClass)component.BaseClass;
        inBox.ExtentsToWrite(new ComponentInstance(inBox, "newDialog"), null, null).Should().Be((847, 847));
        var dialog = (VBCommonDialog)((ComponentBaseClass)component.BaseClass).Instantiate(component);
        foreach (var (name, expected) in new[]
        {
            ("Copies", new Vb6Value(3)), ("FileName", new Vb6Value("keep.txt")),
            ("FontSize", new Vb6Value(12f)), ("MaxFileSize", new Vb6Value(1024)),
        })
        {
            dialog.TryGetProperty(name, out var value).Should().BeTrue();
            value.Should().Be(expected);
        }
        dialog.IsVisible.Should().BeFalse();
        new FormSerializer().Serialize(form, "Form1.frm").Item1.ReplaceLineEndings("\n")
            .Should().Contain("""
                  _ExtentX        =   847
                  _ExtentY        =   847
                  _Version        =   393216
                  Copies          =   3
                  FileName        =   "keep.txt"
                  FontSize        =   12
                  MaxFileSize     =   1024
            """.ReplaceLineEndings("\n"));
    }

    private static string CorpusPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Join(directory.FullName, "corpus")))
            directory = directory.Parent;
        directory.Should().NotBeNull();
        return Path.Join(directory!.FullName, "corpus", "inbox-controls", "saved", "commondialog", "Form1.frm");
    }

    [Fact]
    public void VB6s_saved_CommonDialogs_round_trip_byte_for_byte()
    {
        var source = Vb6TextFile.ReadAllText(CorpusPath());
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        form.Components.Select(c => c.GetPropertyOrDefault(VBProperties.NameProperty))
            .Should().ContainInOrder("CommonDialog4", "CommonDialog1", "CommonDialog2", "CommonDialog3");
        var (text, binary) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().Be(source);
        binary.Should().BeNull();
    }

    [Theory]
    [InlineData("CancelError", "0")]
    [InlineData("Color", "0")]
    [InlineData("Copies", "1")]
    [InlineData("DefaultExt", "\"\"")]
    [InlineData("DialogTitle", "\"\"")]
    [InlineData("FileName", "\"\"")]
    [InlineData("Filter", "\"\"")]
    [InlineData("FilterIndex", "0")]
    [InlineData("Flags", "0")]
    [InlineData("FontBold", "0")]
    [InlineData("FontItalic", "0")]
    [InlineData("FontName", "\"\"")]
    [InlineData("FontSize", "8")]
    [InlineData("FontStrikeThru", "0")]
    [InlineData("FontUnderLine", "0")]
    [InlineData("FromPage", "0")]
    [InlineData("HelpCommand", "0")]
    [InlineData("HelpContext", "0")]
    [InlineData("HelpFile", "\"\"")]
    [InlineData("HelpKey", "\"\"")]
    [InlineData("InitDir", "\"\"")]
    [InlineData("Max", "0")]
    [InlineData("Min", "0")]
    [InlineData("MaxFileSize", "260")]
    [InlineData("PrinterDefault", "-1")]
    [InlineData("ToPage", "0")]
    [InlineData("Orientation", "1")]
    public void Own_defaults_are_omitted(string name, string value)
    {
        var source = $"VERSION 5.00\nBegin VB.Form Form1\n   Begin MSComDlg.CommonDialog dlg\n      {name,-16}=   {value}\n   End\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        new FormSerializer().Serialize(form, "Form1.frm").Item1.Should().NotContain($"{name,-16}=");
    }

    [Theory]
    [InlineData("MaxFileSize", 260)]
    [InlineData("FontSize", 8)]
    [InlineData("Copies", 1)]
    [InlineData("Orientation", 1)]
    [InlineData("PrinterDefault", 1)]
    public void Missing_saved_lines_load_the_measured_runtime_defaults(string name, int expected)
    {
        var source = Vb6TextFile.ReadAllText(CorpusPath());
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
        var minimal = form.Components.Where(c => c.GetPropertyOrDefault(VBProperties.NameProperty) is "CommonDialog2" or "CommonDialog3").ToList();
        minimal.Should().HaveCount(2);
        foreach (var component in minimal)
        {
            var dialog = (VBCommonDialog)((ComponentBaseClass)component.BaseClass).Instantiate(component);
            dialog.TryGetProperty(name, out var value).Should().BeTrue();
            Convert.ToDouble(value.Value).Should().Be(expected);
        }
    }
}
