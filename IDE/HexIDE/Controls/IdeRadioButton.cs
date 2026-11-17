using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace HexIDE.Controls;

// IDE option groups have one Tab stop; running forms own their separate VB6 navigation.
public class IdeRadioButton : RadioButton
{
    protected override Type StyleKeyOverride => typeof(RadioButton);

    public IdeRadioButton()
    {
        SetCurrentValue(IsTabStopProperty, false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCheckedProperty)
            SetCurrentValue(IsTabStopProperty, IsChecked == true);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.KeyModifiers != KeyModifiers.None ||
            e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;

        var options = this.GetVisualParent()?.GetVisualChildren().OfType<IdeRadioButton>()
            .Where(option => option.GroupName == GroupName && option.IsEffectivelyEnabled && option.IsEffectivelyVisible)
            .ToArray() ?? [];
        var index = Array.IndexOf(options, this);
        if (index < 0)
            return;

        var step = e.Key is Key.Left or Key.Up ? -1 : 1;
        var next = options[(index + step + options.Length) % options.Length];
        next.SetCurrentValue(IsCheckedProperty, true);
        next.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }
}
