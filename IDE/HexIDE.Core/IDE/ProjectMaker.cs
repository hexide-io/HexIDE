using HexIDE.Lsp.Messages;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.IDE;

/// <summary>
/// What makes programs of one project type (#3411 slice 2.8, #5621): File > Make, Make Project Group, and the type's own
/// toolchain where it has one, which for VB6's type is Make with VB6 and Run > Start with VB6. The owner whose grant names
/// the type registers it through <see cref="IProjectMakerRegistration"/>, and with none registered, none of them is offered.
/// </summary>
/// <remarks>
/// <para>
/// <b>Internal for phase 2.</b> It has one implementation, the Basic provider's, and its toolchain half is VB6-shaped.
/// Its natural public home is <c>AddinProjectType</c>, which the provider registers VB6's type with (#5625). tasks.md 3.1
/// moves it there, with its PublicAPI baseline.
/// </para>
/// <para>
/// <b>The IDE keeps what a user meets:</b> every dialog, the picker, the refusals, writing the sources through the type's
/// format, and what a make reports. A maker answers questions and does the making, and a member that throws is contained:
/// a make reports failure, and a question gets the answer it would with no maker.
/// </para>
/// </remarks>
internal interface IProjectMaker
{
    /// <summary>
    /// The type it makes, as a project's <see cref="ProjectDefinition.ContributedBy"/> names it. A project of any other type
    /// never reaches it.
    /// </summary>
    string ProjectTypeId { get; }

    /// <summary>
    /// The field that would put a program made with no picker outside its folder, and what it holds, or null when its
    /// name is a plain file name (#5622).
    /// </summary>
    (string Field, string Value)? RefusedProgramName(ProjectDefinition project);

    /// <summary>Where a make with no picker puts the program. Ask <see cref="RefusedProgramName"/> first.</summary>
    string ProgramPath(ProjectDefinition project);

    /// <summary>Why this machine can't make <paramref name="program"/>, or null when it can.</summary>
    MakeRefusal? Unavailable(string program);

    /// <summary>The files a make of <paramref name="program"/> writes beside it, which the IDE asks about replacing.</summary>
    IReadOnlyList<string> WrittenBeside(string program);

    /// <summary>
    /// Makes <paramref name="program"/>, and the files <see cref="WrittenBeside"/> names, from the project's sources. The IDE
    /// has written them into <paramref name="sources"/>, with the project's members pointing there, and the project file
    /// as <paramref name="projectFile"/>. It throws when it can't.
    /// </summary>
    void Make(ProjectDefinition project, string sources, string projectFile, string program);

    /// <summary>Its type's own toolchain, or null when the type has none.</summary>
    IProjectToolchain? Toolchain { get; }
}

/// <summary>
/// Why a make can't start: one of the reasons the IDE knows how to say, with what it names. The IDE words the box and the
/// make's outcome itself, from its own strings, so a maker never chooses what an IDE message says (#5621).
/// </summary>
internal sealed record MakeRefusal(MakeRefusalReason Reason, string Folder = "", string Runner = "");

/// <summary>The reasons a maker can give for not making a program (#5621).</summary>
internal enum MakeRefusalReason
{
    /// <summary>The maker has nothing to make a program with on this platform.</summary>
    UnsupportedPlatform,

    /// <summary>What it makes a program with, <see cref="MakeRefusal.Runner"/>, isn't in <see cref="MakeRefusal.Folder"/>.</summary>
    RuntimeNotFound,
}

/// <summary>
/// A project type's own toolchain (#5621): for VB6's, <c>VB6.EXE /make</c>. The IDE asks the user, shows the compiler's
/// diagnostics, and starts what it made; the toolchain finds its compiler and runs it.
/// </summary>
internal interface IProjectToolchain
{
    /// <summary>
    /// Whether the compiler is installed. Where it is comes from the user's machine, never from a project: for VB6, the
    /// <c>VB6_EXE</c> variable, then VB6's install folders.
    /// </summary>
    bool IsInstalled { get; }

    /// <summary>
    /// Builds <paramref name="project"/> into <paramref name="program"/>, or returns null when the build was stopped for
    /// running too long. <paramref name="keepRecovery"/> is told the path of a copy of the previous program the build
    /// kept, when it couldn't put it back.
    /// </summary>
    Task<ToolchainBuild?> BuildAsync(ProjectDefinition project, string program, Action<string> keepRecovery);

    /// <summary>The diagnostics a build's output reports, each with the document it's in.</summary>
    IReadOnlyList<(string Uri, Diagnostic Diagnostic)> Diagnostics(string output, ProjectDefinition project);
}

/// <summary>What one build reported.</summary>
/// <param name="ExitCode">Zero when the compiler produced a program.</param>
/// <param name="Output">What it wrote, joined for <see cref="IProjectToolchain.Diagnostics"/>.</param>
internal sealed record ToolchainBuild(int ExitCode, string Output);

/// <summary>
/// The host member a maker registers through (#5621). Internal for phase 2, as <see cref="IProjectMaker"/> is: the owned
/// host an add-in is given implements it, and the IDE's own host refuses it.
/// </summary>
/// <remarks>
/// Internal visibility isn't the security gate, since reflection reaches internals. The implementation checks the
/// caller's grant itself, refuses a second maker for a type already served, and withdraws a maker when its owner is
/// disabled.
/// </remarks>
internal interface IProjectMakerRegistration
{
    /// <summary>Registers <paramref name="maker"/> until the returned handle is disposed.</summary>
    /// <exception cref="InvalidOperationException">
    /// The caller's grant doesn't name the maker's type, another owner's maker serves it already, or this host isn't one
    /// an add-in is given. The message names both owners where there are two.
    /// </exception>
    IDisposable RegisterProjectMaker(IProjectMaker maker);
}
