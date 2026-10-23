using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HexIDE.Automation;

/// <summary>
/// Which view-model methods compiled XAML binds as a Command, and with how many parameters (#836 review).
/// </summary>
/// <remarks>
/// <para>
/// Avalonia compiles <c>Command="{CompiledBinding DeleteForm}"</c> into a static trampoline in the assembly that holds
/// the XAML, on a type named <c>XamlIlTrampolines</c>, named
/// <c>{assembly}:{type}+{method}_{arity}!CommandExecuteTrampoline</c>. Those names are exactly what a person can reach,
/// so invoke_command calls and inspect_element lists those methods and no others. A filter by shape reached about
/// thirty more: <c>OnInitialized</c>, every Options page's <c>SaveToSettings</c>, a <c>MakeProject</c> that skips
/// the guard on the <c>MakeProjectCommand</c> the menu binds.
/// </para>
/// <para>
/// The names are Avalonia's own and could change with it. <c>XamlCommandBindingsTests</c> reads them from HexIDE's
/// assembly and fails if the Project Explorer's Remove is no longer found, rather than letting every method silently
/// drop out.
/// </para>
/// </remarks>
internal static class XamlCommandBindings
{
    private const string ExecuteSuffix = "!CommandExecuteTrampoline";

    private static readonly ConcurrentDictionary<Assembly, HashSet<string>> Compiled = new();
    private static readonly ConcurrentDictionary<string, byte> Registered = new(StringComparer.Ordinal);

    /// <summary>
    /// The arity XAML binds <paramref name="method"/> of <paramref name="type"/> or one of its bases with, or null
    /// when no Command is bound to it.
    /// </summary>
    public static int? Arity(Type type, string method) => Arity(type, method, out _);

    /// <summary>Also returns the type named by the matched compiled binding.</summary>
    public static int? Arity(Type type, string method, out Type? boundType)
    {
        for (var t = type; t is not null; t = t.BaseType)
            foreach (var arity in new[] { 0, 1 })
                if (IsBound(Key(t, method, arity)))
                {
                    boundType = t;
                    return arity;
                }
        boundType = null;
        return null;
    }

    /// <summary>For a test, whose view models no XAML binds: registers one binding as compiled XAML would.</summary>
    internal static void Register(Type type, string method, int arity) => Registered[Key(type, method, arity)] = 0;

    /// <summary>The bindings read from one assembly's trampolines, as "{type}+{method}_{arity}".</summary>
    internal static IReadOnlySet<string> In(Assembly assembly) => Compiled.GetOrAdd(assembly, Read);

    private static string Key(Type type, string method, int arity) => $"{type.FullName}+{method}_{arity}";

    private static bool IsBound(string key) =>
        Registered.ContainsKey(key)
        || AppDomain.CurrentDomain.GetAssemblies().Any(a => !a.IsDynamic && In(a).Contains(key));

    private static HashSet<string> Read(Assembly assembly)
    {
        var bindings = new HashSet<string>(StringComparer.Ordinal);
        // Only an assembly that compiled XAML has trampolines, and it references the XAML runtime; the rest are skipped
        // without loading their types.
        if (assembly.GetReferencedAssemblies().All(r => r.Name != "Avalonia.Markup.Xaml")) return bindings;
        Type?[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types; }
        catch (Exception) { return bindings; }

        foreach (var trampolines in types.Where(t => t?.Name == "XamlIlTrampolines"))
        {
            foreach (var m in trampolines!.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!m.Name.EndsWith(ExecuteSuffix, StringComparison.Ordinal)) continue;
                var colon = m.Name.IndexOf(':');
                bindings.Add(m.Name[(colon + 1)..^ExecuteSuffix.Length]);
            }
        }
        return bindings;
    }
}
