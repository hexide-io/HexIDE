# Tasks

This change records contracts and a design only. Every phase below is unchecked. Implement one numbered phase per
delivery, with tests that fail before each behaviour change, and do not archive this change until all are done.
Phases 1 and 2 wait on Jen's decisions D1 to D3 in `design.md`.

## 1. Spikes that settle the design

- [ ] Measure whether the inbox console host on Windows build 26100 keeps OSC 8 and OSC 10/11, through ConPTY, on
  this machine and the oracle VM. Record the answer in `design.md` and decide whether falling back to it is honest.
- [ ] Measure which ConPTY glyph-width flag agrees with the engine's widths: one stream of emoji, VS16 sequences and
  wide characters, compared cell by cell.
- [ ] Try `posix_spawn` with a setsid flag on macOS, to find whether the child acquires its controlling terminal.
  Record the result; it decides whether the native shim can later be retired.

## 2. Dependencies and the engine seam

- [ ] Once D1 is decided: re-read each package's licence from the registration API at the exact version, record it
  in `scripts/package-licences.tsv`, and pin it in `Directory.Packages.props`.
- [ ] Add `ITerminalEngine`, HexIDE's own interface over the engine (feed bytes, read cells and attributes, resize,
  encode keys and mouse, search, selection), and its adapter. Nothing outside the adapter references the package.
- [ ] Write the conformance suite against the interface, with synthetic streams for every row of v1's rendering
  list and the shapes #2482 measured, including OSC 10/11/110/111, OSC 8 and two-cell emoji with VS16.

## 3. Hosting and lifetime

- [ ] Add `IPseudoTerminal`, with HexIDE's own Windows host over the ConPTY redistributable (draining output while
  closing, answering the host's start-up queries) and the Linux and macOS host over the chosen package.
- [ ] Add `ChildProcessJob.Assign(SafeProcessHandle)`, and start `job` profiles suspended, assigned, then resumed.
  Test that a `job` profile's grandchild ends with the session, and a shell's detached program does not.
- [ ] Integration tests per platform: start, run a command, read output, resize, close, and assert no process is
  left. Run them on Linux and Windows in CI. Add the console host binaries to the Windows installer.

## 4. Rendering

- [ ] The terminal control: device-pixel cell metrics, glyph runs with cell advances, colour emoji fallback by
  cluster, cursor shapes, selection, and the sixteen palette keys plus defaults, cursor and selection in
  `Themes/Classic.axaml` and every theme pack.
- [ ] Parse off the UI thread, draw once per frame from dirty rows, and stop reading when parsing falls behind.
  Test a stream of tens of megabytes without a frozen UI.
- [ ] Verify live at 150%, by snapshot and by the automation readout, on Windows and Linux.

## 5. Input

- [ ] Key routing with each platform's reserved chords, Ctrl+C copy-or-interrupt, Linux's Ctrl+Shift+C and V,
  bracketed paste, mouse and focus reports. Never hard-code Ctrl in a key test.
- [ ] IME through `TextInputMethodClient`: candidate window at the cursor, preedit drawn in place, committed text
  sent as UTF-8.

## 6. Sessions and profiles

- [ ] Profile detection per platform, `terminal-profiles.json` in the user data directory, a default profile, and
  profiles a provider suite contributes. Document the file in `docs/`.
- [ ] Sessions as dock tools under View → Terminal and New Terminal; exited sessions with exit code and restart;
  `--user-data-dir` keeping a session's own profiles. Translate every new key into all shipped packs.

## 7. Links, scrollback and security

- [ ] OSC 8 and detected `http`/`https` links, opened on Ctrl+click or Cmd+click after the target is shown;
  scrollback with its setting; OSC 52 ignored; no reports that echo program text.

## 8. Automation and accessibility

- [ ] `read_terminal`, with its reply's empty and ended states, the wide-cell note, `waitFor`, `[DescribesEnum]` on
  every enum it renders, and its description under the cap. Check the generated schema's `required` array is empty.
- [ ] A headless first-contact run told to work from the descriptions alone, recorded in `docs/mcp-first-contact.md`.
- [ ] The automation name and value; file the missing text pattern as an issue against this change.

## 9. Clients

- [ ] An external backend's interactive shell, run as a profile, rendered against #2482's measurements, checked live.
- [ ] The in-process session seam for #1108, with a test that feeds it as the Console library will.
