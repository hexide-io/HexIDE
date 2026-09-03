using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// VB6's in-box constants, structured the way VB6 structures them: 728 constants in 77 enums and 2
/// constant modules, across the four libraries a Standard EXE references by default. The catalogue and
/// the measurements behind it are in <c>docs/vb6-inbox-constants.md</c>; the data is the embedded
/// <c>Interpreter/Data/vb6-inbox-constants.json</c>, read out of the real type libraries by
/// <c>scripts/dump-typelib-constants.ps1</c>.
///
/// <para>
/// <b>Why this replaced a flat dictionary.</b> A single name→value map cannot answer VB6 correctly,
/// because the qualifier <i>selects</i>: <c>vbCancel</c> is <c>2</c> in <c>VBA.VbMsgBoxResult</c> and
/// <c>0</c> in <c>VBRUN.DragConstants</c>, and VB6 resolves <c>VBRUN.vbCancel</c> to <c>0</c> while a
/// bare <c>vbCancel</c> is <c>2</c>. The old table treated both the library and the enum level as
/// transparent — looked past them and resolved the bare name — so it answered <c>2</c> for every form
/// and was wrong on two of the four. That is a wrong value, which CLAUDE.md ranks as never acceptable.
/// </para>
///
/// <para>
/// <b>This is reference data, not static analysis.</b> The old code declined to build this table on the
/// grounds that "there is no table of the libraries' enum names here, and inventing one would be
/// pre-execution analysis" — correct at the time, and the operative word was <i>inventing</i>. This
/// table was measured from the platform rather than inferred from the program, which puts it in the
/// same class as the intrinsic-function registry: a fact about VB6, consulted during execution.
/// </para>
///
/// <para><b>The measured rules</b> (every row a probe in <c>corpus/.../inbox-constant-addressing.json</c>):
/// <list type="bullet">
/// <item>Bare <c>vbCancel</c> → 2. Library precedence is VBA before VBRUN.</item>
/// <item><c>VBA.vbCancel</c> → 2, <c>VBRUN.vbCancel</c> → 0. The library selects, skipping the enum.</item>
/// <item><c>VbMsgBoxResult.vbCancel</c> → 2, <c>DragConstants.vbCancel</c> → 0. The container selects on
///   its own; all 79 container names happen to be unique across the four libraries.</item>
/// <item><c>VBRUN.VbMsgBoxResult.vbCancel</c>, <c>stdole.vbCancel</c> and <c>DragConstants.vbYes</c> are
///   all <b>illegal</b> — "Method or data member not found". Both levels are real scopes, checked
///   strictly; a mismatched qualifier is refused, not skipped.</item>
/// <item><c>Constants.vbCrLf</c> and <c>VBA.Constants.vbCrLf</c> are legal: a constant MODULE qualifies
///   its members exactly as an enum does.</item>
/// <item><c>Dim x As VbMsgBoxResult</c> and <c>Dim x As VBA.VbMsgBoxResult</c> are both legal, while
///   <c>Dim x As Constants</c> is not. An enum name is a type <i>and</i> a qualifier; a module name is
///   only a qualifier. (Note the asymmetry with user enums, where a module-qualified type name was
///   measured <i>illegal</i> — see the Module scope section of the oracle.)</item>
/// <item>Every numeric member is a <b>Long</b>, not an Integer. The old table built values through
///   <c>Vb6Value(int)</c> and its magnitude rule, so small constants came back Integer where VB6 says
///   Long — a wrong type on ~700 values, invisible because the corpus cases that would have caught it
///   were module-scope and therefore ungated.</item>
/// </list>
/// </para>
/// </summary>
internal static class VB6InBoxLibraries
{
    /// <summary>An enum or a constant module: a name, whether it is a type, and its members.</summary>
    internal sealed record Container(string Library, string Name, bool IsEnum,
        IReadOnlyDictionary<string, Vb6Value> Members);

    private sealed record Model(
        IReadOnlyList<string> LibraryOrder,
        IReadOnlyDictionary<string, IReadOnlyList<Container>> ByLibrary,
        IReadOnlyDictionary<string, Container> ByContainer,
        IReadOnlyDictionary<string, Vb6Value> Bare);

    private static readonly Lazy<Model> model = new(Build, isThreadSafe: true);

