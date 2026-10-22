using System.Text.Json;
using HexIDE.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// JSON a model reads as text carries each character as itself, but for what JSON requires and what cannot be seen (#753).
/// </summary>
public class ReadableJsonEncoderTests
{
    private static readonly JsonSerializerOptions Options = new() { Encoder = ReadableJsonEncoder.Instance };

    private static string Write(string text) => JsonSerializer.Serialize(text, Options);

    [Theory]
    [InlineData("'Form1'")]
    [InlineData("Ж1 → there")]
    [InlineData("<a & b> + c")]
    [InlineData("😀 and 𠀀")] // outside the BMP: an emoji and a CJK Extension B character
    [InlineData("a\u00A0b")] // a no-break space is a space, and is seen as one
    public void Text_a_reader_can_see_is_written_as_itself(string text)
    {
        Write(text).Should().Be($"\"{text}\"");
    }

    [Theory]
    [InlineData("say \"hi\"", @"""say \""hi\""""")]
    [InlineData(@"C:\Temp", @"""C:\\Temp""")]
    [InlineData("one\r\ntwo\tthree", @"""one\r\ntwo\tthree""")]
    [InlineData("\b\f\u0001", @"""\b\f\u0001""")]
    public void What_JSON_requires_is_escaped_in_its_short_form_where_it_has_one(string text, string json)
    {
        Write(text).Should().Be(json);
    }

    [Theory]
    [InlineData("abc\u202Edef", @"""abc\u202Edef""")] // right-to-left override
    [InlineData("a\u200Bb\u00ADc\uFEFF", @"""a\u200Bb\u00ADc\uFEFF""")] // zero-width space, soft hyphen, BOM
    [InlineData("a\u2028b\u2029c\u0085", @"""a\u2028b\u2029c\u0085""")] // line and paragraph separators, NEL
    [InlineData("x\U000E0041y", @"""x\uDB40\uDC41y""")] // an invisible tag character, as its pair
    public void What_would_not_be_seen_is_escaped(string text, string json)
    {
        Write(text).Should().Be(json);
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
