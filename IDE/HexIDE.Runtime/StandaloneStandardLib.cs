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

    public async Task<MessageBoxResult> MsgBox(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        MessageBoxResult result = MessageBoxResult.None;
        var msgBox = new MessageBox { Text = text, Buttons = buttons, Icon = icon };
        var window = new Window
        {
            Title = caption,
            Content = msgBox,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        msgBox.AcceptRequest += r => { result = r; window.Close(); };
        await window.ShowDialog(_parent);
        return result;
    }

    public async Task<string?> InputBox(string prompt, string title, string defaultText)
    {
        string? result = null;
        var inputBox = new InputBox { Prompt = prompt, Text = defaultText };
        var window = new Window
        {
            Title = title,
            Content = inputBox,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        inputBox.TextRequest += r => { result = r; window.Close(); };
        await window.ShowDialog(_parent);
        return result;
    }

    public void DebugPrint(Vb6Value value) => VBDebugConsole.Emit(value);
}
