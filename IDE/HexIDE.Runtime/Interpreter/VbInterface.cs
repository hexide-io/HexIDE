using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// The rules for <c>Implements</c> — how a class claims an interface, what that obliges it to supply, and
/// which members are reachable through an interface-typed reference.
///
/// Every rule here is measured against vb6.exe; see "Implements, and interface-typed variables" in
/// docs/vb6-fidelity-oracle.md. The two that are easy to get wrong: <c>TypeName</c> of an interface-typed
/// variable reports the CONCRETE class, and the <c>Private</c> on <c>IFoo_Draw</c> is convention, not a rule.
///
/// VB6 checks conformance at compile time. HexIDE has no compile step and no binder, so it checks at first
/// instantiation instead — the approximation rule in CLAUDE.md: same error, same message, later moment. Both
/// member tables are already collected by <see cref="PrePass"/>, so the check reads two lookups and relates
/// nothing at parse time.
/// </summary>
internal static class VbInterface
{
    /// <summary>VB6 names an interface implementation <c>Interface_Member</c> — the only mangling there is.</summary>
    internal static string MangledName(string interfaceName, string memberName) => interfaceName + "_" + memberName;

    /// <summary>
    /// The name to look up on <paramref name="target"/>'s own class for <paramref name="memberName"/>, given
    /// the static type the reference was read through, or null when the member is not reachable that way.
    ///
    /// A reference read through its own class (or through a Variant, or through <c>Object</c>) reaches
    /// everything the class declares. One read through an interface reaches ONLY that interface's members —
    /// measured: <c>x.Own</c> where <c>x As IFoo</c> is "Method or data member not found", even though the
    /// object really does have an <c>Own</c>.
    /// </summary>
    internal static string? ResolveMember(Vb6Value reference, VbObject target, string memberName)
    {
        var view = reference.DeclaredAs;
        if (view == null
            || string.Equals(view, target.ClassName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(view, "Object", StringComparison.OrdinalIgnoreCase))
            return memberName;

        // Read through a name declared As some OTHER class. If the object implements it, the interface's
        // members are what is reachable; if it does not, nothing is (a Set of a non-implementer is refused
        // upstream, so this is the residual case — e.g. a slot seeded to Nothing and never Set).
        if (!Implements(target.ClassDef, view))
            return null;
        var mangled = MangledName(view, memberName);
        return Declares(target.ClassDef, mangled) ? mangled : null;
    }

    /// <summary>Whether <paramref name="classDef"/> claims <paramref name="interfaceName"/> via <c>Implements</c>.</summary>
    internal static bool Implements(ModuleInfo classDef, string interfaceName)
    {
        foreach (var name in classDef.PrePass.Implemented)
            if (string.Equals(name, interfaceName, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>Whether a reference to <paramref name="target"/> may be stored in a slot declared
    /// <c>As <paramref name="declaredClass"/></c>. Its own class, or any interface it implements — anything
    /// else is Err 13 "Type mismatch" at the Set, measured.</summary>
    internal static bool IsAssignableTo(VbObject target, string declaredClass)
        => string.Equals(target.ClassName, declaredClass, StringComparison.OrdinalIgnoreCase)
           || string.Equals(declaredClass, "Object", StringComparison.OrdinalIgnoreCase)
           || Implements(target.ClassDef, declaredClass);

    private static bool Declares(ModuleInfo classDef, string memberName)
        => classDef.PrePass.Procedures.ContainsKey(memberName) || classDef.PrePass.Properties.ContainsKey(memberName);

    /// <summary>
    /// Every member a class claiming <paramref name="interfaceDef"/> must supply. Public procedures and
    /// properties, minus the lifecycle hooks — <c>Class_Initialize</c>/<c>Class_Terminate</c> belong to the
    /// interface module as a class, not to the contract it defines.
    /// </summary>
    internal static IEnumerable<string> RequiredMembers(ModuleInfo interfaceDef)
    {
        foreach (var (name, proc) in interfaceDef.PrePass.Procedures)
            if (!proc.IsPrivate && !IsLifecycleHook(name))
                yield return name;
        foreach (var (name, _) in interfaceDef.PrePass.Properties)
            if (!IsLifecycleHook(name))
                yield return name;
    }

    private static bool IsLifecycleHook(string name)
        => string.Equals(name, "Class_Initialize", StringComparison.OrdinalIgnoreCase)
           || string.Equals(name, "Class_Terminate", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verify every interface <paramref name="classDef"/> claims, raising VB6's own message for the first
    /// member it fails to supply. Called at first instantiation and memoised by the caller — a class that is
    /// never instantiated is never checked, which is the accepted divergence (VB6 would have refused to
    /// build at all).
    /// </summary>
    internal static void VerifyConformance(ModuleInfo classDef, ModuleRegistry modules)
    {
        foreach (var interfaceName in classDef.PrePass.Implemented)
        {
            if (!modules.TryGet(interfaceName, out var interfaceDef) || interfaceDef.Kind != InterpreterModuleKind.Class)
                throw new VBCompileErrorException("User-defined type not defined: " + interfaceName);
            foreach (var member in RequiredMembers(interfaceDef))
                if (!Declares(classDef, MangledName(interfaceName, member)))
                    // Verbatim VB6, which reports this against the CLASS and names both halves.
                    throw new VBCompileErrorException(
                        $"Object module needs to implement '{member}' for interface '{interfaceName}'");
        }
    }
}
