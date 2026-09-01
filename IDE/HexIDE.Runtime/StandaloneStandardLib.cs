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
    /// The stand-in below is not the real answer: VB6 uses <c>App.Title</c>, which defaults to the project
    /// name, and this runtime has no App object yet. Deliberately not a project name threaded through by
    /// hand — that would be a second source to unpick once App (#136) exists.
    /// </remarks>
    private const string OmittedTitleStandIn = "HexIDE";

    public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        MessageBoxResult result = MessageBoxResult.None;
        var msgBox = new MessageBox { Text = text, Buttons = buttons, Icon = icon };
        var window = new Window
        {
            Title = caption ?? OmittedTitleStandIn,
            Content = msgBox,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };
        msgBox.AcceptRequest += r => { result = r; window.Close(); };
        await window.ShowDialog(_parent);
        return result;
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
        await window.ShowDialog(_parent);
        return result;
    }

    public void DebugPrint(Vb6Value value) => VBDebugConsole.Emit(value);
}
