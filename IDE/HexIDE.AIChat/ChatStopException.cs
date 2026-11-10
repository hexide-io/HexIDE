namespace HexIDE.AIChat;

internal sealed class ChatStopException(string resourceKey, string? explanation = null)
    : Exception(explanation ?? resourceKey)
{
    public string ResourceKey { get; } = resourceKey;
    public string? Explanation { get; } = explanation;
}
