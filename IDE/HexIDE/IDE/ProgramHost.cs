using HexIDE.Runtime.AvaloniaInterop;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Localization;

namespace HexIDE.IDE;

/// <summary>The IDE's dialogs and failure texts for a running program (#4066).</summary>
public sealed class ProgramHost(IWindowManager windowManager, ILocalizationService localization) : IProgramHost
{
    public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxLayout layout) =>
        windowManager.MessageBox(text, caption, buttons, icon, isProgramDialog: true, layout: layout);

    public Task<string?> InputBox(string prompt, string? title, string defaultText) =>
        windowManager.InputBox(prompt, title, defaultText, isProgramDialog: true);

    public Task<bool> PrintDialog() => windowManager.ShowPrintDialog();

    public Task<int?> ColorDialog(int initial) => windowManager.ShowColorDialog(initial);

    public string StartupFormFailedToLoad(string formName, string description) =>
        string.Format(localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad"), formName, description);

    public string NotImplementedYet => localization.GetString("Str.ProjectRunner.NotImplementedYet");

    public async Task<VbFileDialogAnswer?> FileDialog(VbFileDialogRequest request)
    {
#if DEBUG
        // An armed answer stands for a name typed with the filter left as the program set it.
        int? scriptedIndex = HexIDE.IDE.ScriptedFileDialogs.Pending > 0 && request.FilterIndex >= 1 ? request.FilterIndex : null;
#else
        int? scriptedIndex = null;
#endif
        var start = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                    && TopLevel.GetTopLevel(main)?.StorageProvider is { } provider
            ? await VbFileDialogOptions.StartFolder(provider, request)
            : null;
        if (request.Save)
        {
            var path = await windowManager.SaveFilePickerAsync(VbFileDialogOptions.Save(request, start));
            return path is null ? null : new VbFileDialogAnswer(path, scriptedIndex);
        }
        var paths = await windowManager.OpenFilePickerAsync(VbFileDialogOptions.Open(request, start));
        return paths is { Count: > 0 }
            ? new VbFileDialogAnswer(paths[0], scriptedIndex, paths.Skip(1).ToArray())
            : null;
    }

    /// <summary>A CommonDialog's ShowFont (#1084), through the IDE's font dialog.</summary>
    public async Task<VbFontChoice?> FontDialog(VbFontChoice initial)
    {
        var start = initial.Name.Length == 0
            ? null
            : new FontDialogResult(new Avalonia.Media.FontFamily(initial.Name),
                initial.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                initial.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal, initial.Size);
        var chosen = await windowManager.ShowFontDialog(start);
        return chosen is null
            ? null
            : new VbFontChoice(chosen.Family.Name, (float)chosen.Size, chosen.Weight >= Avalonia.Media.FontWeight.Bold,
                chosen.Style != Avalonia.Media.FontStyle.Normal);
    }
}
