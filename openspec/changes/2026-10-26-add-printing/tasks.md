# Tasks

This run records contracts only. All implementation phases remain unchecked. Implement one numbered
phase per delivery, with tests that fail before each behaviour change; do not archive this change yet.

## 1. Snapshot and paginator

- [ ] Add immutable selection/module/project snapshots, including unsaved editor code, original line
  offsets, project order and exclusions; cover missing context and rectangular selection refusal.
- [ ] Add shared layout in points: hardware bounds, margins, resolved fonts/fallback, tabs, wrapping,
  Unicode, optional original line numbers and module page breaks. Validate invalid geometry and sizes.
- [ ] Reuse SkiaSharp exactly at Avalonia's resolved 3.119.3-preview.1.1; add the explicit central pin
  and MIT licence entry before direct use, rechecking registration metadata and native compatibility.
- [ ] Test PDF geometry with fixed fonts and incremental page rendering without a printer; normalise
  source line endings in assertions. Check embedding rights and fallback before PDF output.

## 2. Shared print and Setup flow

- [ ] Wire File Print, File Print Setup and Project Explorer Print to explicit context and scope.
- [ ] Add localized shared scope and session Setup dialogs, disabled unsupported choices, accepted
  defaults, settings validation, hardware-margin feedback and Accept/Cancel semantics.
- [ ] Test context, selection capture before focus loss, session reset, no source changes and no
  submission on Setup or cancellation. Translate all new keys into every shipped full pack.

## 3. Windows adapter

- [ ] Integrate PrintDlgExW and PageSetupDlgW with owner windows and final driver settings; repaginate
  after changes, query bounds/resolution and submit one bounded raster page at a time through the DC.
- [ ] Cover StartDoc/StartPage/StretchDIBits/EndPage/EndDoc failure, AbortDoc, cancellation and resource
  disposal through an adapter seam; prove native settings reconciliation before declaring support.
- [ ] Verify native dialogs, focus, copies, paper/orientation, actual-size margins and glyph output
  with a virtual PDF printer and physical printer, including offline/no-printer paths.

## 4. macOS adapter

- [ ] Spike final-settings reconciliation through the print panel before submission; use a private
  NSPrintInfo, NSPageLayout and PDFKit print operation with actual-size output and no auto-rotation.
- [ ] Integrate main-thread ownership and Objective-C lifetime handling. Regenerate PDF after final
  settings changes; use the documented confirmation/reopen fallback if required, without stale scaling.
- [ ] Test orchestration, cancellation, final geometry and cleanup headlessly; live-check print panel
  lifecycle, parenting, copies, changed media, physical/PDF output and failure paths on macOS.

## 5. Linux adapter

- [ ] Discover destinations/defaults/options with host CUPS 2.4.x lpstat/lpoptions and submit PDF with
  lp, explicit destination/media/copies and captured job id. Use argument lists, bounded execution and
  structured tokens; test fixtures, diagnostics, timeouts, no destinations and unavailable tools.
- [ ] Add the HexIDE destination dialog without GTK. Do not install tools/start services; offer retry
  or cancellation for unavailable printing. Resolve unreliable discovery by review before substituting
  a native query adapter; do not guess printer media.
- [ ] Verify X11/Wayland and GNOME/KDE, authentication, sandbox constraints, queue switching and
  PDF/physical output before declaring Linux complete.

## 6. Completion and publication

- [ ] Run full projects holding changed tests (foreign downloads disabled for HexIDE.Tests), Desktop
  Debug build with zero warnings, strict OpenSpec validation and tree hygiene for each delivery.
- [ ] Verify all dialogs live for keyboard access, localization, focus/modality and cancellation;
  compare selections and multi-module jobs with unsaved editor text, long lines and Unicode.
- [ ] Verify private per-job temporary-file cleanup on success/failure/cancel and report spool
  acceptance as submission, never physical completion; retain no code/PDF logs by default.
- [ ] Update docs/printing.md, MISSING_FEATURES rows and changelog as behaviour ships; leave
  MISSING_LANGUAGE unchanged unless separate runtime work changes it.
- [ ] Only after implementation and live checks, validate and archive through the CLI; inspect the
  merged printing Purpose and contracts. Form-image printing remains a separate later issue.
