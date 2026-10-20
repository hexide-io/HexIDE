using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;

namespace HexIDE.Desktop.Server;

/// <summary>
/// The JSON options a tool's reply is written with: the SDK's own, except that text is left as written (#753).
/// </summary>
/// <remarks>
/// The default encoder escapes every apostrophe, <c>+</c>, <c>&lt;</c> and non-ASCII character, so that JSON can sit
/// safely in a web page. A reply here goes over loopback to an automation client and is read as text, where a quoted
/// name came out as <c>'Form1'</c> and a module named in Cyrillic could not be read at all. Quotes,
/// backslashes and control characters are still escaped, as JSON requires.
/// </remarks>
internal static class ReplyJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(McpJsonUtilities.DefaultOptions)
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.MakeReadOnly();
        return options;
    }
}
