using System.IO;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

public class RichTextBoxPersistenceTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new Exception(error);
    }

    private static FormDefinition Load(string source, byte[]? companion = null) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink(),
            companion is null ? null : FrxDeserializer.Read(companion, source))!;

    private static string Source(string properties) => "VERSION 5.00\r\nBegin VB.Form Form1 \r\n"
        + "   Begin RichTextLib.RichTextBox r \r\n      Width = 4400\r\n      Height = 3000\r\n"
        + properties.ReplaceLineEndings("\r\n") + "\r\n   End\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";

    [Theory]
    [InlineData("      TextRTF = \"{\\rtf1\\ansi\\unknown1 test}\"")]
    [InlineData("      MultiLine = 0\n      TextRTF = \"{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0\\fs20 \\par second}\"")]
    public void Inline_RTF_that_cannot_be_written_holds_the_designer_read_only(string properties)
    {
        var form = Load(Source(properties));
        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
        Action save = () => new FormSerializer().Serialize(form, "Form1.frm");
        save.Should().Throw<NotImplementedException>();
    }

    [Fact]
    public void Explicit_default_own_values_are_omitted_but_ReadOnly_is_preserved_in_its_own_run()
    {
        var form = Load(Source("      MultiLine = -1\n      BorderStyle = 1\n      OLEDropMode = 2\n      ReadOnly = -1  'True\n      HideSelection = -1\n      BackColor = &H80000005&\n      BeginProperty Font {0BE35203-8F91-11CE-9DE3-00AA004BB851}\n         Name = \"MS Sans Serif\"\n         Size = 8.25\n         Charset = 0\n         Weight = 400\n         Underline = 0\n         Italic = 0\n         Strikethrough = 0\n      EndProperty"));
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().NotContain("MultiLine").And.NotContain("BorderStyle").And.NotContain("OLEDropMode").And.NotContain("HideSelection").And.NotContain("BackColor").And.NotContain("BeginProperty Font");
        text.Should().Contain("      Enabled         =   -1  'True\r\n      ReadOnly        =   -1  'True\r\n      TextRTF");
    }

    [Fact]
    public void Recorded_save_round_trips_both_files_byte_for_byte()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Join(root.FullName, "corpus"))) root = root.Parent;
        root.Should().NotBeNull();
        var directory = Path.Join(root!.FullName, "corpus", "inbox-controls");
        var path = Path.Join(directory, "saved", "richtextbox", "Form1.frm");
        var source = File.ReadAllText(path).ReplaceLineEndings("\r\n");
        var bytes = File.ReadAllBytes(Path.ChangeExtension(path, ".frx"));
        var form = Load(source, bytes);
        form.CanSaveFaithfully.Should().BeTrue();
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        text.Should().Be(source);
        companion.Should().Equal(bytes);
    }

    [Fact]
    public void Minimal_block_writes_empty_RTF_and_Enabled_even_at_defaults()
    {
        var (text, companion) = new FormSerializer().Serialize(Load(Source("")), "Form1.frm");
        text.Should().Contain("      Enabled         =   -1  'True\r\n      TextRTF         =   $\"Form1.frx\":0000\r\n");
        var expected = System.Text.Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}\r\n\\viewkind4\\uc1\\pard\\f0\\fs17 \r\n\\par }\r\n");
        companion.Should().Equal([.. BitConverter.GetBytes(expected.Length), .. expected]);
    }

    [Fact]
    public void Inline_RTF_moves_to_a_length_prefixed_companion_and_single_line_keeps_first_paragraph()
    {
        var form = Load(Source("      MultiLine = 0\n      TextRTF = \"{\\rtf1\\ansi{\\fonttbl{\\f0 Arial;}}\\f0\\fs20 Hello \\b bold\\b0\\par second line}\""));
        var (text, companion) = new FormSerializer().Serialize(form, "Other.frm");
        text.Should().Contain("TextRTF         =   $\"Other.frx\":0000");
        var expected = System.Text.Encoding.ASCII.GetBytes("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil Arial;}}\r\n\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs20 Hello \\b bold\r\n\\par }\r\n");
        companion.Should().Equal([.. BitConverter.GetBytes(expected.Length), .. expected]);
    }

    [Fact]
    public void Handwritten_Locked_is_ignored_and_extents_are_recomputed()
    {
        var (text, _) = new FormSerializer().Serialize(Load(Source("      Locked = -1\n      _ExtentX = 7761\n      _ExtentY = 5292")), "Form1.frm");
        text.Should().NotContain("Locked").And.NotContain("ReadOnly");
        text.Should().Contain("      _ExtentX        =   7752\r\n      _ExtentY        =   5292\r\n      _Version        =   393217\r\n");
    }
}
