namespace HexIDE.AIChat.Views;

public enum MessagePartKind { Text, Code }

public sealed class MessagePart(MessagePartKind kind, string content, string? language = null)
{
    public MessagePartKind Kind { get; } = kind;
    public string Content { get; } = content;
    public string? Language { get; } = language;
    public bool IsText => Kind == MessagePartKind.Text;
    public bool IsCode => Kind == MessagePartKind.Code;

    public static IReadOnlyList<MessagePart> Parse(string text)
    {
        var parts = new List<MessagePart>();
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            var fence = remaining.IndexOf("```", StringComparison.Ordinal);
            if (fence < 0)
            {
                parts.Add(new MessagePart(MessagePartKind.Text, remaining.ToString()));
                break;
            }

            if (fence > 0)
                parts.Add(new MessagePart(MessagePartKind.Text, remaining[..fence].ToString()));

            remaining = remaining[(fence + 3)..];
            var closeFence = remaining.IndexOf("```", StringComparison.Ordinal);
            if (closeFence < 0)
            {
                parts.Add(new MessagePart(MessagePartKind.Code, remaining.ToString()));
                break;
            }

            var codeBlock = remaining[..closeFence];
            var newline = codeBlock.IndexOf('\n');
            string lang = string.Empty, code;
            if (newline > 0)
            {
                lang = codeBlock[..newline].Trim().ToString();
                code = codeBlock[(newline + 1)..].ToString();
            }
            else
            {
                code = codeBlock.ToString();
            }

            parts.Add(new MessagePart(MessagePartKind.Code, code.TrimEnd(), lang));
            remaining = remaining[(closeFence + 3)..];
        }

        return parts;
    }
}
