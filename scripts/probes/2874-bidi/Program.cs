using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;

AppBuilder.Configure<Application>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().SetupWithoutStarting();

var fingerprint = args.Length > 0 && args[0] == "--fingerprint";
if (fingerprint)
    FontManager.Current.AddFontCollection(new EmbeddedFontCollection(new Uri("fonts:probe"), new Uri(args[1])));

// One RTL TextBlock. No HexIDE views, styles, translations or bidi controls.
var text = new TextBlock
{
    Text = fingerprint
        ? "7929 689A 38A1 DF58 9869 3FD3 2B67 1249 909C 99D5 C7F6 6092 22D4 FB35 7312 B4C8"
        : args.Length == 0 ? "תודה HexIDE." : args[0],
    FlowDirection = fingerprint && args.Length > 2 ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
    FontFamily = fingerprint ? new FontFamily("fonts:probe#Consolas") : new FontFamily("DejaVu Sans"),
    FontSize = fingerprint ? 11 : 16,
    TextWrapping = fingerprint ? TextWrapping.Wrap : TextWrapping.NoWrap
};
text.Measure(new Size(300, 100));
text.Arrange(new Rect(0, 0, 300, 100));
Console.WriteLine($"Logical: {text.Text}");
if (!fingerprint) Console.WriteLine("Expected left-to-right visual order: .HexIDE הדות");
foreach (var run in text.TextLayout.TextLines[0].TextRuns)
    if (run is ShapedTextRun shaped)
        Console.WriteLine($"Visual run: [{shaped.Text}] level={shaped.ShapedBuffer.BidiLevel}");
    else
        Console.WriteLine($"Non-text run: {run.GetType().Name} length={run.Length}");
foreach (var index in new[] { 5, 10, 11 })
    Console.WriteLine($"index={index} x={text.TextLayout.HitTestTextPosition(index).X}");

Console.WriteLine($"Desired width={text.DesiredSize.Width} bounds={text.Bounds.Width}");
foreach (var line in text.TextLayout.TextLines)
    Console.WriteLine($"Line start={line.Start} width={line.Width} withWhitespace={line.WidthIncludingTrailingWhitespace}");
double visibleRight = 0;
for (var index = 0; index < text.Text!.Length; index++)
    if (!char.IsWhiteSpace(text.Text[index]))
        visibleRight = Math.Max(visibleRight, text.TextLayout.HitTestTextPosition(index).Right);
Console.WriteLine($"Rightmost visible character edge={visibleRight}");
