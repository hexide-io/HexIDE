using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HexIDE.Runtime.Interpreter;

internal sealed record TerminatedMemberAccess(string Module, string Member, int Line);

public partial class BasicInterpreter
{
    private StatementExecutor? topLevelExecutor;
    internal long TerminatedMemberAccessCount { get; private set; }
    private List<TerminatedMemberAccess>? terminatedMemberAccesses;
    internal IReadOnlyList<TerminatedMemberAccess> TerminatedMemberAccesses =>
        terminatedMemberAccesses ?? (IReadOnlyList<TerminatedMemberAccess>)Array.Empty<TerminatedMemberAccess>();

    // Measurement hook for #4375's suite probe; no reporting or allocation on the live-instance path.
    internal static Action<TerminatedMemberAccess>? TerminatedMemberAccessObserved;
    private sealed record TerminatingInstance(VbObject Instance, TerminatingInstance? Parent)
    {
        internal bool Active = true;
    }
    private readonly AsyncLocal<TerminatingInstance?> terminatingInstance = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void NoteMemberAccess(VbObject instance, string member, string? module = null, int line = 0)
    {
        if (instance.Terminated)
            NoteTerminatedMemberAccess(instance, member, module, line);
    }

    private void NoteTerminatedMemberAccess(VbObject instance, string member, string? module, int line)
    {
        // The flag is set before the hook. Its own accesses (including helpers it calls) are legitimate.
        for (var hook = terminatingInstance.Value; hook is not null; hook = hook.Parent)
            if (hook.Active && ReferenceEquals(hook.Instance, instance)) return;
        var frame = activationStack.Count > 0 ? activationStack[^1].ToCallStackFrame() : topLevelExecutor?.ToCallStackFrame();
        var hit = new TerminatedMemberAccess(module ?? frame?.Module ?? instance.ClassName,
            member, line != 0 ? line : frame?.Line ?? 0);
        TerminatedMemberAccessCount++;
        if (TerminatedMemberAccessCount <= 20)
            (terminatedMemberAccesses ??= []).Add(hit);
        TerminatedMemberAccessObserved?.Invoke(hit);
    }
}
