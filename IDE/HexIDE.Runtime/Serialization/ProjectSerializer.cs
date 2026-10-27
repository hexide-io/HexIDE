using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
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
    public string Serialize(ProjectDefinition project, string projectPath, Func<object, string?>? pathOf = null,
        bool includeRelatedDocuments = true)
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

        void WriteKnownLine(string line, bool omit = false)
        {
            // Interleave unknown lines whose position hint falls at or before this known key
            while (unknownIdx < unknowns.Count && unknowns[unknownIdx].PositionHint <= knownKeyCount)
                writer.WriteLine(Kept(unknowns[unknownIdx++].RawLine));
            if (!omit)
                writer.WriteLine(line);
            knownKeyCount++;
        }

        // The template order is the default for new lines and projects with no read slots. Existing lines
        // return to their read slots: VB6 also writes Form before Reference (measured, #855).
        var lines = new List<(string Text, int? Slot, bool Omit)>();
        void AddKnownLine(string line, int? slot = null, bool omit = false) => lines.Add((line, slot, omit));

        AddKnownLine($"{SerializedProject.TypeKey}={ProjectTypeToString(project.ProjectType)}");

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
            AddKnownLine($"{SerializedProject.ReferenceKey}=*\\G{r.Guid}#{r.Version}#{r.Lcid}#{libPath}#{refName}");
        }

        var encodingPaths = new List<string>();
        foreach (var form in project.Forms)
        {
            if ((pathOf?.Invoke(form) ?? form.AbsolutePath) is not { } formPath)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, formPath));
            AddKnownLine($"{SerializedProject.FormKey}={relativePath}");
            if (form.TextEncoding == Vb6TextEncoding.Utf8 &&
                ProjectEncodingRecords.NeedsRecord(form.TextEncoding,
                    new FormSerializer().Serialize(form, Path.GetFileName(formPath)).frmText))
                encodingPaths.Add(relativePath);
        }

        foreach (var module in project.Modules)
        {
            if ((pathOf?.Invoke(module) ?? module.AbsolutePath) is not { } modulePath)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, modulePath));
            if (module.TextEncoding == Vb6TextEncoding.Utf8)
            {
                var text = module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage && module.FormPart is { } designer
                    ? new FormSerializer().Serialize(designer, module.Code, Path.GetFileName(modulePath)).frmText
                    : ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader);
                if (ProjectEncodingRecords.NeedsRecord(module.TextEncoding, text))
                    encodingPaths.Add(relativePath);
            }
            var key = module.Kind switch
            {
                ModuleKind.ClassModule  => SerializedProject.ClassKey,
                ModuleKind.UserControl  => SerializedProject.UserControlKey,
                ModuleKind.PropertyPage => SerializedProject.PropertyPageKey,
                _                       => SerializedProject.ModuleKey
            };

            // Two shapes, and VB6 is strict about which key takes which — all five measured, not assumed
            // (docs/vb6-fidelity-oracle/a-vbp-item-line-has-two-shapes-and-the-keys-do-not-agree-on-which.md, 2026-09-20). `Module=` and `Class=` REQUIRE the `Name; File`
            // form: given `Module=Mod1.bas`, VB6 refuses the whole project with "The project file … is
            // corrupt, and can't be loaded", naming no line. The designer kinds require the bare path:
            // given `UserControl=Gauge; Gauge.ctl`, VB6 reads the entire value as a filename and fails with
            // "File not found: 'Gauge; Gauge.ctl'" — as does `Form=Form1; Form1.frm`, which is why the form
            // loop above writes the path alone. Writing the `Name; File` shape for all four kinds here is
            // what HexIDE did until #483, which made every project it saved that held a UserControl or a
            // PropertyPage unopenable in VB6.
            AddKnownLine(module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage
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
        foreach (var document in project.RelatedDocuments)
        {
            if (document.OriginalItemLine is { } original)
            {
                AddKnownLine(Kept(original), document.OriginalItemSlot, omit: !includeRelatedDocuments);
                continue;
            }
            if (document.AbsolutePath == null)
                continue;
            var relativePath = SerializedProject.ToProjectFilePath(
                Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, document.AbsolutePath));
            AddKnownLine($"{SerializedProject.RelatedDocKey}={relativePath}", omit: !includeRelatedDocuments);
        }

        // Preserved item lines (unsupported UserDocuments, missing-file forms): emit verbatim so the
        // round-trip never drops a node.
        //
        // Through WriteKnownLine, NOT writer.WriteLine — the line is verbatim but it still has to COUNT.
        // The reader increments knownKeyCount for a UserDocument= line (it is a recognised key whose value
        // is merely unmodelled), so a writer that skips the counter leaves every unknown line after it one
        // position out. ActiveX Document Dll.vbp is the corpus case: its Name= came back ahead of the
        // HelpFile= and Command32= lines that had preceded it.
        for (var i = 0; i < project.PreservedItemLines.Count; i++)
            AddKnownLine(Kept(project.PreservedItemLines[i]),
                project.PreservedItemLineSlots.TryGetValue(i, out var slot) ? slot : null);

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
            AddKnownLine($"{SerializedProject.StartupKey}=\"{startupName.Replace("\"", "\"\"")}\"");

        // Name follows Startup in the default sequence; read slots can put either elsewhere.
        AddKnownLine($"{SerializedProject.NameKey}=\"{(project.Name ?? "").Replace("\"", "\"\"")}\"");

        // After Name, as a known key, so it lands where VB6 writes it: HelpContextID, which VB6 writes between the two, is an
        // unknown line counted before it, and CompatibleMode, which follows it, one counted after (#42). Not written when
        // there is no description, as VB6 writes none. A file read with none has a slot made for it after HelpContextID
        // (ProjectDeserializer.MakeRoomForDescription), splitting the trailing unknown run when a description is added.
        if (!string.IsNullOrEmpty(project.Description))
            AddKnownLine($"{SerializedProject.DescriptionKey}=\"{project.Description.Replace("\"", "\"\"")}\"");

        // Reserve kept/reclassified entries first, so an earlier missing Form cannot trade slots with a
        // loaded one. All other lines take the next unclaimed slot of their key, including renamed items.
        var claimed = new bool[project.KnownLineKeys.Count];
        var slots = new int?[lines.Count];
        for (var i = 0; i < lines.Count; i++)
            if (lines[i].Slot is { } slot && slot >= 0 && slot < claimed.Length && !claimed[slot])
            {
                slots[i] = slot;
                claimed[slot] = true;
            }
        for (var i = 0; i < lines.Count; i++)
        {
            if (slots[i] is not null)
                continue;
            var key = lines[i].Text.Split('=', 2)[0];
            for (var slot = 0; slot < claimed.Length; slot++)
                if (!claimed[slot] && key.Equals(project.KnownLineKeys[slot], StringComparison.OrdinalIgnoreCase))
                {
                    slots[i] = slot;
                    claimed[slot] = true;
                    break;
                }
        }
        var order = Enumerable.Range(0, lines.Count).Where(i => slots[i] is not null)
            .OrderBy(i => slots[i]).ToList();
        // New lines follow their predecessor in today's generation sequence; removed slots simply vanish.
        for (var i = 0; i < lines.Count; i++)
            if (slots[i] is null)
                order.Insert(i == 0 ? 0 : order.IndexOf(i - 1) + 1, i);
        // An Object= line the Components dialog added goes just after the last Form= line, where VB6's Save Project puts
        // one (measured, #1103), or after Type= in a project with no form.
        var afterForms = order.FindLastIndex(i => lines[i].Text.StartsWith(SerializedProject.FormKey + "=", StringComparison.OrdinalIgnoreCase)) + 1;
        if (unknowns.Any(u => u.PositionHint == ProjectDefinition.AfterFormsHint))
            unknowns = unknowns.Select(u => u.PositionHint == ProjectDefinition.AfterFormsHint ? (PositionHint: Math.Max(afterForms, 1), u.RawLine) : u)
                .OrderBy(u => u.PositionHint).ToList();
        // Omitted package entries still count their slots, so unknown lines keep their neighbours (#750).
        foreach (var i in order)
            WriteKnownLine(lines[i].Text, lines[i].Omit);

        // Emit any remaining unknown lines that appeared after all known keys
        while (unknownIdx < unknowns.Count)
            writer.WriteLine(Kept(unknowns[unknownIdx++].RawLine));

        // Append the extension tail (third-party [SectionName]...EOF content)
        var extensionTail = ProjectEncodingRecords.Write(project.ExtensionTail, encodingPaths);
        if (!string.IsNullOrEmpty(extensionTail))
        {
            writer.WriteLine();
            writer.Write(extensionTail);
        }

        return writer.ToString();
    }
}
