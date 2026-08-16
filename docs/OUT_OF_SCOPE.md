# HexIDE — Explicitly Out of Scope

VB6 features listed here will **not** be implemented in HexIDE. This is a deliberate design boundary, not a backlog. If a feature isn't listed here it is either implemented, in progress, or tracked as missing in [MISSING_FEATURES.md](MISSING_FEATURES.md).

> **Note on COM features:** Some VB6 features depend on the Windows COM/OLE infrastructure. These are *not* out of scope — they remain in active use by VB6 developers — but they are platform-gated (`OperatingSystem.IsWindows()` only). They are tracked in the Windows-only section of [MISSING_FEATURES.md](MISSING_FEATURES.md), not here.

---

## Project types

| VB6 feature | Reason |
|---|---|
| **DHTML Application** | Browser-hosted VB script layer on top of IE4 DHTML. Obsolete technology — Internet Explorer is dead and there is no cross-platform or modern-browser path. |
| **IIS Application (WebClass)** | Server-side web framework tied to IIS COM interop. A dead technology replaced by ASP.NET. |
| **ActiveX Document (.dob / User Document)** | ActiveX Documents hosted inside Internet Explorer or Office binders. No value in 2026; both host environments are effectively defunct. |

## IDE designers and project items

| VB6 feature | Reason |
|---|---|
| **More ActiveX Designers** | Proprietary third-party designer plug-ins loaded via COM. The plug-in mechanism requires Win32 subclassing that is incompatible with Avalonia's rendering model, making this unworkable even on Windows. The correct long-term path is a managed extensibility API. |

## IDE integrations and tooling

| VB6 feature | Reason |
|---|---|
| **SDI mode** | The single-document interface mode was rarely used and adds significant layout complexity for negligible value. |
| **Source Safe integration** | Visual SourceSafe is a dead product and will never be supported. (Version control in general is no longer delegated wholesale to external tools — staged first-class **Git** integration is planned for the Modern shell.) |
| **COM add-in hosting** | The popular VB6 add-ins rely on deep Win32 subclassing that is incompatible with Avalonia's rendering model — this is blocked by architecture, not just COM, and would not work even on Windows. A managed extensibility API is the correct long-term path. |
| **Package and Deployment Wizard** | Produces MSI/CAB installers using VB6's native runtime DLLs. Incompatible with the HexIDE interpreter model. Projects requiring a deployable EXE use the *Make with VB6…* path (shells out to VB6.EXE if installed). |
| **Data Environment Designer** | Enterprise add-on (`MSDATED.DLL`) that was niche even in 1998 and is unused today. Related hidden menu items (*Save Selection*, *Save Change Script*, *Delete Table from Database*, *Select All Columns*) are permanently hidden. |
| **Visual Data Manager** | VB6-era database browser backed by DAO/ADO. Enterprise tooling that saw minimal adoption and has been replaced by modern database tooling outside the IDE. |
| **Visual Component Manager** | Enterprise add-on for storing reusable ActiveX components in a COM-registered repository. Essentially unused since VB6's commercial peak; no value in 2026. |

## Execution model

| VB6 feature | Reason |
|---|---|
| **Native compilation** (*Make {project}.exe*) | HexIDE runs VB6 code in its own interpreter (`HexIDE.Runtime`). Native compile produces a VB6-runtime-dependent Win32 binary; this is only available via the *Make with VB6…* bridge (shells to VB6.EXE). No separate native compiler is planned. |

## Platforms

| Platform | Reason |
|---|---|
| **Android** | Not supported. Projects deleted. |
| **iOS** | Not supported. Projects deleted. |
| **Browser / WASM** | Moved to out of scope. The add-in system requires `Assembly.Load` at runtime, which is incompatible with AOT publication — and AOT is a hard prerequisite for a viable WASM build. Removing AOT also removes the only viable WASM path. The WebSocket-hosted LSP idea is separately deferred (it remains interesting but no longer drives any current design work). |

---

## UI surface items

Individual menu items and toolbar buttons that appear in the IDE UI but belong to out-of-scope features. Each entry cross-references the feature-level rationale in the sections above.

### File Menu

| Item | Reason |
|---|---|
| **Save Selection** | Hidden (`IsVisible=False`); belongs to the Data Environment Designer. |
| **Save Change Script** | Hidden (`IsVisible=False`); belongs to the Data Environment Designer. |

### Edit Menu

| Item | Reason |
|---|---|
| **Delete Table from Database** | Belongs to the Data Environment Designer. |
| **Select All Columns** | Belongs to the Data Environment Designer. |

### Project Menu

| Item | Reason |
|---|---|
| **Add User Document** | User Document project items are out of scope; see Project types above. |
| **More ActiveX Designers…** | Proprietary third-party COM designer plug-ins; see IDE designers and project items above. |

### View Menu

| Item | Reason |
|---|---|
| **Visual Component Manager** | Enterprise add-on; see IDE integrations and tooling above. |

### Tools Menu

| Item | Reason |
|---|---|
| **Publish** (submenu) | Publishes ActiveX controls and DHTML Applications to web pages. Both underlying project types are out of scope. |

### Add-Ins Menu

| Item | Reason |
|---|---|
| **Visual Data Manager…** | Enterprise database browser; see IDE integrations and tooling above. |
| **Add-In Manager…** | COM add-in hosting is out of scope; see IDE integrations and tooling above. The *managed* Add-In Manager (Phase 44 and later) is in scope — it replaces this item when implemented. |

### Standard Toolbar

| Item | Reason |
|---|---|
| **Visual Component Manager** | Enterprise add-on; see IDE integrations and tooling above. |

### Toolbox

| Item | Reason |
|---|---|
| **Data** | DAO/ADO-bound data control. Database-binding infrastructure is out of scope for Standard EXE projects. |
