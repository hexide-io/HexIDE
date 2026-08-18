using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using HexIDE.Runtime.Components;
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

        // A selected control whose container is also selected must not be dragged in its own right: the
        // container is already moving it, and the descendant fan-out re-reads its absolute position. Writing
        // Canvas.Left on both makes the outcome depend on which TwoWay binding happens to fire first — one
        // order leaves the child where it belongs, the other moves it twice as far as its container.
        //
        // A marquee across a Frame on this flat canvas produces exactly that selection, because VB6 scopes
        // its marquee to the container the drag began in and HexIDE cannot yet. So this is the consequence of
        // a stated divergence rather than a detail.
        var selectedInstances = new HashSet<ComponentInstance>(allSelectedVms.Select(v => v.Instance));

        bool ContainerIsSelected(ControlItem item)
        {
            if (item.DataContext is not ComponentInstanceViewModel vm)
                return false;
            for (var container = vm.Instance.Container; container is not null; container = container.Container)
                if (selectedInstances.Contains(container))
                    return true;
            return false;
        }

        var dragSet = selectedItems.Where(i => !ContainerIsSelected(i)).ToList();
        var dragSetVms = dragSet
            .Select(i => i.DataContext as ComponentInstanceViewModel)
            .OfType<ComponentInstanceViewModel>()
            .ToList();

        foreach (var selectedItem in selectedItems)
        {
            if (AdornerLayer.GetAdorner(selectedItem) is ResizeAdorner adorner)
            {
                // Grabbing a control that sits inside a selected container moves just that control, within
                // its container — which is a legitimate VB6 gesture, and the only coherent reading of a grab
                // on a control the group drag is deliberately not moving.
                var grabbedInsideSelection = ContainerIsSelected(selectedItem);

                if (grabbedInsideSelection || dragSet.Count <= 1)
                {
                    adorner.GroupDragParticipants = null;
                }
                else
                {
                    var participants = new List<ControlItem>(dragSet.Count - 1);
                    foreach (var other in dragSet)
                    {
                        if (other != selectedItem)
                            participants.Add(other);
                    }
                    adorner.GroupDragParticipants = participants;
                }

                var dragTargets = grabbedInsideSelection
                    ? (selectedItem.DataContext is ComponentInstanceViewModel one ? [one] : (IReadOnlyList<ComponentInstanceViewModel>)[])
                    : dragSetVms;
                adorner.OnDragStarted = () => BeginDragCallback?.Invoke(dragTargets);
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
