using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Integration.Tests.Views;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Integration.Tests.Controls;

public class TabStripDrawingTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Selection_changes_the_drawn_relief_even_when_disabled(int style)
    {
        var strip = Strip(style);
        var first = Draw(strip);
        first.Should().Contain(b => b != 0, "the strip must paint a visible row");
        strip.Model.Select(strip.Model.Tabs[1]);
        var second = Draw(strip);
        second.Should().NotEqual(first);
        strip.IsEnabled = false;
        Draw(strip).Should().Equal(second, "VB6 draws disabled tabs exactly like enabled tabs");
    }

    [AvaloniaFact]
    public void Tabs_buttons_and_flat_buttons_have_distinct_appearances()
    {
        var strip = Strip(0);
        var tabs = Draw(strip);
        strip.Values["Style"] = new Vb6Value(1L);
        var buttons = Draw(strip);
        strip.Values["Style"] = new Vb6Value(2L);
        var flat = Draw(strip);
        tabs.Should().NotEqual(buttons);
        flat.Should().NotEqual(buttons);
        flat.Should().NotEqual(tabs);
    }

    [AvaloniaFact]
    public void Mnemonic_is_underlined_and_not_drawn_as_an_ampersand()
    {
        var strip = Strip(0);
        strip.Model.Tabs[0].Caption = "Alpha";
        var plain = Draw(strip);
        strip.Model.Tabs[0].Caption = "&Alpha";
        var mnemonic = Draw(strip);
        mnemonic.Should().NotEqual(plain);
        // An underline changes only a thin horizontal band, not the whole caption.
        var changedRows = Enumerable.Range(0, 80).Where(y =>
            !plain.Skip(y * 240 * 4).Take(240 * 4).SequenceEqual(mnemonic.Skip(y * 240 * 4).Take(240 * 4))).ToArray();
        changedRows.Length.Should().BeInRange(1, 2);
    }

    [AvaloniaFact]
    public void Separators_are_drawn_between_flat_buttons()
    {
        var strip = Strip(2);
        var plain = Draw(strip);
        strip.Values["Separators"] = new Vb6Value(true);
        Draw(strip).Should().NotEqual(plain);
    }

    [AvaloniaTheory]
    [InlineData("overflow")]
    [InlineData("TabStyle")]
    [InlineData("Placement")]
    [InlineData("MultiRow")]
    [InlineData("TabWidthStyle")]
    [InlineData("TabFixedWidth")]
    [InlineData("TabFixedHeight")]
    public void Unsupported_drawing_reports_not_implemented_on_the_form_queue(string property)
    {
        var window = TabStripKeyboardTests.Open();
        Exception? fault = null;
        void Faulted(VBWindowContext context, Exception error)
        {
            if (ReferenceEquals(context, ((IFormView)window).Context))
                fault = error;
        }
        VBWindowContext.Faulted += Faulted;
        try
        {
            var strip = window.GetVisualDescendants().OfType<VBTabStrip>().Single();
            if (property == "overflow")
                for (var i = 0; i < 12; i++)
                    strip.Model.Add(caption: "Another tab");
            else
                strip.Values[property] = property == "MultiRow" ? new Vb6Value(true)
                    : property.StartsWith("TabFixed", StringComparison.Ordinal) ? new Vb6Value(100) : new Vb6Value(1L);
            Draw(strip);
            Dispatcher.UIThread.RunJobs();
            fault.Should().BeOfType<NotImplementedException>().Which.Message.Should().Contain("drawing");
        }
        finally
        {
            VBWindowContext.Faulted -= Faulted;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Model_mutations_repaint_a_shown_strip()
    {
        var strip = Strip(0);
        var window = new Window { Width = 240, Height = 80, Content = strip };
        try
        {
            window.Show();
            byte[] Capture()
            {
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame.Should().NotBeNull();
                return Pixels(frame!);
            }
            var previous = Capture();
            void Changed(Action mutate)
            {
                mutate();
                var next = Capture();
                next.Should().NotEqual(previous);
                previous = next;
            }
            Changed(() => strip.Model.Select(strip.Model.Tabs[1]));
            Changed(() => strip.Model.Tabs[1].Caption = "Changed");
            Changed(() => strip.Model.Add(caption: "Gamma"));
            Changed(() => strip.Model.Remove(new Vb6Value(3)));
            Changed(() => strip.Model.Clear());
        }
        finally { window.Close(); }
    }

    private static VBTabStrip Strip(int style)
    {
        var strip = new VBTabStrip { Width = 240, Height = 80 };
        strip.Model.Tabs[0].Caption = "&Alpha";
        strip.Model.Add(caption: "&Beta");
        strip.Values["Style"] = new Vb6Value((long)style);
        strip.Measure(new Size(240, 80));
        strip.Arrange(new Rect(0, 0, 240, 80));
        return strip;
    }

    private static byte[] Draw(VBTabStrip strip)
    {
        var group = new DrawingGroup();
        using (var context = group.Open())
            strip.Render(context);
        using var bitmap = new RenderTargetBitmap(new PixelSize(240, 80));
        using (var context = bitmap.CreateDrawingContext())
            group.Draw(context);
        return Pixels(bitmap);
    }

    private static byte[] Pixels(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        var pixels = new byte[size.Width * size.Height * 4];
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(default, size), pin.AddrOfPinnedObject(), pixels.Length, size.Width * 4); }
        finally { pin.Free(); }
        return pixels;
    }
}
