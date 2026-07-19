using System.Collections.Generic;
using System.Text.Json.Serialization;
using Avalonia.Input;
using Avalonia.Labs.Input;

namespace HexIDE.Keymaps;

public record KeymapPackRecord(
    string Name,
    string? Description,
    Dictionary<string, string>? Bindings
);

[JsonSerializable(typeof(KeymapPackRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class KeymapJsonContext : JsonSerializerContext { }

/// <summary>
/// Maps JSON command names to their RoutedCommand instances.
/// Covers all commands in ApplicationCommands so packs can assign gestures to any command.
/// Keep in sync with ApplicationCommands.cs when adding new commands.
/// </summary>
internal static class CommandKeyMapping
{
    internal static readonly IReadOnlyDictionary<string, RoutedCommand> Table =
        new Dictionary<string, RoutedCommand>
        {
            // ── File ──────────────────────────────────────────────
            ["NewProjectCommand"]                      = ApplicationCommands.NewProjectCommand,
            ["AddProjectCommand"]                      = ApplicationCommands.AddProjectCommand,
            ["OpenProjectCommand"]                     = ApplicationCommands.OpenProjectCommand,
            ["SaveProjectCommand"]                     = ApplicationCommands.SaveProjectCommand,
            ["SaveProjectAsCommand"]                   = ApplicationCommands.SaveProjectAsCommand,
            ["SaveCommand"]                            = ApplicationCommands.SaveCommand,
            ["SaveAsCommand"]                          = ApplicationCommands.SaveAsCommand,
            ["MakeProjectCommand"]                     = ApplicationCommands.MakeProjectCommand,
            ["MakeProjectGroupCommand"]                = ApplicationCommands.MakeProjectGroupCommand,
            ["MakeWithVb6Command"]                     = ApplicationCommands.MakeWithVb6Command,
            ["RemoveProjectCommand"]                   = ApplicationCommands.RemoveProjectCommand,
            ["ExitCommand"]                            = ApplicationCommands.ExitCommand,
            // ── Edit ──────────────────────────────────────────────
            ["UndoCommand"]                            = ApplicationCommands.UndoCommand,
            ["RedoCommand"]                            = ApplicationCommands.RedoCommand,
            ["CutCommand"]                             = ApplicationCommands.CutCommand,
            ["CopyCommand"]                            = ApplicationCommands.CopyCommand,
            ["PasteCommand"]                           = ApplicationCommands.PasteCommand,
            ["PasteLinkCommand"]                       = ApplicationCommands.PasteLinkCommand,
            ["RemoveCommand"]                          = ApplicationCommands.RemoveCommand,
            ["DeleteCommand"]                          = ApplicationCommands.DeleteCommand,
            ["SelectAllCommand"]                       = ApplicationCommands.SelectAllCommand,
            ["FindCommand"]                            = ApplicationCommands.FindCommand,
            ["FindNextCommand"]                        = ApplicationCommands.FindNextCommand,
            ["ReplaceCommand"]                         = ApplicationCommands.ReplaceCommand,
            ["IndentCommand"]                          = ApplicationCommands.IndentCommand,
            ["OutdentCommand"]                         = ApplicationCommands.OutdentCommand,
            ["InsertFileCommand"]                      = ApplicationCommands.InsertFileCommand,
            ["ListPropertiesMethodsCommand"]           = ApplicationCommands.ListPropertiesMethodsCommand,
            ["ListConstantsCommand"]                   = ApplicationCommands.ListConstantsCommand,
            ["QuickInfoCommand"]                       = ApplicationCommands.QuickInfoCommand,
            ["ParameterInfoCommand"]                   = ApplicationCommands.ParameterInfoCommand,
            ["CompleteWordCommand"]                    = ApplicationCommands.CompleteWordCommand,
            ["BookmarksCommand"]                       = ApplicationCommands.BookmarksCommand,
            ["ToggleBookmarkCommand"]                  = ApplicationCommands.ToggleBookmarkCommand,
            ["NextBookmarkCommand"]                    = ApplicationCommands.NextBookmarkCommand,
            ["PreviousBookmarkCommand"]                = ApplicationCommands.PreviousBookmarkCommand,
            ["ClearAllBookmarksCommand"]               = ApplicationCommands.ClearAllBookmarksCommand,
            ["GoToDefinitionCommand"]                  = ApplicationCommands.GoToDefinitionCommand,
            ["RenameSymbolCommand"]                    = ApplicationCommands.RenameSymbolCommand,
            ["FormatDocumentCommand"]                  = ApplicationCommands.FormatDocumentCommand,
            // ── View ──────────────────────────────────────────────
            ["ViewCodeCommand"]                        = ApplicationCommands.ViewCodeCommand,
            ["ViewObjectCommand"]                      = ApplicationCommands.ViewObjectCommand,
            ["OpenImmediateCommand"]                   = ApplicationCommands.OpenImmediateCommand,
            ["OpenLocalsCommand"]                      = ApplicationCommands.OpenLocalsCommand,
            ["OpenWatchesCommand"]                     = ApplicationCommands.OpenWatchesCommand,
            ["OpenProjectExplorerCommand"]             = ApplicationCommands.OpenProjectExplorerCommand,
            ["OpenPropertiesCommand"]                  = ApplicationCommands.OpenPropertiesCommand,
            ["OpenFormLayoutCommand"]                  = ApplicationCommands.OpenFormLayoutCommand,
            ["OpenObjectBrowserCommand"]               = ApplicationCommands.OpenObjectBrowserCommand,
            ["OpenToolBoxCommand"]                     = ApplicationCommands.OpenToolBoxCommand,
            ["OpenDataViewCommand"]                    = ApplicationCommands.OpenDataViewCommand,
            ["OpenColorPaletteCommand"]                = ApplicationCommands.OpenColorPaletteCommand,
            // ── Run ───────────────────────────────────────────────
            ["StartDefaultProjectCommand"]             = ApplicationCommands.StartDefaultProjectCommand,
            ["StartDefaultProjectWithFullCompileCommand"] = ApplicationCommands.StartDefaultProjectWithFullCompileCommand,
            ["BreakProjectCommand"]                    = ApplicationCommands.BreakProjectCommand,
            ["EndProjectCommand"]                      = ApplicationCommands.EndProjectCommand,
            ["RestartProjectCommand"]                  = ApplicationCommands.RestartProjectCommand,
            ["RunWithVb6Command"]                      = ApplicationCommands.RunWithVb6Command,
            // ── Debug ─────────────────────────────────────────────
            ["StepIntoCommand"]                        = ApplicationCommands.StepIntoCommand,
            ["StepOverCommand"]                        = ApplicationCommands.StepOverCommand,
            ["StepOutCommand"]                         = ApplicationCommands.StepOutCommand,
            ["RunToCursorCommand"]                     = ApplicationCommands.RunToCursorCommand,
            ["AddWatchCommand"]                        = ApplicationCommands.AddWatchCommand,
            ["EditWatchCommand"]                       = ApplicationCommands.EditWatchCommand,
            ["QuickWatchCommand"]                      = ApplicationCommands.QuickWatchCommand,
            ["ToggleBreakpointCommand"]                = ApplicationCommands.ToggleBreakpointCommand,
            ["ClearAllBreakpointsCommand"]             = ApplicationCommands.ClearAllBreakpointsCommand,
            ["SetNextStatementCommand"]                = ApplicationCommands.SetNextStatementCommand,
            ["ShowNextStatementCommand"]               = ApplicationCommands.ShowNextStatementCommand,
            // ── Tools / Help ───────────────────────────────────────
            ["EditMenuCommand"]                        = ApplicationCommands.EditMenuCommand,
            ["NYICommand"]                             = ApplicationCommands.NYICommand,
            ["OpenOptionsCommand"]                     = ApplicationCommands.OpenOptionsCommand,
            ["AddProcedureCommand"]                    = ApplicationCommands.AddProcedureCommand,
            ["AboutCommand"]                           = ApplicationCommands.AboutCommand,
            ["AvaloniaOnWebCommand"]                   = ApplicationCommands.AvaloniaOnWebCommand,
            // ── Project ────────────────────────────────────────────
            ["ProjectReferencesCommand"]               = ApplicationCommands.ProjectReferencesCommand,
            ["ProjectComponentsCommand"]               = ApplicationCommands.ProjectComponentsCommand,
            ["ProjectPropertiesCommand"]               = ApplicationCommands.ProjectPropertiesCommand,
            // ── Format (designer) ──────────────────────────────────
            ["BringToFrontCommand"]                    = ApplicationCommands.BringToFrontCommand,
            ["SendToBackCommand"]                      = ApplicationCommands.SendToBackCommand,
            ["CenterHorizontallyCommand"]              = ApplicationCommands.CenterHorizontallyCommand,
            ["CenterVerticallyCommand"]                = ApplicationCommands.CenterVerticallyCommand,
            ["AlignLeftsCommand"]                      = ApplicationCommands.AlignLeftsCommand,
            ["AlignCentersHCommand"]                   = ApplicationCommands.AlignCentersHCommand,
            ["AlignRightsCommand"]                     = ApplicationCommands.AlignRightsCommand,
            ["AlignTopsCommand"]                       = ApplicationCommands.AlignTopsCommand,
            ["AlignCentersVCommand"]                   = ApplicationCommands.AlignCentersVCommand,
            ["AlignBottomsCommand"]                    = ApplicationCommands.AlignBottomsCommand,
            ["MakeSameWidthCommand"]                   = ApplicationCommands.MakeSameWidthCommand,
            ["MakeSameHeightCommand"]                  = ApplicationCommands.MakeSameHeightCommand,
            ["MakeSameSizeCommand"]                    = ApplicationCommands.MakeSameSizeCommand,
            ["MakeHorizontalSpacingEqualCommand"]      = ApplicationCommands.MakeHorizontalSpacingEqualCommand,
            ["IncreaseHorizontalSpacingCommand"]       = ApplicationCommands.IncreaseHorizontalSpacingCommand,
            ["DecreaseHorizontalSpacingCommand"]       = ApplicationCommands.DecreaseHorizontalSpacingCommand,
            ["RemoveHorizontalSpacingCommand"]         = ApplicationCommands.RemoveHorizontalSpacingCommand,
            ["MakeVerticalSpacingEqualCommand"]        = ApplicationCommands.MakeVerticalSpacingEqualCommand,
            ["IncreaseVerticalSpacingCommand"]         = ApplicationCommands.IncreaseVerticalSpacingCommand,
            ["DecreaseVerticalSpacingCommand"]         = ApplicationCommands.DecreaseVerticalSpacingCommand,
            ["RemoveVerticalSpacingCommand"]           = ApplicationCommands.RemoveVerticalSpacingCommand,
            ["SizeToGridCommand"]                      = ApplicationCommands.SizeToGridCommand,
            ["LockControlsCommand"]                    = ApplicationCommands.LockControlsCommand,
        };
}
