using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// SSTab's position-based page bookkeeping (#1090). Coordinates are host pixels (15 twips per pixel), not form scale units.
/// Initial membership is supplied by the caller; it cannot be inferred from the off-page positions.
/// </summary>
internal sealed class SSTabPages
{
    private const double OffPage = 75000d / 15;
    private readonly Canvas host;
    private readonly List<List<(Control Child, bool TabStop)>> pages = new();

    internal SSTabPages(Canvas host, int showing, IReadOnlyList<IReadOnlyList<Control>> pages,
        IReadOnlyList<bool>? controlEnabled = null)
    {
        this.host = host;
        Tab = showing;
        for (var tab = 0; tab < pages.Count; tab++)
        {
            var recorded = new List<(Control, bool)>();
            foreach (var child in pages[tab])
            {
                // Keep the original TabStop before suppressing focus on hidden pages.
                recorded.Add((child, KeyboardNavigation.GetIsTabStop(child)));
            }
            this.pages.Add(recorded);
        }
        if (controlEnabled is not null && showing != 0 && controlEnabled[0])
        {
            Leave(0);
            this.pages[showing].Clear();
        }
        for (var tab = 0; tab < pages.Count; tab++)
            if (controlEnabled is null ? tab != showing : !controlEnabled[tab])
                foreach (var child in pages[tab])
                    KeyboardNavigation.SetIsTabStop(child, false);
    }

    internal int Tab { get; private set; }
    internal int Tabs => pages.Count;
    internal event Action<int>? Changed;

    internal void Select(int tab)
    {
        if (tab < 0 || tab >= Tabs)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (tab == Tab)
            return;
        var previous = Tab;
        Leave(previous);
        foreach (var (child, tabStop) in pages[tab])
        {
            Canvas.SetLeft(child, Canvas.GetLeft(child) + OffPage);
            KeyboardNavigation.SetIsTabStop(child, tabStop);
        }
        Tab = tab;
        Changed?.Invoke(previous);
    }

    private void Leave(int previous)
    {
        var leaving = pages[previous];
        leaving.Clear();
        foreach (var child in host.Children)
        {
            if (Canvas.GetLeft(child) + child.Width >= 0 && Canvas.GetTop(child) + child.Height >= 0)
            {
                leaving.Add((child, KeyboardNavigation.GetIsTabStop(child)));
                Canvas.SetLeft(child, Canvas.GetLeft(child) - OffPage);
                KeyboardNavigation.SetIsTabStop(child, false);
            }
        }
    }

    internal IEnumerable<Control> RecordedChildren => pages.SelectMany(p => p.Select(c => c.Child)).Distinct();

    internal void Resize(int tabs)
    {
        if (tabs > 99)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        // Whether zero is valid for an empty SSTab has not been separated by the oracle.
        if (tabs <= 0)
        {
            if (pages[0].Count != 0)
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            throw new NotImplementedException("An SSTab's empty tab-count range is not measured");
        }
        if (tabs <= Tab)
            Select(0);
        for (var tab = tabs; tab < Tabs; tab++)
        {
            if (pages[tab].Count != 0)
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        }
        if (tabs < Tabs)
            pages.RemoveRange(tabs, Tabs - tabs);
        while (Tabs < tabs)
            pages.Add(new List<(Control, bool)>());
    }
}
