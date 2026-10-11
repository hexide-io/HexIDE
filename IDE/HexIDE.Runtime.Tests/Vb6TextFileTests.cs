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
}
