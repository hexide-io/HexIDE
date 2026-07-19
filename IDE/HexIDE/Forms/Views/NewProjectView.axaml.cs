using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class NewProjectView : UserControl
{
    public NewProjectView()
    {
        InitializeComponent();
    }

    private void TemplateDoubleTap(object? sender, TappedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm)
        {
            if (vm.OkNew.CanExecute(null))
                vm.OkNew.Execute(null);
        }
    }

    private void FileBrowserDoubleTap(object? sender, TappedEventArgs e)
    {
        if (DataContext is not NewProjectViewModel vm)
            return;

        var selected = vm.FileBrowser.SelectedEntry;
        if (selected == null)
            return;

        if (selected.IsDirectory)
        {
            vm.FileBrowser.NavigateToDirectory(selected.FullPath);
        }
        else
        {
            // Double-click a file → open it
            vm.FileBrowser.FileName = selected.Name;
            if (vm.OkExisting.CanExecute(null))
                vm.OkExisting.Execute(null);
        }
    }

    private void RecentProjectDoubleTap(object? sender, TappedEventArgs e)
    {
        if (DataContext is NewProjectViewModel vm)
        {
            if (vm.OkRecent.CanExecute(null))
                vm.OkRecent.Execute(null);
        }
    }
}