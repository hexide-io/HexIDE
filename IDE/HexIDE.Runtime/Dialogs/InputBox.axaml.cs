using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HexIDE.Runtime.Dialogs;

public partial class InputBox : UserControl
{
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
