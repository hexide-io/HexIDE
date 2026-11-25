using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.IDE;

namespace HexIDE.Runtime.Dialogs;

/// <summary>
/// A running program's MsgBox and InputBox, shown in windows the IDE makes for them (#3513, #5616). They stay separate from
/// the IDE's own prompts: the IDE records the windows it made here as the program's.
/// </summary>
internal static class ProgramDialogs
{
    /// <summary>Whether <paramref name="content"/> is one of a running program's boxes.</summary>
    internal static bool IsProgramBox(object? content) => content is MessageBox or InputBox;

    internal static async Task<string?> ShowInputBox(IProgramDialogWindows windows, bool singleView, string prompt, string? caption,
        string defaultText)
    {
        caption ??= Distribution.DisplayName;
        if (singleView)
        {
            var inputBox = new InputBox
            {
                Prompt = prompt,
                Text = defaultText
            };
            var task = windows.ShowInView(caption, inputBox, ProgramBox.Input, out var close);
            string?[] ret = new string?[1];
            inputBox.TextRequest += InputBoxOnCloseRequested;
            void InputBoxOnCloseRequested(string? result)
            {
                ret[0] = result;
                close();
            }
            await task;
            inputBox.TextRequest -= InputBoxOnCloseRequested;
            return ret[0];
        }
        else if (windows.Owner is Window owner)
        {
            string? result = null;
            var inputBox = new InputBox { Prompt = prompt, Text = defaultText };
            var window = (Window)windows.MakeWindow(caption, inputBox, ProgramBox.Input);
            inputBox.ApplyProgramChrome(window);
            inputBox.TextRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(owner);
            return result;
        }
        else
            throw new NotImplementedException();
    }

    internal static async Task<MessageBoxResult> ShowMessageBox(IProgramDialogWindows windows, bool singleView, string text,
        string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxLayout layout)
    {
        caption ??= Distribution.DisplayName;
        if (singleView)
        {
            var messageBox = new MessageBox
            {
                Text = text,
                Buttons = buttons,
                Icon = icon,
                Layout = layout
            };
            var task = windows.ShowInView(caption, messageBox, ProgramBox.Message, out var close);
            MessageBoxResult[] ret = [MessageBoxResult.None];
            messageBox.AcceptRequest += MessageBoxOnCloseRequested;
            void MessageBoxOnCloseRequested(MessageBoxResult result)
            {
                ret[0] = result;
                close();
            }
            await task;
            messageBox.AcceptRequest -= MessageBoxOnCloseRequested;
            return ret[0];
        }
        else if (windows.Owner is Window owner)
        {
            // A person's close answers as Windows' does, or is refused where the box has none, and a box hidden with
            // its owner keeps waiting (see ShowAndWait).
            var messageBox = new MessageBox { Text = text, Buttons = buttons, Icon = icon, Layout = layout };
            var window = (Window)windows.MakeWindow(caption, messageBox, ProgramBox.Message);
            return await messageBox.ShowAndWait(window, owner);
        }
        else
            throw new NotImplementedException();
    }
}
