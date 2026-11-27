using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// #4988: CommandButton refusals measured in oracle PR #4987, recorded in
// docs/vb6-fidelity-oracle/a-property-named-twice-and-a-measurement-vb6-cant-set-in-a-frm.md.
// The issue also scopes the refusal to other components sharing CaptionProperty.
public class FormCaptionValueTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = [];
        public void LogError(string error) => Errors.Add(error);
    }

    private static string Source(string type, string properties) =>
        $"VERSION 5.00\r\nBegin VB.Form Form1\r\n   Begin VB.{type} Command1\r\n{properties}   End\r\nEnd\r\n";

    private static FormDefinition Load(string source, Sink sink) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"), source, sink)!;

    [Theory]
    [InlineData("CommandButton", "abc")]
    [InlineData("CommandButton", "abc   'note")]
    [InlineData("Label", "abc")]
    [InlineData("Menu", "abc")]
    [InlineData("CommandButton", "5")]
    public void Unquoted_caption_leaves_default_logs_and_saves_raw_line(string type, string value)
    {
        var sink = new Sink();
        var line = $"      Caption = {value}";
        var form = Load(Source(type, line + "\r\n"), sink);
        var absent = Load(Source(type, ""), new Sink());
        form.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty).Should()
            .Be(absent.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty));
        sink.Errors.Should().Contain("Property Caption in Command1 had an invalid value.");
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.ReplaceLineEndings("\n").Split('\n').Should().Contain(line);
    }

    [Fact]
    public void Unquoted_duplicate_caption_keeps_readable_value_and_holds_save()
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", "Caption = \"First\"\r\nCaption = abc\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("First");
        sink.Errors.Should().Contain("Property Caption in Command1 had an invalid value.");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreadableDuplicateProperty);
    }

    [Fact]
    public void Quoted_caption_still_reads()
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", "Caption = \"abc\"\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("abc");
        sink.Errors.Should().BeEmpty();
    }

    // VB6 SP6 measurements: docs/vb6-fidelity-oracle/more-texts-a-frm-line-can-hold.md.
    [Theory]
    [InlineData("abc")]
    [InlineData("5")]
    [InlineData("(5)")]
    public void Unquoted_tag_leaves_default_logs_and_saves_raw_line(string value)
    {
        var sink = new Sink();
        var line = $"      Tag = {value}";
        var form = Load(Source("CommandButton", line + "\r\n"), sink);
        var absent = Load(Source("CommandButton", ""), new Sink());
        form.Components[1].GetPropertyOrDefault(VBProperties.TagProperty).Should()
            .Be(absent.Components[1].GetPropertyOrDefault(VBProperties.TagProperty));
        sink.Errors.Should().Contain("Property Tag in Command1 had an invalid value.");
        var saved = new FormSerializer().Serialize(form, "Form1.frm").frmText;
        saved.ReplaceLineEndings("\n").Split('\n').Should().Contain(line);
    }

    [Fact]
    public void Quoted_tag_still_reads()
    {
        var sink = new Sink();
        var form = Load(Source("CommandButton", "Tag = \"q\"\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.TagProperty).Should().Be("q");
        sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Menu_shortcut_still_reads_unquoted()
    {
        var sink = new Sink();
        var form = Load(Source("Menu", "Caption = \"&Edit\"\r\nShortcut = ^C\r\n"), sink);
        form.Components[1].GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("&Edit");
        form.Components[1].GetPropertyOrDefault(MenuComponentClass.ShortcutProperty).Should().Be("^C");
        sink.Errors.Should().BeEmpty();
    }
}
