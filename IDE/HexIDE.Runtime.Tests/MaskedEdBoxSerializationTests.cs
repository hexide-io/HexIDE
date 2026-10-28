using System;
using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// VB6 SP6's saved files and defaults: docs/vb6-fidelity-oracle/the-maskededbox-mask-text-and-keystrokes.md.
public class MaskedEdBoxSerializationTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) => throw new InvalidOperationException(error);
    }

    private static string Corpus(string folder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls"))) dir = dir.Parent;
        return Vb6TextFile.ReadAllText(Path.Join(dir!.FullName, "corpus", "inbox-controls", folder, "maskedbox", "Form1.frm"));
    }

    private static FormDefinition Read(string source) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;
    private static string Save(FormDefinition form)
    {
        var (text, companion) = new FormSerializer().Serialize(form, "Form1.frm");
        companion.Should().BeNull();
        return text.ReplaceLineEndings("\n");
    }

    [Fact]
    public void The_VB6_saved_form_round_trips_without_an_edit()
    {
        var source = Corpus("saved");
        var form = Read(source);
        form.CanSaveFaithfully.Should().BeTrue();
        Save(form).Should().Be(source.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_reversed_input_normalizes_to_VB6s_save_including_duplicate_OLE_lines_and_no_Text()
    {
        var expected = Corpus("saved").ReplaceLineEndings("\n");
        var input = Corpus("input").ReplaceLineEndings("\n") + "\n' saved\n";
        Save(Read(input)).Should().Be(expected);
    }

    [Fact]
    public void The_saved_form_remains_identical_after_reading_the_save_again()
    {
        var saved = Save(Read(Corpus("saved")));
        Save(Read(saved)).Should().Be(saved);
    }

    [Fact]
    public void An_empty_block_gets_the_measured_size_and_always_writes_PromptChar()
    {
        var form = Read("VERSION 5.00\nBegin VB.Form Form1\n   Begin MSMask.MaskEdBox m\n   End\nEnd\n");
        var box = form.Components.Single(c => c.BaseClass is MaskedEdBoxComponentClass);
        box.GetPropertyOrDefault(VBProperties.WidthProperty).Should().Be(200);
        box.GetPropertyOrDefault(VBProperties.HeightProperty).Should().Be(75);
        Save(form).Should().Contain("      _ExtentX        =   5292\n      _ExtentY        =   1984\n      _Version        =   393216\n      PromptChar      =   \"_\"\n");
    }

    [Fact]
    public void Explicit_measured_defaults_are_omitted_except_PromptChar()
    {
        var minimal = "VERSION 5.00\nBegin VB.Form Form1\n   Begin MSMask.MaskEdBox m\n{0}   End\nEnd\n";
        var defaults = "      BorderStyle = 1\n      Appearance = 1\n      ClipMode = 0\n      MousePointer = 0\n      BackColor = -2147483643\n      ForeColor = -2147483640\n      PromptInclude = -1\n      AllowPrompt = 0\n      AutoTab = 0\n      HideSelection = -1\n      Enabled = -1\n      MaxLength = 64\n      OLEDragMode = 0\n      OLEDropMode = 0\n      Mask = \"\"\n      Format = \"\"\n      PromptChar = \"_\"\n      Text = \"discarded\"\n";
        Save(Read(string.Format(minimal, defaults))).Should().Be(Save(Read(string.Format(minimal, ""))));
    }
}
