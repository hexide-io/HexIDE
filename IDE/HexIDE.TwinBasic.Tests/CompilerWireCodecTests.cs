using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed partial class CompilerWireCodecTests
{
    private readonly CompilerWireCodec codec = new();

    [Fact]
    public void Control_request_matches_observed_byte_command_and_little_endian_id_layout()
    {
        codec.EncodeControlRequest(0xFE, 0x12345678, new byte[] { 0, 0xFF, 0x80 }).Should().Equal(new byte[] { 0xFE, 0x78, 0x56, 0x34, 0x12, 0, 0xFF, 0x80 });
        codec.EncodeControlRequest(1, 0, []).Should().Equal(new byte[] { 1, 0, 0, 0, 0 });
        FluentActions.Invoking(() => codec.EncodeControlRequest(1, -1, [])).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Control_reply_and_notification_preserve_unknown_codes_and_opaque_payloads()
    {
        byte[] reply = [0x78, 0x56, 0x34, 0x12, 0, 0, 0, 0x80, 0xFF, 0, 0x80];
        var decoded = codec.DecodeControl(reply);
        decoded.RequestId.Should().Be(0x12345678);
        decoded.Code.Should().Be(int.MinValue);
        decoded.IsNotification.Should().BeFalse();
        reply[8] = 0;
        decoded.Payload.Should().Equal(new byte[] { 0xFF, 0, 0x80 });
        var notification = codec.DecodeControl([0xFF, 0xFF, 0xFF, 0xFF, 1, 0, 0, 0]);
        notification.IsNotification.Should().BeTrue();
        notification.Code.Should().Be(1);
        notification.Payload.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Incomplete_control_headers_are_rejected(int length) =>
        FluentActions.Invoking(() => codec.DecodeControl(new byte[length])).Should().ThrowExactly<InvalidDataException>();

    [Fact]
    public void Other_negative_control_ids_are_not_notifications() =>
        FluentActions.Invoking(() => codec.DecodeControl([0xFE, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0])).Should().ThrowExactly<InvalidDataException>();

    [Fact]
    public void Single_json_and_batch_preserve_order_unicode_and_nonstandard_fields()
    {
        var one = Encoding.UTF8.GetBytes("{\"event\":\"compilationStarted\",\"label\":\"空😀\"}");
        var two = "{\"method\":\"textDocument/publishDiagnostics\",\"params\":{\"errorCount\":2},\"unknown\":18446744073709551615}"u8.ToArray();
        var messages = codec.DecodeJson(Batch(one, two));
        messages.Length.Should().Be(2);
        messages[0].GetProperty("label").GetString().Should().Be("空😀");
        messages[1].GetProperty("unknown").GetUInt64().Should().Be(ulong.MaxValue);
        messages[1].GetProperty("params").GetProperty("errorCount").GetInt32().Should().Be(2);
        (codec.DecodeJson(one)).Should().ContainSingle().Which.GetRawText().Should().Be(messages[0].GetRawText());
        codec.EncodeJson(messages[0]).Should().Equal(one);
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
        FluentActions.Invoking(() => codec.DecodeJson(Encoding.UTF8.GetBytes(json))).Should().ThrowExactly<InvalidDataException>();

    [Fact]
    public void Whitespace_is_allowed_but_invalid_utf8_inside_a_string_is_rejected()
    {
        codec.DecodeJson(" \r\n{} \t"u8).Should().ContainSingle();
        FluentActions.Invoking(() => codec.DecodeJson([123, 34, 120, 34, 58, 34, 255, 34, 125])).Should().ThrowExactly<InvalidDataException>();
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
        FluentActions.Invoking(() => codec.DecodeJson(batch)).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void Empty_batches_truncated_lengths_and_malformed_later_objects_fail_as_a_whole()
    {
        FluentActions.Invoking(() => codec.DecodeJson([0xEA, 0xEA, 0xEA, 0xFF])).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => codec.DecodeJson([0xEA, 0xEA, 0xEA, 0xFF, 2])).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => codec.DecodeJson(Batch("{}"u8.ToArray(), "no"u8.ToArray()))).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => codec.DecodeJson([0, 1, 2, 3, 4, 5, 6, 7])).Should().ThrowExactly<InvalidDataException>();
    }

    [Fact]
    public void Byte_object_and_depth_limits_apply_at_the_boundaries()
    {
        var limited = new CompilerWireCodec(new(MaximumMessageBytes: 10, MaximumJsonObjects: 1, MaximumJsonDepth: 1));
        limited.DecodeJson(Batch("{}"u8.ToArray())).Should().ContainSingle(); // Exactly ten bytes.
        FluentActions.Invoking(() => limited.DecodeJson(Batch("{ }"u8.ToArray()))).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => limited.DecodeControl(new byte[11])).Should().ThrowExactly<InvalidDataException>();
        limited.EncodeControlRequest(1, 0, new byte[5]).Length.Should().Be(10);
        FluentActions.Invoking(() => limited.EncodeControlRequest(1, 0, new byte[6])).Should().ThrowExactly<InvalidDataException>();
        var countLimited = new CompilerWireCodec(new(MaximumJsonObjects: 1));
        FluentActions.Invoking(() => countLimited.DecodeJson(Batch("{}"u8.ToArray(), "{}"u8.ToArray()))).Should().ThrowExactly<InvalidDataException>();
        FluentActions.Invoking(() => limited.DecodeJson("{\"a\":{}}"u8)).Should().ThrowExactly<InvalidDataException>();
        using var deep = JsonDocument.Parse("{\"a\":{}}");
        FluentActions.Invoking(() => limited.EncodeJson(deep.RootElement)).Should().ThrowExactly<InvalidDataException>();
        using var big = JsonDocument.Parse("{\"value\":12345}");
        FluentActions.Invoking(() => limited.EncodeJson(big.RootElement)).Should().ThrowExactly<InvalidDataException>();
        using var scalar = JsonDocument.Parse("1");
        FluentActions.Invoking(() => codec.EncodeJson(scalar.RootElement)).Should().ThrowExactly<InvalidDataException>();
    }

    [Theory]
    [InlineData(7, 1, 1)]
    [InlineData(8, 0, 1)]
    [InlineData(8, 1, 0)]
    public void Invalid_limits_fail_before_processing(int bytes, int count, int depth) =>
        FluentActions.Invoking(() => new CompilerWireCodec(new(bytes, count, depth))).Should().ThrowExactly<ArgumentOutOfRangeException>();

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
