using System;
using System.Threading.Tasks;
using Avalonia.Controls;
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
        var msgBox = new MessageBox { Text = text, Buttons = buttons, Icon = icon };
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
        var inputBox = new InputBox { Prompt = prompt, Text = defaultText };
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

    public void DebugPrint(Vb6Value value) => VBDebugConsole.Emit(value);
}
