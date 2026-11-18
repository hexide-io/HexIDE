using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// #1631 part B, issuecomment-6425849231: form-wide tab order and forwarded access keys.
internal static class UserControlKeys
{
    internal static void Attach(Control root, FormControls form)
    {
        var hasUserControls = form.All.Any(UserControlMembers.IsUserControl);
        // Keep valid file indices, including holes left by controls with no TabIndex. Out-of-range indices take
        // the free places in form order, as cmdOut (17 -> 3) and Command1 (16 -> 8) did in the measured probe.
        var indexed = form.All.Where(TabOrder.HasTabIndex).ToList();
        var used = indexed.Where(c => TabOrder.Get(c) < indexed.Count).Select(TabOrder.Get).ToHashSet();
        foreach (var control in indexed.Where(c => hasUserControls && TabOrder.Get(c) >= indexed.Count))
        {
            var free = Enumerable.Range(0, indexed.Count).First(i => !used.Contains(i));
            KeyboardNavigation.SetTabIndex(control, free);
            used.Add(free);
        }
        root.AddHandler(InputElement.GotFocusEvent, (_, _) => Update(form), handledEventsToo: true);
        foreach (var control in form.All.Prepend(root))
            control.PropertyChanged += (_, e) =>
            {
                if (e.Property == InputElement.IsEffectivelyEnabledProperty || e.Property == Visual.IsVisibleProperty)
                    Update(form);
            };
        Update(form);
        Key? taken = null;
        root.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (e.Key == taken)
            {
                e.Handled = true;
                taken = null;
            }
        }, RoutingStrategies.Tunnel);
        root.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled)
                return;
            var order = form.All.Where(TabOrder.HasTabIndex).OrderBy(TabOrder.Get).ToList();
            var focused = form.All.LastOrDefault(c => c.IsKeyboardFocusWithin);
            bool Eligible(Control c) => c.Focusable && c.IsEffectivelyEnabled && c.IsEffectivelyVisible;
            Control? After(Control from, bool tab = false) => order.Skip(order.IndexOf(from) + 1).Concat(order.Take(order.IndexOf(from) + 1))
                .FirstOrDefault(c => Eligible(c) && (!tab || TabOrder.GetTabStop(c)));
            if (hasUserControls && e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None && focused is not null && After(focused, tab: true) is { } next)
            {
                e.Handled = true;
                next.Focus(NavigationMethod.Tab);
            }
            else if (hasUserControls && e.KeyModifiers == KeyModifiers.Alt && e.Key >= Key.A && e.Key <= Key.Z)
            {
                var letter = (char)('a' + e.Key - Key.A);
                var sources = form.All.OfType<VBUserControl>()
                    .Where(c => UserControlMembers.HasAccessKeys(c) && c.IsEffectivelyEnabled && c.IsEffectivelyVisible
                        && c.AccessKeys.Contains(letter, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
                // Which duplicate key wins was not measured. Leave it to the existing host handling.
                if (sources.Length > 1)
                    return;
                var source = sources.FirstOrDefault();
                Control? target = null;
                if (source is not null)
                    target = UserControlMembers.ForwardFocus(source) ? After(source)
                        : !UserControlMembers.CanGetFocus(source) ? order.FirstOrDefault(Eligible) : Eligible(source) ? source : null;
                else
                {
                    var label = order.OfType<VBLabel>().FirstOrDefault(c => c.IsEffectivelyEnabled && c.IsEffectivelyVisible && c.RecognizesAccessKey && c.Text is { } text
                        && VBCaption.Parse(text) is { AccessKey: { } key } parsed && char.ToLowerInvariant(parsed.Shown[key]) == letter);
                    if (label is not null && After(label) is VBUserControl receiver)
                        target = receiver;
                }
                if (target is not null)
                {
                    e.Handled = true;
                    if (target is VBUserControl receiver)
                        receiver.AccessKey(letter);
                    target.Focus(NavigationMethod.Tab);
                }
            }
            else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Enter or Key.Escape)
            {
                // A focused button owns Enter; an open combo owns both keys (#2325, #4371).
                if (focused is ComboBox { IsDropDownOpen: true })
                {
                    if (Receivers(form, e.Key).Length == 1)
                        taken = e.Key;
                    return;
                }
                if (e.Key == Key.Enter && focused is VBCommandButton button)
                {
                    e.Handled = true;
                    taken = e.Key;
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
                var receivers = Receivers(form, e.Key);
                if (receivers.Length == 1)
                {
                    e.Handled = true;
                    taken = e.Key;
                    if (receivers[0] is VBCommandButton command)
                        command.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else if (receivers[0] is VBUserControl receiver)
                        receiver.AccessKey(e.Key == Key.Enter ? 13 : 27);
                }
            }
        }, RoutingStrategies.Tunnel);
    }

    private static Control[] Receivers(FormControls form, Key key) => form.All.Where(c =>
        (c is VBCommandButton command ? key == Key.Enter ? command.Default : command.Cancel
            : key == Key.Enter ? UserControlMembers.IsDefault(c) : UserControlMembers.IsCancel(c))
        && c.IsEffectivelyEnabled && c.IsEffectivelyVisible).Take(2).ToArray();

    internal static void UpdateDefault(Control control)
    {
        if (FormControls.TryOf(control, out var form))
            Update(form);
    }

    private static void Update(FormControls form)
    {
        var focused = form.All.OfType<VBCommandButton>().FirstOrDefault(c => c.IsKeyboardFocusWithin);
        var receivers = Receivers(form, Key.Enter);
        var displayed = focused ?? (receivers.Length == 1 ? receivers[0] as VBCommandButton : null);
        foreach (var button in form.All.OfType<VBCommandButton>())
            button.SetDisplayAsDefault(button == displayed);
        var buttonFocused = focused is not null;
        foreach (var control in form.All.OfType<VBUserControl>())
            control.SetDisplayAsDefault(UserControlMembers.IsDefault(control) && !buttonFocused);
    }
}
