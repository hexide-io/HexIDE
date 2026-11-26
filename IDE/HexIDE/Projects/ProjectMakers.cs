using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.Projects;

/// <summary>
/// The makers registered for project types (#3411 slice 2.8, #5621). File > Make, Make Project Group, Make with VB6 and
/// Start with VB6 are offered for a project only while a maker for its type is registered here.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deny by default.</b> An owner may register a maker only for a type its grant names
/// (<see cref="RegistrationGrant.MakesProjectTypes"/>), checked here rather than at the host, so a caller that reaches
/// the internal host member by reflection gains nothing. Today that's the Basic provider, for VB6's type.
/// </para>
/// <para>
/// <b>One per type.</b> While a maker serves a type, another for it is refused, naming both owners, so nothing replaces a
/// maker silently.
/// </para>
/// <para>
/// <b>Its code is contained.</b> What the IDE asks a maker (<see cref="For"/>) goes through a wrapper that logs a throw
/// naming its owner. A question gets the answer it would with no maker (<see cref="Call{T}"/>); a make or a build fails,
/// and the IDE reports it as failed and stays usable.
/// </para>
/// <para>
/// <b>Withdrawal never waits.</b> A handle is disposed when its owner is disabled or its consent revoked, under the add-in
/// registry's lock, from whichever thread did that. Disposing only marks the maker withdrawn, so nothing asks it again,
/// and posts the rest to the UI thread (<see cref="Withdraw"/>), as the run engine's does (#5616).
/// </para>
/// </remarks>
internal sealed class ProjectMakers
{
    /// <summary>Who may register a maker for which types. HexIDE's own table; a test gives its own owners grants here.</summary>
    internal IReadOnlyList<RegistrationGrant> Grants { get; init; } = RegistrationGrants.All;

    private readonly object gate = new();
    private readonly Dictionary<string, Registration> byType = new(StringComparer.Ordinal);

    /// <summary>A maker was registered or let go. Raised on the UI thread, never where the change happened.</summary>
    internal event Action? Changed;

