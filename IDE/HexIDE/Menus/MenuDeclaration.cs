using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Menus;

internal enum MenuItemKind { Submenu, Separator, Command, Provider, Toggle, Placeholder }
internal enum MenuArgumentKind { String, Boolean, Integer }
internal sealed record MenuArgument(
    [property: JsonRequired] MenuArgumentKind Kind,
    [property: JsonRequired] JsonElement Value);

internal sealed record MenuDeclaration
{
    public required MenuItemKind Kind { get; init; }
    public required string Id { get; init; }
    public string? LabelKey { get; init; }
    public string? LabelBinding { get; init; }
    public string? Command { get; init; }
    public Dictionary<string, MenuArgument>? Arguments { get; init; }
    public string? Provider { get; init; }
    public string? State { get; init; }
    public MenuDeclaration[]? Children { get; init; }
}

internal sealed class MenuDeclarationException(string source, string placement, string reason)
    : Exception($"{source}: placement '{placement}': {reason}")
{
    internal string SourceName { get; } = source;
    internal string PlacementId { get; } = placement;
}

internal static class MainMenuDeclaration
{
    internal static MenuDeclaration[] Load()
    {
        using var stream = typeof(MainMenuDeclaration).Assembly.GetManifestResourceStream("HexIDE.Menus.MainMenu.json")
            ?? throw new InvalidOperationException("Missing embedded main menu declaration.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "HexIDE.Menus.MainMenu.json");
    }

    internal static MenuDeclaration[] Parse(string json, string source)
    {
        MenuDeclaration[] items;
        try
        {
            items = JsonSerializer.Deserialize(json, MenuJsonContext.Default.MenuDeclarationArray)
                ?? throw new JsonException("Expected a menu array.");
        }
        catch (JsonException ex)
        {
            throw new MenuDeclarationException(source, ex.Path ?? "$", ex.Message);
        }
        Validate(items, source);
        return items;
    }

    internal static void Validate(IEnumerable<MenuDeclaration> items, string source,
        IReadOnlyDictionary<string, MenuCommandRegistration>? commands = null)
    {
        commands ??= MenuHostRegistry.Commands;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Visit(MenuDeclaration? item)
        {
            if (item is null) throw new MenuDeclarationException(source, "$", "Null menu item.");
            void Require(bool condition, string reason)
            {
                if (!condition) throw new MenuDeclarationException(source, item.Id, reason);
            }
            Require(!string.IsNullOrWhiteSpace(item.Id) && ids.Add(item.Id), "Missing or duplicate placement id.");
            Require(Enum.IsDefined(item.Kind), "Unknown item kind.");
            Require(item.Kind == MenuItemKind.Separator
                ? item.LabelKey is null && item.LabelBinding is null
                : item.LabelKey is not null && item.LabelKey.StartsWith("Str.", StringComparison.Ordinal),
                "Expected a localized label (separators have none).");
            if (item.LabelBinding is not null)
                Require(MenuHostRegistry.Labels.TryGetValue(item.LabelBinding, out var key) && key == item.LabelKey,
                    "Unknown or mismatched host label binding.");
            Require((item.Command is not null) == (item.Kind == MenuItemKind.Command), "Command belongs only on a command item.");
            Require((item.Provider is not null) == (item.Kind == MenuItemKind.Provider), "Provider belongs only on a provider item.");
            Require((item.State is not null) == (item.Kind == MenuItemKind.Toggle), "State belongs only on a toggle item.");
            Require(item.Children is null || item.Kind is MenuItemKind.Submenu or MenuItemKind.Provider,
                "Children belong only on submenus or providers.");
            Require(item.Kind != MenuItemKind.Submenu || item.Children is { Length: > 0 }, "Empty submenu.");
            Require(item.Arguments is null || item.Kind == MenuItemKind.Command, "Arguments belong only on commands.");
            if (item.Command is { } command)
            {
                Require(commands.TryGetValue(command, out var registration), $"Unknown command '{command}'.");
                var arguments = item.Arguments ?? new Dictionary<string, MenuArgument>();
                Require(arguments.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(registration!.Arguments.Keys),
                    "Arguments do not match the command signature.");
                foreach (var (name, argument) in arguments)
                {
                    Require(argument is not null && argument.Kind == registration.Arguments[name],
                        $"Argument '{name}' has the wrong type.");
                    Require(argument!.Kind switch
                    {
                        MenuArgumentKind.String => argument.Value.ValueKind == JsonValueKind.String,
                        MenuArgumentKind.Boolean => argument.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                        MenuArgumentKind.Integer => argument.Value.ValueKind == JsonValueKind.Number && argument.Value.TryGetInt32(out _),
                        _ => false
                    }, $"Argument '{name}' has an invalid value.");
                }
            }
            if (item.Provider is { } provider)
                Require(MenuHostRegistry.Providers.ContainsKey(provider), $"Unknown provider '{provider}'.");
            if (item.State is { } state)
                Require(MenuHostRegistry.States.ContainsKey(state), $"Unknown boolean state '{state}'.");
            if (item.Children is { } children)
                foreach (var child in children) Visit(child);
        }
        foreach (var item in items) Visit(item);
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(MenuDeclaration[]))]
internal partial class MenuJsonContext : JsonSerializerContext;
