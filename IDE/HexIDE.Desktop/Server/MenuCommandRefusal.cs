using System;
using Avalonia.Labs.Input;
using HexIDE.Automation;

namespace HexIDE.Desktop.Server;

/// <summary>Explains a command's refusal using the menu's state when it can be read (#1534).</summary>
internal static class MenuCommandRefusal
{
    // Called after CanExecute returns false. A container's state is stronger evidence than a guess
    // about routing; an ItemsSource entry without a container cannot tell us what the menu shows.
    public static string Describe(MenuPath.Result found, string path, Func<string> focusedElement)
    {
        if (found.Item is { IsEffectivelyEnabled: false } || found.Command is not RoutedCommand)
            return $"'{path}' cannot execute now: the item is disabled.";

        return $"'{path}' cannot execute where keyboard focus is now ({focusedElement()}). It is a routed "
               + "command, which acts on the focused control as a real click does, so an item that belongs to a "
               + "document needs focus in that document: open_file, view_designer or activate_document_tab give it, "
               + "and so does press_key on it. "
               + (found.Item is null
                   ? "The menu wasn't read, so whether the item is disabled is unknown. Opening the menu (or "
                     + "dump_visual_tree on it) would tell."
                   : "An item that is disabled in the menu is refused the same way.");
    }
}
