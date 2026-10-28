using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Controls;

public class MonthViewDrawingTests
{
    private static byte[] Paint(VBMonthView calendar)
    {
        calendar.Measure(new Size(180, 158));
        calendar.Arrange(new Rect(0, 0, 180, 158));
        using var bitmap = new RenderTargetBitmap(new PixelSize(180, 158));
        bitmap.Render(calendar);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    [AvaloniaFact]
    public void Navigation_arrows_paint_at_both_title_edges()
    {
        var calendar = new VBMonthView();
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new DateTime(2001, 6, 15))));
        calendar.Measure(new Size(180, 158));
        calendar.Arrange(new Rect(0, 0, 180, 158));
        using var bitmap = new RenderTargetBitmap(new PixelSize(180, 158));
        bitmap.Render(calendar);
        var pixels = new byte[180 * 158 * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(0, 0, 180, 158), handle.AddrOfPinnedObject(), pixels.Length, 180 * 4); }
        finally { handle.Free(); }
        // Use the proportional title layout, not a VB6 pixel metric.
        var button = (int)(calendar.Bounds.Height / 8);
        foreach (var x in new[] { 0, 180 - button })
            Enumerable.Range(0, button * button)
                .Select(i => BitConverter.ToInt32(pixels, ((i / button) * 180 + x + i % button) * 4))
                .Distinct().Count().Should().BeGreaterThan(1);
    }

    [AvaloniaTheory]
    [InlineData("Value")]
    [InlineData("StartOfWeek")]
    [InlineData("ShowWeekNumbers")]
    [InlineData("SelEnd")]
    public void Calendar_paints_date_week_start_week_numbers_and_selection_changes(string property)
    {
        var calendar = new VBMonthView();
        calendar.TrySetProperty("Value", new Vb6Value(VbDate.FromDateTime(new System.DateTime(2001, 6, 15))));
        calendar.TrySetProperty("MultiSelect", new Vb6Value(true));
        var before = Paint(calendar);
        var value = property switch
        {
            "Value" => new Vb6Value(VbDate.FromDateTime(new System.DateTime(2001, 7, 15))),
            "SelEnd" => new Vb6Value(VbDate.FromDateTime(new System.DateTime(2001, 6, 18))),
            "StartOfWeek" => new Vb6Value(2),
            _ => new Vb6Value(true),
        };
        calendar.TrySetProperty(property, value).Should().BeTrue();
        Paint(calendar).Should().NotEqual(before);
    }
}
