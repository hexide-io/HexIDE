using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.AvaloniaInterop;

/// <summary>
/// The size a font is given when a program asks for one, as vb6.exe reads it back (#1343). Measured on vb6.exe, native and
/// p-code agreeing, at every pixel height from 1 to 400 (the oracle's "A font's FontSize, FontBold and the rest"):
/// <list type="bullet">
/// <item>A size is a whole number of pixels at 96 dpi: <c>size * 4 / 3</c> rounded, halves up, and read back times 3/4.
/// So 10 reads 9.75 and 13 reads 12.75. The smallest is 2 pixels, 1 for the vector fonts Modern, Roman and Script.</item>
/// <item>GDI's bitmap fonts have the heights they have. MS Sans Serif at 9 points reads 8.25, and System is 13 pixels at
/// anything up to 22. Past a height of its own, each but System becomes Arial, and its FontName then reads so.</item>
/// <item>MS Shell Dlg snaps heights from 9 to 15 pixels to 11 or 13. Every other height, and every height of every TrueType
/// family probed, is exact.</item>
/// </list>
/// </summary>
/// <remarks>
/// Each table is runs: from that height on, the height it reads back as, 0 for the height asked, and -1 for the height
/// asked in Arial. The tables were measured to 400 pixels (300 points). Above that the last run is taken to go on: System
/// stays at 104, MS Shell Dlg is the height asked, and the rest are the height asked in Arial. That is assumed, not
/// measured.
/// </remarks>
internal static class VbFontSizes
{
    /// <summary>The largest size vb6.exe takes: 2160 points, 2880 pixels. Anything above is error 6.</summary>
    public const double Largest = 2160;

