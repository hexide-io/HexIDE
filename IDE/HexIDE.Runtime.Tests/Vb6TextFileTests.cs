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
    public void Ansi_bytes_survive_a_decode_encode_round_trip()
    {
        // Every byte value, which is the property Latin-1 buys and the system codepage does not.
        var original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        Vb6TextFile.Encode(Vb6TextFile.Decode(original)).Should().Equal(original);
    }

    [Theory]
    [InlineData(0xA9)] // ©
    [InlineData(0xAE)] // ®
    [InlineData(0xE4)] // ä
    [InlineData(0xFF)] // ÿ
    public void A_high_byte_is_not_replaced(byte b)
    {
        var original = new byte[] { (byte)'x', b, (byte)'y' };

        var text = Vb6TextFile.Decode(original);
        text.Should().NotContain("\uFFFD", "U+FFFD means the byte was already lost");
        Vb6TextFile.Encode(text).Should().Equal(original);
    }

    [Fact]
    public void Utf8_content_HexIDE_wrote_earlier_is_still_read_correctly()
    {
        // Migration case: before this fix HexIDE wrote UTF-8, so "café" is C3 A9. Strict-decoding as UTF-8
        // succeeds, so it must be read as UTF-8 rather than shown as "cafÃ©".
        var utf8 = new UTF8Encoding(false).GetBytes("café");

        Vb6TextFile.Decode(utf8).Should().Be("café");
    }

    [Fact]
    public void A_utf8_BOM_is_honoured_and_stripped()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(new UTF8Encoding(false).GetBytes("Hi")).ToArray();

        Vb6TextFile.Decode(withBom).Should().Be("Hi");
    }

    [Fact]
    public void Content_that_cannot_be_ansi_is_written_as_utf8_rather_than_lost()
    {
        // Genuine CJK typed into HexIDE cannot be represented in Latin-1. Writing something VB6 did not
        // expect beats dropping the characters.
        var text = "こんにちは";

        var bytes = Vb6TextFile.Encode(text);
        new UTF8Encoding(false).GetString(bytes).Should().Be(text);
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
