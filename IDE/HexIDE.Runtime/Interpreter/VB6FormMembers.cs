using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The members VB6's Form class declares: every property and method of its <c>_Form</c> interface, as the type library
/// has them. Read from <c>Interpreter/Data/vb6-form-members.json</c>, which <c>scripts/dump-typelib-members.ps1</c>
/// generates from VB98\VB6.OLB on the oracle VM.
/// </summary>
/// <remarks>
/// A bare name in a form module is looked up on the form before the project's globals (measured, #822 and #995). This
/// is how the interpreter knows a name IS the form's: not only the members HexIDE implements, but every member VB6's
/// Form has. So <c>CurrentX = 0</c>, which HexIDE can't do yet, is refused as not implemented rather than quietly made
/// a new variable, and a name the Form doesn't have is left to the rest of the lookup.
/// </remarks>
internal static class VB6FormMembers
{
    /// <summary>
    /// One member, its accessors folded together: a property with a get and a let is one member that can be read and
    /// assigned. <see cref="RequiredArgs"/> and <see cref="MaxArgs"/> count what goes in the parentheses: a method's
    /// arguments, or a property's index arguments, which no Form property has.
    /// </summary>
    internal sealed record Member(string Name, bool IsMethod, bool CanGet, bool CanLet, bool CanSet, int RequiredArgs, int MaxArgs);

    private static readonly Lazy<IReadOnlyDictionary<string, Member>> members = new(Build, isThreadSafe: true);

    /// <summary>The Form member named <paramref name="name"/>, in any case, or false when VB6's Form has none.</summary>
    public static bool TryGet(string name, out Member member) => members.Value.TryGetValue(name, out member!);

    /// <summary>Every member, for a test that checks the table against what the interpreter implements.</summary>
    internal static IReadOnlyList<Member> All => members.Value.Values.ToList();

    private static IReadOnlyDictionary<string, Member> Build()
    {
        var asm = typeof(VB6FormMembers).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith("vb6-form-members.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var doc = JsonDocument.Parse(stream);

        var byName = new Dictionary<string, Member>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in doc.RootElement.GetProperty("classes").GetProperty("Form").GetProperty("members").EnumerateArray())
        {
            var name = entry.GetProperty("name").GetString()!;
            var invoke = entry.GetProperty("invoke").GetString()!;
            var parameters = entry.GetProperty("params").EnumerateArray().ToList();
            var required = parameters.Count(p => !p.GetProperty("optional").GetBoolean());
            byName.TryGetValue(name, out var known);
            var member = known ?? new Member(name, invoke == "method", false, false, false, required, parameters.Count);
            byName[name] = invoke switch
            {
                "get" => member with { CanGet = true },
                "let" => member with { CanLet = true },
                "set" => member with { CanSet = true },
                _ => member with { IsMethod = true, RequiredArgs = required, MaxArgs = parameters.Count },
            };
        }
        return byName;
    }
}
