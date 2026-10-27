using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// Reads a <c>.vbp</c> and every member it names into a <see cref="ProjectDefinition"/>: its UserControls, forms, standard
/// and class modules, property pages and carried documents, its startup object and its references. One loader for both
/// hosts that start from a project file (#1062): the IDE's open, which keeps its own record of what it read through
/// <see cref="FileRead"/>, and the Standalone host a made program runs in, which used to read only the forms and add none
/// of them to the project it ran.
/// </summary>
/// <remarks>
/// A member that fails to load costs only itself: the error goes to the sink, and the member's line is kept in
/// <see cref="ProjectDefinition.PreservedItemLines"/>, so a save writes it back rather than dropping it from the project.
/// </remarks>
public sealed class ProjectLoader
{
    /// <summary>The code page a file without a byte-order mark is read in.</summary>
    public Vb6TextEncoding HostAnsi { get; init; } = Vb6TextEncoding.Ansi;

    /// <summary>Each member file read, and each companion binary, with its bytes as they were on disk. Not the project file.</summary>
    public Action<string, byte[]>? FileRead { get; init; }

    /// <summary>What a load produced.</summary>
    /// <param name="Project">The project, with every member that loaded.</param>
    /// <param name="Serialized">The project file as it was read, for what a caller reports beyond the project itself.</param>
    /// <param name="ReadByteForByte">
    /// The files the host's code page couldn't decode, which were read byte for byte instead: the project file by its name,
    /// a member by its path as the project file gives it.
    /// </param>
    public sealed record Result(ProjectDefinition Project, SerializedProject Serialized, IReadOnlyList<string> ReadByteForByte);

