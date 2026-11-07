using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Avalonia;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace HexIDE.Tools.LanguageServers;

public partial class LanguageServersToolView : UserControl
{
    private readonly DispatcherTimer _ageTimer;
    private LanguageServersToolViewModel? _languageSource;
    private readonly List<Visual> _visibilitySources = [];

    public LanguageServersToolView()
    {
        InitializeComponent();
        _ageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ageTimer.Tick += (_, _) => RefreshAges();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var visual in this.GetVisualAncestors())
        {
            _visibilitySources.Add(visual);
            visual.PropertyChanged += OnVisibilityChanged;
        }
        PropertyChanged += OnVisibilityChanged;
        DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(this, EventArgs.Empty);
        UpdateAgeTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _ageTimer.Stop();
        DataContextChanged -= OnDataContextChanged;
        if (_languageSource is not null) _languageSource.LanguageChanged -= ResetLabelScopes;
        _languageSource = null;
        PropertyChanged -= OnVisibilityChanged;
        foreach (var visual in _visibilitySources)
            visual.PropertyChanged -= OnVisibilityChanged;
        _visibilitySources.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_languageSource is not null) _languageSource.LanguageChanged -= ResetLabelScopes;
        _languageSource = DataContext as LanguageServersToolViewModel;
        if (_languageSource is not null) _languageSource.LanguageChanged += ResetLabelScopes;
        ResetLabelScopes();
    }

    private void ResetLabelScopes()
    {
        // A shared size scope retains its widest measurement, even after a label gets shorter.
        foreach (var visual in this.GetVisualDescendants())
        {
            if (visual is not Control control || !Grid.GetIsSharedSizeScope(control)) continue;
            Grid.SetIsSharedSizeScope(control, false);
            Grid.SetIsSharedSizeScope(control, true);
        }
    }

    private void OnVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) UpdateAgeTimer();
    }

    private void UpdateAgeTimer()
    {
        if (_ageTimer is null) return;
        if (TopLevel.GetTopLevel(this) is not null && IsEffectivelyVisible)
        {
            RefreshAges();
            _ageTimer.Start();
        }
        else _ageTimer.Stop();
    }

    private void RefreshAges()
    {
        if (DataContext is not LanguageServersToolViewModel vm) return;
        foreach (var group in vm.Groups)
            foreach (var row in group.Rows)
                row.AgeChanged();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Puts the whole window on the clipboard as text, pseudonymised.
    /// </summary>
    /// <remarks>
    /// Code-behind because the clipboard hangs off the <see cref="TopLevel"/>, which a view model has no
    /// business reaching for. The text itself is composed in the view model, so what gets copied is
    /// asserted on by a test and readable by an automation client, neither of which can read a clipboard.
    /// </remarks>
    private async void CopyReport_OnClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LanguageServersToolViewModel vm) return;
        if (vm.ReportText is not { Length: > 0 } report) return;

        await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(report) ?? Task.CompletedTask);
    }
}
