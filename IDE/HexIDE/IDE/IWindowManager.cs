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

    /// <summary>
    /// Show a managed window, blocking what is behind it or not. A running program's other forms block nothing: each stays
    /// usable beside the startup form, whose window holds the IDE (#822). Completes when the window closes.
    /// </summary>
    Task ShowManagedWindow(MDIWindow window, bool blockBehind);
    Task<MessageBoxResult> MessageBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None);
    Task<string?> InputBox(string prompt, string? caption, string defaultText);
    Task<IReadOnlyList<string>?> OpenFilePickerAsync(FilePickerOpenOptions options);
    Task<string?> SaveFilePickerAsync(FilePickerSaveOptions options);
    Task ShowAbout(AboutDialogOptions options);
    Task<FontDialogResult?> ShowFontDialog(FontDialogResult? initial = null);
}