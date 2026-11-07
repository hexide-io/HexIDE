# Code printing proposal (#45)

Proposal for supervisor review and Jen's approval before implementation. Nothing in this document
changes the current Print, Print Setup or Project Explorer commands: they remain stubs.

## Scope and fidelity

The issue's decision is code text first: selection, module and project. Form-image printing is a later
issue. The issue records VB6's Range choices (Selection, Current Module, Current Project), Print What
choices (Code, Form Image, Form As Text), and Setup. These are the only VB6 dialog facts relied on here.
The defaults and layout rules below are **proposed HexIDE behaviour**, not measured VB6 behaviour.
VB6's font, margins, pagination, module ordering, hidden attributes and Form As Text semantics remain
unmeasured; this proposal makes no fidelity claim for them. Runtime `Printer` and CommonDialog's
`ShowPrinter` are separate work.

## Recommended path

Use one immutable code snapshot and paginator, with platform adapters that receive positioned text
runs and page geometry. Use Skia for text measurement and PDF output; Windows also renders those same
runs into a bitmap for its printer DC. This avoids relying on a PDF reader's shell print verb, which
would hand off destination, cancellation and error handling to another application.

| Platform | Proposed print dialog and page setup | How a page reaches the printer | Trade-off |
|---|---|---|---|
| Windows | HexIDE scope dialog, then `PrintDlgExW` for printer, copies and driver properties. Setup opens `PageSetupDlgW` for paper, orientation and margins, alongside HexIDE's font and line-number settings. | Obtain the selected printer DC; `StartDocW`, then `StartPage`, `StretchDIBits` with a Skia-rendered page, `EndPage`, and `EndDoc`. Abort the document on failure. Query printable bounds and device resolution from that DC. | Native driver support without a Windows-only managed framework; raster text costs memory and loses vector sharpness. Render one page at a time at device resolution, with a bounded allocation; report inability to render instead of silently reducing quality. |
| macOS | HexIDE scope dialog, then AppKit's print panel through `NSPrintOperation`. Setup uses `NSPageLayout` and a private `NSPrintInfo`, alongside the shared font and line-number settings. | Build PDF with Skia, load it into PDFKit's `PDFDocument`, obtain `printOperation(for:scalingMode:autoRotate:)`, and run it with the print panel. Use actual-size printing and disable automatic rotation. | Vector PDF and the system print panel; requires a small native interop adapter and careful Objective-C ownership, main-thread calls and window parenting. |
| Linux | HexIDE-owned printer dialog backed by CUPS destinations and supported media; shared Setup supplies paper, orientation, margins, font and line numbers. | Generate PDF; submit with CUPS `lp`, an explicit destination and explicit media/copy options. Check exit status and retain the returned job identifier. | No GTK dependency or second toolkit event loop, and works independently of GNOME/KDE. The dialog is less native and CUPS availability, queue discovery and authentication need live validation. |

