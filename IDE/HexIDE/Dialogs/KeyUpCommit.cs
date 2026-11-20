using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace HexIDE.Dialogs;

// A disabled Wayland owner can repeat a key into the box it raised, with no matching release (#5021).
internal sealed class KeyUpCommit
{
    private readonly Dictionary<(Key, PhysicalKey), KeyEventArgs> _pressed = new();
    private TopLevel? _topLevel;
    private KeyEventArgs? _replayedKeyDown;

    internal KeyUpCommit(Control box)
    {
        box.Loaded += OnLoaded;
        box.Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel((Control)sender!) is not { } topLevel)
            return;

        _topLevel = topLevel;
        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _topLevel?.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
        _topLevel?.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
        _topLevel = null;
        _pressed.Clear();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Escape) || e == _replayedKeyDown)
            return;

        _pressed.TryAdd((e.Key, e.PhysicalKey), e);
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (!_pressed.Remove((e.Key, e.PhysicalKey), out var down) || _topLevel is not { } topLevel)
            return;

        // Route the press as Avalonia normally would, preserving focused, default and cancel button behavior.
        var target = topLevel.FocusManager?.GetFocusedElement() is InputElement focused
            && TopLevel.GetTopLevel(focused) == topLevel ? focused : topLevel;
        var replay = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = down.Key,
            PhysicalKey = down.PhysicalKey,
            KeyModifiers = down.KeyModifiers,
            KeySymbol = down.KeySymbol,
            KeyDeviceType = down.KeyDeviceType,
            Source = target
        };

        e.Handled = true;
        var previousReplay = _replayedKeyDown;
        _replayedKeyDown = replay;
        try
        {
            target.RaiseEvent(replay);
        }
        finally
        {
            _replayedKeyDown = previousReplay;
        }
    }
}
