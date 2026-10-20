using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// take_snapshot's image itself, as image content beside its reply, when the call asks for it with 'inline' (#957).
/// </summary>
/// <remarks>
/// <para>
/// The reply names a file on the IDE's machine, which a client attached from another, through a tunnel to the loopback
/// port, cannot open: every snapshot of a remote run was fetched by hand. Path-only stays the default, because an image
/// in the reply costs a caller on this machine its size in context whether or not it looks.
/// </para>
/// <para>
/// A filter, as <see cref="OpenDialogNote"/> is, so the reply's JSON keeps the shape its record gives it and the image is
/// a block after it. Read from the file the tool has just written, so it is the image the path names. A file that cannot
/// be read leaves the reply as the tool gave it, with a block saying why, rather than costing the call its answer.
/// </para>
/// </remarks>
internal static class InlineSnapshot
{
    internal const string Tool = "take_snapshot";
    internal const string Argument = "inline";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) => With(request.Params, await next(request, ct));

    /// <summary><paramref name="result"/> with the image its path names after it, when <paramref name="call"/> asked.</summary>
    /// <param name="read">How the file is read; replaced by a test.</param>
    internal static CallToolResult With(CallToolRequestParams? call, CallToolResult result, Func<string, byte[]>? read = null)
    {
        if (call?.Name != Tool || !Asked(call) || result.IsError == true)
            return result;
        if (result.Content.OfType<TextContentBlock>().Select(b => PathOf(b.Text)).FirstOrDefault(p => p is not null) is not { } path)
            return result;

        ContentBlock added;
        try
        {
            added = ImageContentBlock.FromBytes((read ?? File.ReadAllBytes)(path), "image/png");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            added = new TextContentBlock { Text = $"The image was not inlined: {path} could not be read ({e.Message})" };
        }

        return new CallToolResult
        {
            Content = [.. result.Content, added],
            IsError = result.IsError,
            StructuredContent = result.StructuredContent,
            Meta = result.Meta,
        };
    }

    private static bool Asked(CallToolRequestParams call) =>
        call.Arguments?.TryGetValue(Argument, out var inline) == true && inline.ValueKind == System.Text.Json.JsonValueKind.True;

    private static string? PathOf(string text)
    {
        try
        {
            return (JsonNode.Parse(text) as JsonObject)?["path"] is JsonValue value && value.TryGetValue<string>(out var path)
                ? path
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
