// Generated from vb6.exe's case sweep for #1560: the program is the probe's UCase and LCase half, and each expected
// line is what vb6.exe logged, native and p-code alike.

using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// UCase and LCase over every character Chr(0) to Chr(255) gives, and six whose Unicode case belongs to another character
/// (#1560). A character takes its other case only where that case maps back to it: ƒ is Ƒ and ÿ is Ÿ, while µ, ς, ſ
/// and ı keep their case in UCase, and the Kelvin sign and İ keep theirs in LCase.
/// </summary>
public class CaseMappingTests : BaseVBTestFixture
{
    [Fact]
    public async Task UCase_and_LCase_map_a_character_only_where_its_case_comes_back()
    {
        await Run("""
            Dim i As Long, c As String, u As Long, lo As Long
            For i = 0 To 255
                c = Chr$(i)
                u = AscW(UCase$(c)): lo = AscW(LCase$(c))
                If u <> AscW(c) Or lo <> AscW(c) Then Debug.Print "case " & i & " " & AscW(c) & ": U " & u & " L " & lo
            Next
            Debug.Print "ucase wide: " & AscW(UCase$(ChrW(&HFF41))) & " lcase wide: " & AscW(LCase$(ChrW(&HFF21)))
            Debug.Print "ucase greek: " & AscW(UCase$(ChrW(&H3B1))) & " lcase greek: " & AscW(LCase$(ChrW(&H391)))
            Debug.Print "ucase final sigma: " & AscW(UCase$(ChrW(&H3C2))) & " ucase micro: " & AscW(UCase$(ChrW(&HB5)))
            Debug.Print "ucase dotless: " & AscW(UCase$(ChrW(&H131))) & " lcase dotted: " & AscW(LCase$(ChrW(&H130)))
            Debug.Print "ucase long s: " & AscW(UCase$(ChrW(&H17F))) & " lcase kelvin: " & AscW(LCase$(ChrW(&H212A)))
            """);

        AssertDebugLog(Lines("""
            case 65 65: U 65 L 97
            case 66 66: U 66 L 98
            case 67 67: U 67 L 99
            case 68 68: U 68 L 100
            case 69 69: U 69 L 101
            case 70 70: U 70 L 102
            case 71 71: U 71 L 103
            case 72 72: U 72 L 104
            case 73 73: U 73 L 105
            case 74 74: U 74 L 106
            case 75 75: U 75 L 107
            case 76 76: U 76 L 108
            case 77 77: U 77 L 109
            case 78 78: U 78 L 110
            case 79 79: U 79 L 111
            case 80 80: U 80 L 112
            case 81 81: U 81 L 113
            case 82 82: U 82 L 114
            case 83 83: U 83 L 115
            case 84 84: U 84 L 116
            case 85 85: U 85 L 117
            case 86 86: U 86 L 118
            case 87 87: U 87 L 119
            case 88 88: U 88 L 120
            case 89 89: U 89 L 121
            case 90 90: U 90 L 122
            case 97 97: U 65 L 97
            case 98 98: U 66 L 98
            case 99 99: U 67 L 99
            case 100 100: U 68 L 100
            case 101 101: U 69 L 101
            case 102 102: U 70 L 102
            case 103 103: U 71 L 103
            case 104 104: U 72 L 104
            case 105 105: U 73 L 105
            case 106 106: U 74 L 106
            case 107 107: U 75 L 107
            case 108 108: U 76 L 108
            case 109 109: U 77 L 109
            case 110 110: U 78 L 110
            case 111 111: U 79 L 111
            case 112 112: U 80 L 112
            case 113 113: U 81 L 113
            case 114 114: U 82 L 114
            case 115 115: U 83 L 115
            case 116 116: U 84 L 116
            case 117 117: U 85 L 117
            case 118 118: U 86 L 118
            case 119 119: U 87 L 119
            case 120 120: U 88 L 120
            case 121 121: U 89 L 121
            case 122 122: U 90 L 122
            case 131 402: U 401 L 402
            case 138 352: U 352 L 353
            case 140 338: U 338 L 339
            case 142 381: U 381 L 382
            case 154 353: U 352 L 353
            case 156 339: U 338 L 339
            case 158 382: U 381 L 382
            case 159 376: U 376 L 255
            case 192 192: U 192 L 224
            case 193 193: U 193 L 225
            case 194 194: U 194 L 226
            case 195 195: U 195 L 227
            case 196 196: U 196 L 228
            case 197 197: U 197 L 229
            case 198 198: U 198 L 230
            case 199 199: U 199 L 231
            case 200 200: U 200 L 232
            case 201 201: U 201 L 233
            case 202 202: U 202 L 234
            case 203 203: U 203 L 235
            case 204 204: U 204 L 236
            case 205 205: U 205 L 237
            case 206 206: U 206 L 238
            case 207 207: U 207 L 239
            case 208 208: U 208 L 240
            case 209 209: U 209 L 241
            case 210 210: U 210 L 242
            case 211 211: U 211 L 243
            case 212 212: U 212 L 244
            case 213 213: U 213 L 245
            case 214 214: U 214 L 246
            case 216 216: U 216 L 248
            case 217 217: U 217 L 249
            case 218 218: U 218 L 250
            case 219 219: U 219 L 251
            case 220 220: U 220 L 252
            case 221 221: U 221 L 253
            case 222 222: U 222 L 254
            case 224 224: U 192 L 224
            case 225 225: U 193 L 225
            case 226 226: U 194 L 226
            case 227 227: U 195 L 227
            case 228 228: U 196 L 228
            case 229 229: U 197 L 229
            case 230 230: U 198 L 230
            case 231 231: U 199 L 231
            case 232 232: U 200 L 232
            case 233 233: U 201 L 233
            case 234 234: U 202 L 234
            case 235 235: U 203 L 235
            case 236 236: U 204 L 236
            case 237 237: U 205 L 237
            case 238 238: U 206 L 238
            case 239 239: U 207 L 239
            case 240 240: U 208 L 240
            case 241 241: U 209 L 241
            case 242 242: U 210 L 242
            case 243 243: U 211 L 243
            case 244 244: U 212 L 244
            case 245 245: U 213 L 245
            case 246 246: U 214 L 246
            case 248 248: U 216 L 248
            case 249 249: U 217 L 249
            case 250 250: U 218 L 250
            case 251 251: U 219 L 251
            case 252 252: U 220 L 252
            case 253 253: U 221 L 253
            case 254 254: U 222 L 254
            case 255 255: U 376 L 255
            ucase wide: -223 lcase wide: -191
            ucase greek: 913 lcase greek: 945
            ucase final sigma: 962 ucase micro: 181
            ucase dotless: 305 lcase dotted: 304
            ucase long s: 383 lcase kelvin: 8490
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
