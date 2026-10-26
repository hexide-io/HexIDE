using System;
using System.IO;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A ProgressBar read and written as VB6 writes it (#1082). Every expectation is VB6's own save: the forms under
/// corpus/inbox-controls/saved, which <see cref="SerializationCorpusTests"/> round-trips whole, and the oracle's "In-box
/// controls: the ProgressBar". These pin what a whole-file round trip can't, which is what the writer does with a control
/// that changed.
/// </summary>
public class ProgressBarSerializationTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Head6 = "VERSION 5.00\r\nObject = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n";
    private const string Head5 = "VERSION 5.00\r\nObject = \"{6B7E6392-850A-101B-AFC0-4210102A8DA7}#1.3#0\"; \"COMCTL32.OCX\"\r\n";

    private const string FormOpen =
        "Begin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3015\r\n   ClientLeft      =   60\r\n"
        + "   ClientTop       =   345\r\n   ClientWidth     =   4560\r\n   LinkTopic       =   \"Form1\"\r\n   ScaleHeight     =   3015\r\n"
        + "   ScaleWidth      =   4560\r\n   StartUpPosition =   3  'Windows Default\r\n";

    private const string FormClose = "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private static string Line(string name, string value) => $"      {name,-16}=   {value}\r\n";

    private static string Block(string library, params string[] lines) =>
        $"   Begin {library}.ProgressBar pb \r\n" + string.Concat(lines) + "   End\r\n";

    // The block VB6 saved for corpus/inbox-controls/saved/window: every own property off its default, Tag set in the window.
    private static readonly string WindowBlock = Block("MSComctlLib",
        Line("Height", "375"), Line("Left", "120"), Line("TabIndex", "0"), Line("Tag", "\"x\""), Line("Top", "120"),
        Line("Width", "2655"), Line("_ExtentX", "4683"), Line("_ExtentY", "661"), Line("_Version", "393216"),
        Line("BorderStyle", "1"), Line("Appearance", "1"), Line("MousePointer", "99"), Line("Enabled", "0   'False"),
        Line("OLEDropMode", "1"), Line("Max", "50"), Line("Orientation", "1"), Line("Scrolling", "1"));

    private static (FormDefinition Form, ComponentInstance Bar) Read(string head, string block)
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            head + FormOpen + block + FormClose, new NullSink())!;
        return (form, form.Components.Single(c => c.BaseClass is ProgressBarComponentClass));
    }

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").Item1;

    [Fact]
    public void A_ProgressBar_is_read_as_one_with_its_properties()
    {
        var (_, bar) = Read(Head6, WindowBlock);

        bar.GetPropertyOrDefault(VBProperties.SingleMaxProperty).Should().Be(50f);
        bar.GetPropertyOrDefault(VBProperties.SingleMinProperty).Should().Be(0f);
        bar.GetPropertyOrDefault(VBProperties.OrientationProperty).Should().Be(VBOrientation.Vertical);
        bar.GetPropertyOrDefault(VBProperties.ScrollingProperty).Should().Be(VBScrolling.Smooth);
        bar.GetPropertyOrDefault(VBProperties.BorderStyleProperty).Should().Be(VBBorder.FixedSingle);
        bar.GetPropertyOrDefault(VBProperties.AppearanceProperty).Should().Be(VBAppearance._3D);
        bar.GetPropertyOrDefault(VBProperties.MousePointerProperty).Should().Be(VBCursorType.Custom);
        bar.GetPropertyOrDefault(VBProperties.EnabledProperty).Should().BeFalse();
        bar.GetPropertyOrDefault(VBProperties.OLEDropModeProperty).Should().Be(VBOLEDropMode.Manual);
        bar.UnknownRawProperties.Select(p => p.Name).Should().Equal("_ExtentX", "_ExtentY", "_Version");
    }

    // VB6 writes a control's own properties in its own order after its extender's, whatever order the file had them in.
    [Fact]
    public void Own_properties_are_written_after_the_extenders_in_VB6s_order()
    {
        var scrambled = Block("MSComctlLib",
            Line("Scrolling", "1"), Line("Orientation", "1"), Line("Max", "50"), Line("OLEDropMode", "1"), Line("Enabled", "0   'False"),
            Line("MousePointer", "99"), Line("Appearance", "1"), Line("BorderStyle", "1"), Line("_Version", "393216"),
            Line("_ExtentY", "661"), Line("_ExtentX", "4683"), Line("Width", "2655"), Line("Top", "120"), Line("Tag", "\"x\""),
            Line("TabIndex", "0"), Line("Left", "120"), Line("Height", "375"));

        Save(Read(Head6, scrambled).Form).Should().Contain(WindowBlock);
    }

    // Appearance is written at its default, the rest only where they differ from it (measured on every saved block).
    [Fact]
    public void Defaults_are_left_out_but_Appearance_is_always_written()
    {
        var (form, _) = Read(Head6, Block("MSComctlLib", Line("Height", "375"), Line("Left", "120"), Line("TabIndex", "0"),
            Line("Top", "120"), Line("Width", "2655"), Line("_ExtentX", "4683"), Line("_ExtentY", "661"), Line("_Version", "393216"),
            Line("BorderStyle", "0"), Line("MousePointer", "0"), Line("Min", "0"), Line("Max", "100"), Line("Orientation", "0")));

        Save(form).Should().Contain(Line("_Version", "393216") + Line("Appearance", "1") + "   End\r\n");
    }

    // Measured: VB6 set _ExtentX to 3493 for 132 px, 4128 for 156 px and _ExtentY to 4763 for 180 px.
    [Theory]
    [InlineData(132, 180, "3493", "4763")]
    [InlineData(156, 25, "4128", "661")]
    [InlineData(177, 50, "4683", "1323")]
    public void A_sized_control_takes_its_sizes_extents(int width, int height, string extentX, string extentY)
    {
        var (form, bar) = Read(Head6, WindowBlock);

        bar.SetProperty(VBProperties.WidthProperty, width);
        bar.SetProperty(VBProperties.HeightProperty, height);

        Save(form).Should().Contain(Line("_ExtentX", extentX) + Line("_ExtentY", extentY) + Line("_Version", "393216"));
    }

    // Measured: a hand-written block whose extents disagreed with its Width and Height kept them through VB6's save, and
    // one with none got the control's default, 2646 and 1323.
    [Fact]
    public void Until_it_is_sized_a_control_keeps_the_extents_it_was_read_with_or_the_default()
    {
        var disagreeing = Block("MSComctlLib", Line("Height", "211"), Line("Left", "120"), Line("TabIndex", "1"), Line("Top", "600"),
            Line("Width", "1234"), Line("_ExtentX", "2646"), Line("_ExtentY", "1323"), Line("_Version", "393216"), Line("Appearance", "1"));
        Save(Read(Head6, disagreeing).Form).Should().Contain(disagreeing);

        var none = Read(Head6, Block("MSComctlLib", Line("Height", "375"), Line("Left", "120"), Line("Top", "120"), Line("Width", "2655")));
        Save(none.Form).Should().Contain(Line("_ExtentX", "2646") + Line("_ExtentY", "1323"));

        none.Bar.SetProperty(VBProperties.WidthProperty, 177d + 1);
        Save(none.Form).Should().Contain(Line("_ExtentX", "4710") + Line("_ExtentY", "661"));
    }

    // Measured: a 5.0 ProgressBar is written back under ComctlLib, and its _Version is 327682 where a 6.0 one's is 393216.
    [Theory]
    [InlineData("ComctlLib", "327682")]
    [InlineData("MSComctlLib", "393216")]
    public void A_control_keeps_the_library_it_was_read_under(string library, string version)
    {
        var (form, bar) = Read(library == "ComctlLib" ? Head5 : Head6,
            Block(library, Line("Height", "375"), Line("Left", "120"), Line("TabIndex", "0"), Line("Top", "120"), Line("Width", "2655"),
                Line("_ExtentX", "4683"), Line("_ExtentY", "661")));

        bar.BaseClass.Should().BeSameAs(ProgressBarComponentClass.Instance);
        Save(form).Should().Contain($"   Begin {library}.ProgressBar pb \r\n").And.Contain(Line("_Version", version));
    }

    [Fact]
    public void A_ProgressBar_made_rather_than_read_is_written_as_VB6_would_place_it()
    {
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Head6 + FormOpen + FormClose,
            new NullSink())!;
        var made = new ComponentInstance(ProgressBarComponentClass.Instance, "pbNew")
            .SetProperty(VBProperties.LeftProperty, 8)
            .SetProperty(VBProperties.TopProperty, 8)
            .SetProperty(VBProperties.WidthProperty, 177)
            .SetProperty(VBProperties.HeightProperty, 25)
            .SetProperty(VBProperties.TabIndexProperty, 0);
        form.UpdateComponents([.. form.Components, made]);

        Save(form).Should().Contain("   Begin MSComctlLib.ProgressBar pbNew \r\n" + Line("Height", "375") + Line("Left", "120")
            + Line("TabIndex", "0") + Line("Top", "120") + Line("Width", "2655") + Line("_ExtentX", "4683") + Line("_ExtentY", "661")
            + Line("_Version", "393216") + Line("Appearance", "1") + "   End\r\n");
    }

    [Fact]
    public void A_loaded_ProgressBar_answers_its_own_Value_line_as_VB6_does_by_carrying_it()
    {
        // VB6 doesn't save Value (measured: a hand-written Value = 20 was gone after its save). HexIDE carries a line it
        // doesn't model rather than lose it; VB6 reads the file and ignores it.
        var (form, bar) = Read(Head6, Block("MSComctlLib", Line("Height", "375"), Line("Left", "120"), Line("Top", "120"),
            Line("Width", "2655"), Line("Value", "20")));

        bar.UnknownRawProperties.Select(p => p.Name).Should().Contain("Value");
        Save(form).Should().Contain(Line("Value", "20"));
    }

    [Fact]
    public void The_corpus_saves_are_read_as_ProgressBars()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Join(dir.FullName, "corpus", "inbox-controls")))
            dir = dir.Parent;
        dir.Should().NotBeNull("corpus/inbox-controls ships with the repository");
        var saved = Path.Join(dir!.FullName, "corpus", "inbox-controls", "saved");

        var bars = Directory.EnumerateFiles(saved, "*.frm", SearchOption.AllDirectories)
            .SelectMany(f => new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
                Vb6TextFile.ReadAllText(f), new NullSink())!.Components)
            .Where(c => c.GetPropertyOrDefault(InBoxComponentClass.LoadedTypeNameProperty) is not null)
            .ToList();

        bars.Should().HaveCount(30).And.OnlyContain(c => c.BaseClass is ProgressBarComponentClass);
        bars.Select(c => c.GetPropertyOrDefault(InBoxComponentClass.LoadedTypeNameProperty)).Distinct()
            .Should().BeEquivalentTo(["MSComctlLib.ProgressBar", "ComctlLib.ProgressBar"]);
    }
}
