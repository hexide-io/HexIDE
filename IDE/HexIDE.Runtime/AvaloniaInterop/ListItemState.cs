using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.Specialized;
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
        var (items, values) = ReadSavedItems(instance);
        // Validate both records before changing the control. The saved properties themselves stay verbatim for saving.
        var state = For(list);
        if (items is not null)
            foreach (var text in items)
                list.Items.Add(text);
        for (var i = 0; i < values.Length; i++) state.Data[i] = values[i];
        state.NewIndex = -1;
        list.SelectedIndex = -1;
    }

    internal static (string[]? Items, long[] Values) ReadSavedItems(ComponentInstance instance)
    {
        // Form deserialization has already repaired the measured end-of-file cuts (#5147).
        var items = Decode(instance.GetPropertyOrDefault(VBProperties.ListProperty),
            () => new InvalidOperationException($"Property List in {instance.GetPropertyOrDefault(VBProperties.NameProperty)} could not be set"));
        var data = Decode(instance.GetPropertyOrDefault(VBProperties.ItemDataProperty), Refused);
        var values = new long[items?.Length ?? 0];
        if (data is not null)
            for (var i = 0; i < Math.Min(data.Length, values.Length); i++)
                values[i] = ReadData(data[i]);
        return (items, values);
    }

    // An ItemData entry, as vb6.exe reads it (part of #5127; SP6 native and p-code, oracle PRs #5130 and #5155):
    // spaces, tabs, CRs and LFs are skipped, then at most one sign that the digits follow directly, then
    // decimal digits up to the first character that isn't one, wrapping past a Long. Anything else reads 0: "abc",
    // "", "&H10", "x5", "+ 8", "+-7" and "--9" all did.
    private static int ReadData(string text)
    {
        var at = 0;
        while (at < text.Length && text[at] is ' ' or '\t' or '\r' or '\n') at++;
        // A vertical tab or a form feed here wasn't measured.
        if (at < text.Length && text[at] is '\v' or '\f') throw Refused();
        var negative = false;
        if (at < text.Length && text[at] is '+' or '-')
            negative = text[at++] == '-';
        var value = 0;
        while (at < text.Length && text[at] is >= '0' and <= '9')
            value = unchecked(value * 10 + text[at++] - '0');
        return negative ? unchecked(-value) : value;
    }

    // #5147 / oracle PR #5314. Keep the original entry bytes and rewrite only the framing.
    internal static PropertyClass? RecoverSavedRecords(ComponentInstance instance, byte[] lastRecord)
    {
        var list = instance.GetPropertyOrDefault(VBProperties.ListProperty);
        var data = instance.GetPropertyOrDefault(VBProperties.ItemDataProperty);
        if (ReferenceEquals(list, lastRecord)
            && TryReadPrefix(lastRecord, int.MaxValue, out var items) && items.Count < items.DeclaredCount
            && lastRecord.Length - items.End != 1)
        {
            instance.SetProperty(VBProperties.ListProperty, Rewrite(lastRecord, items, items.Longest));
            if (data is not null && TryReadPrefix(data, int.MaxValue, out var values)
                && values.Count == values.DeclaredCount && values.Count > items.Count)
            {
                TryReadPrefix(data, items.Count, out var keptValues);
                instance.SetProperty(VBProperties.ItemDataProperty, Rewrite(data, keptValues, items.Longest));
            }
            return VBProperties.ListProperty;
        }
        if (ReferenceEquals(data, lastRecord)
            && TryReadPrefix(lastRecord, int.MaxValue, out var entries) && entries.Count < entries.DeclaredCount
            && entries.End == lastRecord.Length
            && list is not null && TryReadPrefix(list, int.MaxValue, out var wholeList)
            && wholeList.Count == wholeList.DeclaredCount)
        {
            if (entries.Count > wholeList.Count)
                TryReadPrefix(lastRecord, wholeList.Count, out entries);
            instance.SetProperty(VBProperties.ItemDataProperty, Rewrite(lastRecord, entries, wholeList.Longest));
            return VBProperties.ItemDataProperty;
        }
        return null;
    }

    private readonly record struct RecordPrefix(int DeclaredCount, int Count, int End, int Longest);

    private static bool TryReadPrefix(byte[] record, int limit, out RecordPrefix prefix)
    {
        prefix = default;
        if (!TryReadHeader(record, out var declaredCount, out var at)) return false;
        var count = 0;
        var longest = 0;
        while (count < Math.Min(declaredCount, limit) && TryReadEntry(record, at, out var length))
        {
            at += 2 + length;
            longest = Math.Max(longest, length);
            count++;
        }
        prefix = new(declaredCount, count, at, longest);
        return true;
    }

    private static byte[] Rewrite(byte[] record, RecordPrefix prefix, int longest)
    {
        if (prefix.Count == 0) return [0, 0];
        var rewritten = record[..prefix.End];
        BinaryPrimitives.WriteUInt16LittleEndian(rewritten, (ushort)prefix.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(rewritten.AsSpan(2), (ushort)longest);
        return rewritten;
    }

    private static bool TryReadHeader(byte[] record, out int count, out int at)
    {
        count = record.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(record) : 0;
        at = count == 0 ? 2 : 4; // The second field is ignored by VB6 (measured with 0, 2 and 9).
        return record.Length >= at;
    }

    private static bool TryReadEntry(byte[] record, int at, out int length)
    {
        var remaining = record.AsSpan(at);
        length = remaining.Length >= 2 ? BinaryPrimitives.ReadUInt16LittleEndian(remaining) : 0;
        return remaining.Length - 2 >= length;
    }

    private static string[]? Decode(byte[]? record, Func<Exception> truncated)
    {
        if (record is null) return null;
        if (!TryReadHeader(record, out var count, out var at)) throw truncated();
        var items = new string[count];
        var encoding = Vb6TextEncoding.Ansi.GetEncoding();
        for (var i = 0; i < count; i++)
        {
            if (!TryReadEntry(record, at, out var length)) throw truncated();
            at += 2;
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
        return items;
    }

    private static NotImplementedException Refused() =>
        new("Saved List / ItemData record cannot be decoded (#5127)");
}