    /// <summary>Load the project at <paramref name="projectPath"/>. Errors short of an unreadable project file go to the sink.</summary>
    public async Task<Result> LoadAsync(string projectPath, IDeserializeErrorSink errorSink)
    {
        var projectDeserializer = new ProjectDeserializer();
        var formDeserializer = new FormDeserializer();
        var readByteForByte = new List<string>();
        var projectDir = Path.GetDirectoryName(projectPath)!;

        var (projectText, _, projectEncoding, projectByteForByte) = await Vb6TextFile.ReadDocumentAsync(projectPath, null, HostAnsi);
        if (projectByteForByte)
            readByteForByte.Add(Path.GetFileName(projectPath));
        var serializedProject = projectDeserializer.Deserialize(projectText, errorSink);

        var project = new ProjectDefinition(serializedProject.ProjectType, serializedProject.Name ?? "Project1");
        project.AbsolutePath = projectPath;
        project.Description = serializedProject.Description ?? "";
        project.ItemLinesDirectory = projectDir;
        project.TextEncoding = projectEncoding;

        // Pass 1: .ctl files — load UserControls first so FormParts are available when building the registry
        for (var index = 0; index < serializedProject.RelativeModulePaths.Count; index++)
        {
            var (moduleName, modulePath, moduleKind) = serializedProject.RelativeModulePaths[index];
            if (moduleKind != ModuleKind.UserControl)
                continue;

            try
            {
                var moduleAbsolutePath = Path.Join(projectDir, SerializedProject.ToHostPath(modulePath));
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                var (moduleSource, _, moduleEncoding, moduleByteForByte) = await ReadSourceAsync(moduleAbsolutePath, ProjectEncodingRecords.Read(serializedProject.ExtensionTail, modulePath));
                module.TextEncoding = moduleEncoding;
                var ctxBlobs = await LoadCompanionBlobs(moduleAbsolutePath, moduleSource);
                var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink, ctxBlobs);
                if (formPart != null)
                {
                    formPart.AbsolutePath = moduleAbsolutePath;
                    module.UpdateFormPart(formPart);
                    TakeDesignerName(module, formPart);
                    // Body only. FormSerializer regenerates the Begin..End header and then appends Code
                    // verbatim, so storing the whole file here emits the header twice on every save.
                    module.UpdateCode(formPart.Code);
                }
                else
                {
                    // Unparseable .ctl: keep it verbatim so a save round-trips it unchanged (SaveModule
                    // falls through to the header-less branch when FormPart is null).
                    module.UpdateCode(moduleSource);
                }
                else
                    errorSink.LogError("Str.ProjectLoad.MemberNotFound", "Project file not found: {0}", modulePath);
                project.AddModule(module);
                // Only once it has joined: a notice that the file will save back unchanged is untrue of a
                // member that then failed to load.
                if (moduleByteForByte)
                    readByteForByte.Add(modulePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module {ModulePath}", modulePath);
                errorSink.LogError("Str.ProjectLoad.ModuleFailed", "Failed to load module {0}: {1}", modulePath, ex.Message);
                // Kept, as a missing file's line is. Not loaded, so a save cannot write its file; dropping the
                // line would have the next Save Project remove the member from the project instead.
                project.PreserveItemLine(ItemLineFor(moduleKind, moduleName, modulePath), serializedProject.ModuleLineSlots[index]);
            }
        }

        var userControlRegistry = BuildUserControlRegistry(project);

        // Pass 2: .frm files — load forms using the UC registry
        for (var index = 0; index < serializedProject.RelativeFormPaths.Count; index++)
        {
            var formPath = serializedProject.RelativeFormPaths[index];
            var formAbsolutePath = Path.Join(projectDir, SerializedProject.ToHostPath(formPath));

            try
            {
                if (!File.Exists(formAbsolutePath))
                {
                    errorSink.LogError("Str.ProjectLoad.MemberNotFound", "Project file not found: {0}", formPath);
                    // Preserve the Form= line so the missing node survives an open->save round-trip
                    // (mirrors modules, whose lines are kept when their files are absent). Not loaded into
                    // the model — Project Explorer shows the preserved entry with a missing-file marker.
                    project.PreserveItemLine($"{SerializedProject.FormKey}={formPath}", serializedProject.FormLineSlots[index]);
                    continue;
                }

                // Form text before companion: the .frm's cited offsets partition the .frx.
                var (formSource, _, formEncoding, formByteForByte) = await ReadSourceAsync(formAbsolutePath, ProjectEncodingRecords.Read(serializedProject.ExtensionTail, formPath));

                var frxBlobs = await LoadCompanionBlobs(formAbsolutePath, formSource);
                var form = formDeserializer.Deserialize(project, formSource, errorSink, frxBlobs, userControlRegistry);
                if (form != null)
                {
                    form.AbsolutePath = formAbsolutePath;
                    form.TextEncoding = formEncoding;
                    project.AddForm(form);
                    if (formByteForByte)
                        readByteForByte.Add(formPath);
                }
                else
                {
                    errorSink.LogError("Str.ProjectLoad.FormFailed", "Failed to load form {0}", formPath);
                    project.PreserveItemLine($"{SerializedProject.FormKey}={formPath}", serializedProject.FormLineSlots[index]);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load form {FormPath}", formPath);
                errorSink.LogError("Str.ProjectLoad.FormFailedWithReason", "Failed to load form {0}: {1}", formPath, ex.Message);
                // Kept, as a missing form's line is: see the module passes.
                project.PreserveItemLine($"{SerializedProject.FormKey}={formPath}", serializedProject.FormLineSlots[index]);
            }
        }

        // Pass 4: related documents — files the project carries but does not compile.
        //
        // Note what this pass does NOT do: read the file. A related document's content belongs to the
        // editor that opens it, not to project load. Reading here would mean loading every README and
        // spec in a project on open, and — worse — would hand a text buffer to machinery built for VB6
        // source, which is precisely how a non-code file ends up with an Attribute header written into it.
        for (var index = 0; index < serializedProject.RelativeRelatedDocPaths.Count; index++)
        {
            var (docName, docPath, originalItemLine) = serializedProject.RelativeRelatedDocPaths[index];
            try
            {
                var absolute = Path.Join(projectDir, SerializedProject.ToHostPath(docPath));
                project.AddRelatedDocument(
                    new RelatedDocumentDefinition(project, docName, absolute, originalItemLine)
                    {
                        OriginalItemSlot = originalItemLine is null ? null : serializedProject.RelatedDocumentLineSlots[index],
                    });
            }
            catch (Exception ex)
            {
                // A malformed path costs one entry, never the project load.
                Log.Error(ex, "Failed to load related document {DocPath}", docPath);
            }
        }

        // Pass 3: remaining modules (.bas, .cls, .pag) — UserControls already loaded in pass 1
        for (var index = 0; index < serializedProject.RelativeModulePaths.Count; index++)
        {
            var (moduleName, modulePath, moduleKind) = serializedProject.RelativeModulePaths[index];
            if (moduleKind == ModuleKind.UserControl)
                continue;

            try
            {
                var moduleAbsolutePath = Path.Join(projectDir, SerializedProject.ToHostPath(modulePath));
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                var (moduleSource, _, moduleEncoding, moduleByteForByte) = await ReadSourceAsync(moduleAbsolutePath, ProjectEncodingRecords.Read(serializedProject.ExtensionTail, modulePath));
                module.TextEncoding = moduleEncoding;
                // .bas/.cls: strip the VB6 header so Code is the body only (no-op for .pag/.ctl).
                var (preservedHeader, moduleBody) = ModuleFileFormat.SplitHeader(moduleSource, moduleKind);

                module.RecordOriginalHeader(preservedHeader);

                module.UpdateCode(moduleBody);
                if (moduleKind == ModuleKind.PropertyPage)
                {
                    var pgxBlobs = await LoadCompanionBlobs(moduleAbsolutePath, moduleSource);
                    var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink, pgxBlobs);
                    if (formPart != null)
                    {
                        formPart.AbsolutePath = moduleAbsolutePath;
                        module.UpdateFormPart(formPart);
                        TakeDesignerName(module, formPart);
                        // StripHeader above is a no-op for .pag, so Code still holds the whole file —
                        // replace it with the body only or the next save writes the header twice.
                        module.UpdateCode(formPart.Code);
                    }
                }
                else
                    errorSink.LogError("Str.ProjectLoad.MemberNotFound", "Project file not found: {0}", modulePath);
                project.AddModule(module);
                // Only once it has joined: a notice that the file will save back unchanged is untrue of a
                // member that then failed to load.
                if (moduleByteForByte)
                    readByteForByte.Add(modulePath);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module {ModulePath}", modulePath);
                errorSink.LogError("Str.ProjectLoad.ModuleFailed", "Failed to load module {0}: {1}", modulePath, ex.Message);
                // Kept, as a missing file's line is. Not loaded, so a save cannot write its file; dropping the
                // line would have the next Save Project remove the member from the project instead.
                project.PreserveItemLine(ItemLineFor(moduleKind, moduleName, modulePath), serializedProject.ModuleLineSlots[index]);
            }
        }

        // Always retain the startup form NAME so Startup= survives round-trip even when its .frm was
        // missing and no FormDefinition was created (the live StartupForm, if found, takes precedence).
        project.StartupFormName = serializedProject.StartupFormName;
        // `Startup="Sub Main"` names a procedure, not a form — the ordinary shape of a code-only Standard
        // EXE. Read before the form lookup, which would otherwise find nothing and leave the project with
        // no startup at all: the .vbp round-tripped, and F5 refused.
        if (string.Equals(serializedProject.StartupFormName, SerializedProject.SubMainStartup,
                StringComparison.OrdinalIgnoreCase))
            project.StartsAtSubMain = true;
        else if (serializedProject.StartupFormName != null)
            project.StartupForm = project.Forms.FirstOrDefault(x => x.Name == serializedProject.StartupFormName);

        foreach (var reference in serializedProject.References)
            project.AddReference(reference);

        project.UnknownPreSectionLines.AddRange(serializedProject.UnknownPreSectionLines);
        project.KnownLineKeys.AddRange(serializedProject.KnownLineKeys);
        for (var i = 0; i < serializedProject.PreservedItemLines.Count; i++)
            project.PreserveItemLine(serializedProject.PreservedItemLines[i], serializedProject.PreservedItemLineSlots[i]);
        project.ExtensionTail = serializedProject.ExtensionTail;

        return new Result(project, serializedProject, readByteForByte);
    }

