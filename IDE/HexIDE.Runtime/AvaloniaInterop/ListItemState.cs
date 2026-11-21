using System;
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
        // vb6.exe refuses the form for a List record cut short; the run shows this as an internal error, not as a load error (#5147).
        var items = Decode(instance.GetPropertyOrDefault(VBProperties.ListProperty),
            () => new InvalidOperationException($"Property List in {instance.GetPropertyOrDefault(VBProperties.NameProperty)} could not be set"));
        var data = Decode(instance.GetPropertyOrDefault(VBProperties.ItemDataProperty), Refused);
        var values = new long[items?.Length ?? 0];
        if (data is not null)
            for (var i = 0; i < Math.Min(data.Length, values.Length); i++)
                values[i] = ReadData(data[i]);
        // Validate both records before changing the control. The saved properties themselves stay verbatim for saving.
        var state = For(list);
        if (items is not null)
            foreach (var text in items)
                list.Items.Add(text);
        for (var i = 0; i < values.Length; i++) state.Data[i] = values[i];
        state.NewIndex = -1;
        list.SelectedIndex = -1;
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

    private static string[]? Decode(byte[]? record, Func<Exception> truncated)
    {
        if (record is null) return null;
        var at = 0;
        int Word()
        {
            if (record.Length - at < 2) throw truncated();
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
            if (length > record.Length - at) throw truncated();
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
