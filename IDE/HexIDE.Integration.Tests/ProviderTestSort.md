# Test files that bind provider types: HexIDE.Integration.Tests

The sort of this project's tests for #3411, the move of the interpreter and forms engine to an optional provider. `ProviderTestSortTests` writes it and fails when it and the build disagree. Don't edit the rows by hand: run the test, and copy `artifacts/provider-test-sort/HexIDE.Integration.Tests.md` over this file. The same run writes `HexIDE.Integration.Tests-detail.md` beside it, with each file's counts and provider types.

A file is listed when something in it binds a symbol from a provider assembly, `HexIDE.Runtime` or `HexIDE.Standalone`. A symbol counts for the assembly it comes from, and so does every type in its signature. `HexIDE.Core` counts for neither side, so a namespace decides nothing: Core declares `HexIDE.Runtime.*` namespaces of its own. Every other `HexIDE*` assembly is the IDE's. A test counts what its class's setup and the helpers it calls bind, followed through the test project to a fixed point.

- **provider:** its tests bind provider symbols and no IDE symbols. A file with no tests is judged on what it declares.
- **split:** some of its tests bind a provider symbol, and some bind an IDE symbol.
- **IDE:** none of its tests binds a provider symbol, though something else in the file does.

A provider file whose tests also bind Core may be testing Core with a provider type for its fixture, and then it stays with the IDE once that fixture has a stand-in. The detail's Core column says which files to read first.

A test that reaches the provider only at run time, through the application's composition or by opening VB6 files, isn't seen here. The detail flags both, as a text indicator that doesn't decide the label.

