using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Threading;
using Avalonia.Interactivity;
using HexIDE.Forms.ViewModels;

namespace HexIDE.Forms.Views;

public partial class FontDialog : UserControl
{
    public FontDialog()
    {
        InitializeComponent();
        ScriptBox.ItemTemplate = new FuncDataTemplate<ScriptItem>((item, _) =>
        {
            var label = new TextBlock();
            if (item != null) label.Bind(TextBlock.TextProperty, label.GetResourceObservable(item.Key));
            return label;
        });
        DataContextChanged += (_, _) =>
        {
            if (_scriptViewModel != null) _scriptViewModel.PropertyChanged -= ScriptsChanged;
            _scriptViewModel = DataContext as FontDialogViewModel;
            if (_scriptViewModel != null) _scriptViewModel.PropertyChanged += ScriptsChanged;
            UpdateScriptItems();
        };
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (TopLevel.GetTopLevel(this) == null) return;
            FontNameBox.Focus();
            FontNameBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private FontDialogViewModel? _scriptViewModel;

    private void ScriptsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontDialogViewModel.ScriptKeys)) UpdateScriptItems();
    }

    private void UpdateScriptItems()
    {
        if (_scriptViewModel == null) return;
        var selected = _scriptViewModel.SelectedScriptIndex;
        ScriptBox.ItemsSource = _scriptViewModel.ScriptKeys.Select(key =>
            new ScriptItem(key, () => this.TryFindResource(key, out var text) ? text?.ToString() : null)).ToArray();
        ScriptBox.SelectedIndex = selected;
    }

    private sealed record ScriptItem(string Key, Func<string?> LocalizedText)
    {
        public override string ToString() => LocalizedText() ?? Key;
    }

    internal Func<string, Task>? ShowSizeError { get; set; }

    private async void OkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FontDialogViewModel vm) return;
        var error = vm.Accept();
        if (error == null) return;
        if (ShowSizeError != null) await ShowSizeError(error);
        FontSizeBox.Focus();
    }

    private void CancelClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FontDialogViewModel)?.Cancel();
}
