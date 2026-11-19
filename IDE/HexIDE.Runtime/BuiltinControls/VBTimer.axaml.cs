using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

public class VBTimer : TemplatedControl
{
    public static readonly StyledProperty<int> IntervalProperty =
        AvaloniaProperty.Register<VBLabel, int>(nameof(Interval));

    public int Interval
    {
        get => GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    private IDisposable? timer;

    /// <summary>
    /// A Timer has started counting towards its next event: enabled, with an Interval, on a form that is showing. Only the
    /// corpus smoke run listens, to know a startup Timer is armed even when Form_Load set it going (#4774).
    /// </summary>
    internal static event Action<VBTimer>? Armed;

    static VBTimer()
    {
        IntervalProperty.Changed.AddClassHandler<VBTimer>((timer, e) =>
        {
            timer.UpdateTimer();
        });
        IsEnabledProperty.Changed.AddClassHandler<VBTimer>((timer, e) =>
        {
            timer.UpdateTimer();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        timer?.Dispose();
        timer = null;
        if (IsEnabled && Interval > 0 && this.IsAttachedToVisualTree())
        {
            timer = DispatcherTimer.Run(() =>
            {
                this.ExecuteSub(TimerComponentClass.TimerEvent);
                return true;
            }, TimeSpan.FromMilliseconds(Interval));
            Armed?.Invoke(this);
        }
    }
}