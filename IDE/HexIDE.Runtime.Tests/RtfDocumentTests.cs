using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.Tests;

public class RtfDocumentTests
{
    // Windows 10 RICHTX32, native and p-code: oracle's RichTextBox entry (#1100).
    private const string EmptyRtf = "{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}\r\n\\viewkind4\\uc1\\pard\\f0\\fs17 \r\n\\par }\r\n";
    private const string HelloRtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}}\r\n\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs17 Hello\r\n\\par }\r\n";

    [Fact]
    public void Empty_WritesMeasuredDefault() => RtfDocument.Empty.Write().Should().Be(EmptyRtf);

    [Fact]
    public void PlainText_WritesMeasuredDocument() => RtfDocument.FromText("Hello").Write().Should().Be(HelloRtf);

    [Theory]
    [InlineData(EmptyRtf, "")]
    [InlineData(HelloRtf, "Hello")]
    public void MeasuredDocument_ReadsAndWritesExactly(string source, string text)
    {
        var document = RtfDocument.Read(source);
        document.Text.Should().Be(text);
        document.Write().Should().Be(source);
    }

    [Theory]
    [InlineData("just text")]
    [InlineData("{\\rtf1 unclosed")]
    public void NonRtf_BecomesLiteralText(string source) => RtfDocument.Read(source).Text.Should().Be(source);

    [Fact]
    public void AnsiText_UsesMeasuredCodePageBytes()
    {
        var document = RtfDocument.FromText("é€א");
        document.Text.Should().Be("é€?");
        document.Write().Should().Contain("\\'e9\\'80?");
        RtfDocument.Read(document.Write()).Text.Should().Be(document.Text);
    }

    [Fact]
    public void Tab_UsesRtfControlWord()
    {
        var document = RtfDocument.FromText("a\tb {\\}");
        document.Write().Should().Contain("a\\tab b");
        RtfDocument.Read(document.Write()).Text.Should().Be("a\tb {\\}");
    }

    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void Text_KeepsMeasuredLineEndings(string text)
    {
        var document = RtfDocument.FromText(text);
        document.Text.Should().Be(text);
        Action write = () => document.Write();
        write.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    private const string FormattedRtf = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil Arial;}{\\f1\\fnil\\fcharset0 Courier New;}}\r\n{\\colortbl ;\\red255\\green0\\blue0;}\r\n\\viewkind4\\uc1\\pard\\lang1033\\f0\\fs20 The \\cf1\\ul\\b\\i\\f1\\fs28 slow\\cf0\\ulnone\\b0\\i0\\f0\\fs20  brown fox\r\n\\par }\r\n";

    [Fact]
    public void CharacterFormatting_ReadsMeasuredRunsAndWritesExactOracleDocument()
    {
        var document = RtfDocument.Read(FormattedRtf);
        document.Text.Should().Be("The slow brown fox");
        document.Runs.Should().HaveCount(3);
        var slow = document.Runs[1];
        slow.Text.Should().Be("slow");
        slow.Format.Bold.Should().BeTrue();
        slow.Format.Italic.Should().BeTrue();
        slow.Format.Underline.Should().BeTrue();
        slow.Format.FontName.Should().Be("Courier New");
        slow.Format.HalfPoints.Should().Be(28);
        slow.Format.Color.Should().Be(255);
        document.Runs[2].Format.Bold.Should().BeFalse();
        document.Runs[2].Format.FontName.Should().Be("Arial");
        document.Write().Should().Be(FormattedRtf);
    }

    [Fact]
    public void CharacterFormatting_NormalizesMeasuredInputAndKeepsParagraphBreak()
    {
        const string source = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 plain \b bold\b0  \i ital\i0 \par second}";
        var document = RtfDocument.Read(source);
        document.Text.Should().Be("plain bold ital\r\nsecond");
        document.Runs.Single(r => r.Text == "bold").Format.Bold.Should().BeTrue();
        document.Runs.Single(r => r.Text == "ital").Format.Italic.Should().BeTrue();
        RtfDocument.Read(document.Write()).Text.Should().Be(document.Text);
        document.Write().Should().StartWith("{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil Arial;}}\r\n\\viewkind4\\uc1\\pard\\lang1033");
        document.Write().Should().EndWith("second\r\n\\par }\r\n");
    }

    [Fact]
    public void CharacterFormatting_GroupRestoresStrikeAndProtection()
    {
        // Parser invariant, not an additional VB6 measurement.
        var document = RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 a{\strike\protect b}c}");
        document.Text.Should().Be("abc");
        document.Runs[1].Format.StrikeThrough.Should().BeTrue();
        document.Runs[1].Format.Protected.Should().BeTrue();
        document.Runs[2].Format.StrikeThrough.Should().BeFalse();
        document.Runs[2].Format.Protected.Should().BeFalse();
        RtfDocument.Read(document.Write()).Runs.Should().Equal(document.Runs);
    }

    [Fact]
    public void CharacterFormatting_DefaultFontHeaderDoesNotBypassFormattedReader()
    {
        var source = HelloRtf.Replace("Hello", @"\b Hello", StringComparison.Ordinal);
        var document = RtfDocument.Read(source);
        document.Runs[0].Format.Bold.Should().BeTrue();
        RtfDocument.Read(document.Write()).Runs.Should().Equal(document.Runs);
    }

    [Fact]
    public void ParagraphFormatting_ReadsRecordedControlsWithoutFlattening()
    {
        // Control fragments are measured; this combined document is a parser invariant.
        var document = RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 \qc\fi-195\li690\ri300\tx720\tx1440 first\par \pard second}");
        document.Text.Should().Be("first\r\nsecond");
        var paragraph = document.Runs[0].Paragraph;
        paragraph.Alignment.Should().Be("qc");
        paragraph.FirstLineIndent.Should().Be(-195);
        paragraph.LeftIndent.Should().Be(690);
        paragraph.RightIndent.Should().Be(300);
        paragraph.TabStops.Should().Equal(720, 1440);
        document.Runs[^1].Paragraph.Alignment.Should().Be("ql");
        document.Runs[^1].Paragraph.LeftIndent.Should().Be(0);
        document.Runs[^1].Paragraph.TabStops.Should().BeEmpty();
        document.Write().Should().Contain(@"\qc\fi-195\li690\ri300\tx720\tx1440");
        var reread = RtfDocument.Read(document.Write());
        reread.Text.Should().Be(document.Text);
        reread.Runs[0].Paragraph.Should().BeEquivalentTo(paragraph);
        reread.Runs[^1].Paragraph.Should().BeEquivalentTo(document.Runs[^1].Paragraph);
    }

    [Fact]
    public void ParagraphFormatting_GroupRestoresParagraphAndKeepsEmptyParagraphs()
    {
        // Parser invariant, not an additional VB6 measurement.
        var document = RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 a{\qc b}c\par \par d}");
        document.Text.Should().Be("abc\r\n\r\nd");
        document.Runs[1].Paragraph.Alignment.Should().Be("qc");
        document.Runs[2].Paragraph.Alignment.Should().Be("ql");
        RtfDocument.Read(document.Write()).Text.Should().Be(document.Text);
        RtfDocument.Read(document.Write()).Runs[1].Paragraph.Alignment.Should().Be("qc");
    }

    [Fact]
    public void Selection_WritesExactRecordedSlowDocument()
    {
        const string expected = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Courier New;}}\r\n{\\colortbl ;\\red255\\green0\\blue0;}\r\n\\uc1\\pard\\cf1\\lang1033\\ul\\b\\i\\f0\\fs28 slow}\r\n";
        RtfDocument.Read(FormattedRtf).WriteSelection(4, 4).Should().Be(expected);
    }

    [Fact]
    public void Selection_EmptyWritesRecordedEmptyDocument() =>
        RtfDocument.Empty.WriteSelection(0, 0).Should().Be("{\\rtf1\\ansi\\ansicpg1252\\deff0\\deflang1033\\uc1 }\r\n");

    [Fact]
    public void Bullet_ReadsRecordedGroupsWithoutAddingBulletToText()
    {
        // The bullet groups and Symbol entry are measured; the surrounding document is a parser invariant.
        const string source = "{\\rtf1\\ansi\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 MS Sans Serif;}{\\f1\\fnil\\fcharset2 Symbol;}}\r\n\\viewkind4\\uc1\\pard{\\pntext\\f1\\'B7\\tab}{\\*\\pn\\pnlvlblt\\pnf1\\pnindent0{\\pntxtb\\'B7}}\\lang1033\\f0\\fs17 item\r\n\\par }\r\n";
        var document = RtfDocument.Read(source);
        document.Text.Should().Be("item");
        document.Runs[0].Paragraph.BulletFont.Should().Be(1);
        document.Write().Should().Be(source);
        Action multipleBullets = () => RtfDocument.Read(source.Replace("item", @"item\par {\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent0{\pntxtb\'B7}}second", StringComparison.Ordinal));
        multipleBullets.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [Fact]
    public void PlainText_CrLfWritesParagraphBreak()
    {
        var document = RtfDocument.FromText("a\r\nb");
        document.Write().Should().Be(HelloRtf.Replace("Hello", "a\r\n\\par b", StringComparison.Ordinal));
        RtfDocument.Read(document.Write()).Text.Should().Be("a\r\nb");
    }

    [Theory]
    [InlineData(@"\unknown")]
    [InlineData(@"\cf9")]
    [InlineData(@"\f9")]
    public void UnmodelledFormatting_IsRefused(string control)
    {
        Action read = () => RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0\fs20 " + control + " text}");
        read.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }

    [Fact]
    public void UnsupportedFormatting_IsRefusedWithoutLosingSource()
    {
        const string source = "{\\rtf1\\ansi\\b bold}";
        Action read = () => RtfDocument.Read(source);
        read.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action readUnknown = () => RtfDocument.Read(HelloRtf.Replace("Hello", "\\unknown Hello", StringComparison.Ordinal));
        readUnknown.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action readSymbol = () => RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0\fnil\fcharset2 Symbol;}} text}");
        readSymbol.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action mixedSelection = () => RtfDocument.Read(FormattedRtf).WriteSelection(0, 8);
        mixedSelection.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action paragraphSelection = () => RtfDocument.FromText("a\r\nb").WriteSelection(0, 4);
        paragraphSelection.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action inferCharset = () => RtfDocument.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 text}").WriteSelection(0, 4);
        inferCharset.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
        Action readBinary = () => RtfDocument.Read("{\\rtf1\\bin1 {} history}");
        readBinary.Should().Throw<NotImplementedException>().WithMessage("*#1100*");
    }
}
