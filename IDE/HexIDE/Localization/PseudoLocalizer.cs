using System.Collections.Generic;
using System.Text;

namespace HexIDE.Localization;

/// <summary>
/// Runtime pseudo-localization. Transforms the English pack so that (a) every translatable string is
/// visibly altered — anything left in plain English on screen is therefore a <i>missed</i> string — and
/// (b) layout is stress-tested against ~30%-longer text and right-to-left rendering. Mnemonic access
/// keys (<c>_X</c>), format placeholders (<c>{0}</c>) and ampersands are preserved verbatim. Generated
/// from the English pack at runtime, so it is always key-complete and needs no file on disk.
/// <para>
/// The length padding is drawn from Klingon filler (<c>Qapla'!</c>) — a nod that keeps the pseudo build
/// fun while the accented English base stays legible enough to confirm the right string is wired up.
/// </para>
/// </summary>
internal static class PseudoLocalizer
{
    private const char OpenBracket = '⟦';  // ⟦ — marks a transformed string; un-bracketed text was missed
    private const char CloseBracket = '⟧'; // ⟧
    private const char Rle = '‫';          // RIGHT-TO-LEFT EMBEDDING
    private const char Pdf = '‬';          // POP DIRECTIONAL FORMATTING

    // Klingon-flavoured filler used to pad strings (and give the pseudo build its character).
    private static readonly string[] s_klingonFiller =
        ["Qapla'", "tlhIngan", "nuqneH", "batlh", "Hegh", "may'", "wo'", "ghobe'"];

    private static readonly Dictionary<char, char> s_accents = new()
    {
        ['a'] = 'á', ['e'] = 'é', ['i'] = 'í', ['o'] = 'ó', ['u'] = 'ú', ['y'] = 'ý',
        ['A'] = 'Á', ['E'] = 'É', ['I'] = 'Í', ['O'] = 'Ó', ['U'] = 'Ú', ['Y'] = 'Ý',
        ['c'] = 'ç', ['n'] = 'ñ', ['s'] = 'š',
        ['C'] = 'Ç', ['N'] = 'Ñ', ['S'] = 'Š',
    };

    /// <summary>Transforms every value in <paramref name="english"/>; keys are unchanged.</summary>
    public static Dictionary<string, string> Transform(IReadOnlyDictionary<string, string> english, bool rtl)
    {
        var result = new Dictionary<string, string>(english.Count);
        foreach (var (key, value) in english)
            result[key] = TransformValue(value, rtl);
        return result;
    }

    private static string TransformValue(string value, bool rtl)
    {
        var core = new StringBuilder(value.Length + 8);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            // Mnemonic / escaped underscore: keep '_' and the next char verbatim so the access key
            // (Alt+letter) still resolves to the original ASCII letter.
            if (c == '_')
            {
                core.Append('_');
                if (i + 1 < value.Length)
                {
                    core.Append(value[i + 1]);
                    i++;
                }
                continue;
            }

            // Format placeholder / XAML escape: copy "{...}" verbatim.
            if (c == '{')
            {
                var close = value.IndexOf('}', i);
                if (close >= 0)
                {
                    core.Append(value, i, close - i + 1);
                    i = close;
                    continue;
                }
            }

            core.Append(s_accents.TryGetValue(c, out var accented) ? accented : c);
        }

        var coreText = core.ToString();
        var wrapped = $"{OpenBracket}{coreText} {BuildFiller(coreText.Length)}{CloseBracket}";
        return rtl ? $"{Rle}{wrapped}{Pdf}" : wrapped;
    }

    /// <summary>Builds ~30%-of-length Klingon filler, deterministically (no RNG) so output is stable.</summary>
    private static string BuildFiller(int coreLength)
    {
        var target = coreLength * 3 / 10;
        if (target < 3) target = 3;

        var sb = new StringBuilder(target + 8);
        var index = coreLength % s_klingonFiller.Length; // deterministic, but varies per string length
        while (sb.Length < target)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s_klingonFiller[index]);
            index = (index + 1) % s_klingonFiller.Length;
        }
        return sb.ToString();
    }
}
