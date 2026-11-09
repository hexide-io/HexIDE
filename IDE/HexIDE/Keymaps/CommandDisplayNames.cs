using System.Collections.Generic;

namespace HexIDE.Keymaps;

internal static class CommandDisplayNames
{
    internal static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["ProductOnWebCommand"] = "Str.Menu.Help.ProductOnWeb",
        ["NYICommand"] = "Str.Shell.NotImplemented",
        ["AboutCommand"] = "Str.Menu.Help.About",
        ["AddProcedureCommand"] = "Str.Menu.Tools.AddProcedure",
        ["AddProjectCommand"] = "Str.Menu.File.AddProject",
        ["AddWatchCommand"] = "Str.Menu.Debug.AddWatch",
        ["AlignBottomsCommand"] = "Str.Menu.Format.Align.Bottoms",
        ["AlignCentersHCommand"] = "Str.Menu.Format.Align.CentersHorizontally",
        ["AlignCentersVCommand"] = "Str.Menu.Format.Align.CentersVertically",
        ["AlignLeftsCommand"] = "Str.Menu.Format.Align.Lefts",
        ["AlignRightsCommand"] = "Str.Menu.Format.Align.Rights",
        ["AlignTopsCommand"] = "Str.Menu.Format.Align.Tops",
        ["BreakProjectCommand"] = "Str.Menu.Run.Break",
        ["BringToFrontCommand"] = "Str.Menu.Format.Order.BringToFront",
        ["CallHierarchyCommand"] = "Str.Menu.Edit.CallHierarchy",
        ["CenterHorizontallyCommand"] = "Str.Menu.Format.CenterInForm.Horizontally",
        ["CenterVerticallyCommand"] = "Str.Menu.Format.CenterInForm.Vertically",
        ["ClearAllBookmarksCommand"] = "Str.Menu.Edit.Bookmarks.ClearAll",
        ["ClearAllBreakpointsCommand"] = "Str.Menu.Debug.ClearAllBreakpoints",
        ["CompleteWordCommand"] = "Str.Menu.Edit.CompleteWord",
        ["CopyCommand"] = "Str.Menu.Edit.Copy",
        ["CutCommand"] = "Str.Menu.Edit.Cut",
        ["DecreaseHorizontalSpacingCommand"] = "Str.Menu.Format.Spacing.Decrease",
        ["DecreaseVerticalSpacingCommand"] = "Str.Menu.Format.Spacing.Decrease",
        ["DeleteCommand"] = "Str.Menu.Edit.Delete",
        ["DeleteLineCommand"] = "Str.Options.Keymap.DeleteLine",
        ["DisabledCommand"] = "Str.Menu.File.Print",
        ["EditMenuCommand"] = "Str.Menu.Tools.MenuEditor",
        ["EditWatchCommand"] = "Str.Menu.Debug.EditWatch",
        ["EndProjectCommand"] = "Str.Menu.Run.End",
        ["ExitCommand"] = "Str.Menu.File.Exit",
        ["ExpandSelectionCommand"] = "Str.CodeEditor.Context.ExpandSelection",
        ["FindCommand"] = "Str.Menu.Edit.Find",
        ["FindNextCommand"] = "Str.Menu.Edit.FindNext",
        ["FindReferencesCommand"] = "Str.Tool.FindReferences.Title",
        ["FormatDocumentCommand"] = "Str.CodeEditor.Context.FormatDocument",
        ["FormatSelectionCommand"] = "Str.CodeEditor.Context.FormatSelection",
        ["GoToDeclarationCommand"] = "Str.Menu.View.Declaration",
        ["GoToDefinitionCommand"] = "Str.Menu.View.Definition",
        ["GoToImplementationCommand"] = "Str.Menu.View.Implementation",
        ["GoToTypeDefinitionCommand"] = "Str.Menu.View.TypeDefinition",
        ["IncreaseHorizontalSpacingCommand"] = "Str.Menu.Format.Spacing.Increase",
        ["IncreaseVerticalSpacingCommand"] = "Str.Menu.Format.Spacing.Increase",
        ["IndentCommand"] = "Str.Menu.Edit.Indent",
        ["InsertFileCommand"] = "Str.Menu.Edit.InsertFile",
        ["ListConstantsCommand"] = "Str.Menu.Edit.ListConstants",
        ["ListPropertiesMethodsCommand"] = "Str.Menu.Edit.ListPropertiesMethods",
        ["LockControlsCommand"] = "Str.Menu.Format.LockControls",
        ["MakeHorizontalSpacingEqualCommand"] = "Str.Menu.Format.Spacing.MakeEqual",
        ["MakeProjectGroupCommand"] = "Str.Menu.File.MakeProjectGroup",
        ["MakeSameHeightCommand"] = "Str.Menu.Format.MakeSameSize.Height",
        ["MakeSameSizeCommand"] = "Str.Menu.Format.MakeSameSize.Both",
        ["MakeSameWidthCommand"] = "Str.Menu.Format.MakeSameSize.Width",
        ["MakeVerticalSpacingEqualCommand"] = "Str.Menu.Format.Spacing.MakeEqual",
        ["NewProjectCommand"] = "Str.Menu.File.NewProject",
        ["NextBookmarkCommand"] = "Str.Menu.Edit.Bookmarks.Next",
        ["OpenCallStackCommand"] = "Str.Menu.View.CallStack",
        ["OpenColorPaletteCommand"] = "Str.Menu.View.ColorPalette",
        ["OpenDataViewCommand"] = "Str.Toolbar.Tip.DataViewWindow",
        ["OpenFormLayoutCommand"] = "Str.Menu.View.FormLayoutWindow",
        ["OpenImmediateCommand"] = "Str.Menu.View.ImmediateWindow",
        ["OpenLocalsCommand"] = "Str.Menu.View.LocalsWindow",
        ["OpenObjectBrowserCommand"] = "Str.Menu.View.ObjectBrowser",
        ["OpenOptionsCommand"] = "Str.Menu.Tools.Options",
        ["OpenProjectCommand"] = "Str.Menu.File.OpenProject",
        ["OpenProjectExplorerCommand"] = "Str.Menu.View.ProjectExplorer",
        ["OpenPropertiesCommand"] = "Str.Menu.View.PropertiesWindow",
        ["OpenToolBoxCommand"] = "Str.Menu.View.Toolbox",
        ["OpenWatchesCommand"] = "Str.Menu.View.WatchWindow",
        ["OutdentCommand"] = "Str.Menu.Edit.Outdent",
        ["ParameterInfoCommand"] = "Str.Menu.Edit.ParameterInfo",
        ["PasteAsCommentCommand"] = "Str.Menu.Edit.PasteAsComment",
        ["PasteCommand"] = "Str.Menu.Edit.Paste",
        ["PasteLinkCommand"] = "Str.Menu.Edit.PasteLink",
        ["PreviousBookmarkCommand"] = "Str.Menu.Edit.Bookmarks.Previous",
        ["ProjectComponentsCommand"] = "Str.Menu.Project.Components",
        ["ProjectReferencesCommand"] = "Str.Menu.Project.References",
        ["QuickInfoCommand"] = "Str.Menu.Edit.QuickInfo",
        ["QuickWatchCommand"] = "Str.Menu.Debug.QuickWatch",
        ["RedoCommand"] = "Str.Toolbar.Tip.Redo",
        ["RemoveCommand"] = "Str.Menu.Edit.Remove",
        ["RemoveHorizontalSpacingCommand"] = "Str.Menu.Format.Spacing.Remove",
        ["RemoveVerticalSpacingCommand"] = "Str.Menu.Format.Spacing.Remove",
        ["RenameSymbolCommand"] = "Str.CodeEditor.Context.Rename",
        ["ReopenClosedDocumentCommand"] = "Str.Menu.Window.ReopenClosed",
        ["ReplaceCommand"] = "Str.Menu.Edit.Replace",
        ["ResetWindowLayoutCommand"] = "Str.Menu.Window.ResetLayout",
        ["RestartProjectCommand"] = "Str.Menu.Run.Restart",
        ["RunCodeLensCommand"] = "Str.Editor.RunCodeLens",
        ["RunToCursorCommand"] = "Str.Menu.Debug.RunToCursor",
        ["RunWithVb6Command"] = "Str.Menu.Run.StartWithVB6",
        ["SaveProjectAsCommand"] = "Str.Menu.File.SaveProjectAs",
        ["SaveProjectCommand"] = "Str.Toolbar.Tip.SaveProject",
        ["SelectAllCommand"] = "Str.Menu.Edit.SelectAll",
        ["SendToBackCommand"] = "Str.Menu.Format.Order.SendToBack",
        ["SetNextStatementCommand"] = "Str.Menu.Debug.SetNextStatement",
        ["ShowNextStatementCommand"] = "Str.Menu.Debug.ShowNextStatement",
        ["ShrinkSelectionCommand"] = "Str.CodeEditor.Context.ShrinkSelection",
        ["SizeToGridCommand"] = "Str.Menu.Format.SizeToGrid",
        ["StartDefaultProjectCommand"] = "Str.Menu.Run.Start",
        ["StartDefaultProjectWithFullCompileCommand"] = "Str.Menu.Run.StartWithFullCompile",
        ["StepIntoCommand"] = "Str.Menu.Debug.StepInto",
        ["StepOutCommand"] = "Str.Menu.Debug.StepOut",
        ["StepOverCommand"] = "Str.Menu.Debug.StepOver",
        ["ToggleBookmarkCommand"] = "Str.Menu.Edit.Bookmarks.Toggle",
        ["ToggleBreakpointCommand"] = "Str.Menu.Debug.ToggleBreakpoint",
        ["ToggleFoldCommand"] = "Str.Menu.Edit.ToggleFold",
        ["UndoCommand"] = "Str.Toolbar.Tip.Undo",
        ["ViewCodeCommand"] = "Str.Menu.View.Code",
        ["ViewObjectCommand"] = "Str.Menu.View.Object",
        ["BookmarksCommand"] = "Str.Menu.Edit.Bookmarks",
        ["MakeProjectCommand"] = "Str.Menu.File.MakeProjectFallback",
        ["MakeWithVb6Command"] = "Str.Menu.File.MakeWithVb6Fallback",
        ["RemoveProjectCommand"] = "Str.Menu.File.RemoveProjectFallback",
        ["ProjectPropertiesCommand"] = "Str.Menu.Project.PropertiesFallback",
        ["SaveCommand"] = "Str.Menu.File.SaveFormFallback",
        ["SaveAsCommand"] = "Str.Menu.File.SaveFormAsFallback",
    };
    internal static string DisplayName(string id, System.Func<string, string> text)
    {
        if (!Keys.TryGetValue(id, out var key)) return id;
        var path = new List<string>();
        if (key.StartsWith("Str.Menu.Format."))
        {
            path.Add("Str.Menu.Format");
            if (id.Contains("HorizontalSpacing")) path.Add("Str.Menu.Format.HorizontalSpacing");
            else if (id.Contains("VerticalSpacing")) path.Add("Str.Menu.Format.VerticalSpacing");
            else if (key.Contains(".Align.")) path.Add("Str.Menu.Format.Align");
            else if (key.Contains(".MakeSameSize.")) path.Add("Str.Menu.Format.MakeSameSize");
            else if (key.Contains(".CenterInForm.")) path.Add("Str.Menu.Format.CenterInForm");
            else if (key.Contains(".Order.")) path.Add("Str.Menu.Format.Order");
        }
        else if (id == "EndProjectCommand") path.Add("Str.Menu.Run");
        else if (id == "RemoveCommand") path.Add("Str.Menu.Edit");
        path.Add(key);
        return string.Join(" ▸ ", System.Linq.Enumerable.Select(path, k => text(k).Replace("_", "").TrimEnd('.', '…')));
    }
}