The Windows print dialog can return a printer DC ([Microsoft's guide](https://learn.microsoft.com/en-us/windows/win32/printdocs/retrieving-a-printer-device-context)).
PDFKit supplies a print operation for a PDF document ([Apple API](https://developer.apple.com/documentation/pdfkit/pdfdocument/printoperation(for:scalingmode:autorotate:))).
`NSPageLayout` works with `NSPrintInfo` ([Apple API](https://developer.apple.com/documentation/appkit/nspagelayout/printinfo)).
CUPS accepts PDF, and `lp` submits jobs with destination, copies and media options
([CUPS overview](https://github.com/OpenPrinting/cups), [lp manual](https://openprinting.github.io/cups/doc/man-lp.html)).
These establish API paths, not proof that an Avalonia integration already works.

On Linux, propose CUPS 2.4.x client tools supplied by the distribution: `lpstat` for queues/default,
`lpoptions` for available options and `lp` for submission. Use argument lists without a shell, bounded
execution time and captured diagnostics. Do not parse translated prose as a stable protocol: use the
commands' structured option tokens and cover discovery with fixtures; if discovery proves unreliable,
review a libcups destination-query adapter rather than silently guessing media. Do not install tools or
start a daemon automatically. If CUPS is absent, unreachable or has no destination, explain that printing
is unavailable; allow retry or cancellation. Sandboxed distributions and interactive authentication are
an explicit integration risk to resolve before declaring Linux support complete.

Native dialogs may change paper or orientation after Setup. Windows and macOS adapters must collect the
final settings and repaginate before submitting; on macOS the print operation must use the resulting
PDF, not scale a stale one. Prove that settings can be reconciled through the panel lifecycle in an early
interop spike. If this cannot be done reliably, return to the scope/setup dialog for confirmation and
regenerate before reopening the native panel. Never silently scale or crop. Native page-number ranges
refer to the final selected source's pages, not to module numbers; keep scope selection in HexIDE.

## Dialog and page setup choices

Propose a shared, localized Avalonia dialog before the platform dialog. It supplies the VB6 scope choices
without trying to insert identical custom controls into three different native dialogs:

- **Selection:** enabled only for a non-empty code-editor selection. Capture the selected text before
  focus leaves the editor. Propose stream selections first; rectangular selections need a separately
  reviewed rule before enabling this choice for them.
- **Current Module:** the active code document; a designer or Project Explorer module resolves to its
  associated code. Disable it when no code module can be identified.
- **Current Project:** code-bearing modules of the selected project, in project model order, including
  unsaved editor text. Exclude carried files and serialized form layout. Start each module on a new page
  and identify its name. A project group requires one selected project, not an implicit merge.
- **Print What:** Code is the sole enabled choice. Show Form Image and Form As Text as unavailable with
  an explanation. Hiding them is simpler, but visible disabled choices explain the scoped VB6 surface.
  Do not treat Form As Text as an alias for code without a measurement and decision.
- **Setup:** available both here and from File → Print Setup; changes configuration without sending a job.
  Accept commits session settings; Cancel discards edits. Session-only storage is the proposed first cut,
  avoiding printer-specific state in `.vbp` files. Print Setup does not itself print.

The final native dialog selects the destination on Windows/macOS; Linux's HexIDE dialog selects it from
CUPS. Native cancellation sends nothing. File → Print and Project Explorer → Print share this flow,
with the latter setting its context explicitly. A separate module/project printing command would be
simpler to wire but would lose the recorded scope choices.

| Setting | Proposal | Alternative weighed |
|---|---|---|
| Paper and orientation | Start from the selected printer's reported default; offer supported sizes and portrait/landscape. Resolve these before pagination. | Hard-coded A4 or Letter is predictable but wrong for some printers and regions. No VB6 default is assumed. |
| Margins | Shared editable margins in physical units, stored internally in points (72 per inch). Propose 12.7 mm initially, clamped visibly to hardware minimums. Reject settings leaving no printable body. | Native-only margins differ between platforms; ignoring hardware bounds clips code. The proposed initial value needs approval, not a VB6 claim. |
| Font | Shared font chooser and point size, initially the editor's configured family and size; black text on white paper. Measure with the actual resolved font; use explicit fallback for missing glyphs. | Copying theme colours wastes ink; a fixed font ignores accessibility and local scripts. Font embedding rights and fallback must be checked before PDF generation. |
| Line numbers | Optional, initially off. If enabled, use original module line numbers, including for a selection; continuation rows have no new number. | Renumbering selection lines is easier but obscures where they came from. This is a proposal, not a measured VB6 rule. |

Propose soft wrapping long lines without losing characters, preserving indentation and tabs using the
editor's tab width. Account for the number gutter before wrapping. Do not print hidden serialization
attributes or invent form text; print the editable code snapshot. Normalise line endings for layout
without changing the documents. Validate font size and content area, and paginate incrementally so a
large project cannot allocate every raster page at once. Printer settings never modify source files.

## Dependencies and alternatives

No package is added by this proposal. The candidate renderer is **SkiaSharp
3.119.3-preview.1.1**, already resolved transitively in this checkout through Avalonia. Its prerelease
status is a review concern: do not upgrade or replace Avalonia's native Skia stack independently to get
printing. Propose reusing that exact resolved version, with an explicit central pin and licence entry
if implementation introduces a direct reference. Verify native asset compatibility on all three hosts.
`SKDocument.CreatePdf` provides PDF output in point units
([API reference](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skdocument.createpdf?view=skiasharp-3.119)).

Licence checked on 2026-10-25: the pinned version's
[NuGet registration leaf](https://api.nuget.org/v3/registration5-gz-semver2/skiasharp/3.119.3-preview.1.1.json)
points to its [catalog entry](https://api.nuget.org/v3/catalog0/data/2026.02.11.12.51.43/skiasharp.3.119.3-preview.1.1.json),
whose `licenseExpression` is **MIT** (permissive). Any later candidate/version must be checked again,
including newly direct native-asset packages. Jen's dependency approval remains outstanding.

Windows common dialogs/GDI and macOS AppKit/PDFKit are system APIs, used by locally authored interop;
no native framework is copied into the repository. Linux's proposed external CUPS 2.4.x requirement is
[Apache-2.0 with its linking exception](https://openprinting.github.io/cups/), supplied by the host,
not bundled. Driver/filter licences belong to that installation; no driver or filter code is imported.
These system requirements and the renderer need approval before implementation.

Alternatives: GTK's print dialog provides Linux printer integration but brings another toolkit and
native lifecycle into Avalonia; prefer the smaller CUPS process boundary for this first version.
A PDF-only workflow is portable and useful for diagnosis but does not implement printing or printer
setup. WPF/WinForms printing would add a Windows-only UI framework. A commercial PDF/printing library
adds a new licence and deployment cost without removing the platform-dialog question. A hand-written
PDF writer avoids a package but duplicates font and Unicode work; do not build one.

## Verification and review gates

Headless tests during implementation should cover scope/context resolution, unsaved snapshots,
selection offsets, project ordering, exclusion of carried files and form layout, cancellation, invalid
settings, wrapping/tabs/Unicode, line numbering, hardware-margin transforms and repagination after
paper changes. Use fixed fonts for deterministic layout assertions and normalise line endings in
source-text assertions. Test platform orchestration through adapters: no submit on Cancel, native
failure propagation, resource disposal, CUPS arguments/exit failures/timeouts and temporary-file cleanup.
PDF generation and page geometry can be tested without a printer; these cannot prove driver output.

Live checks on **each desktop platform** must verify ownership/modality and focus restoration, keyboard
access, translated labels, native Setup/Print cancellation, printer discovery and switching, copies,
portrait/landscape and different paper sizes, physical margins, actual-size output, Unicode glyphs and
long-line wrapping. Compare a multi-module job and a selection to their unsaved editor text. Exercise a
virtual PDF printer and a physical printer, offline/no-printer errors, and macOS's final-settings
repagination. Linux needs both X11 and Wayland plus GNOME/KDE checks; headless tests cannot prove these
surfaces render, that authentication works, or that a queued job reaches paper.

Use private temporary files with per-job names and delete them after the backend has consumed them,
including error/cancel paths. Spool acceptance is not physical completion: report the job as submitted,
not printed. Avoid logging code text or retaining generated PDFs by default.

Review is requested for the platform adapters, Linux CUPS requirement, reuse of the prerelease Skia
version, proposed layout defaults and scope rules. After approval, record the accepted contracts in
OpenSpec before building. This note leaves all implementation and live verification open; feature and
language coverage statuses remain unchanged.
