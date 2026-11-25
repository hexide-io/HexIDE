using System.IO.Compression;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Making;

/// <summary>
/// What makes programs of VB6's projects, which the Basic provider registers through its host (#3411 slice 2.8, #5621).
/// </summary>
/// <remarks>
/// <para>
/// <b>File > Make</b> makes the provider's standalone runner into the program: the runner, renamed, beside an archive
/// holding the project's sources, re-encoded as UTF-8 so the program reads the same text on any host code page (#731).
/// The runner is one self-contained file per platform, in <c>standalone/</c> beside the IDE (#1971).
/// </para>
/// <para>
/// <b>Its toolchain is VB6's own</b>, <see cref="Vb6Toolchain"/>, for Make with VB6 and Run > Start with VB6.
/// </para>
/// <para>
/// Moved from the IDE's <c>ProjectService</c>, which keeps the picker, the refusals and writing the sources.
/// </para>
/// </remarks>
internal sealed class Vb6ProjectMaker(IProjectToolchain? toolchain = null) : IProjectMaker
{
    /// <summary>VB6's project type, as the IDE registers it and its projects carry it.</summary>
    internal const string Vb6ProjectTypeId = "hexide.vb6";

    public string ProjectTypeId => Vb6ProjectTypeId;

    public IProjectToolchain? Toolchain { get; } = toolchain ?? new Vb6Toolchain();

    /// <summary>The folder the runner is found in: the IDE's own, whatever the current directory is (#1947).</summary>
    internal string RuntimeDirectory { get; init; } = AppContext.BaseDirectory;

    /// <summary>
    /// The runner a made program is, in <c>standalone/</c>: one self-contained single-file executable per platform
    /// (#1971). Null where none is built.
    /// </summary>
    internal static string? StandaloneRunnerName() =>
        OperatingSystem.IsWindows() ? "HexIDE.Standalone.exe"
        : OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ? "HexIDE.Standalone"
        : null;

    public (string Field, string Value)? RefusedProgramName(ProjectDefinition project) => ProjectOutputPath.RefusedName(project);

    public string ProgramPath(ProjectDefinition project) => ProjectOutputPath.OutputExePath(project);

    public MakeRefusal? Unavailable(string program)
    {
        if (StandaloneRunnerName() is not { } runnerName)
            return new MakeRefusal(MakeRefusalReason.UnsupportedPlatform);

        var folder = Path.Join(RuntimeDirectory, "standalone");
        // A Debug build has none until HexIDE.Standalone is published into it.
        return File.Exists(Path.Join(folder, runnerName))
            ? null
            : new MakeRefusal(MakeRefusalReason.RuntimeNotFound, folder, runnerName);
    }

    // The picker only confirms the executable; the interpreter's archive is a separate output (#2504).
    public IReadOnlyList<string> WrittenBeside(string program) => [Path.ChangeExtension(program, "dll")];

    public void Make(ProjectDefinition project, string sources, string projectFile, string program)
    {
        var runner = StandaloneRunnerName() is { } runnerName ? Path.Join(RuntimeDirectory, "standalone", runnerName) : null;
        if (runner is null || !File.Exists(runner))
            throw new InvalidOperationException("The standalone runner isn't there to make a program of.");

        EncodePackageSources(project, projectFile);

        // This is not a .dll file, but it will look better :wink: :wink:
        AtomicFileWriter.Write(WrittenBeside(program)[0], stream => ZipFile.CreateFromDirectory(sources, stream));

        // The program is the runner, renamed, beside its project archive. The runner is one self-contained file holding .NET,
        // its own assemblies and the native libraries, so nothing else goes with it (#1971). It used to be the runner's
        // apphost and three native libraries, which ran only while the runner was compiled AOT; since #30 the apphost has
        // needed HexIDE.Standalone.dll beside it, and no made program started.
        AtomicFileWriter.Copy(runner, program);
    }

    // Only the disposable copies: a made program must read the same text on any host code page (#731).
    internal static void EncodePackageSources(ProjectDefinition project, string projectFile)
    {
        foreach (var form in project.Forms)
            Convert(form.AbsolutePath!, form.TextEncoding);
        foreach (var module in project.Modules)
            Convert(module.AbsolutePath!, module.TextEncoding);
        Convert(projectFile, project.TextEncoding);

        static void Convert(string path, Vb6TextEncoding encoding)
        {
            var text = Vb6TextFile.Decode(File.ReadAllBytes(path), encoding);
            AtomicFileWriter.WriteAllBytes(path, Vb6TextFile.Encode(text, Vb6TextEncoding.Utf8WithBom));
        }
    }
}
