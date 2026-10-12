using System;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Guards VB6 source files against being read as UTF-8 (issue #20).
///
/// VB6 wrote ANSI with no encoding declaration. Reading as UTF-8 turned every byte ≥ 0x80 into U+FFFD and
/// wrote it back as EF BF BD, so a copyright header, an accented identifier or a localized string literal
/// was destroyed by the first save. The corpus barely exercises this — one file in the whole VB6 tree has
/// high bytes — but any project authored outside an English locale is full of them.
/// </summary>
public class Vb6TextFileTests
{
    [Fact]
    public void Western_ANSI_displays_smart_quotes_and_the_euro_sign()
    {
        var encoding = new Vb6TextEncoding(1252);
        byte[] bytes = [0x80, 0x93, 0x94];
        Vb6TextFile.Decode(bytes, encoding).Should().Be("€“”");
        Vb6TextFile.Encode("€“”", encoding).Should().Equal(bytes);
    }

    [Fact]
    public void Japanese_source_uses_its_selected_ANSI_code_page()
    {
        var encoding = new Vb6TextEncoding(932);
        byte[] bytes = [0x82, 0xB1, 0x82, 0xF1, 0x82, 0xC9, 0x82, 0xBF, 0x82, 0xCD];
        Vb6TextFile.Decode(bytes, encoding).Should().Be("こんにちは");
        Vb6TextFile.Encode("こんにちは", encoding).Should().Equal(bytes);
    }

    [Fact]
    public void Invalid_multibyte_source_is_refused_instead_of_replaced()
    {
        Action read = () => Vb6TextFile.Decode([0x82], new Vb6TextEncoding(932));
        read.Should().Throw<DecoderFallbackException>();
    }

    [Fact]
    public void Ansi_bytes_survive_a_decode_encode_round_trip()
    {
        // Test an explicit Western code page independently of the host's Windows ACP.
        var original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        var encoding = new Vb6TextEncoding(1252);
        Vb6TextFile.Encode(Vb6TextFile.Decode(original, encoding), encoding).Should().Equal(original);
    }

    [Theory]
    [InlineData(0xA9)] // ©
    [InlineData(0xAE)] // ®
    [InlineData(0xE4)] // ä
    [InlineData(0xFF)] // ÿ
    public void A_high_byte_is_not_replaced(byte b)
    {
        var original = new byte[] { (byte)'x', b, (byte)'y' };

        var encoding = new Vb6TextEncoding(1252);
        var text = Vb6TextFile.Decode(original, encoding);
        text.Should().NotContain("\uFFFD", "U+FFFD means the byte was already lost");
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    [Fact]
    public void Bomless_UTF8_requires_an_explicit_selection()
    {
        // BOM-less UTF-8 and ANSI overlap. Migration requires explicit selection, never guessing.
        var utf8 = new UTF8Encoding(false).GetBytes("café");

        Vb6TextFile.Decode(utf8, new Vb6TextEncoding(1252)).Should().Be("cafÃ©");
        Vb6TextFile.Decode(utf8, Vb6TextEncoding.Utf8).Should().Be("café");
        Vb6TextFile.Encode("café", Vb6TextEncoding.Utf8).Should().Equal(utf8);
    }

    [Fact]
    public void A_utf8_BOM_is_honoured_and_stripped()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(new UTF8Encoding(false).GetBytes("Hi")).ToArray();

        Vb6TextFile.Decode(withBom).Should().Be("Hi");
    }

    [Fact]
    public void Content_outside_the_selected_ANSI_code_page_is_refused()
    {
        Action save = () => Vb6TextFile.Encode("こんにちは", new Vb6TextEncoding(1252));
        save.Should().Throw<EncoderFallbackException>();
    }

    [Fact]
    public void Pure_ascii_is_byte_identical_either_way()
    {
        var ascii = Encoding.ASCII.GetBytes("Attribute VB_Name = \"Module1\"\r\n");

        Vb6TextFile.Encode(Vb6TextFile.Decode(ascii)).Should().Equal(ascii);
    }

    [Fact]
    public void The_one_corpus_file_with_high_bytes_round_trips()
    {
        var root = Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                   ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";
        var path = Path.Join(root, "Forms", "Web Browser.frm");
        if (!File.Exists(path)) return; // VB6 not installed (CI)

        var original = File.ReadAllBytes(path);
        original.Any(b => b >= 0x80).Should().BeTrue("this fixture is chosen for its high bytes");

        Vb6TextFile.Encode(Vb6TextFile.Decode(original)).Should().Equal(original);
    }

    // ── Reading a document: the chain that cannot fail ───────────────────────────────────────────────
    // A read that throws drops a project member, and the next Save Project removes its line. So every
    // document read ends somewhere, and the last resort gives every byte back.

    [Fact]
    public void A_Western_file_the_hosts_double_byte_code_page_cannot_read_is_read_byte_for_byte()
    {
        // E9 is a Shift-JIS lead byte, and a line break is not a valid trail byte.
        var original = Encoding.Latin1.GetBytes("' café\r\nPublic Sub Main()\r\nEnd Sub\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(932));

        byteForByte.Should().BeTrue();
        encoding.Should().Be(Vb6TextEncoding.Latin1);
        text.Should().Contain("' café");
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    [Fact]
    public void A_Shift_JIS_spelling_Windows_would_not_write_is_read_byte_for_byte_rather_than_refused()
    {
        // 87 90 is the NEC row-13 spelling of U+2252; Windows writes 81 E0. Strict decoding refuses it.
        var original = new byte[] { 0x27, 0x20, 0x87, 0x90, 0x0D, 0x0A };

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(932));

        byteForByte.Should().BeTrue();
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    [Fact]
    public void Bytes_the_host_code_page_reads_are_read_in_it()
    {
        var original = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("' こんにちは\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(932));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(new Vb6TextEncoding(932));
        text.Should().Be("' こんにちは\r\n");
    }

    [Fact]
    public void A_document_last_read_as_UTF8_is_read_as_UTF8_again_when_it_still_is()
    {
        var original = Encoding.UTF8.GetBytes("' café\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(
            original, lastReadIn: Vb6TextEncoding.Utf8WithBom, ansi: new Vb6TextEncoding(1252));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(Vb6TextEncoding.Utf8, "the BOM was removed outside the IDE; it stays UTF-8 without one");
        text.Should().Be("' café\r\n");
    }

    [Fact]
    public void A_document_last_read_as_UTF8_falls_through_when_it_has_been_replaced_by_an_ANSI_revision()
    {
        var original = Encoding.Latin1.GetBytes("' café\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(
            original, lastReadIn: Vb6TextEncoding.Utf8, ansi: new Vb6TextEncoding(1252));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(new Vb6TextEncoding(1252));
        text.Should().Be("' café\r\n");
    }

    [Fact]
    public void A_byte_order_mark_declares_UTF8_whatever_the_document_was_last_read_in()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("' café\r\n")).ToArray();

        var (text, encoding, _) = Vb6TextFile.ReadDocument(
            original, lastReadIn: new Vb6TextEncoding(1252), ansi: new Vb6TextEncoding(1252));

        encoding.Should().Be(Vb6TextEncoding.Utf8WithBom);
        text.Should().Be("' café\r\n");
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    [Fact]
    public void A_document_last_read_byte_for_byte_is_read_afresh()
    {
        // Latin-1 accepts everything, so as the encoding last read in it would stick for good.
        var original = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes("' こんにちは\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(
            original, lastReadIn: Vb6TextEncoding.Latin1, ansi: new Vb6TextEncoding(932));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(new Vb6TextEncoding(932));
        text.Should().Be("' こんにちは\r\n");
    }

    [Fact]
    public void A_byte_order_mark_over_text_that_is_not_UTF8_is_kept_out_of_the_text()
    {
        var original = Encoding.UTF8.GetPreamble().Concat(Encoding.Latin1.GetBytes("Attribute VB_Name = \"M\"\r\n' café\r\n")).ToArray();

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(1252));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(new Vb6TextEncoding(1252, Utf8Bom: true));
        text.Should().StartWith("Attribute VB_Name", "a mark decoded as text would hide the header line");
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    // ── Files earlier versions wrote as UTF-8 ────────────────────────────────────────────────────────
    // Earlier versions wrote a file as UTF-8, with no byte-order mark, exactly when its text held a
    // character above U+00FF; otherwise as Latin-1. So that, and only that, is read as UTF-8 now.

    [Fact]
    public void A_file_an_earlier_version_wrote_as_UTF8_is_read_as_UTF8()
    {
        var original = Encoding.UTF8.GetBytes("' “café”\r\n");

        var (text, encoding, byteForByte) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(1252));

        byteForByte.Should().BeFalse();
        encoding.Should().Be(Vb6TextEncoding.Utf8, "written back as it came: UTF-8 without a byte-order mark");
        text.Should().Be("' “café”\r\n");
        Vb6TextFile.Encode(text, encoding).Should().Equal(original);
    }

    [Fact]
    public void Accented_text_alone_is_not_taken_as_UTF8_even_when_its_bytes_are()
    {
        // The #728 case: C3 A9 is "Ã©" in Windows-1252, and nothing above U+00FF says otherwise.
        var original = Encoding.UTF8.GetBytes("' café\r\n");

        var (text, encoding, _) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(1252));

        encoding.Should().Be(new Vb6TextEncoding(1252));
        text.Should().Be("' cafÃ©\r\n");
    }

    [Fact]
    public void Plain_ASCII_is_read_in_the_host_code_page_not_as_UTF8()
    {
        var (_, encoding, _) = Vb6TextFile.ReadDocument(Encoding.ASCII.GetBytes("' plain\r\n"), ansi: new Vb6TextEncoding(1252));

        encoding.Should().Be(new Vb6TextEncoding(1252), "an edit to it later is written as VB6 would write it");
    }

    [Fact]
    public void The_signature_needs_the_whole_file_to_be_UTF8()
    {
        var original = Encoding.UTF8.GetBytes("' “x”\r\n").Concat(new byte[] { 0xE9, 0x0D, 0x0A }).ToArray();

        var (_, encoding, _) = Vb6TextFile.ReadDocument(original, ansi: new Vb6TextEncoding(1252));

        encoding.Should().Be(new Vb6TextEncoding(1252));
    }

    [Fact]
    public void A_reload_honours_the_signature_over_the_encoding_last_read_in()
    {
        // Open and reload agree: a file read as Windows-1252, replaced by one an earlier version wrote.
        var original = Encoding.UTF8.GetBytes("' “café”\r\n");

        var (text, encoding, _) = Vb6TextFile.ReadDocument(
            original, lastReadIn: new Vb6TextEncoding(1252), ansi: new Vb6TextEncoding(1252));

        encoding.Should().Be(Vb6TextEncoding.Utf8);
        text.Should().Be("' “café”\r\n");
    }

    [Fact]
    public void Latin1_refuses_an_edit_it_cannot_hold_rather_than_substituting()
    {
        Action save = () => Vb6TextFile.Encode("' €\r\n", Vb6TextEncoding.Latin1);

        save.Should().Throw<EncoderFallbackException>();
    }

    [Fact]
    public void EncodeDocument_names_the_first_character_a_code_page_cannot_hold()
    {
        Action save = () => Vb6TextFile.EncodeDocument("Module1", "' a→b←\r\n", new Vb6TextEncoding(1252));

        var refusal = save.Should().Throw<SourceEncodingRefusal>().Which;
        refusal.Document.Should().Be("Module1");
        refusal.CodePage.Should().Be(1252);
        refusal.Character.Should().Be("→");
        refusal.CodePoint.Should().Be("U+2192");
        refusal.InnerException.Should().BeOfType<EncoderFallbackException>();
        refusal.Message.Should().Be(
            "Module1 is kept in code page 1252, which cannot hold '→' (U+2192). Nothing was written. HexIDE keeps "
            + "a file in the code page it was read or created in rather than convert it, so use characters that "
            + "code page holds.");
    }

    [Fact]
    public void EncodeDocument_names_a_character_outside_the_basic_plane_whole()
    {
        var face = char.ConvertFromUtf32(0x1F600);
        Action save = () => Vb6TextFile.EncodeDocument("Form1", "Caption = \"" + face + "\"", new Vb6TextEncoding(1252));

        var refusal = save.Should().Throw<SourceEncodingRefusal>().Which;
        refusal.Character.Should().Be(face);
        refusal.CodePoint.Should().Be("U+1F600");
    }

    [Fact]
    public void EncodeDocument_names_a_lone_surrogate_by_its_code_unit()
    {
        Action save = () => Vb6TextFile.EncodeDocument("Module1", "x" + (char)0xD800 + "y", Vb6TextEncoding.Utf8);

        var refusal = save.Should().Throw<SourceEncodingRefusal>().Which;
        refusal.Character.Should().Be(((char)0xD800).ToString());
        refusal.CodePoint.Should().Be("U+D800");
    }

    [Fact]
    public void EncodeDocument_returns_the_same_bytes_as_Encode_when_the_text_fits()
    {
        var encoding = new Vb6TextEncoding(1252) { Utf8Bom = true };

        Vb6TextFile.EncodeDocument("Module1", "' café\r\n", encoding)
            .Should().Equal(Vb6TextFile.Encode("' café\r\n", encoding));
    }
}
