using System;
using System.IO;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using Path = System.IO.Path;

namespace HexIDE.Runtime.Serialization;

public class ProjectSerializer
{
    /// <summary>
    /// The spelling VB6 uses, which is <c>Exe</c> and not <c>EXE</c>.
    ///
    /// All seven .vbp files in VB6's own Template tree agree, and the other three names here were already
    /// right — only the EXE row was shouting. VB6 reads either (verified with <c>/make</c>), so this was
    /// never a load failure; it was a project that came back different from the one that went in, which
    /// shows up as a spurious diff and as an untouched project reporting itself modified.
    /// </summary>
    private static string ProjectTypeToString(VBProjectType type) => type switch
    {
        VBProjectType.EXE     => "Exe",
        VBProjectType.OleDll  => "OleDll",
        VBProjectType.OleExe  => "OleExe",
        VBProjectType.Control => "Control",
        _                     => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    /// <param name="pathOf">
    /// Where a form or module will be when this text is written, for a caller checking a save before it moves
    /// anything; null for a member staying where it is. Without it, each member is named where it is now.
    /// </param>
    public string Serialize(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null)
    {
        var writer = new StringWriter { NewLine = "\r\n" };
        // Sort by PositionHint so interleaving is correct even if unknowns were appended out of order
        // (e.g. by the loader). The trailing-remainder loop already guarantees none are lost; this
        // hardens their ORDER. OrderBy is stable, so equal-hint lines keep their insertion order.
        var unknowns = project.UnknownPreSectionLines.OrderBy(u => u.PositionHint).ToList();
        int knownKeyCount = 0;
        int unknownIdx = 0;

        // A line kept as it was read names its file relative to the folder it was read in. Written to a project file in
        // another folder, by Save Project As or a save to a directory, it names the same file from there: a member kept
        // as its line, a carried file's original line (#737), and the paths VB6's own Save As rebases among the lines
        // HexIDE does not model, Designer=, ResFile32= and Path32= (#1406). HelpFile= is left as it was, as VB6 leaves it.
        var keptFrom = project.ItemLinesDirectory
                       ?? (project.AbsolutePath is { } current ? Path.GetDirectoryName(current) : null);
        var writtenIn = Path.GetDirectoryName(projectPath) ?? "";
        string Kept(string line) => keptFrom is null ? line : SerializedProject.RebaseItemLine(line, keptFrom, writtenIn);

        void WriteKnownLine(string line)
        {
            // Interleave unknown lines whose position hint falls at or before this known key
            while (unknownIdx < unknowns.Count && unknowns[unknownIdx].PositionHint <= knownKeyCount)
                writer.WriteLine(Kept(unknowns[unknownIdx++].RawLine));
            writer.WriteLine(line);
            knownKeyCount++;
        }

        // KEY ORDER IS VB6'S, and it is load-bearing rather than cosmetic.
        //
        // VB6 writes Type, then References, then Objects, then the items, then Startup, then Name. This
        // used to write Name first and References last, which is wrong twice over: the file comes back
        // reordered, AND the unknown-line interleaving above silently misaligns. PositionHint is counted
        // while READING, in file order — "how many known keys came before this unknown line" — so it only
        // puts a line back in the right place if the writer visits the known keys in that same order.
        // With Name written first, every hint was off by one from the start.
        //
        // `Object=` lines are unknown keys, and they land between Reference and Form by their hints alone.
        WriteKnownLine($"{SerializedProject.TypeKey}={ProjectTypeToString(project.ProjectType)}");

        foreach (var r in project.References)
        {
            // VB6 reference format: *\G{GUID}#Version#LCID#LibPath#Name. The trailing Name is REQUIRED:
            // vb6.exe /make reports "could not be loaded" for a name-less reference even when the GUID
            // resolves via the registry. LibPath may be empty — VB6 then resolves the typelib from the GUID.
            //
            // A relative LibPath is named from the folder the project file is written in, as VB6's Save As writes it
            // (#1406). VB6 re-derives the path from the registry; this keeps the one the file had, which is the same
            // path wherever the type library has not moved.
            var libPath = keptFrom is null ? r.LibPath ?? "" : SerializedProject.RebasePath(r.LibPath ?? "", keptFrom, writtenIn);
            var refName = r.Name ?? "";
            WriteKnownLine($"{SerializedProject.ReferenceKey}=*\\G{r.Guid}#{r.Version}#{r.Lcid}#{libPath}#{refName}");
        }

        foreach (var form in project.Forms)
        {
            if ((pathOf?.Invoke(form) ?? form.AbsolutePath) is not { } formPath)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, formPath));
            WriteKnownLine($"{SerializedProject.FormKey}={relativePath}");
        }

