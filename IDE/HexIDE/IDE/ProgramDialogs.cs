using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Runtime.Dialogs;

namespace HexIDE.IDE;

// The built-in engine's dialogs stay separate from the IDE's own prompts (#3513).
internal static class ProgramDialogs
{
    internal static bool IsRuntimeBox(object? content) => content is MessageBox or InputBox;
    internal static bool IsRuntimeMessageBox(object? content) => content is MessageBox;
    internal static bool IsProgramBox(object? content) =>
        content is MessageBox { IsProgramDialog: true } or InputBox { IsProgramDialog: true };

    internal static async Task<string?> ShowInputBox(WindowManager manager, string prompt, string? caption, string defaultText)
    {
        caption ??= Distribution.DisplayName;
        if (Static.SingleView)
        {
            var inputBox = new InputBox
            {
                IsProgramDialog = true,
                Prompt = prompt,
                Text = defaultText
            };
            var task = manager.ShowManagedWindow(out var window, caption, inputBox, false);

            string?[] ret = new string?[1];

            inputBox.TextRequest += InputBoxOnCloseRequested;

            void InputBoxOnCloseRequested(string? result)
            {
                ret[0] = result;
                window.CloseCommand.Execute(window.CloseCommandParameter);
            }

            await task;

            inputBox.TextRequest -= InputBoxOnCloseRequested;

            return ret[0];
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? result = null;
            var inputBox = new InputBox { IsProgramDialog = true, Prompt = prompt, Text = defaultText };
            var window = manager.MakeDialogWindow(caption, inputBox);
            inputBox.TextRequest += r => { result = r; window.Close(); };
            await window.ShowDialog(manager.GetTopWindow(desktop));
            return result;
        }
        else
            throw new NotImplementedException();
    }

    private static Task<MessageBoxResult> ShowProgramDesktopMessageBox(WindowManager manager, IClassicDesktopStyleApplicationLifetime desktop, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, Window? owner, MessageBoxLayout layout)
    {
        var messageBox = new MessageBox { IsProgramDialog = true, Text = text, Buttons = buttons, Icon = icon, Layout = layout };
        var window = manager.MakeDialogWindow(caption, messageBox);
        return messageBox.ShowAndWait(window, owner ?? manager.GetTopWindow(desktop));
    }

    internal static async Task<MessageBoxResult> ShowMessageBox(WindowManager manager, string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, Window? owner, MessageBoxLayout layout)
    {
        caption ??= Distribution.DisplayName;
        if (Static.SingleView)
        {
            var messageBox = new MessageBox
            {
                IsProgramDialog = true,
                Text = text,
                Buttons = buttons,
                Icon = icon,
                Layout = layout
            };
            var task = manager.ShowManagedWindow(out var window, caption, messageBox, false);

            MessageBoxResult[] ret = [MessageBoxResult.None];

            messageBox.AcceptRequest += MessageBoxOnCloseRequested;

            void MessageBoxOnCloseRequested(MessageBoxResult result)
            {
                ret[0] = result;
                window.CloseCommand.Execute(window.CloseCommandParameter);
            }

            await task;

            messageBox.AcceptRequest -= MessageBoxOnCloseRequested;

            return ret[0];
        }
        else if (Application.Current!.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A person's close answers as Windows' does, or is refused where the box has none, and a box hidden with
            // its owner keeps waiting (see ShowAndWait).
            return await ShowProgramDesktopMessageBox(manager, desktop, text, caption, buttons, icon, owner, layout);
        }
        else
            throw new NotImplementedException();
    }

}
