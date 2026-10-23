using System;
using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The text of a form's <b>code section</b> — everything a <c>.frm</c> holds after its designer block — and the
/// whole file the code window composes from it.
/// </summary>
/// <remarks>
/// <para>A form's code section opens with an <c>Attribute VB_*</c> block that VB6's code window hid. HexIDE's
/// shows the whole file (hexide-io/HexIDE#273): the designer half (<see cref="Prefix(FormDefinition)"/>), then
/// the code section, with everything from the top of the file through that block greyed, read-only and folded.
/// <c>VB_Name</c> has to agree with the designer block's <c>Begin</c> line, and only the IDE writes it, following a
/// rename (<see cref="RetargetVbName"/>). It is not the form's identity, though it looks like one: where the two
/// disagree, vb6.exe takes the <c>Begin</c> line's name and logs "Conflicting names were found" (measured, #516).</para>
///
/// <para><b>Before phase 3 of #273 the block was on screen and unprotected.</b> Measured in the running IDE on
/// 2026-09-20, it was the opening lines of the code window, syntax-coloured as ordinary code and freely
/// editable, where comments in the tree said it was hidden. Being on screen was never the same as being written
/// back either: anyone composing "the code" of a form writes the part they came to write, and a straight
/// replacement then deleted the block with no warning — which happened, and reached a commit before
/// <c>git diff</c> caught it. <see cref="GuardedContent"/> now keeps the document's header in front of code sent
/// alone and refuses content that would change it.</para>
///
/// <para>A whole <c>.frm</c>, designer block and all, is no longer the mirror mistake it was: a caller that
/// sends one back is sending what reading a form returns (hexide-io/HexIDE#273 task 3.18), and
/// <see cref="GuardedContent"/> compares its header with the form's rather than putting it into the code.
/// The <c>LooksLikeFormFile</c> test that refused one outright (#338) went with that change.</para>
/// </remarks>
public static class FormCodeText
{
    /// <summary>
    /// The leading <c>Attribute</c> block of a code section, with its line endings, or <c>""</c> if absent.
    /// </summary>
    /// <remarks>
    /// Leading blank lines are carried into the block, so the block and what follows it are the code exactly. Only a
    /// LEADING run counts: an <c>Attribute</c> line further down describes a member — the procedure or variable
    /// declared above it — and is that member's read-only run (<see cref="ReadOnlyRegions"/>), not part of the
    /// file's identity.
    /// </remarks>
    public static string AttributeBlock(string code)
    {
        var end = 0;
        var pos = 0;
        while (pos < code.Length)
        {
            // Walked against the original string, so a CRLF ending is not under-counted and the block is sliced from
            // `code` as it is.
            var (textEnd, next) = SourceLines.LineAt(code, pos);
            var line = code.AsSpan(pos, textEnd - pos).Trim();
            if (line.Length == 0) { pos = next; continue; }
            if (!line.StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase)) break;
            pos = next;
            end = pos;
        }
        return end == 0 ? "" : code[..end];
    }

    /// <summary>
    /// Where the <c>Attribute VB_Name</c> line sits in a code section's leading <c>Attribute</c> run: the
    /// offset and length of the line's text, its terminator excluded. Null when the run carries none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Walks the original string's own offsets rather than a normalised copy of it.</b> A code section
    /// is CRLF or LF depending on where it came from — the <c>.frm</c> reader rebuilds it with the host's
    /// line ending, and an editor buffer carries whatever was typed into it — and the span is handed straight
    /// to a document replace. Counting one character per terminator after splitting on <c>'\n'</c> would come
    /// up one short per CRLF line above it and cut into the text, which is what
    /// hexide-io/HexIDE#465 recorded <see cref="AttributeBlock"/> doing before it was fixed the same way.
    /// </para>
    /// <para>
    /// Only the LEADING run counts, for the same reason as <see cref="AttributeBlock"/>: an
    /// <c>Attribute</c> line further down describes a member. Blank lines before it are skipped.
    /// </para>
    /// </remarks>
    public static (int Start, int Length)? VbNameLine(string code)
    {
        var pos = 0;
        while (pos < code.Length)
        {
            var (end, next) = SourceLines.LineAt(code, pos);

            var line = code.AsSpan(pos, end - pos).Trim();
            if (line.Length > 0)
            {
                if (!line.StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (VbNameValue(line) is not null)
                    return (pos, end - pos);
            }
            pos = next;
        }
        return null;
    }

    /// <summary>
    /// <paramref name="code"/> with its <c>Attribute VB_Name</c> saying <paramref name="name"/>, and every
    /// other character left exactly as it was. Returns <paramref name="code"/> itself when the line already
    /// says that name, or when there is no such line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Compared by value, not by the line's text.</b> This runs on every committed designer change, and a
    /// file whose line VB6 did not write the way <see cref="VbNameAttribute"/> does — different spacing —
    /// would otherwise be rewritten by the first nudge of a control, which is a change nobody made.
    /// </para>
    /// <para>
    /// <b>Adds nothing where there is nothing.</b> A form HexIDE created carries no attribute block at all,
    /// and inventing one here would be a decision about what the file says taken by a routine that only
    /// exists to keep a name in step — recorded as a gap of its own rather than papered over.
    /// </para>
    /// </remarks>
    public static string RetargetVbName(string code, string name)
    {
        if (VbNameLine(code) is not var (start, length))
            return code;
        if (string.Equals(VbNameValue(code.AsSpan(start, length).Trim()), name, StringComparison.Ordinal))
            return code;
        return string.Concat(code.AsSpan(0, start), VbNameAttribute(name), code.AsSpan(start + length));
    }

    /// <summary>
    /// The name <paramref name="code"/>'s leading <c>Attribute VB_Name</c> gives, or null when its attribute run has none.
    /// </summary>
    public static string? VbNameOf(string code) =>
        VbNameLine(code) is var (start, length) ? VbNameValue(code.AsSpan(start, length).Trim()) : null;

    /// <summary>The line VB6 writes for a document called <paramref name="name"/>.</summary>
    public static string VbNameAttribute(string name) => $"Attribute VB_Name = \"{name}\"";

    /// <summary>
    /// The name an <c>Attribute VB_Name = "…"</c> line gives, or null when the line is some other attribute.
    /// </summary>
    /// <remarks>
    /// Parsed as keyword, attribute name, <c>=</c>, value, rather than tested with a prefix: a prefix test
    /// for <c>Attribute VB_Name</c> would also claim an attribute whose name merely starts with it.
    /// </remarks>
    private static string? VbNameValue(ReadOnlySpan<char> line)
    {
        const string keyword = "Attribute";
        const string attribute = "VB_Name";
        if (!line.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = line[keyword.Length..];
        if (rest.Length == 0 || !char.IsWhiteSpace(rest[0]))
            return null;
        rest = rest.TrimStart();
        if (!rest.StartsWith(attribute, StringComparison.OrdinalIgnoreCase))
            return null;
        rest = rest[attribute.Length..].TrimStart();
        if (rest.Length == 0 || rest[0] != '=')
            return null;
        return rest[1..].Trim().Trim('"').ToString();
    }

    /// <summary>
    /// The whole file this form represents: its designer half, then its code section.
    /// </summary>
    /// <remarks>
    /// <b>The composition phase 3 of hexide-io/HexIDE#273 rests on, and the invariant is exact</b>: for a
    /// form read from disk and not since modified, this returns the file byte for byte, terminators
    /// included. Both halves are slices of the text that was read.
    ///
    /// <para>
    /// A form HexIDE created has no designer text <b>until something renders one</b>, so until then this is
    /// its code alone. That is right rather than a gap while it lasts: nothing has decided what the file will
    /// say. A form created with a file (<c>ProjectService.AddNewForm</c>) is rendered by the write that creates
    /// it. One created with none, the Standard EXE template's <c>Form1</c> or the Project Explorer's Add Form,
    /// is rendered by its first committed designer change, under the name its first save will use, or by that
    /// save (task 3.4), after which this composes both halves like any other form. What still has no answer is
    /// the window in between — such a form opened and never touched shows no header, and what ought to appear
    /// there, with how the sidecar would tell whether its marks already count it, is hexide-io/HexIDE#885.
    /// </para>
    ///
    /// <para>
    /// The same is true of a <c>.ctl</c> or <c>.pag</c> whose designer block could not be parsed: nothing
    /// was split off, so nothing is put back.
    /// </para>
    ///
    /// <para>
    /// Deliberately NOT a re-render from <c>Components</c>. A re-render is what a save produces and it is a
    /// reproduction, not the text: the <c>VERSION</c> line comes from a literal and the block's
    /// <c>Begin</c>/<c>End</c> are rebuilt at a computed indent. Composing from a render would show the
    /// developer a file subtly unlike the one on disk.
    /// </para>
    /// </remarks>
    public static string WholeFile(FormDefinition form) => Prefix(form) + form.Code;

    /// <summary>
    /// What the code window puts in front of this form's code: its designer half, or nothing.
    /// </summary>
    /// <remarks>
    /// <b>The prefix is not the protected region, and conflating them corrupts a file in either
    /// direction.</b> The region read-only protection covers is the top of the file through the last line
    /// of the leading <c>Attribute</c> run, which for a form straddles this boundary — part of it is the
    /// prefix and part of it is the first lines of <c>Code</c>. Prepending the attribute run here would
    /// show it twice; splitting after it would take <c>VB_Name</c> out of <c>Code</c> and write a form
    /// without one. They answer different questions and are computed separately.
    /// </remarks>
    public static string Prefix(FormDefinition form) => form.DesignerText ?? "";

    /// <summary>What the code window puts in front of this module's code.</summary>
    /// <remarks>
    /// A <c>.ctl</c> or <c>.pag</c> takes its designer half from the <c>FormPart</c>; everything else takes
    /// the module header, whose null-versus-empty rule is argued at
    /// <see cref="ModuleFileFormat.BufferHeader"/>.
    /// </remarks>
    public static string Prefix(ModuleDefinition module) =>
        ModuleFileFormat.HandlesHeader(module.Kind)
            ? ModuleFileFormat.BufferHeader(module.Name, module.Kind, module.OriginalHeader)
            : module.FormPart?.DesignerText ?? "";

    /// <summary>What the code window puts in front of this document's code, whichever kind it is.</summary>
    public static string Prefix(DocumentIdentity document) =>
        document.Module is { } module ? Prefix(module) : Prefix(document.Form!);

    /// <summary>
    /// The number of line breaks in <paramref name="text"/>. For a <see cref="Prefix(FormDefinition)"/>, which
    /// always ends with its terminator, that is the number of lines it occupies, and so the 0-based line the
    /// code after it starts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One count for everything that moves a mark because of a prefix.</b> The header refresh moves a
    /// document's marks by the difference between two prefixes' counts, and the sidecar's migration moves marks
    /// written before the code window showed a prefix at all by the whole of one (hexide-io/HexIDE#273 task
    /// 3.17). Two counters would be two answers to one question, and the first time they disagreed a mark would
    /// land on another statement.
    /// </para>
    /// <para>
    /// Counts line ends by <see cref="SourceLines"/>' rule rather than splitting on <c>Environment.NewLine</c>. A
    /// <c>.frm</c> is a Windows-native format and is CRLF-terminated wherever it came from, but a prefix is also composed
    /// from text HexIDE itself produced, and <c>build-ide</c> runs on <c>ubuntu-latest</c>: a count that asked the host
    /// what a line ending is would answer differently there and move every mark on the wrong machine. A lone CR counts,
    /// as it does for the code window and for <see cref="ReadOnlyRegions.HeaderLineCount(string, int)"/>
    /// (hexide-io/HexIDE#1020).
    /// </para>
    /// </remarks>
    public static int LineBreaks(string text) => SourceLines.Count(text);

    /// <summary>
    /// The body of a composed buffer: everything after the prefix it was composed with.
    /// </summary>
    /// <remarks>
    /// <b>Split by the prefix's LENGTH, and by the prefix the buffer actually carries</b> rather than the
    /// one the model would produce now. The two diverge the moment a document is renamed — a module called
    /// <c>Utilities</c> has a longer <c>VB_Name</c> line than one called <c>Mod1</c> — and splitting at the
    /// model's current length would then cut into the body or leave part of the header in it. The buffer's
    /// own prefix is authoritative until something refreshes both together.
    /// </remarks>
    public static string BodyOf(string buffer, string prefix) =>
        prefix.Length > 0 && buffer.Length >= prefix.Length ? buffer[prefix.Length..] : buffer;

    /// <summary>
    /// The designer half of a rendered form file: everything <c>FormSerializer</c> wrote before the code it
    /// was given.
    /// </summary>
    /// <remarks>
    /// <b>The inverse of the render, taken by length, exactly as <see cref="BodyOf"/> takes the body.</b>
    /// The serializer appends the code verbatim as its last write, with no separator in front of it, so the
    /// remainder is the designer half and is the same span <c>FormDeserializer</c> records when it reads a
    /// file. <c>DesignerHalfIsTheRenderMinusTheCode</c> pins that, because a separator introduced there
    /// later would move this split silently and put a stray line into the code window on every save.
    /// </remarks>
    public static string DesignerHalfOf(string rendered, string code) =>
        code.Length > 0 && rendered.Length >= code.Length ? rendered[..^code.Length] : rendered;

    /// <summary>
    /// The file name a form's designer half is rendered against: the name of its file, or — for a form that
    /// has none yet — the name its first save will use.
    /// </summary>
    /// <remarks>
    /// <b>It reaches the rendered text through exactly one thing: the companion citations.</b>
    /// <c>FormSerializer</c> reads the argument only to derive the <c>.frx</c> / <c>.ctx</c> / <c>.pgx</c>
    /// name, and that name is written only for a property holding a blob — so for a form carrying none, and
    /// every form HexIDE has just created carries none, the render is the same string whatever is passed.
    /// The rule is therefore about being right for the one form that does carry one, which it acquires by
    /// being pasted into from a form that was loaded.
    ///
    /// <para>
    /// <b>Spelled from the identity, so there is one answer rather than a second one.</b>
    /// <c>DocumentIdentity</c> already resolves a UserControl's or PropertyPage's designer half to its
    /// module and answers the extension that kind is saved with, which is also what
    /// <c>DocumentWireName</c> puts after an <c>untitled:</c> name. <c>&lt;Name&gt;.frx</c> in the task's
    /// own wording is right only for a <c>.frm</c>; the other two kinds take <c>.ctx</c> and <c>.pgx</c>,
    /// derived from this by the serializer.
    /// </para>
    ///
    /// <para>
    /// <b><c>Path.GetFileName</c> is correct here</b>, in spite of the rule against host path APIs on VB6
    /// paths: <c>AbsolutePath</c> is a real filesystem path that HexIDE resolved, not a path that came out
    /// of a <c>.vbp</c>, and this is the same call the save path makes to get the same argument.
    /// </para>
    ///
    /// <para>
    /// <b>The empty guard is not defensive tidiness.</b> <c>Path.ChangeExtension("", ".frx")</c> returns
    /// <c>""</c> (measured on .NET 10), so a render handed a nameless document would emit a citation of
    /// <c>"":HHHH</c> rather than failing — a file that looks valid and names nothing. A form with no name
    /// is not a state anything here produces, and if one ever arrives it gets no render at all.
    /// </para>
    /// </remarks>
    public static string? RenderFileNameFor(FormDefinition form)
    {
        var identity = DocumentIdentity.For(form);
        if (identity.AbsolutePath is { } path)
            return System.IO.Path.GetFileName(path);
        return identity.Name.Length == 0 ? null : identity.Name + identity.Extension;
    }

    /// <summary>
    /// The whole file this module represents, whichever kind it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two different shapes behind one question, which is the point of having it in one place, and both go
    /// through <see cref="Prefix(ModuleDefinition)"/>. A <c>.bas</c> or <c>.cls</c> composes through
    /// <see cref="ModuleFileFormat.BufferHeader"/>, so it picks up the preserved header, the canonical literal
    /// for a module never read from disk, or nothing for one whose header was not recognised. A <c>.ctl</c> or
    /// <c>.pag</c> does not: its header is a designer block, <see cref="ModuleFileFormat.HandlesHeader"/> is
    /// false for those kinds, so it composes from the <c>FormPart</c> that carries the designer text with the
    /// MODULE's code beside it — which is the half the save path writes for those kinds too.
    /// </para>
    /// <para>
    /// <b>This used to say a <c>.bas</c> or <c>.cls</c> composed through
    /// <see cref="ModuleFileFormat.ToFileContent"/>.</b> It does not. The two differ only for a file with no
    /// <c>VB_Name</c> at all, whose next save adds the canonical literal that the code window does not show. For a
    /// file whose header the reader did not recognise, they agree, since a save adds no second header to a body
    /// that carries one (hexide-io/HexIDE#472).
    /// </para>
    /// </remarks>
    public static string WholeFile(ModuleDefinition module) => Prefix(module) + module.Code;
}
