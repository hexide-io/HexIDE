using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

/// <summary>Resolves the owning window's clipboard at call time, including calls before a form is shown.</summary>
public sealed class AvaloniaTextClipboard(Func<IClipboard?> resolve) : ITextClipboard
{
    private IClipboard Clipboard => resolve()
        ?? throw new NotImplementedException("The host has no system clipboard (#1156)");

    public Task Clear() => Clipboard.ClearAsync();
    public Task<string?> GetText() => Clipboard.TryGetTextAsync();
    public async Task SetText(string text)
    {
        var clipboard = Clipboard;
        // VB6 adds/replaces text without discarding other formats. Until we can preserve those, refuse
        // rather than silently destroy them. Notepad explicitly calls Clear before SetText.
        if ((await clipboard.GetDataFormatsAsync()).Any(format => format != DataFormat.Text))
            throw new NotImplementedException("Clipboard.SetText cannot yet preserve non-text formats; call Clipboard.Clear first (#1156)");
        await clipboard.SetTextAsync(text);
    }
}
