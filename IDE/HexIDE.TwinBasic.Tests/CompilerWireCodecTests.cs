using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerWireCodecTests
{
    private readonly CompilerWireCodec codec = new();

    [Fact]
    public void Control_request_matches_observed_byte_command_and_little_endian_id_layout()
    {
        Assert.Equal(new byte[] { 0xFE, 0x78, 0x56, 0x34, 0x12, 0, 0xFF, 0x80 },
            codec.EncodeControlRequest(0xFE, 0x12345678, new byte[] { 0, 0xFF, 0x80 }));
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0 }, codec.EncodeControlRequest(1, 0, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => codec.EncodeControlRequest(1, -1, []));
    }

    [Fact]
    public void Control_reply_and_notification_preserve_unknown_codes_and_opaque_payloads()
    {
        byte[] reply = [0x78, 0x56, 0x34, 0x12, 0, 0, 0, 0x80, 0xFF, 0, 0x80];
        var decoded = codec.DecodeControl(reply);
        Assert.Equal(0x12345678, decoded.RequestId);
        Assert.Equal(int.MinValue, decoded.Code);
        Assert.False(decoded.IsNotification);
        reply[8] = 0;
        Assert.Equal(new byte[] { 0xFF, 0, 0x80 }, decoded.Payload);
        var notification = codec.DecodeControl([0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0]);
        Assert.True(notification.IsNotification);
        Assert.Equal(1, notification.Code);
        Assert.Empty(notification.Payload);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Incomplete_control_headers_are_rejected(int length) =>
        Assert.Throws<InvalidDataException>(() => codec.DecodeControl(new byte[length]));

    [Fact]
    public void Other_negative_control_ids_are_not_notifications() =>
        Assert.Throws<InvalidDataException>(() => codec.DecodeControl([0xFE, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0]));

    [Fact]
    public void Single_json_and_batch_preserve_order_unicode_and_nonstandard_fields()
    {
        var one = Encoding.UTF8.GetBytes("{\"event\":\"compilationStarted\",\"label\":\"空😀\"}");
        var two = "{\"method\":\"textDocument/publishDiagnostics\",\"params\":{\"errorCount\":2},\"unknown\":18446744073709551615}"u8.ToArray();
        var messages = codec.DecodeJson(Batch(one, two));
        Assert.Equal(2, messages.Length);
        Assert.Equal("空😀", messages[0].GetProperty("label").GetString());
        Assert.Equal(ulong.MaxValue, messages[1].GetProperty("unknown").GetUInt64());
        Assert.Equal(2, messages[1].GetProperty("params").GetProperty("errorCount").GetInt32());
        Assert.Equal(messages[0].GetRawText(), Assert.Single(codec.DecodeJson(one)).GetRawText());
        Assert.Equal(one, codec.EncodeJson(messages[0]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("1")]
    [InlineData("{}{}")]
    [InlineData("{\"broken\":}")]
    [InlineData("{/*comment*/}")]
    [InlineData("{\"x\":1,}")]
    public void Json_requires_one_complete_object(string json) =>
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void Whitespace_is_allowed_but_invalid_utf8_inside_a_string_is_rejected()
    {
        Assert.Single(codec.DecodeJson(" \r\n{} \t"u8));
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson([123, 34, 120, 34, 58, 34, 255, 34, 125]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(1)]
    [InlineData(3)]
    public void Batch_lengths_must_describe_complete_objects(int length)
    {
        var batch = Batch("{}"u8.ToArray());
        BinaryPrimitives.WriteInt32LittleEndian(batch.AsSpan(4), length);
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson(batch));
    }

    [Fact]
    public void Empty_batches_truncated_lengths_and_malformed_later_objects_fail_as_a_whole()
    {
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson([0xEA, 0xEA, 0xEA, 0xFF]));
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson([0xEA, 0xEA, 0xEA, 0xFF, 2]));
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson(Batch("{}"u8.ToArray(), "no"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => codec.DecodeJson([0, 1, 2, 3, 4, 5, 6, 7]));
    }

    [Fact]
    public void Byte_object_and_depth_limits_apply_at_the_boundaries()
    {
        var limited = new CompilerWireCodec(new(MaximumMessageBytes: 10, MaximumJsonObjects: 1, MaximumJsonDepth: 1));
        Assert.Single(limited.DecodeJson(Batch("{}"u8.ToArray()))); // Exactly ten bytes.
        Assert.Throws<InvalidDataException>(() => limited.DecodeJson(Batch("{ }"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => limited.DecodeControl(new byte[11]));
        Assert.Equal(10, limited.EncodeControlRequest(1, 0, new byte[5]).Length);
        Assert.Throws<InvalidDataException>(() => limited.EncodeControlRequest(1, 0, new byte[6]));
        var countLimited = new CompilerWireCodec(new(MaximumJsonObjects: 1));
        Assert.Throws<InvalidDataException>(() => countLimited.DecodeJson(Batch("{}"u8.ToArray(), "{}"u8.ToArray())));
        Assert.Throws<InvalidDataException>(() => limited.DecodeJson("{\"a\":{}}"u8));
        using var deep = JsonDocument.Parse("{\"a\":{}}");
        Assert.Throws<InvalidDataException>(() => limited.EncodeJson(deep.RootElement));
        using var big = JsonDocument.Parse("{\"value\":12345}");
        Assert.Throws<InvalidDataException>(() => limited.EncodeJson(big.RootElement));
        using var scalar = JsonDocument.Parse("1");
        Assert.Throws<InvalidDataException>(() => codec.EncodeJson(scalar.RootElement));
    }

    [Theory]
    [InlineData(7, 1, 1)]
    [InlineData(8, 0, 1)]
    [InlineData(8, 1, 0)]
    public void Invalid_limits_fail_before_processing(int bytes, int count, int depth) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompilerWireCodec(new(bytes, count, depth)));

    // Independent test construction mirrors the pinned upstream wire observer's documented envelope,
    // not a production batch encoder. The production client does not send this inbound-only format.
    private static byte[] Batch(params byte[][] objects)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0xffeaeaeau);
        foreach (var json in objects) { writer.Write(json.Length); writer.Write(json); }
        return stream.ToArray();
    }
}
