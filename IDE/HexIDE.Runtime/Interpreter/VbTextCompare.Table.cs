// Generated from vb6.exe's measured Text order: see "Option Compare Text" in docs/vb6-fidelity-oracle/option-compare-text-and-vb6s-text-compare.md (#1567). The
// rows come from a measurement, not from a rule, so a change belongs in a new measurement; TextCompareTests replays
// every measured pair against them.
namespace HexIDE.Runtime.Interpreter;

internal static partial class VbTextCompare
{
    // Every character Chr(0) to Chr(255) gives, in vb6.exe's Text order. A Weighted character's Weight is its primary
    // class and its Accent its place among the class's forms, which ignore case; a WordSort character's Weight is its
    // place among the word-sort characters. The seven characters that expand are in Expansions instead.
    // A property, not a field: the order of static initializers across a partial class's files is not defined.
    private static Row[] Rows =>
    [
        new('\u0000', Kind.Ignorable, 0, 0),        // control 0x00
        new('\u00AD', Kind.Ignorable, 0, 0),        // soft hyphen
        new('\u0001', Kind.WordSort, 0, 0),         // control 0x01
        new('\u0002', Kind.WordSort, 1, 0),         // control 0x02
        new('\u0003', Kind.WordSort, 2, 0),         // control 0x03
        new('\u0004', Kind.WordSort, 3, 0),         // control 0x04
        new('\u0005', Kind.WordSort, 4, 0),         // control 0x05
        new('\u0006', Kind.WordSort, 5, 0),         // control 0x06
        new('\u0007', Kind.WordSort, 6, 0),         // control 0x07
        new('\u0008', Kind.WordSort, 7, 0),         // control 0x08
        new('\u000E', Kind.WordSort, 8, 0),         // control 0x0e
        new('\u000F', Kind.WordSort, 9, 0),         // control 0x0f
        new('\u0010', Kind.WordSort, 10, 0),        // control 0x10
        new('\u0011', Kind.WordSort, 11, 0),        // control 0x11
        new('\u0012', Kind.WordSort, 12, 0),        // control 0x12
        new('\u0013', Kind.WordSort, 13, 0),        // control 0x13
        new('\u0014', Kind.WordSort, 14, 0),        // control 0x14
        new('\u0015', Kind.WordSort, 15, 0),        // control 0x15
        new('\u0016', Kind.WordSort, 16, 0),        // control 0x16
        new('\u0017', Kind.WordSort, 17, 0),        // control 0x17
        new('\u0018', Kind.WordSort, 18, 0),        // control 0x18
        new('\u0019', Kind.WordSort, 19, 0),        // control 0x19
        new('\u001A', Kind.WordSort, 20, 0),        // control 0x1a
        new('\u001B', Kind.WordSort, 21, 0),        // control 0x1b
        new('\u001C', Kind.WordSort, 22, 0),        // control 0x1c
        new('\u001D', Kind.WordSort, 23, 0),        // control 0x1d
        new('\u001E', Kind.WordSort, 24, 0),        // control 0x1e
        new('\u001F', Kind.WordSort, 25, 0),        // control 0x1f
        new('\u007F', Kind.WordSort, 26, 0),        // control 0x7f
        new('\'', Kind.WordSort, 27, 0),            // apostrophe
        new('-', Kind.WordSort, 28, 0),             // hyphen-minus
        new('\u2013', Kind.WordSort, 29, 0),        // en dash
        new('\u2014', Kind.WordSort, 30, 0),        // em dash
        new(' ', Kind.Weighted, 0, 0),              // space
        new('\u00A0', Kind.Weighted, 1, 0),         // no-break space
        new('\u0009', Kind.Weighted, 2, 0),         // control 0x09
        new('\u000A', Kind.Weighted, 3, 0),         // control 0x0a
        new('\u000B', Kind.Weighted, 4, 0),         // control 0x0b
        new('\u000C', Kind.Weighted, 5, 0),         // control 0x0c
        new('\u000D', Kind.Weighted, 6, 0),         // control 0x0d
        new('!', Kind.Weighted, 7, 0),              // exclamation mark
        new('"', Kind.Weighted, 8, 0),              // quotation mark
        new('#', Kind.Weighted, 9, 0),              // number sign
        new('$', Kind.Weighted, 10, 0),             // dollar sign
        new('%', Kind.Weighted, 11, 0),             // percent sign
        new('&', Kind.Weighted, 12, 0),             // ampersand
        new('(', Kind.Weighted, 13, 0),             // left parenthesis
        new(')', Kind.Weighted, 14, 0),             // right parenthesis
        new('*', Kind.Weighted, 15, 0),             // asterisk
        new(',', Kind.Weighted, 16, 0),             // comma
        new('.', Kind.Weighted, 17, 0),             // full stop
        new('/', Kind.Weighted, 18, 0),             // solidus
        new(':', Kind.Weighted, 19, 0),             // colon
        new(';', Kind.Weighted, 20, 0),             // semicolon
        new('?', Kind.Weighted, 21, 0),             // question mark
        new('@', Kind.Weighted, 22, 0),             // commercial at
        new('[', Kind.Weighted, 23, 0),             // left square bracket
        new('\\', Kind.Weighted, 24, 0),            // reverse solidus
        new(']', Kind.Weighted, 25, 0),             // right square bracket
        new('^', Kind.Weighted, 26, 0),             // circumflex accent
        new('\u02C6', Kind.Weighted, 26, 0),        // modifier letter circumflex accent
        new('_', Kind.Weighted, 27, 0),             // low line
        new('`', Kind.Weighted, 28, 0),             // grave accent
        new('{', Kind.Weighted, 29, 0),             // left curly bracket
        new('|', Kind.Weighted, 30, 0),             // vertical line
        new('}', Kind.Weighted, 31, 0),             // right curly bracket
        new('~', Kind.Weighted, 32, 0),             // tilde
        new('\u00A1', Kind.Weighted, 33, 0),        // inverted exclamation mark
        new('\u00A6', Kind.Weighted, 34, 0),        // broken bar
        new('\u00A8', Kind.Weighted, 35, 0),        // diaeresis
        new('\u00AF', Kind.Weighted, 36, 0),        // macron
        new('\u00B4', Kind.Weighted, 37, 0),        // acute accent
        new('\u00B8', Kind.Weighted, 38, 0),        // cedilla
        new('\u00BF', Kind.Weighted, 39, 0),        // inverted question mark
        new('\u02DC', Kind.Weighted, 40, 0),        // small tilde
        new('\u2018', Kind.Weighted, 41, 0),        // left single quotation mark
        new('\u2019', Kind.Weighted, 42, 0),        // right single quotation mark
        new('\u201A', Kind.Weighted, 43, 0),        // single low-9 quotation mark
        new('\u201C', Kind.Weighted, 44, 0),        // left double quotation mark
        new('\u201D', Kind.Weighted, 45, 0),        // right double quotation mark
        new('\u201E', Kind.Weighted, 46, 0),        // double low-9 quotation mark
        new('\u2039', Kind.Weighted, 47, 0),        // single left-pointing angle quotation mark
        new('\u203A', Kind.Weighted, 48, 0),        // single right-pointing angle quotation mark
        new('\u00A2', Kind.Weighted, 49, 0),        // cent sign
        new('\u00A3', Kind.Weighted, 50, 0),        // pound sign
        new('\u00A4', Kind.Weighted, 51, 0),        // currency sign
        new('\u00A5', Kind.Weighted, 52, 0),        // yen sign
        new('\u20AC', Kind.Weighted, 53, 0),        // euro sign
        new('+', Kind.Weighted, 54, 0),             // plus sign
        new('<', Kind.Weighted, 55, 0),             // less-than sign
        new('=', Kind.Weighted, 56, 0),             // equals sign
        new('>', Kind.Weighted, 57, 0),             // greater-than sign
        new('\u00B1', Kind.Weighted, 58, 0),        // plus-minus sign
        new('\u00AB', Kind.Weighted, 59, 0),        // left-pointing double angle quotation mark
        new('\u00BB', Kind.Weighted, 60, 0),        // right-pointing double angle quotation mark
        new('\u00D7', Kind.Weighted, 61, 0),        // multiplication sign
        new('\u00F7', Kind.Weighted, 62, 0),        // division sign
        new('\u00A7', Kind.Weighted, 63, 0),        // section sign
        new('\u00A9', Kind.Weighted, 64, 0),        // copyright sign
        new('\u00AC', Kind.Weighted, 65, 0),        // not sign
        new('\u00AE', Kind.Weighted, 66, 0),        // registered sign
        new('\u00B0', Kind.Weighted, 67, 0),        // degree sign
        new('\u00B5', Kind.Weighted, 68, 0),        // micro sign
        new('\u00B6', Kind.Weighted, 69, 0),        // pilcrow sign
        new('\u00B7', Kind.Weighted, 70, 0),        // middle dot
        new('\u2026', Kind.Weighted, 71, 0),        // horizontal ellipsis
        new('\u2020', Kind.Weighted, 72, 0),        // dagger
        new('\u2021', Kind.Weighted, 73, 0),        // double dagger
        new('\u2022', Kind.Weighted, 74, 0),        // bullet
        new('\u2030', Kind.Weighted, 75, 0),        // per mille sign
        new('\u0081', Kind.Weighted, 76, 0),        // control 0x81
        new('\u008D', Kind.Weighted, 76, 1),        // control 0x8d
        new('\u008F', Kind.Weighted, 76, 2),        // control 0x8f
        new('\u0090', Kind.Weighted, 76, 3),        // control 0x90
        new('\u009D', Kind.Weighted, 76, 4),        // control 0x9d
        new('0', Kind.Weighted, 77, 0),             // digit zero
        new('\u00BC', Kind.Weighted, 78, 0),        // vulgar fraction one quarter
        new('\u00BD', Kind.Weighted, 79, 0),        // vulgar fraction one half
        new('\u00BE', Kind.Weighted, 80, 0),        // vulgar fraction three quarters
        new('1', Kind.Weighted, 81, 0),             // digit one
        new('\u00B9', Kind.Weighted, 81, 0),        // superscript one
        new('2', Kind.Weighted, 82, 0),             // digit two
        new('\u00B2', Kind.Weighted, 82, 0),        // superscript two
        new('3', Kind.Weighted, 83, 0),             // digit three
        new('\u00B3', Kind.Weighted, 83, 0),        // superscript three
        new('4', Kind.Weighted, 84, 0),             // digit four
        new('5', Kind.Weighted, 85, 0),             // digit five
        new('6', Kind.Weighted, 86, 0),             // digit six
        new('7', Kind.Weighted, 87, 0),             // digit seven
        new('8', Kind.Weighted, 88, 0),             // digit eight
        new('9', Kind.Weighted, 89, 0),             // digit nine
        new('A', Kind.Weighted, 90, 0),             // latin capital letter a
        new('a', Kind.Weighted, 90, 0),             // latin small letter a
        new('\u00AA', Kind.Weighted, 90, 1),        // feminine ordinal indicator
        new('\u00C1', Kind.Weighted, 90, 2),        // latin capital letter a with acute
        new('\u00E1', Kind.Weighted, 90, 2),        // latin small letter a with acute
        new('\u00C0', Kind.Weighted, 90, 3),        // latin capital letter a with grave
        new('\u00E0', Kind.Weighted, 90, 3),        // latin small letter a with grave
        new('\u00C2', Kind.Weighted, 90, 4),        // latin capital letter a with circumflex
        new('\u00E2', Kind.Weighted, 90, 4),        // latin small letter a with circumflex
        new('\u00C4', Kind.Weighted, 90, 5),        // latin capital letter a with diaeresis
        new('\u00E4', Kind.Weighted, 90, 5),        // latin small letter a with diaeresis
        new('\u00C3', Kind.Weighted, 90, 6),        // latin capital letter a with tilde
        new('\u00E3', Kind.Weighted, 90, 6),        // latin small letter a with tilde
        new('\u00C5', Kind.Weighted, 90, 7),        // latin capital letter a with ring above
        new('\u00E5', Kind.Weighted, 90, 7),        // latin small letter a with ring above
        new('B', Kind.Weighted, 92, 0),             // latin capital letter b
        new('b', Kind.Weighted, 92, 0),             // latin small letter b
        new('C', Kind.Weighted, 93, 0),             // latin capital letter c
        new('c', Kind.Weighted, 93, 0),             // latin small letter c
        new('\u00C7', Kind.Weighted, 93, 1),        // latin capital letter c with cedilla
        new('\u00E7', Kind.Weighted, 93, 1),        // latin small letter c with cedilla
        new('D', Kind.Weighted, 94, 0),             // latin capital letter d
        new('d', Kind.Weighted, 94, 0),             // latin small letter d
        new('\u00D0', Kind.Weighted, 94, 1),        // latin capital letter eth
        new('\u00F0', Kind.Weighted, 94, 1),        // latin small letter eth
        new('E', Kind.Weighted, 95, 0),             // latin capital letter e
        new('e', Kind.Weighted, 95, 0),             // latin small letter e
        new('\u00C9', Kind.Weighted, 95, 1),        // latin capital letter e with acute
        new('\u00E9', Kind.Weighted, 95, 1),        // latin small letter e with acute
        new('\u00C8', Kind.Weighted, 95, 2),        // latin capital letter e with grave
        new('\u00E8', Kind.Weighted, 95, 2),        // latin small letter e with grave
        new('\u00CA', Kind.Weighted, 95, 3),        // latin capital letter e with circumflex
        new('\u00EA', Kind.Weighted, 95, 3),        // latin small letter e with circumflex
        new('\u00CB', Kind.Weighted, 95, 4),        // latin capital letter e with diaeresis
        new('\u00EB', Kind.Weighted, 95, 4),        // latin small letter e with diaeresis
        new('F', Kind.Weighted, 96, 0),             // latin capital letter f
        new('f', Kind.Weighted, 96, 0),             // latin small letter f
        new('\u0192', Kind.Weighted, 96, 1),        // latin small letter f with hook
        new('G', Kind.Weighted, 97, 0),             // latin capital letter g
        new('g', Kind.Weighted, 97, 0),             // latin small letter g
        new('H', Kind.Weighted, 98, 0),             // latin capital letter h
        new('h', Kind.Weighted, 98, 0),             // latin small letter h
        new('I', Kind.Weighted, 99, 0),             // latin capital letter i
        new('i', Kind.Weighted, 99, 0),             // latin small letter i
        new('\u00CD', Kind.Weighted, 99, 1),        // latin capital letter i with acute
        new('\u00ED', Kind.Weighted, 99, 1),        // latin small letter i with acute
        new('\u00CC', Kind.Weighted, 99, 2),        // latin capital letter i with grave
        new('\u00EC', Kind.Weighted, 99, 2),        // latin small letter i with grave
        new('\u00CE', Kind.Weighted, 99, 3),        // latin capital letter i with circumflex
        new('\u00EE', Kind.Weighted, 99, 3),        // latin small letter i with circumflex
        new('\u00CF', Kind.Weighted, 99, 4),        // latin capital letter i with diaeresis
        new('\u00EF', Kind.Weighted, 99, 4),        // latin small letter i with diaeresis
        new('J', Kind.Weighted, 100, 0),            // latin capital letter j
        new('j', Kind.Weighted, 100, 0),            // latin small letter j
        new('K', Kind.Weighted, 101, 0),            // latin capital letter k
        new('k', Kind.Weighted, 101, 0),            // latin small letter k
        new('L', Kind.Weighted, 102, 0),            // latin capital letter l
        new('l', Kind.Weighted, 102, 0),            // latin small letter l
        new('M', Kind.Weighted, 103, 0),            // latin capital letter m
        new('m', Kind.Weighted, 103, 0),            // latin small letter m
        new('N', Kind.Weighted, 104, 0),            // latin capital letter n
        new('n', Kind.Weighted, 104, 0),            // latin small letter n
        new('\u00D1', Kind.Weighted, 104, 1),       // latin capital letter n with tilde
        new('\u00F1', Kind.Weighted, 104, 1),       // latin small letter n with tilde
        new('O', Kind.Weighted, 105, 0),            // latin capital letter o
        new('o', Kind.Weighted, 105, 0),            // latin small letter o
        new('\u00BA', Kind.Weighted, 105, 1),       // masculine ordinal indicator
        new('\u00D3', Kind.Weighted, 105, 2),       // latin capital letter o with acute
        new('\u00F3', Kind.Weighted, 105, 2),       // latin small letter o with acute
        new('\u00D2', Kind.Weighted, 105, 3),       // latin capital letter o with grave
        new('\u00F2', Kind.Weighted, 105, 3),       // latin small letter o with grave
        new('\u00D4', Kind.Weighted, 105, 4),       // latin capital letter o with circumflex
        new('\u00F4', Kind.Weighted, 105, 4),       // latin small letter o with circumflex
        new('\u00D6', Kind.Weighted, 105, 5),       // latin capital letter o with diaeresis
        new('\u00F6', Kind.Weighted, 105, 5),       // latin small letter o with diaeresis
        new('\u00D5', Kind.Weighted, 105, 6),       // latin capital letter o with tilde
        new('\u00F5', Kind.Weighted, 105, 6),       // latin small letter o with tilde
        new('\u00D8', Kind.Weighted, 105, 7),       // latin capital letter o with stroke
        new('\u00F8', Kind.Weighted, 105, 7),       // latin small letter o with stroke
        new('P', Kind.Weighted, 107, 0),            // latin capital letter p
        new('p', Kind.Weighted, 107, 0),            // latin small letter p
        new('Q', Kind.Weighted, 108, 0),            // latin capital letter q
        new('q', Kind.Weighted, 108, 0),            // latin small letter q
        new('R', Kind.Weighted, 109, 0),            // latin capital letter r
        new('r', Kind.Weighted, 109, 0),            // latin small letter r
        new('S', Kind.Weighted, 110, 0),            // latin capital letter s
        new('s', Kind.Weighted, 110, 0),            // latin small letter s
        new('\u0160', Kind.Weighted, 110, 1),       // latin capital letter s with caron
        new('\u0161', Kind.Weighted, 110, 1),       // latin small letter s with caron
        new('T', Kind.Weighted, 112, 0),            // latin capital letter t
        new('t', Kind.Weighted, 112, 0),            // latin small letter t
        new('\u2122', Kind.Weighted, 114, 0),       // trade mark sign
        new('U', Kind.Weighted, 115, 0),            // latin capital letter u
        new('u', Kind.Weighted, 115, 0),            // latin small letter u
        new('\u00DA', Kind.Weighted, 115, 1),       // latin capital letter u with acute
        new('\u00FA', Kind.Weighted, 115, 1),       // latin small letter u with acute
        new('\u00D9', Kind.Weighted, 115, 2),       // latin capital letter u with grave
        new('\u00F9', Kind.Weighted, 115, 2),       // latin small letter u with grave
        new('\u00DB', Kind.Weighted, 115, 3),       // latin capital letter u with circumflex
        new('\u00FB', Kind.Weighted, 115, 3),       // latin small letter u with circumflex
        new('\u00DC', Kind.Weighted, 115, 4),       // latin capital letter u with diaeresis
        new('\u00FC', Kind.Weighted, 115, 4),       // latin small letter u with diaeresis
        new('V', Kind.Weighted, 116, 0),            // latin capital letter v
        new('v', Kind.Weighted, 116, 0),            // latin small letter v
        new('W', Kind.Weighted, 117, 0),            // latin capital letter w
        new('w', Kind.Weighted, 117, 0),            // latin small letter w
        new('X', Kind.Weighted, 118, 0),            // latin capital letter x
        new('x', Kind.Weighted, 118, 0),            // latin small letter x
        new('Y', Kind.Weighted, 119, 0),            // latin capital letter y
        new('y', Kind.Weighted, 119, 0),            // latin small letter y
        new('\u00DD', Kind.Weighted, 119, 1),       // latin capital letter y with acute
        new('\u00FD', Kind.Weighted, 119, 1),       // latin small letter y with acute
        new('\u0178', Kind.Weighted, 119, 2),       // latin capital letter y with diaeresis
        new('\u00FF', Kind.Weighted, 119, 2),       // latin small letter y with diaeresis
        new('Z', Kind.Weighted, 120, 0),            // latin capital letter z
        new('z', Kind.Weighted, 120, 0),            // latin small letter z
        new('\u017D', Kind.Weighted, 120, 1),       // latin capital letter z with caron
        new('\u017E', Kind.Weighted, 120, 1),       // latin small letter z with caron
    ];

    // The characters that compare as two letters, and match them in a search (measured: a sweep of Chr(128) to Chr(255)
    // against every one- and two-letter string found these seven and no others).
    private static (char Character, string Letters)[] Expansions =>
    [
        ('\u00DF', "ss"),                           // latin small letter sharp s
        ('\u00E6', "ae"),                           // latin small letter ae
        ('\u00C6', "ae"),                           // latin capital letter ae
        ('\u0153', "oe"),                           // latin small ligature oe
        ('\u0152', "oe"),                           // latin capital ligature oe
        ('\u00FE', "th"),                           // latin small letter thorn
        ('\u00DE', "th"),                           // latin capital letter thorn
    ];
}