        foreach (var module in project.Modules)
        {
            if ((pathOf?.Invoke(module) ?? module.AbsolutePath) is not { } modulePath)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, modulePath));
            var key = module.Kind switch
            {
                ModuleKind.ClassModule  => SerializedProject.ClassKey,
                ModuleKind.UserControl  => SerializedProject.UserControlKey,
                ModuleKind.PropertyPage => SerializedProject.PropertyPageKey,
                _                       => SerializedProject.ModuleKey
            };

            // Two shapes, and VB6 is strict about which key takes which — all five measured, not assumed
            // (docs/vb6-fidelity-oracle.md, 2026-09-20). `Module=` and `Class=` REQUIRE the `Name; File`
            // form: given `Module=Mod1.bas`, VB6 refuses the whole project with "The project file … is
            // corrupt, and can't be loaded", naming no line. The designer kinds require the bare path:
            // given `UserControl=Gauge; Gauge.ctl`, VB6 reads the entire value as a filename and fails with
            // "File not found: 'Gauge; Gauge.ctl'" — as does `Form=Form1; Form1.frm`, which is why the form
            // loop above writes the path alone. Writing the `Name; File` shape for all four kinds here is
            // what HexIDE did until #483, which made every project it saved that held a UserControl or a
            // PropertyPage unopenable in VB6.
            WriteKnownLine(module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage
                ? $"{key}={relativePath}"
                : $"{key}={module.Name}; {relativePath}");
        }

        // Related documents — files the project carries but does not compile.
        //
        // Two shapes, and the difference matters. One HexIDE read from a `RelatedDoc=` line, or one the
        // developer added, is written as `RelatedDoc=`. One RECLASSIFIED from a `Module=`/`Class=` line is
        // written back as the line it arrived on, byte for byte: reclassifying is an inference about what
        // the developer meant, and rewriting their project file on an inference is a bigger liberty than
        // declining to. The line changes only when membership actually changes.
        //
        // Known trade, and it is deliberate: a reclassified line originally interleaved between two
        // `Module=` lines re-emits after them, so such a file round-trips semantically rather than
        // byte-for-byte. Preserving its exact slot needs a per-item ordinal the project model does not
        // carry. Worth naming rather than hiding — but note the file this affects is corrupted outright
        // today (an `Attribute VB_Name` header prepended to it on any Save Project), so a reordered line is
        // a long way up from where it starts.
        foreach (var document in project.RelatedDocuments)
        {
            if (document.OriginalItemLine is { } original)
            {
                WriteKnownLine(Kept(original));
                continue;
            }
            if (document.AbsolutePath == null)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, document.AbsolutePath));
            WriteKnownLine($"{SerializedProject.RelatedDocKey}={relativePath}");
        }

        // Preserved item lines (unsupported UserDocuments, missing-file forms): emit verbatim so the
        // round-trip never drops a node.
        //
        // Through WriteKnownLine, NOT writer.WriteLine — the line is verbatim but it still has to COUNT.
        // The reader increments knownKeyCount for a UserDocument= line (it is a recognised key whose value
        // is merely unmodelled), so a writer that skips the counter leaves every unknown line after it one
        // position out. ActiveX Document Dll.vbp is the corpus case: its Name= came back ahead of the
        // HelpFile= and Command32= lines that had preceded it.
        foreach (var preserved in project.PreservedItemLines)
            WriteKnownLine(Kept(preserved));

        // Startup form name survives even when the form itself couldn't be loaded (missing .frm), so the
        // Startup= line is never dropped. The live StartupForm (if any) takes precedence over the fallback.
        //
        // QUOTED, as VB6 writes it — every Startup= line in its Template tree carries them, including
        // `Startup="(None)"`. They are not decoration for a value with a space in it: `Startup="Sub Main"`
        // is the canonical form and dropping the quotes made an untouched project differ from itself.
        // VB6 accepts the unquoted form too (verified with /make), so this was cosmetic — but cosmetic is
        // the whole of what a round-trip is about.
        // `Sub Main` is a startup object like any form, and outranks the retained name: choosing it in
        // Project Properties must replace whatever the .vbp previously said, not lose to the fallback.
        var startupName = project.StartsAtSubMain
            ? SerializedProject.SubMainStartup
            : project.StartupForm?.Name ?? project.StartupFormName;
        if (!string.IsNullOrEmpty(startupName))
            WriteKnownLine($"{SerializedProject.StartupKey}=\"{startupName.Replace("\"", "\"\"")}\"");

        // Name last of the known keys, which is where VB6 puts it: after Startup, and before the trailing
        // run of unknowns (HelpContextID, CompatibleMode, MajorVer …) that the interleaving places by hint.
        WriteKnownLine($"{SerializedProject.NameKey}=\"{(project.Name ?? "").Replace("\"", "\"\"")}\"");

        // After Name, as a known key, so it lands where VB6 writes it: HelpContextID, which VB6 writes between the two, is an
        // unknown line counted before it, and CompatibleMode, which follows it, one counted after (#42). Not written when
        // there is no description, as VB6 writes none. A file read with none has a slot made for it after HelpContextID
        // (ProjectDeserializer.MakeRoomForDescription), which works only because this is the LAST known key: written into
        // that slot, it raises the count past what the reader counted, and a known key written after it would be misplaced.
        if (!string.IsNullOrEmpty(project.Description))
            WriteKnownLine($"{SerializedProject.DescriptionKey}=\"{project.Description.Replace("\"", "\"\"")}\"");

        // Emit any remaining unknown lines that appeared after all known keys
        while (unknownIdx < unknowns.Count)
            writer.WriteLine(Kept(unknowns[unknownIdx++].RawLine));

        // Append the extension tail (third-party [SectionName]...EOF content)
        if (!string.IsNullOrEmpty(project.ExtensionTail))
        {
            writer.WriteLine();
            writer.Write(project.ExtensionTail);
        }

        return writer.ToString();
    }
}
