using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
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
    /// <summary>
    /// Show a message box and wait for its answer: over <paramref name="owner"/> when given, else over the active window. A
    /// box that asks about one window's content is shown over that window, which it then holds, as a person sees it there.
    /// </summary>
    Task<MessageBoxResult> MessageBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None, Window? owner = null, bool isProgramDialog = false);
    /// <summary>Show a message box holding every desktop window until it is answered.</summary>
    Task<MessageBoxResult> TaskModalMessageBox(string text, string? caption = null, MessageBoxButtons buttons = MessageBoxButtons.Ok, MessageBoxIcon icon = MessageBoxIcon.None, Window? owner = null);
    Task<string?> InputBox(string prompt, string? caption, string defaultText, bool isProgramDialog = false);
    Task<IReadOnlyList<string>?> OpenFilePickerAsync(FilePickerOpenOptions options);
    Task<string?> SaveFilePickerAsync(FilePickerSaveOptions options);
    Task ShowAbout(AboutDialogOptions options);
    Task<bool> ShowPrintDialog();
    Task<int?> ShowColorDialog(int initial);
    Task<FontDialogResult?> ShowFontDialog(FontDialogResult? initial = null);
}