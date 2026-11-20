using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class FindReplaceView : UserControl
{
    private FindReplaceViewModel? _viewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindModel(TopLevel.GetTopLevel(this) is null ? null : DataContext as FindReplaceViewModel);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindModel(DataContext as FindReplaceViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        BindModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void BindModel(FindReplaceViewModel? model)
    {
        if (_viewModel is not null) _viewModel.FocusRequested -= FocusFindWhat;
        _viewModel = model;
        if (_viewModel is not null) _viewModel.FocusRequested += FocusFindWhat;
    }

    private void FocusFindWhat()
    {
        (TopLevel.GetTopLevel(this) as Window)?.Activate();
        FindWhat.Focus(NavigationMethod.Tab);
    }

    public FindReplaceView()
    {
        InitializeComponent();

        // Direction's access key focuses its group, and the group hands focus to its selected option, where Tab would
        // have entered it, as a Windows option group does (#2738, #4142).
        DirectionGroup.GotFocus += (_, e) =>
        {
            if (!ReferenceEquals(e.Source, DirectionGroup))
                return;
            var options = DirectionGroup.Children.OfType<RadioButton>().ToArray();
            (options.FirstOrDefault(option => option.IsChecked == true) ?? options.FirstOrDefault())
                ?.Focus(NavigationMethod.Tab);
        };
    }
}