    private static Model Build()
    {
        var asm = typeof(VB6InBoxLibraries).Assembly;
        var name = asm.GetManifestResourceNames()
            .Single(n => n.EndsWith("vb6-inbox-constants.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);

        var order = new List<string>();
        var byLibrary = new Dictionary<string, IReadOnlyList<Container>>(StringComparer.OrdinalIgnoreCase);
        var byContainer = new Dictionary<string, Container>(StringComparer.OrdinalIgnoreCase);
        // Bare resolution is first-wins in library order, which is what makes VBA beat VBRUN for
        // `vbCancel`. Insertion order therefore encodes the precedence and must not become a sort.
        var bare = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);

        foreach (var lib in doc.RootElement.GetProperty("libraries").EnumerateObject())
        {
            order.Add(lib.Name);
            var containers = new List<Container>();
            foreach (var cont in lib.Value.GetProperty("containers").EnumerateObject())
            {
                var isEnum = cont.Value.GetProperty("kind").GetString() == "enum";
                var members = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
                foreach (var m in cont.Value.GetProperty("members").EnumerateObject())
                {
                    // A numeric constant is a Long — the type libraries declare them I4 and VB6 reports
                    // TypeName "Long" even for a value of 2. A string constant (the VBA.Constants family)
                    // keeps its text, control characters and all: vbTab really is a tab.
                    var v = m.Value.ValueKind == JsonValueKind.Number
                        ? new Vb6Value(m.Value.GetInt64())
                        : new Vb6Value(m.Value.GetString());
                    members[m.Name] = v;
                    if (!bare.ContainsKey(m.Name)) bare[m.Name] = v;
                }

                var c = new Container(lib.Name, cont.Name, isEnum, members);
                containers.Add(c);
                // All 79 container names are unique across the four libraries (asserted by a test), so a
                // single flat index serves the unqualified `Enum.Member` form.
                byContainer[cont.Name] = c;
            }
            byLibrary[lib.Name] = containers;
        }

        return new Model(order, byLibrary, byContainer, bare);
    }

    /// <summary>The four library names, in the precedence a bare name resolves through.</summary>
    internal static IReadOnlyList<string> LibraryOrder => model.Value.LibraryOrder;

    internal static bool IsLibrary(string name) => model.Value.ByLibrary.ContainsKey(name);

    /// <summary>An enum or constant-module name, from any of the four libraries.</summary>
    internal static bool IsContainer(string name) => model.Value.ByContainer.ContainsKey(name);

    internal static bool TryContainer(string name, out Container container)
        => model.Value.ByContainer.TryGetValue(name, out container!);

    /// <summary>A bare, unqualified member — resolved in library precedence order, first wins.</summary>
    internal static bool TryBare(string member, out Vb6Value value)
        => model.Value.Bare.TryGetValue(member, out value);

    /// <summary>A member reached through its library, with the container level skipped:
    /// <c>VBRUN.vbCancel</c>. Searches only that library, which is what makes it answer 0 where the
    /// bare form answers 2.</summary>
    internal static bool TryInLibrary(string library, string member, out Vb6Value value)
    {
        if (model.Value.ByLibrary.TryGetValue(library, out var containers))
            foreach (var c in containers)
                if (c.Members.TryGetValue(member, out value))
                    return true;
        value = default;
        return false;
    }

    /// <summary>A member reached through its enum or module: <c>DragConstants.vbCancel</c>. Strict — a
    /// member that belongs to a different container is refused, not skipped.</summary>
    internal static bool TryInContainer(string container, string member, out Vb6Value value)
    {
        if (model.Value.ByContainer.TryGetValue(container, out var c))
            return c.Members.TryGetValue(member, out value);
        value = default;
        return false;
    }

    /// <summary>The full <c>Lib.Container.Member</c> form. Strict on BOTH levels: the container must
    /// actually belong to that library, so <c>VBRUN.VbMsgBoxResult.vbCancel</c> is refused.</summary>
    internal static bool TryInLibraryContainer(string library, string container, string member,
        out Vb6Value value)
    {
        if (model.Value.ByContainer.TryGetValue(container, out var c)
            && c.Library.Equals(library, StringComparison.OrdinalIgnoreCase))
            return c.Members.TryGetValue(member, out value);
        value = default;
        return false;
    }

    /// <summary>Is this container declared by that library? Used to refuse a mismatched middle segment
    /// rather than step over it.</summary>
    internal static bool ContainerBelongsTo(string library, string container)
        => model.Value.ByContainer.TryGetValue(container, out var c)
           && c.Library.Equals(library, StringComparison.OrdinalIgnoreCase);

    /// <summary>An in-box ENUM usable in type position — <c>Dim x As VbMsgBoxResult</c>. Constant
    /// modules are excluded: <c>Dim x As Constants</c> is illegal ("Automation type not supported").</summary>
    internal static bool TryEnumType(string name, out Container container)
        => model.Value.ByContainer.TryGetValue(name, out container!) && container.IsEnum;

    /// <summary>The same, library-qualified: <c>Dim x As VBA.VbMsgBoxResult</c>, measured legal.</summary>
    internal static bool TryEnumType(string library, string name, out Container container)
        => TryEnumType(name, out container)
           && container.Library.Equals(library, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every container, for the tests that assert the data matches the catalogue.</summary>
    internal static IEnumerable<Container> AllContainers =>
        model.Value.ByLibrary.Values.SelectMany(cs => cs);
}
