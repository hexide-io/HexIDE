using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

internal static class ToolSchemas
{
    public static IEnumerable<McpServerTool> For(HexIdeTools target) =>
        typeof(HexIdeTools).GetMethods()
            .Where(method => method.GetCustomAttribute<McpServerToolAttribute>() is not null)
            .Select(method => Create(method, target));

    public static McpServerTool Create(MethodInfo method, HexIdeTools target) =>
        McpServerTool.Create(method, target, new McpServerToolCreateOptions
        {
            SerializerOptions = ReplyJson.Options,
            SchemaCreateOptions = new AIJsonSchemaCreateOptions
            {
                TransformSchemaNode = (context, schema) =>
                {
                    if (method.Name == nameof(HexIdeTools.SetBreakpointsAsync)
                        && context.TypeInfo.Type == typeof(Dictionary<int, string>))
                        schema["propertyNames"] = new JsonObject { ["pattern"] = "^[0-9]+$" };
                    return schema;
                }
            }
        });
}
