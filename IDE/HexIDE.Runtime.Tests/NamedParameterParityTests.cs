using System.Text.RegularExpressions;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// The runtime's intrinsic parameter names (VB6BuiltIns.NamedParameters, measured with vb6.exe, #1465) and the editor's
/// signature help (the language server's VbSignatures.cs) are two tables. The runtime's is what runs; this keeps the two from
/// disagreeing about a name. It reads VbSignatures.cs as text, since the runtime has no reference to the language server, and
/// it fails closed: an intrinsic the runtime takes names for must have an entry there, and the file must yield entries at
/// all, so a change to its shape turns this red rather than quietly checking nothing. It checks one direction only. The
/// tooltip may show names VB6 refuses (`InStr`'s, `Len`'s): that is display, and VB6 refusing them is a fact about the runtime.
/// </summary>
public class NamedParameterParityTests
{
    private static readonly Regex Entry = new(
        @"\[""(?<name>\w+)""\]\s*=\s*\[\s*new SignatureEntry\(\s*""(?:[^""\\]|\\.)*"",\s*\[(?<params>[^\]]*)\]",
        RegexOptions.CultureInvariant);

    private static Dictionary<string, string[]> TooltipParameters()
    {
        var path = Path.Combine(RepoRoot(), "LspServer", "HexIDE.VbLspServer", "VbSignatures.cs");
        File.Exists(path).Should().BeTrue("the signature table should be at {0}", path);
        var entries = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Entry.Matches(File.ReadAllText(path)))
            entries.TryAdd(m.Groups["name"].Value,
                [.. Regex.Matches(m.Groups["params"].Value, @"""([^""]*)""").Select(p => p.Groups[1].Value)]);
        return entries;
    }

    [Fact]
    public void The_signature_table_is_read()
    {
        // Far fewer than the 90 it holds today means the pattern no longer matches the file.
        TooltipParameters().Should().HaveCountGreaterThan(50);
    }

    [Fact]
    public void Every_intrinsic_that_takes_names_has_the_tooltips_names_in_the_tooltips_order()
    {
        var tooltip = TooltipParameters();
        VB6BuiltIns.IntrinsicsTakingNamedArguments.Should().NotBeEmpty();

        var disagreements = new List<string>();
        foreach (var (name, parameters) in VB6BuiltIns.IntrinsicsTakingNamedArguments)
        {
            if (!tooltip.TryGetValue(name, out var shown))
                disagreements.Add($"{name}: no entry in VbSignatures.cs");
            else if (!shown.SequenceEqual(parameters.Names, StringComparer.Ordinal))
                disagreements.Add($"{name}: runtime ({string.Join(", ", parameters.Names)}), tooltip ({string.Join(", ", shown)})");
        }

        disagreements.Should().BeEmpty();
    }

    // The sweep #1475 asked for, kept: an intrinsic added to the registry is measured for names and given a tooltip in the
    // same change, or this names it.
    [Fact]
    public void Every_registered_intrinsic_says_how_it_takes_names_and_has_a_tooltip()
    {
        var tooltip = TooltipParameters();
        VB6BuiltIns.RegisteredIntrinsics.Should().NotBeEmpty();

        VB6BuiltIns.RegisteredIntrinsics.Where(name => !VB6BuiltIns.SaysHowItTakesNames(name))
            .Should().BeEmpty("each intrinsic's named arguments should be measured and listed in VB6BuiltIns.NamedParameters");
        VB6BuiltIns.RegisteredIntrinsics.Where(name => !tooltip.ContainsKey(name))
            .Should().BeEmpty("each intrinsic should have an entry in VbSignatures.cs");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        dir.Should().NotBeNull("the repository root should be findable from {0}", AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
