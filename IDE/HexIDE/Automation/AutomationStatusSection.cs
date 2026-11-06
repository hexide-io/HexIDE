#if DEBUG
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Classic.Avalonia.Theme;
using Classic.CommonControls;

namespace HexIDE.Automation;

/// <summary>
/// The status bar section that says the automation server is listening (#2451): <c>MCP :5140</c>, with the session
/// after it when the IDE was started for one, the rest on hover, and a dot before it that lights while a call runs.
/// </summary>
/// <remarks>
/// <para>
/// Built in code, not markup, so that it compiles out with the server: AXAML has no <c>#if</c>, and a section left in
/// the markup would sit hidden in every distributed build.
/// </para>
/// <para>
/// Why it is there at all: several agent sessions run their own builds on one machine, whose windows look alike; the
/// server has no authentication (#352), so an IDE that can be driven should say so where its user is looking; and an
/// agent driving the IDE shows nothing on screen while a call runs.
/// </para>
/// <para>
/// <b>The cue is a dot, not a highlighted section.</b> A section filled with the highlight colour reads as selected,
/// not busy, and <c>take_snapshot</c> is itself a call, so every snapshot of the IDE would show the status bar lit up.
/// The dot says the same quietly, and a snapshot showing it lit is telling the truth.
/// </para>
/// </remarks>
public sealed class AutomationStatusSection : Decorator
{
    private readonly AutomationServerStatus _status;
    private readonly Ellipse _dot;
    private readonly TextBlock _text;
    private IDisposable? _fill;
    private bool? _painted;

    public AutomationStatusSection(AutomationServerStatus status)
    {
        _status = status;
        Margin = new Thickness(1, 0, 0, 0);
        IsVisible = false;
        AutomationProperties.SetAutomationId(this, "StatusBar.Automation");
        _dot = new Ellipse
        {
            Width = 6,
            Height = 6,
            StrokeThickness = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        };
        _dot.Bind(Shape.StrokeProperty, _dot.GetResourceObservable(SystemColors.GrayTextBrushKey));
        _text = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0),
            FontSize = 11,
        };
        _text.Bind(TextBlock.ForegroundProperty, _text.GetResourceObservable(SystemColors.WindowTextBrushKey));
        // Sealed, so worn rather than derived from: the status bar's other sections are one of these each.
        Child = new ClassicBorderDecorator
        {
            BorderStyle = ClassicBorderStyle.Sunken,
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _dot, _text } },
        };
    }

    /// <summary>The dot that lights while a call runs.</summary>
    internal Ellipse Dot => _dot;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _status.Changed += Refresh;
        // The strings are resources, so a language switch arrives as a resource change.
        ResourcesChanged += OnResourcesChanged;
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _status.Changed -= Refresh;
        ResourcesChanged -= OnResourcesChanged;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnResourcesChanged(object? sender, ResourcesChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_status.Port is not { } port)
        {
            IsVisible = false;
            return;
        }
        IsVisible = true;
        var shown = _status.Session is { } session
            ? Format("Str.StatusBar.Automation.TextSession", port, session)
            : Format("Str.StatusBar.Automation.Text", port);
        _text.Text = shown;
        AutomationProperties.SetName(this, shown);
        var tip = Tip();
        ToolTip.SetTip(this, tip);
        AutomationProperties.SetHelpText(this, tip);
        Paint(_status.IsBusy);
    }

    /// <summary>What hovering over the section says: everything a person would otherwise read off the command line.</summary>
    internal string Tip()
    {
        var lines = new List<string>
        {
            Format("Str.StatusBar.Automation.Tip.Listening", _status.Endpoint!),
            _status.Session is { } session
                ? Format("Str.StatusBar.Automation.Tip.Session", session)
                : Text("Str.StatusBar.Automation.Tip.AnyCaller"),
            Format("Str.StatusBar.Automation.Tip.Process", Environment.ProcessId, _status.BuildDirectory ?? ""),
            Text("Str.StatusBar.Automation.Tip.Loopback"),
        };
        if (_status.IsBusy && _status.RunningTool is { } running)
            lines.Add(Format("Str.StatusBar.Automation.Tip.Running", running));
        lines.Add(_status.LastCallAt is { } at && _status.LastTool is { } last
            ? Format("Str.StatusBar.Automation.Tip.LastCall", last, at.ToLocalTime().ToString("T", CultureInfo.CurrentCulture))
            : Text("Str.StatusBar.Automation.Tip.NoCall"));
        if (_status.RefusedCount > 0)
            lines.Add(_status.LastRefusedFrom is { } from
                ? Format("Str.StatusBar.Automation.Tip.RefusedFrom", _status.RefusedCount, from)
                : Format("Str.StatusBar.Automation.Tip.RefusedUnnamed", _status.RefusedCount));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>The theme's highlight fills the dot while a call runs, and nothing fills it otherwise.</summary>
    private void Paint(bool busy)
    {
        if (_painted == busy)
            return;
        _painted = busy;
        _fill?.Dispose();
        _fill = null;
        if (busy)
            _fill = _dot.Bind(Shape.FillProperty, _dot.GetResourceObservable(SystemColors.HighlightBrushKey));
        else
            _dot.ClearValue(Shape.FillProperty);
    }

    private string Text(string key) => this.TryFindResource(key, ActualThemeVariant, out var value) && value is string text
        ? text : key;

    private string Format(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, Text(key), args);
}
#endif
