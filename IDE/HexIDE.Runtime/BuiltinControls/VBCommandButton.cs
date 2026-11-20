using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Classic.Avalonia.Theme;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBCommandButton : Button
{
    protected override Type StyleKeyOverride => typeof(VBCommandButton);

    private ContentPresenter? captionPresenter;
    private ClassicBorderDecorator? buttonEdge;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (buttonEdge != null)
            buttonEdge.PropertyChanged -= OnEdgePropertyChanged;
        base.OnApplyTemplate(e);
        captionPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        buttonEdge = e.NameScope.Find<ClassicBorderDecorator>("ButtonEdge");
        if (buttonEdge != null)
            buttonEdge.PropertyChanged += OnEdgePropertyChanged;
        UpdateCaptionEffect();
    }

    // The edge's highlight follows the theme, so the copy follows the edge.
    private void OnEdgePropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == ClassicBorderDecorator.BorderLightLightBrushProperty)
            UpdateCaptionEffect();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsEffectivelyEnabledProperty)
            UpdateCaptionEffect();
    }

    private void UpdateCaptionEffect()
    {
        if (captionPresenter == null)
            return;
        // VB6 SP6 draws a disabled caption in the edge's shadow colour over a copy in its highlight colour, one pixel
        // down and right (#4551 / #4586, oracle PR #5031). A drop shadow draws the copy and leaves one caption for
        // automation and access keys. It's set here because an effect in a style setter has no resource host.
        captionPresenter.Effect = !IsEffectivelyEnabled && buttonEdge?.BorderLightLightBrush is ISolidColorBrush highlight
            ? new DropShadowEffect { OffsetX = 1, OffsetY = 1, BlurRadius = 0, Opacity = 1, Color = highlight.Color }
            : null;
    }

    internal void SetDisplayAsDefault(bool value) => PseudoClasses.Set(":default", value);

    private bool defaultButton;
    private bool cancelButton;

    internal bool Default
    {
        get => defaultButton;
        set => SetRole(value, false);
    }

    internal bool Cancel
    {
        get => cancelButton;
        set => SetRole(value, true);
    }

    // #4371, oracle PR #4376: each role has one holder; disabling or hiding it keeps the role.
    private void SetRole(bool value, bool cancel)
    {
        if (cancel)
            cancelButton = value;
        else
            defaultButton = value;
        if (value && FormControls.TryOf(this, out var form))
            foreach (var control in form.All)
                if (control is VBCommandButton other && other != this)
                {
                    if (cancel)
                        other.cancelButton = false;
                    else
                        other.defaultButton = false;
                }
        if (!cancel)
            UserControlKeys.UpdateDefault(this);
    }

    static VBCommandButton()
    {
        ControlKeyEvents.Attach<VBCommandButton>();
        AttachedEvents.AttachFocusEvents<VBCommandButton>();
        // An arrow moves the focus to the previous or next control in its container (measured, #1625).
        ArrowNavigation.Attach<VBCommandButton>();
        AttachedEvents.AttachClick<VBCommandButton>();
        // MouseUp follows Click, which the Button raised in its own release handler (measured, #974).
        AttachedEvents.AttachMouse<VBCommandButton>();
        ContentTemplateProperty.OverrideDefaultValue<VBCommandButton>(AccessTextDataTemplate.Access);
        // Named as the caption reads, not with the _ the drawn text marks its access key with (#954).
        VBCaption.NameByCaption<VBCommandButton>(ContentProperty);
    }
}