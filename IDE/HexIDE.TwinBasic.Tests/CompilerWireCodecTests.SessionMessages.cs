using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed partial class CompilerWireCodecTests
{
    [Theory]
    [InlineData(WebSocketMessageType.Binary, new byte[] { 1 })]
    [InlineData(WebSocketMessageType.Binary, new byte[] { 2 })]
    [InlineData(WebSocketMessageType.Binary, new byte[] { 7 })]
    [InlineData(WebSocketMessageType.Binary, new byte[] { 1, 2 })]
    [InlineData(WebSocketMessageType.Binary, new byte[] { 0, 0xFF, 0x80 })]
    [InlineData(WebSocketMessageType.Text, new byte[] { 123, 125 })]
    public void Opaque_channel_messages_preserve_type_and_bytes(WebSocketMessageType type, byte[] bytes)
    {
        var encoded = codec.EncodeChannel(codec.DecodeChannel(type, bytes));
        encoded.Type.Should().Be(type);
        encoded.Bytes.ToArray().Should().Equal(bytes);
    }

    [Theory]
    [InlineData("", "Truncated compiler settings request.")]
    [InlineData("1", "Truncated compiler settings request.")]
    [InlineData("[]", "Compiler settings must be a JSON object.")]
    [InlineData("null", "Compiler settings must be a JSON object.")]
    [InlineData("{bad}", "Invalid compiler settings JSON.")]
    [InlineData("{}{}", "Invalid compiler settings JSON.")]
    public void Settings_errors_remain_exact(string text, string error)
    {
        var reply = Control(243, [0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]);
        FluentActions.Invoking(() => codec.ReadSettingsRequest(reply)).Should()
            .ThrowExactly<InvalidDataException>().WithMessage(error);
    }

    [Fact]
    public void Settings_depth_remains_64()
    {
        static byte[] Settings(int depth) => [0, 0, 0, 0, .. Encoding.UTF8.GetBytes(
            string.Concat(Enumerable.Repeat("{\"x\":", depth)) + "0" + new string('}', depth))];
        codec.ReadSettingsRequest(Control(243, Settings(64))).Should().BeOfType<CompilerSettingsRequest>();
        FluentActions.Invoking(() => codec.ReadSettingsRequest(Control(243, Settings(65)))).Should()
            .ThrowExactly<InvalidDataException>().WithMessage("Invalid compiler settings JSON.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public void Package_identifier_length_errors_remain_exact(int length) =>
        FluentActions.Invoking(() => codec.ReadPackageRequest(Control(255, new byte[length]))).Should()
            .ThrowExactly<InvalidDataException>().WithMessage("Invalid compiler package identifier length.");

    [Theory]
    [InlineData(" ")]
    [InlineData("a\0b")]
    public void Package_identifier_validation_remains_exact(string id) =>
        FluentActions.Invoking(() => codec.ReadPackageRequest(Control(255, Encoding.UTF8.GetBytes(id)))).Should()
            .ThrowExactly<InvalidDataException>().WithMessage("Invalid compiler package identifier.");

    [Fact]
    public void Package_identifier_utf8_and_size_boundaries_remain_exact()
    {
        FluentActions.Invoking(() => codec.ReadPackageRequest(Control(255, [0xFF]))).Should()
            .ThrowExactly<InvalidDataException>().WithMessage("Invalid UTF-8 compiler package identifier.");
        var identifier = new string('a', 4096);
        codec.ReadPackageRequest(Control(255, Encoding.UTF8.GetBytes(identifier))).Identifier.Should().Be(identifier);
        var request = codec.ReadPackageRequest(Control(255, Encoding.UTF8.GetBytes("包")));
        codec.MaximumPackageContentBytes(request, 32).Should().Be(15);
        codec.EncodeProjectRequest(2, new CompilerProvidePackage(request, new byte[] { 0, 0xFF }), 32)
            .Should().Equal([4, 2, 0, 0, 0, 3, 0, 0, 0, 0xE5, 0x8C, 0x85, .. "AP8="u8]);
    }

    [Fact]
    public void Initialization_size_includes_delimiter_headers_and_original_language_text()
    {
        using var document = JsonDocument.Parse("{ \"hello\": \"空\" }");
        var initialization = new CompilerRootInitialization(["包"], CompilerWireCodec.ReadLanguageData(document.RootElement));
        var exact = 5 + 12 + 4 + Encoding.UTF8.GetByteCount(document.RootElement.GetRawText());
        var request = codec.PrepareInitialization(initialization, exact);
        codec.EncodeInitialize(request).Length.Should().Be(exact);
        FluentActions.Invoking(() => codec.PrepareInitialization(initialization, exact - 1)).Should()
            .ThrowExactly<InvalidDataException>().WithMessage("Compiler initialization exceeds the byte limit.");
    }

    private static CompilerControlMessage Control(int code, byte[] payload) => new(-1, code, ImmutableArray.CreateRange(payload));
}
