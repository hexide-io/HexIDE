using System;
using System.Collections.Generic;
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
        using var current = await clipboard.TryGetDataAsync();
        var items = new List<SnapshotItem>();
        if (current != null)
        {
            foreach (var source in current.Items)
            {
                var values = new Dictionary<DataFormat, object>();
                foreach (var format in source.Formats)
                {
                    if (format == DataFormat.Text) continue;
                    // Read everything before replacing the clipboard: delayed reads may become invalid afterwards.
                    values.Add(format, await source.TryGetRawAsync(format)
                        ?? throw new NotImplementedException("Clipboard.SetText cannot preserve an unreadable format; call Clipboard.Clear first (#1246)"));
                }
                if (values.Count > 0) items.Add(new SnapshotItem(values));
            }
        }
        var formats = current?.Formats ?? await clipboard.GetDataFormatsAsync();
        if (formats.Any(format => format != DataFormat.Text && !items.Any(item => item.Values.ContainsKey(format))))
            throw new NotImplementedException("Clipboard.SetText cannot preserve an unreadable format; call Clipboard.Clear first (#1246)");
        if (items.Count == 0)
        {
            await clipboard.SetTextAsync(text);
            return;
        }
        items[0].Values[DataFormat.Text] = text;
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