| File | Label |
|---|---|
| `Automation/ActiveDocumentDumpTests.cs` | split |
| `Automation/AutomationNameGuardTests.cs` | split |
| `Automation/BoundControlCommandTests.cs` | split |
| `Automation/BreakpointByNameTests.cs` | split |
| `Automation/CaretPlacementTests.cs` | split |
| `Automation/CloseWindowTests.cs` | split |
| `Automation/DesignerOverlayTreeTests.cs` | split |
| `Automation/HeldButtonTests.cs` | split |
| `Automation/InspectFrameTests.cs` | split |
| `Automation/InspectItemCountTests.cs` | split |
| `Automation/InspectPointerTests.cs` | split |
| `Automation/InteractClickTests.cs` | split |
| `Automation/OpenDialogsTests.cs` | split |
| `Automation/PressKeyWindowInputTests.cs` | split |
| `Automation/ProgramPrintedStartTests.cs` | split |
| `Automation/ProgramWaitTests.cs` | split |
| `Automation/ProgramWindowTests.cs` | split |
| `Automation/RunningCodeEditTests.cs` | split |
| `Automation/RunningFormNamesTests.cs` | split |
| `Automation/TypeKeysTests.cs` | split |
| `Automation/UiAutomationDriverTests.cs` | split |
| `Automation/UiSaveAnswerTimingTests.cs` | split |
| `Controls/BracketDepthRenderingTests.cs` | split |
| `Controls/ClassicRenderTests.cs` | split |
| `Controls/DockOpacityTests.cs` | split |
| `Controls/DropdownListNameTests.cs` | split |
| `Controls/MousePointerValuesTests.cs` | split |
| `Controls/OptionButtonPropertyEditorTests.cs` | split |
| `Controls/ProjectGroupExpansionTests.cs` | split |
| `Debugging/CallStackReplyTests.cs` | split |
| `TestRunners.cs` | split |
| `Views/ActiveFormSaveTests.cs` | split |
| `Views/ArchiveProjectBrowsingTests.cs` | split |
| `Views/BattleshipFormsDemoTests.cs` | split |
| `Views/BeginOrderTests.cs` | split |
| `Views/CallHierarchyTests.cs` | split |
| `Views/CarriedBoxStackingTests.cs` | split |
| `Views/CarriedPasteTests.cs` | split |
| `Views/CodeEditorSelectionTests.cs` | split |
| `Views/CodeEditorSplitTests.cs` | split |
| `Views/CodeLensTests.cs` | split |
| `Views/ColorDialogTests.cs` | split |
| `Views/ComboBoxTextTests.cs` | split |
| `Views/ComboStyleDesignerTests.cs` | split |
| `Views/CommonDialogTests.cs` | split |
| `Views/ComponentsViewTests.cs` | split |
| `Views/ContainerRuntimeHarness.cs` | provider |
| `Views/ControlNameClashTests.cs` | split |
| `Views/DebuggerLinesAreFileLinesTests.cs` | split |
| `Views/DesignerClientSizeTests.cs` | split |
| `Views/DesignerCommitTests.cs` | split |
| `Views/DesignerContainerSelectionTests.cs` | split |
| `Views/DesignerContextMenuThemeTests.cs` | split |
| `Views/DesignerContractTests.cs` | split |
| `Views/DesignerInstantiationFailureTests.cs` | split |
| `Views/DesignerModifiedMarkerTests.cs` | split |
| `Views/DesignerPictureTests.cs` | split |
| `Views/DesignerReferenceTests.cs` | split |
| `Views/DesignerRenameTests.cs` | split |
| `Views/DesignerRootKindTests.cs` | split |
| `Views/DesignerRootPropertiesTests.cs` | split |
| `Views/DesignerTabOrderTests.cs` | split |
| `Views/DesignerTitleBarTests.cs` | split |
| `Views/DesignerVisibilityTests.cs` | split |
| `Views/DialogButtonCueTests.cs` | split |
| `Views/DialogFlowDirectionTests.cs` | split |
| `Views/DrawIntoContainerTests.cs` | split |
| `Views/EventOrderTests.cs` | split |
| `Views/FileWatcherReloadTests.cs` | split |
| `Views/FindReferencesTests.cs` | split |
| `Views/FlatScrollBarFileTests.cs` | split |
| `Views/FontSizeInPointsTests.cs` | split |
| `Views/ForegroundWindowLifetimeTests.cs` | split |
| `Views/ForegroundWindowTests.cs` | split |
| `Views/FormActivationTests.cs` | split |
| `Views/FormEditViewModelContainerTests.cs` | split |
| `Views/FormLayoutToolViewTests.cs` | split |
| `Views/FormPreviewTests.cs` | split |
| `Views/FormatCommandTwipsTests.cs` | split |
| `Views/FormsCollectionTests.cs` | split |
| `Views/GuardedEditorInputTests.cs` | split |
| `Views/HaltOnErrorTests.cs` | split |
| `Views/HeaderFoldTests.cs` | split |
| `Views/ImplementationNavigationTests.cs` | split |
| `Views/InBoxToolboxTests.cs` | split |
| `Views/LineControlTests.cs` | split |
| `Views/LineNumbersFromTheTopOfTheFileTests.cs` | split |
| `Views/LockControlsSaveTests.cs` | split |
| `Views/MarkGutterOnReadOnlyLineTests.cs` | split |
| `Views/MemberRunFoldTests.cs` | split |
| `Views/MemberRunFollowsItsLineTests.cs` | split |
| `Views/MenuMembersTests.cs` | split |
| `Views/MessageBoxAcrossRestartTests.cs` | split |
| `Views/MinesweeperFormsDemoTests.cs` | split |
| `Views/MouseEventTests.cs` | split |
| `Views/NewControlNameTests.cs` | split |
| `Views/ObjectBrowserBehaviourTests.cs` | split |
| `Views/ObjectBrowserViewIntegrationTests.cs` | split |
| `Views/OnTypeFormattingTests.cs` | split |
| `Views/OpeningCaretTests.cs` | split |
| `Views/PictureBoxAutoSizeTests.cs` | split |
| `Views/PictureLoadTests.cs` | split |
| `Views/PositionRowsTests.cs` | split |
| `Views/PrintDialogTests.cs` | split |
| `Views/ProjectPropertiesViewIntegrationTests.cs` | split |
| `Views/QuickFixTests.cs` | split |
| `Views/RawPropertyRowsTests.cs` | split |
| `Views/ReadOnlyBannerIntegrationTests.cs` | split |
| `Views/ReadOnlyGreyingTests.cs` | split |
| `Views/ResetPromptTests.cs` | split |
| `Views/RootDoubleClickTests.cs` | split |
| `Views/RunFaultTests.cs` | split |
| `Views/RuntimeMenuTests.cs` | split |
| `Views/SSTabDesignerTests.cs` | split |
| `Views/SaveChangesViewIntegrationTests.cs` | split |
| `Views/SingleViewFormsTests.cs` | split |
| `Views/StartupFormFailsToLoadTests.cs` | split |
| `Views/SubMainFormsTests.cs` | split |
| `Views/SyntaxErrorAtStartTests.cs` | split |
| `Views/TabIndexAssignmentTests.cs` | split |
| `Views/TabStripForm.cs` | provider |
| `Views/TabStripKeyboardTests.cs` | split |
| `Views/TimerOnTheDesignerTests.cs` | split |
| `Views/ToolbarDesignerPersistenceTests.cs` | split |
| `Views/ToolboxFromRegistryTests.cs` | split |
| `Views/UncreatableControlTests.cs` | split |
| `Views/UnloadTests.cs` | split |
| `Views/UnmodelledControlPlaceholderTests.cs` | split |
| `Views/WindowShortcutFocusTests.cs` | split |
| `WindowLifetimeTests.cs` | split |
