using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>The kind of a loaded module. Standard modules are shared singletons; class modules are
/// instantiable templates (used from Phase 3 of interpreter-advanced).</summary>
public enum InterpreterModuleKind
{
    Standard,
    Class,
}

/// <summary>
/// One module loaded into the interpreter's project-wide registry — its declared procedures (via
/// <see cref="PrePass"/>) and its own module-level scope (<see cref="ModuleEnv"/>). Standard modules are
/// singletons: their module-level state persists in shared <see cref="ExecutionState"/> slots, addressed
/// through this env. All modules in a program share one <see cref="ExecutionState"/> but keep separate envs.
/// </summary>
public sealed class ModuleInfo
{
    public string Name { get; }
    public InterpreterModuleKind Kind { get; }
    public PrePass PrePass { get; }
    public ExecutionEnvironment ModuleEnv { get; }

    public ModuleInfo(string name, InterpreterModuleKind kind, PrePass prePass, ExecutionEnvironment moduleEnv)
    {
        Name = name;
        Kind = kind;
        PrePass = prePass;
        ModuleEnv = moduleEnv;
    }
}

/// <summary>
/// The project-wide runtime registry of modules the interpreter resolves names against <em>at execution
/// time</em>. This is execution machinery (what any interpreter has), not a static/bound symbol model — no
/// pre-execution analysis, no type inference (see the interpreter-advanced spec's scope boundary).
/// </summary>
public sealed class ModuleRegistry
{
    private readonly Dictionary<string, ModuleInfo> modules = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ModuleInfo> ordered = new();

    public IReadOnlyList<ModuleInfo> All => ordered;

    public void Add(ModuleInfo module)
    {
        modules[module.Name] = module;
        ordered.Add(module);
    }

    public bool TryGet(string name, out ModuleInfo module) => modules.TryGetValue(name, out module!);

    public bool Contains(string name) => modules.ContainsKey(name);
}
