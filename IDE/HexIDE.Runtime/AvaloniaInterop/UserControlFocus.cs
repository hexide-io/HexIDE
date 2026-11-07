using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.AvaloniaInterop;

// #1631 part C, issuecomment-6425849231: entering the new UC precedes leaving the old one.
internal static class UserControlFocus
{
    internal static void Attach(Control root)
    {
        root.AddHandler(InputElement.LostFocusEvent, (_, e) =>
        {
            if (e is not FocusChangedEventArgs moved)
                return;
            if (moved.NewFocusedElement is VBUserControl next && !UserControlMembers.Windowless(next))
                next.ExecuteSub("UserControl_EnterFocus");
            if (moved.OldFocusedElement is VBUserControl old)
            {
                old.ExecuteSub("UserControl_LostFocus");
                old.ExecuteSub("UserControl_ExitFocus");
                ((Control)old).ExecuteSub(ComponentBaseClass.LostFocusEvent);
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        root.AddHandler(InputElement.GotFocusEvent, (_, e) =>
        {
            if (e is not FocusChangedEventArgs moved || moved.NewFocusedElement is not VBUserControl next)
                return;
            if (moved.OldFocusedElement is null && !UserControlMembers.Windowless(next))
                next.ExecuteSub("UserControl_EnterFocus");
            ((Control)next).ExecuteSub(ComponentBaseClass.GotFocusEvent);
            if (UserControlMembers.Windowless(next))
                next.ExecuteSub("UserControl_EnterFocus");
            next.ExecuteSub("UserControl_GotFocus");
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