    /// <summary>Registers <paramref name="maker"/> for its type, on behalf of <paramref name="origin"/>.</summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="origin"/> holds no grant to make projects, the maker's type isn't one its grant names or can't be
    /// read, or another owner's maker serves that type.
    /// </exception>
    internal IDisposable Register(IProjectMaker maker, AddinOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(maker);
        // Before the maker is asked anything: a refused owner's code never runs.
        if (RegistrationGrants.For(origin, Grants) is not { MakesProjectTypes.Count: > 0 } grant)
        {
            Log.Warning("Project makers: refused {Addin} ({AddinId}): it holds no grant to make projects", origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets make projects.");
        }

        string type;
        try
        {
            type = maker.ProjectTypeId;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Project makers: refused {Addin} ({AddinId}): its maker's project type couldn't be read", origin.Title, origin.Id);
            throw new InvalidOperationException($"{origin.Title}'s maker didn't say which projects it makes.", ex);
        }
        if (string.IsNullOrEmpty(type) || !grant.MakesProjectType(type))
        {
            Log.Warning("Project makers: refused {Addin} ({AddinId}): it holds no grant to make {Type} projects", origin.Title,
                origin.Id, type);
            throw new InvalidOperationException($"{origin.Title} isn't one HexIDE lets make {type} projects.");
        }

        Registration registration;
        lock (gate)
        {
            if (byType.TryGetValue(type, out var held))
            {
                Log.Warning("Project makers: refused {Addin} ({AddinId}): {Owner} ({OwnerId}) makes {Type} projects already",
                    origin.Title, origin.Id, held.Owner.Title, held.Owner.Id, type);
                throw new InvalidOperationException(
                    $"{origin.Title} can't make {type} projects: {held.Owner.Title} ({held.Owner.Id}) makes them already.");
            }
            byType[type] = registration = new Registration(this, type, origin, maker);
        }

        Log.Information("Project makers: {Addin} ({AddinId}) makes {Type} projects", origin.Title, origin.Id, type);
        PostChanged();
        return registration;
    }

    /// <summary>
    /// The maker for <paramref name="project"/>'s type, wrapped so what it throws is contained, or null when none is
    /// registered or it's being withdrawn.
    /// </summary>
    internal IProjectMaker? For(ProjectDefinition? project) => RegistrationFor(project) is { } registration
        ? new ContainedMaker(registration)
        : null;

    /// <summary>Whether a maker is registered for <paramref name="project"/>'s type.</summary>
    internal bool Makes(ProjectDefinition? project) => RegistrationFor(project) is not null;

    /// <summary>
    /// The toolchain of <paramref name="project"/>'s type, when it has one and its compiler is installed here, or null. A
    /// maker that throws while asked is logged and answered as having none.
    /// </summary>
    internal IProjectToolchain? InstalledToolchainFor(ProjectDefinition? project) =>
        For(project)?.Toolchain is { IsInstalled: true } toolchain ? toolchain : null;

    private Registration? RegistrationFor(ProjectDefinition? project)
    {
        if (project is null)
            return null;
        // A project that names no type is one HexIDE made itself, which it reads as VB6's, as ProjectService.TypeOf does.
        var type = project.ContributedBy ?? Vb6ProjectType.Id;
        lock (gate)
            return byType.TryGetValue(type, out var registration) && !registration.WithdrawingUnderLock ? registration : null;
    }

    // On the UI thread, with no lock held: the maker is let go, what's offered is asked again, and a maker that's
    // IDisposable is disposed.
    private void Withdraw(Registration registration)
    {
        lock (gate)
        {
            if (byType.TryGetValue(registration.Type, out var current) && ReferenceEquals(current, registration))
                byType.Remove(registration.Type);
        }
        Changed?.Invoke();
        if (registration.Maker is IDisposable disposable)
            Call(registration, () => { disposable.Dispose(); return true; }, false, nameof(IDisposable.Dispose));
        Log.Information("Project makers: {Addin} ({AddinId}) no longer makes {Type} projects", registration.Owner.Title,
            registration.Owner.Id, registration.Type);
    }

    private void PostChanged() => Dispatcher.UIThread.Post(() => Changed?.Invoke());

    /// <summary>
    /// Asks the maker, containing what it throws: logged, naming its owner, and answered with <paramref name="fallback"/>,
    /// what the IDE answers with no maker.
    /// </summary>
    internal static T Call<T>(Registration registration, Func<T> call, T fallback, string member)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Project makers: {Member} threw in the maker of {Addin} ({AddinId}); answered as with no maker",
                member, registration.Owner.Title, registration.Owner.Id);
            return fallback;
        }
    }

    /// <summary>
    /// Has the maker do something a make or a build needs, logging what it throws, naming its owner, and letting it go on
    /// to the caller, which reports the make as failed.
    /// </summary>
    internal static T Failing<T>(Registration registration, Func<T> call, string member)
    {
        try
        {
            return call();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Project makers: {Member} threw in the maker of {Addin} ({AddinId}); the make failed",
                member, registration.Owner.Title, registration.Owner.Id);
            throw;
        }
    }

    /// <summary>One maker's registration, and the handle that withdraws it.</summary>
    internal sealed class Registration(ProjectMakers makers, string type, AddinOrigin owner, IProjectMaker maker) : IDisposable
    {
        private bool withdrawing;

        internal string Type => type;
        internal AddinOrigin Owner => owner;
        internal IProjectMaker Maker => maker;

        /// <summary>Whether it's being withdrawn: nothing asks it again from the moment its handle is disposed.</summary>
        internal bool Withdrawing
        {
            get { lock (makers.gate) return withdrawing; }
        }

        internal bool WithdrawingUnderLock => withdrawing;

        public void Dispose()
        {
            lock (makers.gate)
            {
                if (withdrawing)
                    return;
                withdrawing = true;
            }
            // Never waits: the registry disposes this under its lock, from whatever thread disables the owner.
            Dispatcher.UIThread.Post(() => makers.Withdraw(this));
        }
    }

    // What the IDE holds of a maker: every call goes through the containment above.
    private sealed class ContainedMaker(Registration registration) : IProjectMaker
    {
        private IProjectMaker Inner => registration.Maker;

        public string ProjectTypeId => registration.Type;

        public (string Field, string Value)? RefusedProgramName(ProjectDefinition project) =>
            Failing(registration, () => Inner.RefusedProgramName(project), nameof(RefusedProgramName));

        public string ProgramPath(ProjectDefinition project) =>
            Failing(registration, () => Inner.ProgramPath(project), nameof(ProgramPath));

        public MakeRefusal? Unavailable(string program) =>
            Failing(registration, () => Inner.Unavailable(program), nameof(Unavailable));

        public IReadOnlyList<string> WrittenBeside(string program) =>
            Failing(registration, () => Inner.WrittenBeside(program), nameof(WrittenBeside));

        public void Make(ProjectDefinition project, string sources, string projectFile, string program) =>
            Failing(registration, () => { Inner.Make(project, sources, projectFile, program); return true; }, nameof(Make));

        public IProjectToolchain? Toolchain =>
            Call(registration, () => Inner.Toolchain, null, nameof(Toolchain)) is { } toolchain
                ? new ContainedToolchain(registration, toolchain)
                : null;
    }

    private sealed class ContainedToolchain(Registration registration, IProjectToolchain inner) : IProjectToolchain
    {
        public bool IsInstalled => Call(registration, () => inner.IsInstalled, false, "Toolchain.IsInstalled");

        public async Task<ToolchainBuild?> BuildAsync(ProjectDefinition project, string program, Action<string> keepRecovery)
        {
            try
            {
                return await inner.BuildAsync(project, program, keepRecovery);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Project makers: Toolchain.BuildAsync threw in the maker of {Addin} ({AddinId}); the build failed",
                    registration.Owner.Title, registration.Owner.Id);
                throw;
            }
        }

        public IReadOnlyList<(string Uri, Diagnostic Diagnostic)> Diagnostics(string output, ProjectDefinition project) =>
            Failing(registration, () => inner.Diagnostics(output, project), "Toolchain.Diagnostics");
    }
}
