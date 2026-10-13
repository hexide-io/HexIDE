using System.Text.Json;
using System.Text.Json.Nodes;
using HexIDE.Addins;

namespace HexIDE.AIChat;

/// <summary>Executes IDE tool calls requested by the AI and returns a JSON result string.</summary>
public sealed class IdeToolExecutor(IHexIdeHost host)
{
    /// <summary>Tools that change project state or execute code. These are gated behind an in-panel
    /// approve/reject prompt; every other tool (read / query / navigate) runs without a prompt.</summary>
    public static readonly IReadOnlySet<string> WriteTools =
        new HashSet<string>(StringComparer.Ordinal) { "apply_edit", "run_project", "stop_project" };

    /// <summary>What a file's content is, said once for every tool that returns or takes it.</summary>
    private const string WholeFile =
        "A file's content is the WHOLE file, as the IDE's code window shows it: a form's or UserControl's VERSION "
        + "line and designer block (its controls and their properties), a class's header, and the Attribute lines "
        + "before the code come first, then the code. Line 1 is the file's first line.";

    private const string ProjectNote =
        "Optional: the project holding the file. Needed only when two loaded projects each have a file of this "
        + "name; list_files shows which project each file is in.";

    public static readonly JsonArray ToolDefinitions = new()
    {
        Tool("get_active_file",
            "Get the name, project and content of the file in the active code window. " + WholeFile,
            new JsonObject()),
        Tool("get_file_content",
            "Get the content of a named form, module or class, opening it in the IDE. " + WholeFile,
            Params(new JsonObject
            {
                ["file_name"] = Described("string", "The form, module or class name, e.g. Form1 or Module1"),
                ["project"]   = Described("string", ProjectNote),
            }, ["file_name"])),
        Tool("list_files", "List the forms, modules and classes of every loaded project, and the project each is in",
            new JsonObject()),
        Tool("get_diagnostics",
            "Get diagnostics (errors and warnings), for every file or for one. Lines and columns count from 1, and "
            + "lines count from the top of the file with its header included, as in the content get_active_file returns. "
            + "A file name no loaded project has, or one two projects share, is refused with the reason rather than "
            + "answered with an empty list",
            Params(new JsonObject
            {
                ["file_name"] = Described("string", "Optional: only return diagnostics for this file"),
                ["project"]   = Described("string", ProjectNote),
            }, [])),
        Tool("apply_edit",
            "Replace a named file's content. " + WholeFile + " The header (everything before the code: the designer "
            + "block, the class header and those Attribute lines) belongs to the IDE and cannot be changed here; "
            + "controls and their properties are changed in the form designer. So send either the whole file with "
            + "its header exactly as get_active_file or get_file_content returned it, or the code alone, and the "
            + "file keeps its own header either way. Content whose header differs is refused, nothing changes, and "
            + "the reply says why.",
            Params(new JsonObject
            {
                ["file_name"] = Described("string", "The file to edit"),
                ["content"]   = Described("string",
                    "The new content: the whole file with its header unchanged, or the code alone"),
                ["project"]   = Described("string", ProjectNote),
            }, ["file_name", "content"])),
        Tool("navigate_to",
            "Open a file and move the cursor to a line. Lines count from 1 at the top of the file, header "
            + "included, as in the content get_active_file returns. The reply says when the file or line was not found",
            Params(new JsonObject
            {
                ["file_name"] = Described("string", "The form, module or class name"),
                ["line"]      = Described("integer", "The line, counted from 1 at the top of the file"),
                ["project"]   = Described("string", ProjectNote),
            }, ["file_name", "line"])),
        Tool("run_project",   "Start the VB6 project in the IDE runtime",   new JsonObject()),
        Tool("stop_project",  "Stop the currently running VB6 project",     new JsonObject()),
    };

