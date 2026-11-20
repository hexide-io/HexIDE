using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

/// <summary>Resolves the owning window's clipboard at call time, including calls before a form is shown.</summary>
public sealed class AvaloniaTextClipboard(Func<IClipboard?> resolve) : ITextClipboard
{
    private IClipboard Clipboard => resolve()
        ?? throw new NotImplementedException("The host has no system clipboard (#1156)");

    private static readonly DataFormat<byte[]> RtfFormat = DataFormat.CreateBytesPlatformFormat(
        OperatingSystem.IsWindows() ? "Rich Text Format" : OperatingSystem.IsMacOS() ? "public.rtf" : "text/rtf");

    // Avalonia Win32 ClipboardFormatRegistry.cs:39-47 names these text companions this way (#5078).
    // Other platforms have no known companions: extra Linux text targets count as other formats and refuse queries.
    private static readonly DataFormat[] TextCompanions =
    [
        DataFormat.CreateBytesPlatformFormat("Unknown_Format_1"),
        DataFormat.CreateBytesPlatformFormat("Unknown_Format_7"),
        DataFormat.CreateBytesPlatformFormat("Unknown_Format_16")
    ];

    private static Encoding AnsiEncoding => CodePagesEncodingProvider.Instance.GetEncoding(Vb6TextEncoding.Ansi.CodePage)
        ?? throw new NotImplementedException("The clipboard ANSI code page is unavailable");

    public async Task<string?> GetRtf()
    {
        var bytes = await Clipboard.TryGetValueAsync(RtfFormat);
        if (bytes == null) return null;
        var nul = Array.IndexOf(bytes, (byte)0);
        return AnsiEncoding.GetString(bytes, 0, nul < 0 ? bytes.Length : nul);
    }

    public Task SetRtf(string text) => Replace(RtfFormat, AnsiEncoding.GetBytes(text + "\0"));

    public async Task<bool> HasOtherFormats() =>
        (await Clipboard.GetDataFormatsAsync()).Any(format => !IsText(format) && format != RtfFormat);

    private static bool IsText(DataFormat format) => format == DataFormat.Text || TextCompanions.Contains(format);

    public Task Clear() => Clipboard.ClearAsync();
    public Task<string?> GetText() => Clipboard.TryGetTextAsync();
    public Task SetText(string text) => Replace(DataFormat.Text, text);

    private async Task Replace(DataFormat replaced, object value)
    {
        bool Replaces(DataFormat format) => format == replaced || replaced == DataFormat.Text && IsText(format);
        var clipboard = Clipboard;
        using var current = await clipboard.TryGetDataAsync();
        var items = new List<SnapshotItem>();
        if (current != null)
        {
            foreach (var source in current.Items)
            {
                var values = new Dictionary<DataFormat, object>();
                foreach (var format in source.Formats)
                {
                    if (Replaces(format)) continue;
                    // Read everything before replacing the clipboard: delayed reads may become invalid afterwards.
                    values.Add(format, await source.TryGetRawAsync(format)
                        ?? throw new NotImplementedException("Clipboard.SetText cannot preserve an unreadable format; call Clipboard.Clear first (#1246)"));
                }
                if (values.Count > 0) items.Add(new SnapshotItem(values));
            }
        }
        var formats = current?.Formats ?? await clipboard.GetDataFormatsAsync();
        if (formats.Any(format => !Replaces(format) && !items.Any(item => item.Values.ContainsKey(format))))
            throw new NotImplementedException("Clipboard.SetText cannot preserve an unreadable format; call Clipboard.Clear first (#1246)");
        if (items.Count == 0 && replaced == DataFormat.Text)
        {
            await clipboard.SetTextAsync((string)value);
            return;
        }
        if (items.Count == 0) items.Add(new SnapshotItem(new Dictionary<DataFormat, object>()));
        items[0].Values[replaced] = value;
        await clipboard.SetDataAsync(new Snapshot(items));
    }

    private sealed class Snapshot(List<SnapshotItem> items) : IDataTransfer, IAsyncDataTransfer
    {
        public IReadOnlyList<DataFormat> Formats => items.SelectMany(item => item.Formats).Distinct().ToArray();
        IReadOnlyList<IDataTransferItem> IDataTransfer.Items => items;
        IReadOnlyList<IAsyncDataTransferItem> IAsyncDataTransfer.Items => items;
        public void Dispose() { }
    }

    private sealed class SnapshotItem(Dictionary<DataFormat, object> values) : IDataTransferItem, IAsyncDataTransferItem
    {
        public Dictionary<DataFormat, object> Values => values;
        public IReadOnlyList<DataFormat> Formats => values.Keys.ToArray();
        public object? TryGetRaw(DataFormat format) => values.GetValueOrDefault(format);
        public Task<object?> TryGetRawAsync(DataFormat format) => Task.FromResult(TryGetRaw(format));
    }
}
