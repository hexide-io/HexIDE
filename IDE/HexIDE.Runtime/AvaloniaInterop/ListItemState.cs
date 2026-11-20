using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.IDE;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.AvaloniaInterop;

// #5106 / #5114: direct-call measurements in the issue, SP6 native and p-code (oracle PR #5115).
internal sealed class ListItemState
{
    private static readonly ConditionalWeakTable<ItemsControl, ListItemState> States = new();
    internal static ListItemState For(ItemsControl list) => States.GetValue(list, l => new ListItemState(l));

    internal int NewIndex { get; private set; } = -1;
    internal List<long> Data { get; } = new();

    private ListItemState(ItemsControl list)
    {
        Data.AddRange(Enumerable.Repeat(0L, list.ItemCount));
        list.Items.CollectionChanged += (_, e) =>
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    Data.InsertRange(e.NewStartingIndex, Enumerable.Repeat(0L, e.NewItems!.Count));
                    NewIndex = e.NewStartingIndex;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    Data.RemoveRange(e.OldStartingIndex, e.OldItems!.Count);
                    NewIndex = -1;
                    break;
                case NotifyCollectionChangedAction.Reset:
                    Data.Clear();
                    Data.AddRange(Enumerable.Repeat(0L, list.ItemCount));
                    NewIndex = -1;
                    break;
                // Replacing text keeps the item's data and NewIndex.
            }
        };
    }

    internal void ZeroData()
    {
        for (var i = 0; i < Data.Count; i++) Data[i] = 0L;
    }

    internal static void Load(SelectingItemsControl list, ComponentInstance instance)
    {
        var items = Decode(instance.GetPropertyOrDefault(VBProperties.ListProperty));
        var data = Decode(instance.GetPropertyOrDefault(VBProperties.ItemDataProperty));
        if (data is not null && data.Length != (items?.Length ?? 0))
            throw Refused();
        var values = new long[items?.Length ?? 0];
        if (data is not null)
            for (var i = 0; i < data.Length; i++)
            {
                if (!int.TryParse(data[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                    throw Refused();
                values[i] = value;
            }
        // Validate both records before changing the control. The saved properties themselves stay verbatim for saving.
        var state = For(list);
        if (items is not null)
            foreach (var text in items)
                list.Items.Add(text);
        for (var i = 0; i < values.Length; i++) state.Data[i] = values[i];
        state.NewIndex = -1;
        list.SelectedIndex = -1;
    }

    private static string[]? Decode(byte[]? record)
    {
        if (record is null) return null;
        var at = 0;
        int Word()
        {
            if (record.Length - at < 2) throw Refused();
            var value = record[at] | record[at + 1] << 8;
            at += 2;
            return value;
        }
        var count = Word();
        if (count > 0) _ = Word(); // The second field is ignored by VB6 (measured with 0, 2 and 9).
        var items = new string[count];
        var encoding = Vb6TextEncoding.Ansi.GetEncoding();
        for (var i = 0; i < count; i++)
        {
            var length = Word();
            if (length > record.Length - at) throw Refused();
            try
            {
                items[i] = encoding.GetString(record, at, length);
            }
            catch (DecoderFallbackException)
            {
                throw Refused();
            }
            at += length;
        }
        if (at != record.Length) throw Refused();
        return items;
    }

    private static NotImplementedException Refused() =>
        new("Saved List / ItemData record cannot be decoded (#5114)");
}
