using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Menus;

internal enum ToolbarItemKind { Command, Separator, Toggle, Flyout, Provider }

internal sealed record ToolbarBand
{
    public required string Id { get; init; }
    public required string AutomationId { get; init; }
    public required string VisibilityBinding { get; init; }
    public required ToolbarPlacement[] Items { get; init; }
}

internal sealed record ToolbarPlacement
{
    public required ToolbarItemKind Kind { get; init; }
    public required string Id { get; init; }
    public string? AutomationId { get; init; }
    public string? TooltipKey { get; init; }
    public string? AutomationNameKey { get; init; }
    public string? VisibilityBinding { get; init; }
    public string? Command { get; init; }
    public Dictionary<string, MenuArgument>? Arguments { get; init; }
    public string? CheckedBinding { get; init; }
    public string? Provider { get; init; }
    public MenuDeclaration[]? Children { get; init; }
}

internal static class ToolbarDeclaration
{
    internal static ToolbarBand[] Load()
    {
        using var stream = typeof(ToolbarDeclaration).Assembly.GetManifestResourceStream("HexIDE.Menus.Toolbars.json")
            ?? throw new InvalidOperationException("Missing embedded toolbar declaration.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), "HexIDE.Menus.Toolbars.json");
    }

    internal static ToolbarBand[] Parse(string json, string source)
    {
        ToolbarBand[] bands;
        try
        {
            bands = JsonSerializer.Deserialize(json, ToolbarJsonContext.Default.ToolbarBandArray)
                ?? throw new JsonException("Expected a toolbar array.");
        }
        catch (JsonException ex)
        {
            throw new MenuDeclarationException(source, ex.Path ?? "$", ex.Message);
        }
        Validate(bands, source);
        return bands;
    }

    internal static void Validate(IEnumerable<ToolbarBand> bands, string source,
        IReadOnlyDictionary<string, MenuCommandRegistration>? commands = null)
    {
        commands ??= MenuHostRegistry.Commands;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Claim(string? id)
        {
            if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                throw new MenuDeclarationException(source, id ?? "$", "Missing or duplicate placement id.");
        }
        foreach (var band in bands)
        {
            if (band is null) throw new MenuDeclarationException(source, "$", "Null toolbar band.");
            Claim(band.Id);
            void BandRequire(bool condition, string reason)
            {
                if (!condition) throw new MenuDeclarationException(source, band.Id, reason);
            }
            BandRequire(!string.IsNullOrWhiteSpace(band.AutomationId), "Missing band automation id.");
            BandRequire(MenuHostRegistry.VisibilityBindings.Contains(band.VisibilityBinding), "Unknown host visibility binding.");
            BandRequire(band.Items is { Length: > 0 }, "Empty toolbar band.");
            foreach (var item in band.Items!)
            {
                if (item is null) throw new MenuDeclarationException(source, band.Id, "Null toolbar placement.");
                Claim(item.Id);
                void Require(bool condition, string reason)
                {
                    if (!condition) throw new MenuDeclarationException(source, item.Id, reason);
                }
                Require(Enum.IsDefined(item.Kind), "Unknown toolbar item kind.");
                var labelled = item.Kind is ToolbarItemKind.Command or ToolbarItemKind.Toggle or ToolbarItemKind.Flyout;
                Require(labelled
                    ? item.TooltipKey?.StartsWith("Str.", StringComparison.Ordinal) == true
                        && item.AutomationNameKey?.StartsWith("Str.", StringComparison.Ordinal) == true
                        && !string.IsNullOrWhiteSpace(item.AutomationId)
                    : item.TooltipKey is null && item.AutomationNameKey is null && item.AutomationId is null,
                    "Expected localized tooltip and automation name on interactive placements only.");
                Require(item.VisibilityBinding is null || MenuHostRegistry.VisibilityBindings.Contains(item.VisibilityBinding),
                    "Unknown host visibility binding.");
                Require((item.Command is not null) == (item.Kind is ToolbarItemKind.Command or ToolbarItemKind.Toggle),
                    "Command belongs only on command buttons and toggles.");
                Require(item.Arguments is null || item.Command is not null, "Arguments require a command.");
                Require((item.CheckedBinding is not null) == (item.Kind == ToolbarItemKind.Toggle), "Checked binding belongs only on toggles.");
                if (item.CheckedBinding is not null)
                    Require(MenuHostRegistry.CheckedBindings.Contains(item.CheckedBinding), "Unknown host checked binding.");
                Require(item.Kind switch
                {
                    ToolbarItemKind.Provider => item.Provider is not null && item.Children is null,
                    ToolbarItemKind.Flyout => item.Provider is not null
                        ? item.Children is null : item.Children is { Length: > 0 },
                    _ => item.Provider is null && item.Children is null
                }, "Invalid provider or flyout shape.");
                if (item.Provider is not null)
                    Require(MenuHostRegistry.ToolbarProviders.ContainsKey(item.Provider), "Unknown toolbar provider.");
                if (item.Command is not null)
                    MainMenuDeclaration.ValidateCommand(item.Command, item.Arguments, commands, Require);
                if (item.Children is { } children)
                {
                    MainMenuDeclaration.Validate(children, source, commands);
                    void Visit(MenuDeclaration child)
                    {
                        Claim(child.Id);
                        if (child.Children is { } nested)
                            foreach (var next in nested) Visit(next);
                    }
                    foreach (var child in children) Visit(child);
                }
            }
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(ToolbarBand[]))]
internal partial class ToolbarJsonContext : JsonSerializerContext;
