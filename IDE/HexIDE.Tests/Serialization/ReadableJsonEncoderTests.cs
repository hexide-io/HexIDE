using System.Text.Json;
using HexIDE.Serialization;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// JSON a model reads as text carries each character as itself, but for what JSON requires and what cannot be seen (#753).
/// </summary>
public class ReadableJsonEncoderTests
{
    private static readonly JsonSerializerOptions Options = ReplyJson.Options;

    private static string Write(string text) => JsonSerializer.Serialize(text, Options);

    private static string WriteUtf8(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Options.Encoder }))
            writer.WriteStringValue(System.Text.Encoding.UTF8.GetBytes(text));
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [Theory]
    [InlineData("اشکال\u200Cزدایی")]
    [InlineData("ب\u064E\u200Cت")]
    [InlineData("\u0750\u200C\u08A0")] // Supplement and Extended-A
    [InlineData("\u0870\u200C\uFB50")] // Extended-B and Presentation Forms-A
    [InlineData("\uFE8F\u200Cب")] // Presentation Forms-B
    [InlineData("ب\u200C\u064Eت")]
    [InlineData("ب\u200Cت\u064E")]
    [InlineData("'Form1'")]
    [InlineData("Ж1 → there")]
    [InlineData("<a & b> + c")]
    [InlineData("😀 and 𠀀")] // outside the BMP: an emoji and a CJK Extension B character
    [InlineData("a\u00A0b")] // a no-break space is a space, and is seen as one
    public void Text_a_reader_can_see_is_written_as_itself(string text)
    {
        Write(text).Should().Be($"\"{text}\"");
        WriteUtf8(text).Should().Be($"\"{text}\"");
    }

    [Theory]
    [InlineData("say \"hi\"", @"""say \""hi\""""")]
    [InlineData(@"C:\Temp", @"""C:\\Temp""")]
    [InlineData("one\r\ntwo\tthree", @"""one\r\ntwo\tthree""")]
    [InlineData("\b\f\u0001", @"""\b\f\u0001""")]
    public void What_JSON_requires_is_escaped_in_its_short_form_where_it_has_one(string text, string json)
    {
        Write(text).Should().Be(json);
        WriteUtf8(text).Should().Be(json);
    }

    [Theory]
    [InlineData("abc\u202Edef", @"""abc\u202Edef""")] // right-to-left override
    [InlineData("a\u200Bb\u00ADc\uFEFF", @"""a\u200Bb\u00ADc\uFEFF""")] // zero-width space, soft hyphen, BOM
    [InlineData("a\u2028b\u2029c\u0085", @"""a\u2028b\u2029c\u0085""")] // line and paragraph separators, NEL
    [InlineData("x\U000E0041y", @"""x\uDB40\uDC41y""")] // an invisible tag character, as its pair
    [InlineData("Fo\u200Crm1", @"""Fo\u200Crm1""")]
    [InlineData("\u200Cب", @"""\u200Cب""")]
    [InlineData("ب\u200C", @"""ب\u200C""")]
    [InlineData("ب\u200Ca", @"""ب\u200Ca""")]
    [InlineData("a\u200Cب", @"""a\u200Cب""")]
    [InlineData("ب\u200Dت", @"""ب\u200Dت""")]
    [InlineData("a\u064E\u200Cت", @"""aَ\u200Cت""")]
    [InlineData("١\u200Cت", @"""١\u200Cت""")]
    public void What_would_not_be_seen_is_escaped(string text, string json)
    {
        Write(text).Should().Be(json);
        WriteUtf8(text).Should().Be(json);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(249)]
    [InlineData(250)]
    [InlineData(251)]
    [InlineData(4089)]
    [InlineData(4090)]
    [InlineData(4091)]
    [InlineData(65530)]
    public void Persian_joiner_survives_after_an_escape_and_across_writer_buffers(int padding)
    {
        var text = "\n" + new string('x', padding) + "اشکال\u200Cزدایی";
        text.Should().Contain(((char)0x200C).ToString()); // compiled input contains the scalar
        var expected = "\"\\n" + text[1..] + "\"";
        // The joiner is at UTF-16 offsets 255/256/257 and 4095/4096/4097.
        Write(text).Should().Be(expected);
        WriteUtf8(text).Should().Be(expected);
        JsonSerializer.SerializeToUtf8Bytes(text, Options).Should().Equal(System.Text.Encoding.UTF8.GetBytes(expected));

        using var stream = new MemoryStream();
        JsonSerializer.Serialize(stream, text, Options);
        System.Text.Encoding.UTF8.GetString(stream.ToArray()).Should().Be(expected);
    }

    [Fact]
    public void Everything_written_reads_back_as_it_was()
    {
        var random = new Random(753);
        for (var n = 0; n < 2000; n++)
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 0; i < 40; i++)
            {
                var scalar = random.Next(4) switch
                {
                    0 => random.Next(0x20, 0x7F),
                    1 => random.Next(0, 0x20),
                    2 => random.Next(0x80, 0xD800),
                    _ => random.Next(0x10000, 0x110000),
                };
                builder.Append(char.ConvertFromUtf32(scalar));
            }
            var text = builder.ToString();
            JsonSerializer.Deserialize<string>(Write(text)).Should().Be(text);
        }
    }

    [Fact]
    public void A_surrogate_asked_about_alone_is_one_to_encode()
    {
        ReadableJsonEncoder.Instance.WillEncode(0xD800).Should().BeTrue();
        ReadableJsonEncoder.Instance.WillEncode(0x1F600).Should().BeFalse();
    }

    [Fact]
    public void A_lone_surrogate_is_written_as_the_replacement_character_as_before()
    {
        JsonSerializer.Deserialize<string>(Write("a\uD800b")).Should().Be("a\uFFFDb");
    }
}
