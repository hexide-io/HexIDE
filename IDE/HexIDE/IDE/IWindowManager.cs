using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Controls;

namespace HexIDE.IDE;

public interface IWindowManager
{
    Task<bool> ShowDialog(IDialog dialog);
    Task ShowWindow(IDialog window);
    Task ShowManagedWindow(MDIWindow window);
    Task<MessageBoxResult> MessageBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None);
    Task<string?> InputBox(string prompt, string? caption, string defaultText);
    Task<IReadOnlyList<string>?> OpenFilePickerAsync(FilePickerOpenOptions options);
    Task<string?> SaveFilePickerAsync(FilePickerSaveOptions options);
    Task ShowAbout(AboutDialogOptions options);
    Task<FontDialogResult?> ShowFontDialog(FontDialogResult? initial = null);
}