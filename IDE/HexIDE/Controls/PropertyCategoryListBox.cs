using System;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using HexIDE.VisualDesigner;

namespace HexIDE.Controls;

public class PropertyCategoryListBox : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        var needsContainer = base.NeedsContainerOverride(item, index, out recycleKey);
        if (needsContainer)
            recycleKey = item is PropertyCategoryViewModel ? typeof(PropertyCategoryViewModel) : typeof(PropertyViewModel);
        return needsContainer;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new PropertyCategoryListBoxItem();

    private sealed class PropertyCategoryListBoxItem : ListBoxItem
    {
        protected override Type StyleKeyOverride => typeof(ListBoxItem);

        protected override AutomationPeer OnCreateAutomationPeer()
            => DataContext is PropertyCategoryViewModel
                ? new CategoryAutomationPeer(this)
                : base.OnCreateAutomationPeer();
    }

    private sealed class CategoryAutomationPeer(ListBoxItem owner) : ListItemAutomationPeer(owner), IExpandCollapseProvider
    {
        private PropertyCategoryViewModel Category => (PropertyCategoryViewModel)owner.DataContext!;

        public ExpandCollapseState ExpandCollapseState => Category.IsExpanded
            ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public bool ShowsMenu => false;

        public void Expand() => Category.IsExpanded = true;
        public void Collapse() => Category.IsExpanded = false;
    }
}
