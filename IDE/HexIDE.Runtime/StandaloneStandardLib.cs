using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public class StandaloneStandardLib : IBasicStandardLibrary
{
    private readonly Window _parent;

    public StandaloneStandardLib(Window parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// A null <paramref name="caption"/> means the VB6 caller omitted the Title argument, where VB6 shows
    /// the application name. An empty string means they passed one deliberately, and stays empty.
    /// </summary>
    /// <remarks>
    /// <c>App.Title</c> is substituted upstream now that the App object exists (#136), so a null reaching
    /// here means there was no project behind the program at all — a bare interpreter or the headless
    /// runner. This is the last resort for that case, not the application-name default.
    /// </remarks>
    private const string OmittedTitleStandIn = "HexIDE";

    public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        var msgBox = new MessageBox { IsProgramDialog = true, Text = text, Buttons = buttons, Icon = icon };
        var window = new Window
        {
            Title = caption ?? OmittedTitleStandIn,
            Content = msgBox,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        return await msgBox.ShowAndWait(window, _parent);
    }

    public async Task<string?> InputBox(string prompt, string? title, string defaultText)
    {
        string? result = null;
        var inputBox = new InputBox { IsProgramDialog = true, Prompt = prompt, Text = defaultText };
        var window = new Window
        {
            Title = title ?? OmittedTitleStandIn,
            Content = inputBox,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        inputBox.TextRequest += r => { result = r; window.Close(); };
        if (_parent.IsVisible)
            await window.ShowDialog(_parent);
        else
        {
            // Before the form is shown, as from Form_Load, which now runs first (#822). Avalonia refuses a dialog over a
            // hidden owner, so the box is a window of its own, as a MsgBox is then (MessageBox.ShowAndWait).
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            // Closed, unanswered, with its owner, as a MsgBox is: ending the run closes the form, even one never shown,
            // and a box left open would hand the ended run an answer (#1010 review).
            void OnOwnerClosed(object? sender, EventArgs e) => window.Close();
            _parent.Closed += OnOwnerClosed;
            window.Show();
            await closed.Task;
            _parent.Closed -= OnOwnerClosed;
        }
        return result;
    }

    // The Immediate window shows the text, not the typed values (#1380).
    public void DebugPrint(Vb6Value value) { }

    public void DebugWrite(string text) => VBDebugConsole.Write(text);

    public int DebugColumn => VBDebugConsole.Column;

    public ITextClipboard Clipboard => new AvaloniaTextClipboard(() => _parent.Clipboard);
    public IVbSettingsStore Settings => VbSettingsStores.ForThisUser();

    private AvaloniaInterop.AvaloniaDisplay? display;
    public IVbDisplay Display => display ??= new AvaloniaInterop.AvaloniaDisplay(() => _parent);

    /// <summary>A CommonDialog's Open or Save As (#1084), as the platform's own picker over the program's window.</summary>
    public async Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request)
    {
        if (HostDialogs.File is { } application)
            return await application(request);
        var provider = TopLevel.GetTopLevel(_parent)?.StorageProvider
                       ?? throw new NotImplementedException("A CommonDialog needs a window to show its dialog over (#1084)");
        var start = await VbFileDialogOptions.StartFolder(provider, request);
        if (request.Save)
        {
            var saved = await provider.SaveFilePickerWithResultAsync(VbFileDialogOptions.Save(request, start));
            return saved.File?.TryGetLocalPath() is { } savedPath
                ? new VbFileDialogAnswer(savedPath, VbFileDialogOptions.IndexOf(request, saved.SelectedFileType))
                : null;
        }
        var opened = await provider.OpenFilePickerAsync(VbFileDialogOptions.Open(request, start));
        if (opened.Count == 0 || opened[0].TryGetLocalPath() is not { } firstPath)
            return null;
        var paths = new List<string> { firstPath };
        foreach (var file in opened.Skip(1))
            paths.Add(file.TryGetLocalPath()
                      ?? throw new NotImplementedException("A CommonDialog non-local file selection is not implemented (#1084)"));
        return new VbFileDialogAnswer(paths[0], null, paths.Skip(1).ToArray());
    }

    public async Task<bool> ShowPrintDialog() =>
        HostDialogs.Print is { } application
            ? await application()
            : throw new NotImplementedException("A CommonDialog's print dialog is not implemented outside the IDE (#2299)");

    public async Task<int?> ShowColorDialog(int initial) =>
        HostDialogs.Color is { } application
            ? await application(initial)
            : throw new NotImplementedException("A CommonDialog's color dialog is not implemented outside the IDE (#2299)");

    /// <summary>A CommonDialog's ShowFont (#1084): the application's font dialog, where it has one.</summary>
    public async Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) =>
        HostDialogs.Font is { } application
            ? await application(initial)
            : throw new NotImplementedException("A CommonDialog's font dialog is not implemented outside the IDE (#1084)");
}
