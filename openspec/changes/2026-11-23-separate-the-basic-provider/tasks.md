## 1. Measure (phase 1)

- [x] 1.1 Read every product assembly's references into the provider at `6d421e942`, from the built DLLs' metadata and from the source, and post the report and the slice list on #3411.

## 2. Separate in the tree (phase 2)

Each slice removes its rows from the guard's list. Sizes and owners are in the phase 1 comment on #3411.

- [x] 2.1 The guard: a test fails on a product reference into a provider assembly that its list doesn't name, and on a listed one that has gone (#5388).
- [x] 2.2 The run engine behind an interface that `ProjectRunnerService` calls, with `DISetup`'s debugger wiring (slice 2 folded in: its other uses already go through Core's interface).
- [ ] 2.3 The running controls: the eight files that use them.
- [ ] 2.4 A provider-fed designer: the component registry and Toolbox, "is this a form" checks, the designer's preview, and the Object Browser's in-box members.
- [ ] 2.5 The VB6 file formats and code-window text: the text policy, path helpers, the project file format, and the designer's `.frx` helpers.
- [x] 2.6 Language helpers through the language registration.
- [ ] 2.7 VB6 value types and the provider's resources in the IDE.
- [ ] 2.8 Make and Make with VB6 as the VB6 project type's offer.
- [ ] 2.9 The provider's strings into a pack area of its own, under the parity guard.
- [ ] 2.10 A neutral default profile.
- [ ] 2.11 #3663: move the provider's test files, and split the test files that test both sides.
- [ ] 2.12 Core's internals: the four members the provider uses, and `ControlLibraryReferences`, which the runtime's
  library order calls since #5610.
- [ ] 2.13 The composition point: one provider entry type, started from one `HexIDE.Desktop` file, registering through the host interface, with tests that a colliding registration is refused and that a provider can't reach a consent dialog's strings. Built with 2.7, whose resources are the first registration; security work, reviewed by the supervisor before merge.
- [ ] 2.14 The guard's list holds only the composition point's rows, which phase 3 removes.

## 3. Later phases (the supervisor sequences them)

- [ ] 3.1 Phase 3: VB6 registers as a bundled first-party add-in, and the registration grant switches from the Desktop composition's origin to a first-party signature with the `hexide.basic` id. The language editing registration moves from Core's internal `ILanguageEditingRegistration` onto `IHexIdeHost` then, with the contracts it reaches baselined as add-in API: about 9 types and 125 lines, measured on 2026-11-25 (#5403). Once third-party code can register, a reader the code window calls on the UI thread needs a time budget or moves off that thread, and its answers are checked before they're used. The run engine's registration moves from Core's internal `IRunEngineRegistration` onto `IHexIdeHost` then too, with the engine and debugger contract it reaches baselined as add-in API (#5616, #5619).
- [ ] 3.2 Phase 4: the namespace renames the proposal names, in one mechanical PR, announced as a breaking API change.
- [ ] 3.3 Phase 4: the repository split.