    private async Task<(string Text, byte[] Bytes, Vb6TextEncoding Encoding, bool ByteForByte)> ReadSourceAsync(string path, Vb6TextEncoding? recorded)
    {
        var read = await Vb6TextFile.ReadDocumentAsync(path, null, HostAnsi, recorded);
        FileRead?.Invoke(path, read.Bytes);
        return read;
    }

    /// <summary>
    /// Read the companion binary resource file (.frx / .ctx / .pgx) beside a .frm / .ctl / .pag, if present.
    /// Always route companion reads through here — hardcoding ".frx" silently drops UserControl and
    /// PropertyPage resources, and the save path then deletes the companion it never read.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, byte[]>?> LoadCompanionBlobs(string sourceFilePath, string? citingSource = null)
    {
        var companionPath = Path.ChangeExtension(sourceFilePath, CompanionBinaryExtension(sourceFilePath));
        if (!File.Exists(companionPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(companionPath);
        FileRead?.Invoke(companionPath, bytes);
        return citingSource is null
            ? FrxDeserializer.Read(bytes)
            : FrxDeserializer.Read(bytes, citingSource);
    }

    /// <summary>The companion binary's extension for a designer file: .ctx for a .ctl, .pgx for a .pag, .frx otherwise.</summary>
    public static string CompanionBinaryExtension(string sourceFilePath) =>
        Path.GetExtension(sourceFilePath).ToLowerInvariant() switch
        {
            ".ctl" => ".ctx",
            ".pag" => ".pgx",
            _ => ".frx"
        };

    /// <summary>
    /// The project-file line for a member, as kept when it fails to load so a save writes it back rather than dropping
    /// it. In the shape VB6 requires for each kind: <c>Name; File</c> for a module or class, the bare path
    /// for the designer kinds (see <see cref="ProjectSerializer"/>).
    /// </summary>
    public static string ItemLineFor(ModuleKind kind, string name, string relativePath) => kind switch
    {
        ModuleKind.ClassModule  => $"{SerializedProject.ClassKey}={name}; {relativePath}",
        ModuleKind.UserControl  => $"{SerializedProject.UserControlKey}={relativePath}",
        ModuleKind.PropertyPage => $"{SerializedProject.PropertyPageKey}={relativePath}",
        _                       => $"{SerializedProject.ModuleKey}={name}; {relativePath}",
    };

    /// <summary>Each UserControl of the project as a component its forms can place, keyed by its project-qualified type name.</summary>
    /// <summary>
    /// Names a UserControl or PropertyPage by its designer block's <c>Begin</c> line, as VB6 does, whatever its file
    /// or its <c>Attribute VB_Name</c> says (#1411).
    /// </summary>
    /// <remarks>
    /// Measured with <c>vb6.exe /make</c>. With <c>Gauge.ctl</c> holding <c>Begin VB.UserControl ctlDial</c>, a form's
    /// <c>Begin probe.ctlDial</c> builds, and <c>Begin probe.Gauge</c> fails: "Class probe.Gauge of control uc1 was not
    /// a loaded control class". With <c>Dial.ctl</c> holding <c>Begin VB.UserControl ucBegin</c> and
    /// <c>VB_Name = "Gauge"</c>, only <c>probe.ucBegin</c> builds. The project file's bare-path line gives no name, so
    /// the file's was all HexIDE had, and a form naming the control as VB6 does found no class.
    /// </remarks>
    private static void TakeDesignerName(ModuleDefinition module, FormDefinition formPart)
    {
        if (formPart.Name is { Length: > 0 } name && !string.Equals(module.Name, name, StringComparison.Ordinal))
            module.Name = name;
    }

    public static IReadOnlyDictionary<string, ComponentBaseClass> BuildUserControlRegistry(ProjectDefinition project)
    {
        var projectName = project.Name;
        var dict = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in project.Modules)
        {
            if (module.Kind != ModuleKind.UserControl)
                continue;
            var typeName = $"{projectName}.{module.Name}";
            if (module.FormPart != null)
                dict[typeName] = UserControlComponentClass.ForModule(typeName, module.FormPart, dict);
            else
                dict[typeName] = PlaceholderComponentClass.ForType(typeName);
        }
        return dict;
    }
}