    private static readonly Dictionary<string, (int From, int Pixels)[]> Bitmap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MS Sans Serif"] = [(1, 11), (11, 0), (12, 11), (13, 0), (14, 13), (16, 0), (17, 16), (18, 0), (19, 18), (20, 0), (21, 20), (22, 0), (25, 24), (26, 0), (27, 26), (30, 0), (31, 30), (32, 0), (33, 32), (36, 0), (37, 36), (39, 0), (41, 40), (44, 0), (45, 44), (46, 0), (47, 46), (48, 0), (49, 48), (52, 0), (53, 52), (54, 0), (56, 55), (60, 0), (61, 60), (64, 0), (66, 65), (69, 0), (70, 69), (72, 0), (73, 72), (78, 80), (80, 0), (81, 80), (88, 90), (90, 0), (91, 90), (92, 0), (93, 92), (96, 0), (97, 96), (100, 0), (101, 100), (108, 0), (109, 108), (111, 115), (115, 0), (116, 115), (120, 0), (121, 120), (127, 128), (128, 0), (129, 128), (138, 0), (139, 138), (143, 150), (144, 0), (145, 144), (146, 150), (150, 0), (151, 150), (157, 160), (160, 0), (161, 160), (176, 180), (180, 0), (181, 180), (188, 192), (192, 0), (193, 192), (195, 200), (200, 0), (201, 200), (210, 0), (211, 210), (219, 224), (224, 0), (225, 224), (233, 240), (240, 0), (241, 240), (253, 256), (256, 0), (257, 256), (270, 280), (280, 0), (281, 280), (310, 320), (320, 0), (321, 320), (386, -1)],
        ["MS Serif"] = [(1, 8), (8, 0), (10, 9), (11, 0), (12, 11), (13, 0), (14, 13), (16, 0), (17, 16), (18, 0), (19, 18), (20, 0), (21, 20), (22, 0), (25, 24), (26, 0), (28, 27), (30, 0), (31, 30), (32, 0), (33, 32), (36, 0), (37, 36), (39, 0), (41, 40), (44, 0), (47, 46), (48, 0), (49, 48), (52, 0), (53, 52), (54, 0), (56, 55), (60, 0), (61, 60), (64, 0), (66, 65), (69, 0), (70, 69), (72, 0), (73, 72), (78, 80), (80, 0), (81, 80), (88, 90), (90, 0), (91, 90), (92, 0), (93, 92), (96, 0), (97, 96), (100, 0), (101, 100), (108, 0), (109, 108), (111, 115), (115, 0), (116, 115), (120, 0), (121, 120), (127, 128), (128, 0), (129, 128), (138, 0), (139, 138), (143, 150), (144, 0), (145, 144), (146, 150), (150, 0), (151, 150), (157, 160), (160, 0), (161, 160), (176, 180), (180, 0), (181, 180), (188, 192), (192, 0), (193, 192), (195, 200), (200, 0), (201, 200), (210, 0), (211, 210), (219, 224), (224, 0), (225, 224), (233, 240), (240, 0), (241, 240), (253, 256), (256, 0), (257, 256), (270, 280), (280, 0), (281, 280), (310, 320), (320, 0), (321, 320), (386, -1)],
        ["System"] = [(1, 13), (13, 0), (14, 13), (23, 26), (26, 0), (27, 26), (36, 39), (39, 0), (40, 39), (49, 52), (52, 0), (53, 52), (62, 65), (65, 0), (66, 65), (75, 78), (78, 0), (79, 78), (88, 91), (91, 0), (92, 91), (101, 104), (104, 0), (105, 104)],
        ["Terminal"] = [(1, 6), (6, 0), (7, 6), (8, 0), (9, 8), (12, 0), (13, 12), (16, 0), (17, 16), (18, 0), (19, 18), (24, 0), (25, 24), (31, 32), (32, 0), (33, 32), (36, 0), (37, 36), (40, 0), (41, 40), (46, 48), (48, 0), (49, 48), (53, 54), (54, 0), (55, 54), (60, 0), (61, 60), (64, 0), (65, 64), (70, 72), (72, 0), (73, 72), (79, 80), (80, 0), (81, 80), (87, 90), (90, 0), (91, 90), (104, 108), (108, 0), (109, 108), (112, 0), (113, 112), (122, 126), (126, 0), (127, 126), (140, 144), (144, 0), (145, 144), (210, -1)],
        ["Fixedsys"] = [(1, 12), (12, 0), (13, 12), (22, 24), (24, 0), (25, 24), (33, 36), (36, 0), (37, 36), (45, 48), (48, 0), (49, 48), (57, 60), (60, 0), (61, 60), (69, 72), (72, 0), (73, 72), (81, 84), (84, 0), (85, 84), (93, 96), (96, 0), (97, 96), (162, -1)],
        ["Courier"] = [(1, 13), (13, 0), (14, 13), (16, 0), (17, 16), (20, 0), (21, 20), (25, 0), (27, 26), (32, 0), (33, 32), (38, 39), (39, 0), (41, 40), (47, 48), (48, 0), (49, 48), (50, 0), (51, 50), (52, 0), (53, 52), (58, 60), (60, 0), (61, 60), (64, 0), (66, 65), (72, 75), (75, 0), (76, 75), (80, 0), (81, 80), (91, 0), (92, 91), (94, 100), (96, 0), (97, 100), (100, 0), (101, 100), (112, 0), (113, 112), (117, 120), (119, 125), (120, 0), (121, 120), (122, 125), (125, 0), (126, 125), (140, 0), (141, 140), (146, 150), (150, 0), (151, 150), (160, 0), (161, 160), (169, 175), (175, 0), (176, 175), (194, 200), (200, 0), (201, 200), (266, -1)],
        ["Small Fonts"] = [(1, 3), (3, 0), (6, 5), (7, 0), (12, 11), (14, 0), (15, 14), (16, 0), (17, 16), (18, 0), (19, 18), (20, 0), (23, 22), (24, 0), (25, 24), (27, 0), (29, 28), (30, 0), (31, 30), (32, 0), (34, 33), (36, 0), (37, 36), (40, 0), (41, 40), (44, 0), (46, 45), (50, 0), (51, 50), (55, 0), (56, 55), (64, 60), (66, 0), (67, 66), (70, 0), (71, 70), (76, 77), (77, 0), (78, 77), (86, 88), (88, 0), (89, 88), (154, -1)],
        ["MS Shell Dlg"] = [(1, 2), (2, 0), (9, 11), (11, 0), (12, 11), (13, 0), (14, 13), (16, 0)],
    };

    // GDI's vector fonts, which go down to one pixel where a TrueType font stops at two.
    private static readonly HashSet<string> Vector = new(StringComparer.OrdinalIgnoreCase) { "Modern", "Roman", "Script" };

    /// <summary>
    /// The size, in points, and the family a font of <paramref name="family"/> asked to be <paramref name="size"/> points
    /// is given. The family is <paramref name="family"/> unless a bitmap font gave way to Arial.
    /// </summary>
    public static (double Size, string Family) Snap(string family, double size)
    {
        var asked = (int)Math.Round((decimal)size * 4m / 3m, MidpointRounding.AwayFromZero);
        var pixels = Math.Max(asked, Vector.Contains(family) ? 1 : 2);
        if (Bitmap.TryGetValue(family, out var runs))
        {
            var code = 0;
            foreach (var (from, value) in runs)
                if (from <= Math.Max(asked, 1))
                    code = value;
            if (code > 0)
                pixels = code;
            else if (code < 0)
                family = "Arial";
        }
        return (pixels * 0.75, family);
    }
}