    /// <summary>
    /// How every reply is written. One naming rule for every field, so a model that reads <c>project</c> from one
    /// tool finds it under the same name in the next.
    /// </summary>
    /// <remarks>
    /// The replies were serialized from anonymous types under the default policy, which keeps each member's own
    /// spelling, so <c>Project</c> came back from get_active_file and <c>project</c> from list_files, and
    /// apply_edit's <c>fileName</c> sat beside the read tools' <c>FileName</c> (hexide-io/HexIDE#273 task 3.19
    /// review). A policy rather than hand-renamed members, so a field added later cannot reopen the split.
    /// </remarks>
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<string> ExecuteAsync(ToolCall call)
    {
        try
        {
            JsonObject? args = null;
            if (!string.IsNullOrWhiteSpace(call.ArgumentsJson) && call.ArgumentsJson != "{}")
                args = JsonNode.Parse(call.ArgumentsJson) as JsonObject;

            return call.Name switch
            {
                "get_active_file"  => GetActiveFile(),
                "get_file_content" => await GetFileContentAsync(args),
                "list_files"       => ListFiles(),
                "get_diagnostics"  => GetDiagnostics(args),
                "apply_edit"       => await ApplyEditAsync(args),
                "navigate_to"      => await NavigateToAsync(args),
                "run_project"      => RunProject(),
                "stop_project"     => StopProject(),
                _                  => Reply(new { error = $"Unknown tool: {call.Name}" })
            };
        }
        catch (Exception ex)
        {
            return Reply(new { error = ex.Message });
        }
    }

    private string GetActiveFile()
    {
        var doc = host.Editor.GetActiveDocument();
        if (doc is null) return Reply(new { error = "No active editor" });
        return Reply(new { doc.FileName, doc.Project, doc.Content });
    }

