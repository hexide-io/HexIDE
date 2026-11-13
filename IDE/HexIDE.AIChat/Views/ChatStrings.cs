using Avalonia;

namespace HexIDE.AIChat.Views;

internal static class ChatStrings
{
    internal static string GetString(string key) =>
        Application.Current?.TryGetResource(key, null, out var value) == true
            ? value as string ?? key : key;

    internal static string Format(string key, params object[] arguments) =>
        string.Format(GetString(key), arguments);
}
