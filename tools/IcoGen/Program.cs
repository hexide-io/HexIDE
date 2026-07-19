using SkiaSharp;
using Svg.Skia;

// Usage: IcoGen <input.svg> <output.ico> [sizes]
// Default sizes for app icon:   16,32,48,256
// Default sizes for favicon:    16,32,48,64,128,256

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: IcoGen <input.svg> <output.ico> [size1,size2,...]");
    return 1;
}

var svgPath  = args[0];
var icoPath  = args[1];
var sizes    = args.Length > 2
    ? args[2].Split(',').Select(int.Parse).ToArray()
    : [16, 32, 48, 256];

if (!File.Exists(svgPath)) { Console.Error.WriteLine($"SVG not found: {svgPath}"); return 1; }

// Rasterize each size
var pngBlobs = new List<(int size, byte[] png)>();
foreach (var sz in sizes)
{
    using var svg = new SKSvg();
    svg.Load(svgPath);

    var picture = svg.Picture;
    if (picture is null) { Console.Error.WriteLine("Failed to parse SVG"); return 1; }

    float scale = sz / Math.Max(picture.CullRect.Width, picture.CullRect.Height);

    var info = new SKImageInfo(sz, sz, SKColorType.Bgra8888, SKAlphaType.Premul);
    using var surface = SKSurface.Create(info);
    var canvas = surface.Canvas;
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(scale);
    canvas.DrawPicture(picture);
    canvas.Flush();

    using var image = surface.Snapshot();
    using var data  = image.Encode(SKEncodedImageFormat.Png, 100);
    pngBlobs.Add((sz, data.ToArray()));
    Console.WriteLine($"  Rendered {sz}x{sz}");
}

// Write ICO
// ICO format: ICONDIR + ICONDIRENTRY[] + image data
// For PNG-in-ICO (Vista+): width/height = 0 for 256+, else actual size
using var ico = new BinaryWriter(File.Create(icoPath));

// ICONDIR
ico.Write((ushort)0);               // reserved
ico.Write((ushort)1);               // type = ICO
ico.Write((ushort)pngBlobs.Count);  // image count

int dataOffset = 6 + pngBlobs.Count * 16;

// ICONDIRENTRY[]
foreach (var (sz, png) in pngBlobs)
{
    byte w = sz >= 256 ? (byte)0 : (byte)sz;
    byte h = sz >= 256 ? (byte)0 : (byte)sz;
    ico.Write(w);           // width  (0 = 256)
    ico.Write(h);           // height (0 = 256)
    ico.Write((byte)0);     // color count (0 = true color)
    ico.Write((byte)0);     // reserved
    ico.Write((ushort)1);   // planes
    ico.Write((ushort)32);  // bit count
    ico.Write((uint)png.Length);
    ico.Write((uint)dataOffset);
    dataOffset += png.Length;
}

// Image data
foreach (var (_, png) in pngBlobs)
    ico.Write(png);

ico.Flush();
Console.WriteLine($"Written: {icoPath}");
return 0;