    /// <remarks>
    /// The add-in surface has no member that reads a document by name, so this opens the file, waits until the
    /// IDE has brought it to the front, and reads the active document. It checks that the active document is the
    /// one asked for before answering. Without that check it answered with whatever window was in front, which
    /// for a file already open in a background tab was a different file under the name the model asked for
    /// (hexide-io/HexIDE#273 task 3.19 review). Reading this way also moves the caret to the file's first line.
    /// A member that reads by name would do neither; that is hexide-io/HexIDE#887.
    /// </remarks>
    private async Task<string> GetFileContentAsync(JsonObject? args)
    {
        var (file, refusal) = FindFile(args);
        if (file is null) return refusal!;

        var opened = await host.Editor.TryNavigateTo(file.FileName, 1, 1, file.Project);
        if (!opened.Succeeded)
            return Reply(new { error = $"Could not open {file.FileName}: {opened.Reason}" });

        var doc = host.Editor.GetActiveDocument();
        if (doc is null
            || !doc.FileName.Equals(file.FileName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(doc.Project, file.Project, StringComparison.OrdinalIgnoreCase))
            return Reply(new
            {
                error = $"Could not read {file.FileName}: the IDE opened it, but the active code window holds "
                        + (doc is null ? "no file" : $"{doc.FileName}{(doc.Project is { } p ? $" in project {p}" : "")}")
                        + ", so its content would not be the file asked for."
            });
        return Reply(new { doc.FileName, doc.Project, doc.Content });
    }

    private string ListFiles()
    {
        var files = host.Project.GetProjects()
            .SelectMany(p => p.Files.Select(f => new { f.FileName, kind = f.Kind.ToString(), project = p.ProjectName }));
        return Reply(new { files });
    }

    /// <remarks>
    /// A file named is found here first, as get_file_content finds it, and its diagnostics asked for in the
    /// project it was found in. The host answers an empty list for a name it cannot resolve to one document,
    /// which a model reads as a clean file: a name two loaded projects share, or one no project has, was
    /// reported as having no errors (hexide-io/HexIDE#273 task 3.19 review).
    /// </remarks>
    private string GetDiagnostics(JsonObject? args)
    {
        IReadOnlyList<AddinDiagnostic> diags;
        if (Text(args, "file_name") is null)
            diags = host.Diagnostics.GetAll();
        else
        {
            var (file, refusal) = FindFile(args);
            if (file is null) return refusal!;
            diags = host.Diagnostics.GetFor(file.FileName, file.Project);
        }

        var items = diags.Select(d => new
        {
            d.FileName, d.Project, d.Line, d.Column, d.Message,
            severity = d.Severity.ToString(),
            d.Code, d.Source
        });
        return Reply(new { diagnostics = items });
    }

    /// <remarks>
    /// The outcome is the IDE's answer, not an assumption. This used to check the name against the startup
    /// project and then report success whatever the write did, so a header the IDE refused, or a name another
    /// loaded project also has, reached the model as an edit that had landed (hexide-io/HexIDE#273 task 3.19).
    /// Content that is missing or blank is refused here: the IDE would take it as code alone, keep the header, and
    /// empty the file's code, and a truncated call is likelier than a model that means that.
    /// </remarks>
    private async Task<string> ApplyEditAsync(JsonObject? args)
    {
        var fileName = Text(args, "file_name") ?? "";
        var content  = Text(args, "content");
        if (string.IsNullOrWhiteSpace(content))
            return Reply(new
            {
                success = false,
                error = "No content was sent, so nothing was changed. Send the whole file with its header unchanged, "
                        + "or the code alone.",
            });

        var result = await host.Editor.TrySetContent(fileName, content, Text(args, "project"));
        return result.Succeeded
            ? Reply(new { success = true, fileName, headerKept = result.HeaderKept })
            : Reply(new { success = false, error = result.Reason });
    }

    private async Task<string> NavigateToAsync(JsonObject? args)
    {
        var fileName = Text(args, "file_name") ?? "";
        var line     = args?["line"]?.GetValue<int>() ?? 1;
        var result = await host.Editor.TryNavigateTo(fileName, line, 1, Text(args, "project"));
        return result.Succeeded
            ? Reply(new { success = true })
            : Reply(new { success = false, error = result.Reason });
    }

    private string RunProject() =>
        host.RunProject()
            ? Reply(new { success = true })
            : Reply(new { success = false, error = "Cannot start project (none loaded or already running)" });

    private string StopProject() =>
        host.StopProject()
            ? Reply(new { success = true })
            : Reply(new { success = false, error = "No project is currently running" });

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string Reply<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>
    /// The file a tool's <c>file_name</c> and <c>project</c> name, among every loaded project's files, or the
    /// reply refusing it: no such file, or a name two projects share with both listed.
    /// </summary>
    private (AddinFileInfo? File, string? Error) FindFile(JsonObject? args)
    {
        var name = Text(args, "file_name") ?? "";
        var project = Text(args, "project");
        var files = host.Project.GetProjects()
            .SelectMany(p => p.Files)
            .Where(f => f.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)
                        && (project is null || string.Equals(f.Project, project, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return files.Count switch
        {
            1 => (files[0], null),
            0 => (null, Reply(new
            {
                error = (project is null ? $"File not found: {name}" : $"File not found: {name} in project {project}")
                        + ". list_files lists every loaded project's files."
            })),
            _ => (null, Reply(new
            {
                error = $"'{name}' is in more than one project: {string.Join(", ", files.Select(f => f.Project))}. "
                        + "Pass project to say which."
            })),
        };
    }

    private static JsonObject Tool(string name, string description, JsonObject parameters) =>
        new()
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"]        = name,
                ["description"] = description,
                ["parameters"]  = parameters.Count > 0
                    ? parameters
                    : new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() }
            }
        };

    private static JsonObject Params(JsonObject properties, string[] required)
    {
        var req = new JsonArray();
        foreach (var r in required) req.Add(r);
        return new JsonObject
        {
            ["type"]       = "object",
            ["properties"] = properties,
            ["required"]   = req
        };
    }

    private static JsonObject Described(string type, string description) =>
        new()
        {
            ["type"]        = type,
            ["description"] = description
        };

    /// <summary>A string argument, or null when it is absent or empty.</summary>
    private static string? Text(JsonObject? args, string name) =>
        args?[name]?.GetValue<string>() is { Length: > 0 } value ? value : null;
}
