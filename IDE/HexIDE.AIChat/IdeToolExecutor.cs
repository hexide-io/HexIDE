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

    public static readonly JsonArray ToolDefinitions = new()
    {
        Tool("get_active_file",  "Get the filename and full content of the currently active editor",  new JsonObject()),
        Tool("get_file_content", "Get the content of a named file in the project",
            Params(Prop("file_name", "string", "The form or module name, e.g. Form1 or Module1"), ["file_name"])),
        Tool("list_files",    "List all forms and modules in the startup project", new JsonObject()),
        Tool("get_diagnostics", "Get LSP diagnostics (errors and warnings). Optionally filter by file",
            Params(Prop("file_name", "string", "Optional: only return diagnostics for this file"), [])),
        Tool("apply_edit",    "Replace the entire content of a named file with new code",
            Params(
                new JsonObject
                {
                    ["file_name"] = new JsonObject { ["type"] = "string", ["description"] = "The file to edit" },
                    ["content"]   = new JsonObject { ["type"] = "string", ["description"] = "The complete new source code" }
                }, ["file_name", "content"])),
        Tool("navigate_to",   "Open a file and move the cursor to a specific line",
            Params(
                new JsonObject
                {
                    ["file_name"] = new JsonObject { ["type"] = "string" },
                    ["line"]      = new JsonObject { ["type"] = "integer" }
                }, ["file_name", "line"])),
        Tool("run_project",   "Start the VB6 project in the IDE runtime",   new JsonObject()),
        Tool("stop_project",  "Stop the currently running VB6 project",     new JsonObject()),
    };

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
                "get_file_content" => GetFileContent(args),
                "list_files"       => ListFiles(),
                "get_diagnostics"  => GetDiagnostics(args),
                "apply_edit"       => await ApplyEditAsync(args),
                "navigate_to"      => NavigateTo(args),
                "run_project"      => RunProject(),
                "stop_project"     => StopProject(),
                _                  => $"{{\"error\":\"Unknown tool: {call.Name}\"}}"
            };
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private string GetActiveFile()
    {
        var doc = host.Editor.GetActiveDocument();
        if (doc is null) return "{\"error\":\"No active editor\"}";
        return JsonSerializer.Serialize(new { doc.FileName, doc.Content });
    }

    private string GetFileContent(JsonObject? args)
    {
        var name = args?["file_name"]?.GetValue<string>() ?? "";
        var files = host.Project.GetFiles();
        var file = files.FirstOrDefault(f =>
            f.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (file is null) return JsonSerializer.Serialize(new { error = $"File not found: {name}" });

        // Read via editor if open, otherwise from disk
        // For now, open the file and read it
        host.Editor.NavigateTo(file.FileName, 1, 1);
        var doc = host.Editor.GetActiveDocument();
        return doc is not null
            ? JsonSerializer.Serialize(new { doc.FileName, doc.Content })
            : JsonSerializer.Serialize(new { error = "Could not open file" });
    }

    private string ListFiles()
    {
        var files = host.Project.GetFiles()
            .Select(f => new { f.FileName, kind = f.Kind.ToString() });
        return JsonSerializer.Serialize(new { files });
    }

    private string GetDiagnostics(JsonObject? args)
    {
        var fileName = args?["file_name"]?.GetValue<string>();
        var diags = string.IsNullOrEmpty(fileName)
            ? host.Diagnostics.GetAll()
            : host.Diagnostics.GetFor(fileName);

        var items = diags.Select(d => new
        {
            d.FileName, d.Line, d.Column, d.Message,
            severity = d.Severity.ToString()
        });
        return JsonSerializer.Serialize(new { diagnostics = items });
    }

    private async Task<string> ApplyEditAsync(JsonObject? args)
    {
        var fileName = args?["file_name"]?.GetValue<string>() ?? "";
        var content  = args?["content"]?.GetValue<string>() ?? "";

        // SetContent is a silent no-op when the file isn't in the project, so validate
        // first — otherwise the model is told an edit landed when nothing changed.
        var exists = host.Project.GetFiles()
            .Any(f => f.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));
        if (!exists)
            return JsonSerializer.Serialize(new { success = false, error = $"File not found: {fileName}" });

        await host.Editor.SetContent(fileName, content);
        return JsonSerializer.Serialize(new { success = true, fileName });
    }

    private string NavigateTo(JsonObject? args)
    {
        var fileName = args?["file_name"]?.GetValue<string>() ?? "";
        var line     = args?["line"]?.GetValue<int>() ?? 1;
        host.Editor.NavigateTo(fileName, line, 1);
        return JsonSerializer.Serialize(new { success = true });
    }

    private string RunProject() =>
        host.RunProject()
            ? JsonSerializer.Serialize(new { success = true })
            : JsonSerializer.Serialize(new { success = false, error = "Cannot start project (none loaded or already running)" });

    private string StopProject() =>
        host.StopProject()
            ? JsonSerializer.Serialize(new { success = true })
            : JsonSerializer.Serialize(new { success = false, error = "No project is currently running" });

    // ── helpers ──────────────────────────────────────────────────────────────

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

    private static JsonObject Prop(string name, string type, string description) =>
        new()
        {
            [name] = new JsonObject
            {
                ["type"]        = type,
                ["description"] = description
            }
        };
}
