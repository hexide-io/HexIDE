using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.Runtime.Dialogs;

public partial class InputBox : UserControl
{
    public bool IsProgramDialog { get; init; }

    public string Prompt { get; set; } = "";
    public string Text { get; set; } = "";

    public event Action<string?>? TextRequest;

    public InputBox()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // The IDE installs Str.* resources; the standalone runner keeps the English fallback.
        PART_Ok.Content = this.TryFindResource("Str.Dialog.MessageBox.Button.Ok", out var ok)
            && ok is string okCaption ? okCaption : "OK";
        PART_Cancel.Content = this.TryFindResource("Str.Dialog.MessageBox.Button.Cancel", out var cancel)
            && cancel is string cancelCaption ? cancelCaption : "Cancel";
        PART_Prompt.Text = Prompt;
        PART_Input.Text = Text;
        PART_Input.SelectAll();
        PART_Input.Focus();
    }

    private void OkClick(object? sender, RoutedEventArgs e) =>
        TextRequest?.Invoke(PART_Input.Text);

    private void CancelClick(object? sender, RoutedEventArgs e) =>
        TextRequest?.Invoke(null);
}
