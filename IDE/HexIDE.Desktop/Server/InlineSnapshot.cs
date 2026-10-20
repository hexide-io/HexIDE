using System;
using System.Collections.Concurrent;
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
/// in the reply costs a caller on this machine context whether or not it looks.
/// </para>
/// <para>
/// The tool encodes the inline copy from the image it has in hand and holds it here under the path it reports; this
/// filter attaches it after the reply, as <see cref="OpenDialogNote"/> adds its note, so the JSON keeps the shape its
/// record gives it. Nothing is read back from disk, where a file in a shared temp directory could have been swapped
/// between the write and the read (#957 review).
/// </para>
/// <para>
/// The copy is fitted to <see cref="LongestEdge"/>. A model is shown an image at about that size whatever is sent, a
/// request holding many images refuses one over 2000 pixels on a side, and the full-size PNG of a 4K window is several
/// times the bytes. The reply gives the copy's own size and scale, so a control's place in it can still be worked out
/// from inspect_element's boundingRect.
/// </para>
/// </remarks>
internal static class InlineSnapshot
{
    internal const string Tool = "take_snapshot";
    internal const string Argument = "inline";

    /// <summary>The longest side of the inline copy, in pixels.</summary>
    internal const int LongestEdge = 1568;

    private static readonly ConcurrentDictionary<string, byte[]> Held = new(StringComparer.Ordinal);

    /// <summary>The size of the inline copy of an image this size: unchanged when it already fits.</summary>
    internal static (int Width, int Height) Fit(int width, int height)
    {
        var longest = Math.Max(width, height);
        if (longest <= LongestEdge)
            return (width, height);
        var factor = (double)LongestEdge / longest;
        return (Math.Max(1, (int)Math.Round(width * factor)), Math.Max(1, (int)Math.Round(height * factor)));
    }

    /// <summary>Holds the inline copy of the image saved at <paramref name="path"/>, for the filter to attach.</summary>
    internal static void Hold(string path, byte[] png) => Held[path] = png;

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) => With(request.Params, await next(request, ct));

    /// <summary><paramref name="result"/> with the copy held for its path after it, when <paramref name="call"/> asked.</summary>
    internal static CallToolResult With(CallToolRequestParams? call, CallToolResult result)
    {
        if (call?.Name != Tool)
            return result;
        var path = result.Content.OfType<TextContentBlock>().Select(b => PathOf(b.Text)).FirstOrDefault(p => p is not null);
        // Taken whatever the reply, so nothing is left held.
        var png = path is not null && Held.TryRemove(path, out var held) ? held : null;
        if (!Asked(call) || result.IsError == true || path is null)
            return result;

        ContentBlock added = png is not null
            ? ImageContentBlock.FromBytes(png, "image/png")
            : new TextContentBlock { Text = $"The image was not inlined: no copy of {path} was made." };
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
