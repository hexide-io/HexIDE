using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.VisualDesigner;

namespace HexIDE.Controls;

public class ControlsContainer : ListBox
{
    public ControlsContainer()
    {
        SelectionMode = SelectionMode.Multiple;
        SelectionChanged += (_, _) => UpdatePrimaryFlags();
    }

    public Action<IReadOnlyList<ComponentInstanceViewModel>>? BeginDragCallback { get; set; }
    public Action? EndDragCallback { get; set; }

    // Called by FormEditView code-behind to perform a VM-driven single-item selection
    // (clears multi-selection, then selects one item).
    public void SetSingleSelection(object? item)
    {
        Selection.BeginBatchUpdate();
        Selection.Clear();
        var index = Items.IndexOf(item);
        if (index >= 0)
            Selection.Select(index);
        Selection.EndBatchUpdate();
    }

    private void UpdatePrimaryFlags()
    {
        var selectedItems = new List<ControlItem>();
        for (int i = 0; i < Items.Count; i++)
        {
            if (ContainerFromIndex(i) is ControlItem item)
            {
                item.IsPrimary = (i == SelectedIndex);
                if (item.IsSelected)
                    selectedItems.Add(item);
            }
        }

        // Every selected adorner gets all other selected controls as participants
        // so that dragging any of them moves the whole group.
        var allSelectedVms = selectedItems
            .Select(i => i.DataContext as ComponentInstanceViewModel)
            .OfType<ComponentInstanceViewModel>()
            .ToList();

        foreach (var selectedItem in selectedItems)
        {
            if (AdornerLayer.GetAdorner(selectedItem) is ResizeAdorner adorner)
            {
                if (selectedItems.Count > 1)
                {
                    var participants = new List<ControlItem>(selectedItems.Count - 1);
                    foreach (var other in selectedItems)
                    {
                        if (other != selectedItem)
                            participants.Add(other);
                    }
                    adorner.GroupDragParticipants = participants;
                }
                else
                {
                    adorner.GroupDragParticipants = null;
                }

                adorner.OnDragStarted = () => BeginDragCallback?.Invoke(allSelectedVms);
                adorner.OnDragCompleted = () => EndDragCallback?.Invoke();
            }
        }
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new ControlItem();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<ControlItem>(item, out recycleKey);
    }
}
