using System;
using System.Collections.Generic;
using HexIDE.IDE;

namespace HexIDE.Menus;

// Host registrations describe the bindings shared by the AXAML menu and its data-driven replacement.
internal static class MenuHostRegistry
{
    internal static readonly IReadOnlyDictionary<string, MenuCommandRegistration> Commands =
        new Dictionary<string, MenuCommandRegistration>(StringComparer.Ordinal)
        {
            ["application.AboutCommand"] = new("{x:Static vb:ApplicationCommands.AboutCommand}", ApplicationCommands.AboutCommand, null),
            ["application.AddProcedureCommand"] = new("{x:Static vb:ApplicationCommands.AddProcedureCommand}", ApplicationCommands.AddProcedureCommand, null),
            ["application.AddProjectCommand"] = new("{x:Static vb:ApplicationCommands.AddProjectCommand}", ApplicationCommands.AddProjectCommand, null),
            ["application.AddWatchCommand"] = new("{x:Static vb:ApplicationCommands.AddWatchCommand}", ApplicationCommands.AddWatchCommand, null),
            ["application.AlignBottomsCommand"] = new("{x:Static vb:ApplicationCommands.AlignBottomsCommand}", ApplicationCommands.AlignBottomsCommand, null),
            ["application.AlignCentersHCommand"] = new("{x:Static vb:ApplicationCommands.AlignCentersHCommand}", ApplicationCommands.AlignCentersHCommand, null),
            ["application.AlignCentersVCommand"] = new("{x:Static vb:ApplicationCommands.AlignCentersVCommand}", ApplicationCommands.AlignCentersVCommand, null),
            ["application.AlignLeftsCommand"] = new("{x:Static vb:ApplicationCommands.AlignLeftsCommand}", ApplicationCommands.AlignLeftsCommand, null),
            ["application.AlignRightsCommand"] = new("{x:Static vb:ApplicationCommands.AlignRightsCommand}", ApplicationCommands.AlignRightsCommand, null),
            ["application.AlignTopsCommand"] = new("{x:Static vb:ApplicationCommands.AlignTopsCommand}", ApplicationCommands.AlignTopsCommand, null),
            ["application.ProductOnWebCommand"] = new("{x:Static vb:ApplicationCommands.ProductOnWebCommand}", ApplicationCommands.ProductOnWebCommand, null),
            ["application.BreakProjectCommand"] = new("{x:Static vb:ApplicationCommands.BreakProjectCommand}", ApplicationCommands.BreakProjectCommand, null),
            ["application.BringToFrontCommand"] = new("{x:Static vb:ApplicationCommands.BringToFrontCommand}", ApplicationCommands.BringToFrontCommand, null),
            ["application.CallHierarchyCommand"] = new("{x:Static vb:ApplicationCommands.CallHierarchyCommand}", ApplicationCommands.CallHierarchyCommand, null),
            ["application.CenterHorizontallyCommand"] = new("{x:Static vb:ApplicationCommands.CenterHorizontallyCommand}", ApplicationCommands.CenterHorizontallyCommand, null),
            ["application.CenterVerticallyCommand"] = new("{x:Static vb:ApplicationCommands.CenterVerticallyCommand}", ApplicationCommands.CenterVerticallyCommand, null),
            ["application.ClearAllBookmarksCommand"] = new("{x:Static vb:ApplicationCommands.ClearAllBookmarksCommand}", ApplicationCommands.ClearAllBookmarksCommand, null),
            ["application.ClearAllBreakpointsCommand"] = new("{x:Static vb:ApplicationCommands.ClearAllBreakpointsCommand}", ApplicationCommands.ClearAllBreakpointsCommand, null),
            ["application.CompleteWordCommand"] = new("{x:Static vb:ApplicationCommands.CompleteWordCommand}", ApplicationCommands.CompleteWordCommand, null),
            ["application.CopyCommand"] = new("{x:Static vb:ApplicationCommands.CopyCommand}", ApplicationCommands.CopyCommand, null),
            ["application.CutCommand"] = new("{x:Static vb:ApplicationCommands.CutCommand}", ApplicationCommands.CutCommand, null),
            ["application.DecreaseHorizontalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.DecreaseHorizontalSpacingCommand}", ApplicationCommands.DecreaseHorizontalSpacingCommand, null),
            ["application.DecreaseVerticalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.DecreaseVerticalSpacingCommand}", ApplicationCommands.DecreaseVerticalSpacingCommand, null),
            ["application.DeleteCommand"] = new("{x:Static vb:ApplicationCommands.DeleteCommand}", ApplicationCommands.DeleteCommand, null),
            ["application.DisabledCommand"] = new("{x:Static vb:ApplicationCommands.DisabledCommand}", ApplicationCommands.DisabledCommand, null),
            ["application.EditMenuCommand"] = new("{x:Static vb:ApplicationCommands.EditMenuCommand}", ApplicationCommands.EditMenuCommand, null),
            ["application.EditWatchCommand"] = new("{x:Static vb:ApplicationCommands.EditWatchCommand}", ApplicationCommands.EditWatchCommand, null),
            ["application.EndProjectCommand"] = new("{x:Static vb:ApplicationCommands.EndProjectCommand}", ApplicationCommands.EndProjectCommand, null),
            ["application.ExitCommand"] = new("{x:Static vb:ApplicationCommands.ExitCommand}", ApplicationCommands.ExitCommand, null),
            ["application.FindCommand"] = new("{x:Static vb:ApplicationCommands.FindCommand}", ApplicationCommands.FindCommand, null),
            ["application.FindNextCommand"] = new("{x:Static vb:ApplicationCommands.FindNextCommand}", ApplicationCommands.FindNextCommand, null),
            ["application.FindReferencesCommand"] = new("{x:Static vb:ApplicationCommands.FindReferencesCommand}", ApplicationCommands.FindReferencesCommand, null),
            ["application.GoToDeclarationCommand"] = new("{x:Static vb:ApplicationCommands.GoToDeclarationCommand}", ApplicationCommands.GoToDeclarationCommand, null),
            ["application.GoToDefinitionCommand"] = new("{x:Static vb:ApplicationCommands.GoToDefinitionCommand}", ApplicationCommands.GoToDefinitionCommand, null),
            ["application.GoToImplementationCommand"] = new("{x:Static vb:ApplicationCommands.GoToImplementationCommand}", ApplicationCommands.GoToImplementationCommand, null),
            ["application.GoToTypeDefinitionCommand"] = new("{x:Static vb:ApplicationCommands.GoToTypeDefinitionCommand}", ApplicationCommands.GoToTypeDefinitionCommand, null),
            ["application.IncreaseHorizontalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.IncreaseHorizontalSpacingCommand}", ApplicationCommands.IncreaseHorizontalSpacingCommand, null),
            ["application.IncreaseVerticalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.IncreaseVerticalSpacingCommand}", ApplicationCommands.IncreaseVerticalSpacingCommand, null),
            ["application.IndentCommand"] = new("{x:Static vb:ApplicationCommands.IndentCommand}", ApplicationCommands.IndentCommand, null),
            ["application.InsertFileCommand"] = new("{x:Static vb:ApplicationCommands.InsertFileCommand}", ApplicationCommands.InsertFileCommand, null),
            ["application.ListConstantsCommand"] = new("{x:Static vb:ApplicationCommands.ListConstantsCommand}", ApplicationCommands.ListConstantsCommand, null),
            ["application.ListPropertiesMethodsCommand"] = new("{x:Static vb:ApplicationCommands.ListPropertiesMethodsCommand}", ApplicationCommands.ListPropertiesMethodsCommand, null),
            ["application.LockControlsCommand"] = new("{x:Static vb:ApplicationCommands.LockControlsCommand}", ApplicationCommands.LockControlsCommand, null),
            ["application.MakeHorizontalSpacingEqualCommand"] = new("{x:Static vb:ApplicationCommands.MakeHorizontalSpacingEqualCommand}", ApplicationCommands.MakeHorizontalSpacingEqualCommand, null),
            ["application.MakeProjectCommand"] = new("{x:Static vb:ApplicationCommands.MakeProjectCommand}", ApplicationCommands.MakeProjectCommand, null),
            ["application.MakeProjectGroupCommand"] = new("{x:Static vb:ApplicationCommands.MakeProjectGroupCommand}", ApplicationCommands.MakeProjectGroupCommand, null),
            ["application.MakeSameHeightCommand"] = new("{x:Static vb:ApplicationCommands.MakeSameHeightCommand}", ApplicationCommands.MakeSameHeightCommand, null),
            ["application.MakeSameSizeCommand"] = new("{x:Static vb:ApplicationCommands.MakeSameSizeCommand}", ApplicationCommands.MakeSameSizeCommand, null),
            ["application.MakeSameWidthCommand"] = new("{x:Static vb:ApplicationCommands.MakeSameWidthCommand}", ApplicationCommands.MakeSameWidthCommand, null),
            ["application.MakeVerticalSpacingEqualCommand"] = new("{x:Static vb:ApplicationCommands.MakeVerticalSpacingEqualCommand}", ApplicationCommands.MakeVerticalSpacingEqualCommand, null),
            ["application.MakeWithVb6Command"] = new("{x:Static vb:ApplicationCommands.MakeWithVb6Command}", ApplicationCommands.MakeWithVb6Command, null),
            ["application.NYICommand"] = new("{x:Static vb:ApplicationCommands.NYICommand}", ApplicationCommands.NYICommand, null),
            ["application.NewProjectCommand"] = new("{x:Static vb:ApplicationCommands.NewProjectCommand}", ApplicationCommands.NewProjectCommand, null),
            ["application.NextBookmarkCommand"] = new("{x:Static vb:ApplicationCommands.NextBookmarkCommand}", ApplicationCommands.NextBookmarkCommand, null),
            ["application.OpenCallStackCommand"] = new("{x:Static vb:ApplicationCommands.OpenCallStackCommand}", ApplicationCommands.OpenCallStackCommand, null),
            ["application.OpenColorPaletteCommand"] = new("{x:Static vb:ApplicationCommands.OpenColorPaletteCommand}", ApplicationCommands.OpenColorPaletteCommand, null),
            ["application.OpenFormLayoutCommand"] = new("{x:Static vb:ApplicationCommands.OpenFormLayoutCommand}", ApplicationCommands.OpenFormLayoutCommand, null),
            ["application.OpenImmediateCommand"] = new("{x:Static vb:ApplicationCommands.OpenImmediateCommand}", ApplicationCommands.OpenImmediateCommand, null),
            ["application.OpenLocalsCommand"] = new("{x:Static vb:ApplicationCommands.OpenLocalsCommand}", ApplicationCommands.OpenLocalsCommand, null),
            ["application.OpenObjectBrowserCommand"] = new("{x:Static vb:ApplicationCommands.OpenObjectBrowserCommand}", ApplicationCommands.OpenObjectBrowserCommand, null),
            ["application.OpenOptionsCommand"] = new("{x:Static vb:ApplicationCommands.OpenOptionsCommand}", ApplicationCommands.OpenOptionsCommand, null),
            ["application.OpenProjectCommand"] = new("{x:Static vb:ApplicationCommands.OpenProjectCommand}", ApplicationCommands.OpenProjectCommand, null),
            ["application.OpenProjectExplorerCommand"] = new("{x:Static vb:ApplicationCommands.OpenProjectExplorerCommand}", ApplicationCommands.OpenProjectExplorerCommand, null),
            ["application.OpenPropertiesCommand"] = new("{x:Static vb:ApplicationCommands.OpenPropertiesCommand}", ApplicationCommands.OpenPropertiesCommand, null),
            ["application.OpenToolBoxCommand"] = new("{x:Static vb:ApplicationCommands.OpenToolBoxCommand}", ApplicationCommands.OpenToolBoxCommand, null),
            ["application.OpenWatchesCommand"] = new("{x:Static vb:ApplicationCommands.OpenWatchesCommand}", ApplicationCommands.OpenWatchesCommand, null),
            ["application.OutdentCommand"] = new("{x:Static vb:ApplicationCommands.OutdentCommand}", ApplicationCommands.OutdentCommand, null),
            ["application.ParameterInfoCommand"] = new("{x:Static vb:ApplicationCommands.ParameterInfoCommand}", ApplicationCommands.ParameterInfoCommand, null),
            ["application.PasteAsCommentCommand"] = new("{x:Static vb:ApplicationCommands.PasteAsCommentCommand}", ApplicationCommands.PasteAsCommentCommand, null),
            ["application.PasteCommand"] = new("{x:Static vb:ApplicationCommands.PasteCommand}", ApplicationCommands.PasteCommand, null),
            ["application.PasteLinkCommand"] = new("{x:Static vb:ApplicationCommands.PasteLinkCommand}", ApplicationCommands.PasteLinkCommand, null),
            ["application.PreviousBookmarkCommand"] = new("{x:Static vb:ApplicationCommands.PreviousBookmarkCommand}", ApplicationCommands.PreviousBookmarkCommand, null),
            ["application.ProjectComponentsCommand"] = new("{x:Static vb:ApplicationCommands.ProjectComponentsCommand}", ApplicationCommands.ProjectComponentsCommand, null),
            ["application.ProjectPropertiesCommand"] = new("{x:Static vb:ApplicationCommands.ProjectPropertiesCommand}", ApplicationCommands.ProjectPropertiesCommand, null),
            ["application.ProjectReferencesCommand"] = new("{x:Static vb:ApplicationCommands.ProjectReferencesCommand}", ApplicationCommands.ProjectReferencesCommand, null),
            ["application.QuickInfoCommand"] = new("{x:Static vb:ApplicationCommands.QuickInfoCommand}", ApplicationCommands.QuickInfoCommand, null),
            ["application.QuickWatchCommand"] = new("{x:Static vb:ApplicationCommands.QuickWatchCommand}", ApplicationCommands.QuickWatchCommand, null),
            ["application.RedoCommand"] = new("{x:Static vb:ApplicationCommands.RedoCommand}", ApplicationCommands.RedoCommand, null),
            ["application.RemoveCommand"] = new("{x:Static vb:ApplicationCommands.RemoveCommand}", ApplicationCommands.RemoveCommand, null),
            ["application.RemoveHorizontalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.RemoveHorizontalSpacingCommand}", ApplicationCommands.RemoveHorizontalSpacingCommand, null),
            ["application.RemoveProjectCommand"] = new("{x:Static vb:ApplicationCommands.RemoveProjectCommand}", ApplicationCommands.RemoveProjectCommand, null),
            ["application.RemoveVerticalSpacingCommand"] = new("{x:Static vb:ApplicationCommands.RemoveVerticalSpacingCommand}", ApplicationCommands.RemoveVerticalSpacingCommand, null),
            ["application.ReopenClosedDocumentCommand"] = new("{x:Static vb:ApplicationCommands.ReopenClosedDocumentCommand}", ApplicationCommands.ReopenClosedDocumentCommand, null),
            ["application.ReplaceCommand"] = new("{x:Static vb:ApplicationCommands.ReplaceCommand}", ApplicationCommands.ReplaceCommand, null),
            ["application.ResetWindowLayoutCommand"] = new("{x:Static vb:ApplicationCommands.ResetWindowLayoutCommand}", ApplicationCommands.ResetWindowLayoutCommand, null),
            ["application.RestartProjectCommand"] = new("{x:Static vb:ApplicationCommands.RestartProjectCommand}", ApplicationCommands.RestartProjectCommand, null),
            ["application.RunToCursorCommand"] = new("{x:Static vb:ApplicationCommands.RunToCursorCommand}", ApplicationCommands.RunToCursorCommand, null),
            ["application.RunWithVb6Command"] = new("{x:Static vb:ApplicationCommands.RunWithVb6Command}", ApplicationCommands.RunWithVb6Command, null),
            ["application.SaveAsCommand"] = new("{x:Static vb:ApplicationCommands.SaveAsCommand}", ApplicationCommands.SaveAsCommand, null),
            ["application.SaveCommand"] = new("{x:Static vb:ApplicationCommands.SaveCommand}", ApplicationCommands.SaveCommand, null),
            ["application.SaveProjectAsCommand"] = new("{x:Static vb:ApplicationCommands.SaveProjectAsCommand}", ApplicationCommands.SaveProjectAsCommand, null),
            ["application.SaveProjectCommand"] = new("{x:Static vb:ApplicationCommands.SaveProjectCommand}", ApplicationCommands.SaveProjectCommand, null),
            ["application.SelectAllCommand"] = new("{x:Static vb:ApplicationCommands.SelectAllCommand}", ApplicationCommands.SelectAllCommand, null),
            ["application.SendToBackCommand"] = new("{x:Static vb:ApplicationCommands.SendToBackCommand}", ApplicationCommands.SendToBackCommand, null),
            ["application.SetNextStatementCommand"] = new("{x:Static vb:ApplicationCommands.SetNextStatementCommand}", ApplicationCommands.SetNextStatementCommand, null),
            ["application.ShowNextStatementCommand"] = new("{x:Static vb:ApplicationCommands.ShowNextStatementCommand}", ApplicationCommands.ShowNextStatementCommand, null),
            ["application.SizeToGridCommand"] = new("{x:Static vb:ApplicationCommands.SizeToGridCommand}", ApplicationCommands.SizeToGridCommand, null),
            ["application.SplitWindowCommand"] = new("{x:Static vb:ApplicationCommands.SplitWindowCommand}", ApplicationCommands.SplitWindowCommand, null),
            ["application.StartDefaultProjectCommand"] = new("{x:Static vb:ApplicationCommands.StartDefaultProjectCommand}", ApplicationCommands.StartDefaultProjectCommand, null),
            ["application.StartDefaultProjectWithFullCompileCommand"] = new("{x:Static vb:ApplicationCommands.StartDefaultProjectWithFullCompileCommand}", ApplicationCommands.StartDefaultProjectWithFullCompileCommand, null),
            ["application.StepIntoCommand"] = new("{x:Static vb:ApplicationCommands.StepIntoCommand}", ApplicationCommands.StepIntoCommand, null),
            ["application.StepOutCommand"] = new("{x:Static vb:ApplicationCommands.StepOutCommand}", ApplicationCommands.StepOutCommand, null),
            ["application.StepOverCommand"] = new("{x:Static vb:ApplicationCommands.StepOverCommand}", ApplicationCommands.StepOverCommand, null),
            ["application.ToggleBookmarkCommand"] = new("{x:Static vb:ApplicationCommands.ToggleBookmarkCommand}", ApplicationCommands.ToggleBookmarkCommand, null),
            ["application.ToggleBreakpointCommand"] = new("{x:Static vb:ApplicationCommands.ToggleBreakpointCommand}", ApplicationCommands.ToggleBreakpointCommand, null),
            ["application.ToggleFoldCommand"] = new("{x:Static vb:ApplicationCommands.ToggleFoldCommand}", ApplicationCommands.ToggleFoldCommand, null),
            ["application.UndoCommand"] = new("{x:Static vb:ApplicationCommands.UndoCommand}", ApplicationCommands.UndoCommand, null),
            ["application.ViewCodeCommand"] = new("{x:Static vb:ApplicationCommands.ViewCodeCommand}", ApplicationCommands.ViewCodeCommand, null),
            ["application.ViewObjectCommand"] = new("{x:Static vb:ApplicationCommands.ViewObjectCommand}", ApplicationCommands.ViewObjectCommand, null),
            ["shell.AddClassModuleCommand"] = new("{CompiledBinding AddClassModuleCommand}", null, nameof(MainViewViewModel.AddClassModuleCommand)),
            ["shell.AddFileCommand"] = new("{CompiledBinding AddFileCommand}", null, nameof(MainViewViewModel.AddFileCommand)),
            ["shell.AddFormCommand"] = new("{CompiledBinding AddFormCommand}", null, nameof(MainViewViewModel.AddFormCommand)),
            ["shell.AddModuleCommand"] = new("{CompiledBinding AddModuleCommand}", null, nameof(MainViewViewModel.AddModuleCommand)),
            ["shell.AddPropertyPageCommand"] = new("{CompiledBinding AddPropertyPageCommand}", null, nameof(MainViewViewModel.AddPropertyPageCommand)),
            ["shell.AddUserControlCommand"] = new("{CompiledBinding AddUserControlCommand}", null, nameof(MainViewViewModel.AddUserControlCommand)),
            ["shell.OpenAddInManager"] = new("{CompiledBinding OpenAddInManager}", null, nameof(MainViewViewModel.OpenAddInManager)),
            ["shell.OpenDiagnosticsCommand"] = new("{CompiledBinding OpenDiagnosticsCommand}", null, nameof(MainViewViewModel.OpenDiagnosticsCommand)),
            ["shell.OpenBrandHomeCommand"] = new("{CompiledBinding OpenBrandHomeCommand}", null, nameof(MainViewViewModel.OpenBrandHomeCommand)),
            ["shell.OpenBrandSupportCommand"] = new("{CompiledBinding OpenBrandSupportCommand}", null, nameof(MainViewViewModel.OpenBrandSupportCommand)),
            ["shell.OpenGithubRepo"] = new("{CompiledBinding OpenGithubRepo}", null, nameof(MainViewViewModel.OpenGithubRepo)),
            ["shell.OpenLanguageServersCommand"] = new("{CompiledBinding OpenLanguageServersCommand}", null, nameof(MainViewViewModel.OpenLanguageServersCommand)),
            ["shell.OpenOutlineCommand"] = new("{CompiledBinding OpenOutlineCommand}", null, nameof(MainViewViewModel.OpenOutlineCommand)),
            ["shell.OpenProtocolInspectorCommand"] = new("{CompiledBinding OpenProtocolInspectorCommand}", null, nameof(MainViewViewModel.OpenProtocolInspectorCommand)),
            ["shell.OpenRecentEditsTool"] = new("{CompiledBinding OpenRecentEditsTool}", null, nameof(MainViewViewModel.OpenRecentEditsTool)),
            ["shell.RecoverUnsavedWorkCommand"] = new("{CompiledBinding RecoverUnsavedWorkCommand}", null, nameof(MainViewViewModel.RecoverUnsavedWorkCommand)),
            ["shell.RemoveDocumentCommand"] = new("{CompiledBinding RemoveDocumentCommand}", null, nameof(MainViewViewModel.RemoveDocumentCommand)),
            ["shell.StartExternalDebugCommand"] = new("{CompiledBinding StartExternalDebugCommand}", null, nameof(MainViewViewModel.StartExternalDebugCommand)),
        };

