using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// #5059: SP6 native/p-code, en-GB/1252. Exact rows and insertion model:
// docs/vb6-fidelity-oracle/the-order-a-sorted-listbox-or-combobox-keeps.md.
public class SortedListItemsTests
{
    private static async Task<ItemsControl> Run(string type, int style, string[] added, bool? sorted = true, string after = "")
    {
        var form = $$"""
            VERSION 5.00
            Begin VB.Form Form1
               Begin VB.{{type}} Items1
                  Width = 2400
                  Height = 1200
                  Style = {{style}}
                  {{(sorted is null ? "" : $"Sorted = {(sorted.Value ? -1 : 0)}")}}
               End
            End
            Attribute VB_Name = "Form1"
            """;
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var code = string.Concat(added.Select(text => $"Items1.AddItem \"{text.Replace("\"", "\"\"")}\"\r\n"));
        await ContainerRuntimeHarness.Run(ctx, env, code + after.ReplaceLineEndings("\r\n"));
        return (ItemsControl)ContainerRuntimeHarness.Child(canvas, "Items1");
    }

    private static string CodePoints(string text) => string.Join(" ", text.EnumerateRunes()
        .Select(rune => rune.Value.ToString("X4", CultureInfo.InvariantCulture)));

    private static void Check(ItemsControl list, string[] expected) =>
        list.Items.Cast<string>().Select(CodePoints).Should().Equal(expected.Select(CodePoints));

    [AvaloniaTheory]
    [InlineData("ListBox", 0, "c|a|b", "a|b|c")]
    [InlineData("ListBox", 1, "c|a|b", "a|b|c")]
    [InlineData("ComboBox", 0, "c|a|b", "a|b|c")]
    [InlineData("ComboBox", 2, "c|a|b", "a|b|c")]
    [InlineData("ListBox", 0, "b|B", "B|b")]
    [InlineData("ListBox", 1, "b|B", "B|b")]
    [InlineData("ListBox", 0, "[a|z|a", "a|z|[a")]
    [InlineData("ListBox", 1, "[a|z|a", "a|z|[a")]
    public async Task Saved_Sorted_uses_the_measured_insertion_rules(string type, int style, string added, string kept)
    {
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        Check(await Run(type, style, added.Split('|')), kept.Split('|'));
    }

    // #821: docs/vb6-fidelity-oracle/a-lists-members-listcount-list.md.
    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ListBox", 1)]
    [InlineData("ComboBox", 0)]
    [InlineData("ComboBox", 2)]
    public async Task An_explicit_index_wins_over_Sorted(string type, int style) =>
        Check(await Run(type, style, ["a", "b", "c"], after: "Items1.AddItem \"z\", 0"), ["z", "a", "b", "c"]);

    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ListBox", 1)]
    [InlineData("ComboBox", 0)]
    [InlineData("ComboBox", 2)]
    public async Task An_unsorted_list_still_appends(string type, int style)
    {
        foreach (bool? sorted in new bool?[] { false, null })
            Check(await Run(type, style, ["c", "a", "b"], sorted), ["c", "a", "b"]);
    }

    // #5059 explicitly applies the shared rules to Style 1: reasoned, not a VB6 measurement.
    [AvaloniaFact]
    public async Task Simple_Combo_uses_the_same_sorting_policy() =>
        Check(await Run("ComboBox", 1, ["c", "a", "b"]), ["a", "b", "c"]);

    // Host-policy check from #5059's decision, not an unmeasured VB6 Swedish result.
    [AvaloniaTheory]
    [InlineData("ListBox", 0)]
    [InlineData("ListBox", 1)]
    [InlineData("ComboBox", 0)]
    [InlineData("ComboBox", 2)]
    public async Task Off_Windows_comparison_uses_the_program_culture(string type, int style)
    {
        if (OperatingSystem.IsWindows())
            throw Xunit.Sdk.SkipException.ForSkip("#5059: host comparison is the non-Windows policy.");
        // Choose a program culture whose ordering differs from the thread without changing that thread.
        var culture = CultureInfo.GetCultureInfo(CultureInfo.CurrentCulture.CompareInfo
            .Compare("ä", "z", CompareOptions.IgnoreCase) < 0 ? "sv-SE" : "en-GB");
        var expected = new[] { "ä", "z" }.Order(StringComparer.Create(culture, true)).ToArray();
        using var locale = VbLocale.Use(culture);
        Check(await Run(type, style, expected.Reverse().ToArray()), expected);
    }

    public static IEnumerable<object[]> WindowsOrders()
    {
        yield return ["41 items",
            "b|B|a|A|10|9|1|2|a10|a2|a1|coop|co-op|co op|Co-op|_a|~a|'a|-a| a|é|e|f|E|x|x||Z|z|[a|{a|0|Æ|ß|ss|ae|ab|a-b|a b|AB|aB".Split('|'),
            "| a|_a|{a|~a|0|1|10|2|9|A|a|'a|-a|a b|a1|a10|a2|AB|aB|ab|a-b|ae|Æ|B|b|co op|coop|Co-op|co-op|E|e|é|f|ss|ß|x|x|z|Z|[a".Split('|')];
        var printable = Enumerable.Range(0x20, 0x7f - 0x20).Concat(Enumerable.Range(0xa0, 0x100 - 0xa0))
            .Select(code => char.ConvertFromUtf32(code)).ToArray();
        var keptPrintable = """
            U+00AD ' - U+0020 U+00A0 ! " # $ % & ( ) * , . / : ; ? @ \ ] ^ _ ` { | } ~ ¡ ¦ ¨ ¯ ´ ¸ ¿ ¢ £ ¤ ¥ + < = > ± « » × ÷
            § © ¬ ® ° µ ¶ · 0 ¼ ½ ¾ 1 ¹ 2 ² 3 ³ 4 5 6 7 8 9 a A ª á Á à À â Â ä Ä ã Ã å Å æ Æ b B c C ç Ç d D ð Ð e E é É è È ê
            Ê ë Ë f F g G h H i I í Í ì Ì î Î ï Ï j J k K l L m M n N ñ Ñ o O º ó Ó ò Ò ô Ô ö Ö õ Õ ø Ø p P q Q r R s S ß t T þ Þ
            u U ú Ú ù Ù û Û ü Ü v V w W x X y Y ý Ý ÿ z Z [
            """;
        yield return ["printable characters", printable,
            keptPrintable.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.StartsWith("U+", StringComparison.Ordinal)
                    ? char.ConvertFromUtf32(int.Parse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)) : token).ToArray()];
        yield return ["accents, ligatures and hyphens",
            "u|ü|v|U|Ü|n|ñ|o|ö|oe|œ|ae|æ|Æ|a|å|Å|b|c|ç|d|ð|th|þ|t|ss|ß|st|y|ÿ|z|é|e|f|è|ê|ë|E|É|resume|résumé|Resume|resumé|coop|co-op|coöp|a-b|ab|a b|a'b|a_b|a.b|ab-|-ab|a--b|a'-b".Split('|'),
            "a|Å|å|a b|a.b|a_b|ab|ab-|a'b|a'-b|a-b|a--b|-ab|Æ|æ|ae|b|c|ç|coop|co-op|coöp|d|ð|E|e|É|é|è|ê|ë|f|n|ñ|o|ö|œ|oe|Resume|resume|resumé|résumé|ß|ss|st|t|þ|th|U|u|Ü|ü|v|y|ÿ|z".Split('|')];
        yield return ["leading brackets",
            "a|z|m|[b|[A|[|[-a-]|[-b-]|a[b|ab|ac|z[|za|]a".Split('|'),
            "]a|a|a[b|ab|ac|m|z|z[|za|[|[A|[-a-]|[b|[-b-]".Split('|')];
        yield return ["bracket added first", "[a|z|a|m|_a|0|Z".Split('|'), "_a|0|a|m|Z|z|[a".Split('|')];
        // #5059's latest comment restores the actual fourth input, Chr(7), omitted from the page's display.
        yield return ["symbols",
            "m [a ]a \a ^a `a @a a z !a #a (a )a *a /a :a ;a <a =a >a ?a |a }a $a %a &a +a ,a .a \"a Za za".Split(' '),
            "\a !a \"a #a $a %a &a (a )a *a ,a .a /a :a ;a ?a @a ]a ^a `a |a }a +a <a =a >a a m z za Za [a".Split(' ')];
        yield return ["sharp s first", new[] { "ß", "ss" }, new[] { "ss", "ß" }];
        yield return ["sharp s last", new[] { "ss", "ß" }, new[] { "ß", "ss" }];
        yield return ["ae", new[] { "æ", "ae" }, new[] { "ae", "æ" }];
        yield return ["oe", new[] { "œ", "oe" }, new[] { "oe", "œ" }];
        yield return ["thorn", new[] { "þ", "th" }, new[] { "th", "þ" }];
        yield return ["three ligatures", new[] { "Æ", "æ", "ae" }, new[] { "ae", "æ", "Æ" }];
        yield return ["binary search midpoint", new[] { "ae", "AE", "Æ" }, new[] { "Æ", "AE", "ae" }];
    }

    [AvaloniaTheory]
    [MemberData(nameof(WindowsOrders))]
    public async Task Windows_keeps_every_measured_code_point(string row, string[] added, string[] kept)
    {
        if (!OperatingSystem.IsWindows())
            throw Xunit.Sdk.SkipException.ForSkip("#5059: exact en-GB order requires Windows NLS.");
        using var locale = VbLocale.Use(CultureInfo.GetCultureInfo("en-GB"));
        using var assertions = new AwesomeAssertions.Execution.AssertionScope(row);
        Check(await Run("ListBox", 0, added), kept);
    }
}