    internal static readonly IReadOnlyDictionary<string, string> Providers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["add-ins"] = "AddInsMenuItem",
            ["closed-documents"] = "{CompiledBinding ClosedDocumentMenuItems}",
            ["panel-layouts"] = "PanelLayoutsMenuItem",
            ["recent-projects"] = "{CompiledBinding RecentProjects}",
            ["run-choices"] = "{CompiledBinding RunWith.Choices}",
            ["run-starts"] = "{CompiledBinding RunWith.Starts}",
        };

    internal static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [nameof(MainViewViewModel.RemoveProjectMenuHeader)] = "Str.Menu.File.RemoveProjectFallback",
            [nameof(MainViewViewModel.SaveProjectMenuHeader)] = "Str.Menu.File.SaveProjectFallback",
            [nameof(MainViewViewModel.SaveFormMenuHeader)] = "Str.Menu.File.SaveFormFallback",
            [nameof(MainViewViewModel.SaveFormAsMenuHeader)] = "Str.Menu.File.SaveFormAsFallback",
            [nameof(MainViewViewModel.MakeProjectMenuHeader)] = "Str.Menu.File.MakeProjectFallback",
            [nameof(MainViewViewModel.MakeWithVb6MenuHeader)] = "Str.Menu.File.MakeWithVb6Fallback",
            [nameof(MainViewViewModel.UndoHeader)] = "Str.Menu.Edit.Undo",
            [nameof(MainViewViewModel.RedoHeader)] = "Str.Menu.Edit.Redo",
            [nameof(MainViewViewModel.RemoveFormMenuHeader)] = "Str.Menu.Project.RemoveFormFallback",
            [nameof(MainViewViewModel.BrandHomeMenuHeader)] = "Str.Menu.Help.BrandHome",
            [nameof(MainViewViewModel.BrandSupportMenuHeader)] = "Str.Menu.Help.BrandSupport",
            [nameof(MainViewViewModel.ProjectPropertiesMenuHeader)] = "Str.Menu.Project.PropertiesFallback",
        };

    internal static readonly HashSet<string> VisibilityBindings = new(StringComparer.Ordinal)
    {
        nameof(MainViewViewModel.HasBrandHome),
        nameof(MainViewViewModel.HasBrandSupport),
    };

    internal static readonly IReadOnlyDictionary<string, MenuBooleanState> States =
        new Dictionary<string, MenuBooleanState>(StringComparer.Ordinal)
        {
            ["toolbar.standard.visible"] = new(nameof(SettingsService.IsStandardToolbarVisible), s => s.IsStandardToolbarVisible, (s, value) => s.IsStandardToolbarVisible = value),
            ["toolbar.edit.visible"] = new(nameof(SettingsService.IsEditToolbarVisible), s => s.IsEditToolbarVisible, (s, value) => s.IsEditToolbarVisible = value),
            ["toolbar.debug.visible"] = new(nameof(SettingsService.IsDebugToolbarVisible), s => s.IsDebugToolbarVisible, (s, value) => s.IsDebugToolbarVisible = value),
            ["toolbar.form-editor.visible"] = new(nameof(SettingsService.IsFormEditorToolbarVisible), s => s.IsFormEditorToolbarVisible, (s, value) => s.IsFormEditorToolbarVisible = value),
            ["editor.minimap.visible"] = new(nameof(SettingsService.IsMinimapVisible), s => s.IsMinimapVisible, (s, value) => s.IsMinimapVisible = value),
        };
}

internal sealed record MenuCommandRegistration(string AxamlBinding, System.Windows.Input.ICommand? RoutedCommand, string? ShellMember)
{
    internal IReadOnlyDictionary<string, MenuArgumentKind> Arguments { get; init; } =
        new Dictionary<string, MenuArgumentKind>();
}

internal sealed record MenuBooleanState(string SettingsProperty, Func<SettingsService, bool> Get, Action<SettingsService, bool> Set);
