# Proposed design — awaiting Jen's decision

## Context and limits

`AddinManifest` currently uses schema 1. Unknown fields survive as extension data, but do not alter
behaviour. `IHexIdeHost` exposes editor/project/diagnostic reads, editor writes, contributions, events
and project execution. It has no language-server registration, general file-write or external-process
launch API today. The vocabulary below includes those consequences without pretending those APIs exist.

Add-ins run in-process with the user's privileges. Host checks make cooperative use attributable;
they cannot prevent direct filesystem, process, network or reflection access by malicious code.
Signing identifies the publisher and binds its declaration; revocation is the deterrent. Neither this
proposal nor the consent text may describe the declaration as a sandbox or containment guarantee.

## Candidate vocabulary

All identifiers and copy below are proposals. Categories are independent: declaring one does not
implicitly declare another. Scope is all loaded projects, matching today's multi-project host API.

| Identifier | What the add-in declares it will do | Proposed host API gate | Proposed one-line consent description |
|---|---|---|---|
| `project.read` | Inspect source, selections, project paths/file lists and diagnostics, including lifecycle metadata | Every `IProjectAccess` and `IDiagnosticsAccess` method; `IEditorAccess.GetActiveDocument`/`GetSelection`; subscriptions to project/file events on `IAddinEvents` | Read source code and project information from your open projects. |
| `project.write` | Change editor content or create/change files in an open project | Every overload of `SetContent`, `ApplyEdits`, `TrySetContent`, `TryApplyEdits`; future workspace-artefact writer (#455) | Change source code and create or change files in your open projects. |
| `program.run` | Start/stop the startup project or launch a program | `IHexIdeHost.RunProject`/`StopProject`; future external-process launch API | Run your project or start programs on your computer. |
| `ui.contribute` | Add IDE controls, commands, templates and settings, or navigate a document | `Menus`, `ToolWindows`, `Options` registration; `RegisterCommand`, `RegisterComponent`, `RegisterProjectTemplate`; all `NavigateTo`/`TryNavigateTo` overloads | Add menus, windows, controls and templates, and navigate your code. |
| `languageServer.contribute` | Supply a language server which receives documents for its configured languages | Future add-in language-server contribution seam (#457); no such member on `IHexIdeHost` today | Provide a language server that receives source code for its configured languages. |

Run-state event subscriptions carry no source/path data and need no capability. `Registry` remains
available for add-in identity/status discovery; it must not become a route to another add-in's privileged
host. Disposal/unsubscription is always allowed. Reading source to prepare an edit needs both read and
write declarations. Language-server contribution does not authorize direct editor reads or arbitrary
program launches; the host may start the contributed server and send its configured documents as that
capability's stated consequence. A separate arbitrary launch needs `program.run`.

The broad UI category includes template materialization and control execution as existing contribution
consequences. It does not grant a general workspace writer. Jen should review whether template creation
needs an additional write declaration. Network access and reads outside loaded projects are not assigned
new identifiers in this draft; the in-process limitation must remain visible, and extending the
vocabulary requires a versioned review rather than implying that undeclared direct access is impossible.

## Manifest and signing

Propose schema 2 with a required top-level array adjacent to `entry` and `files`:

```json
{
  "schema": 2,
  "capabilities": ["project.read", "ui.contribute"]
}
```

This is a field excerpt, not a complete loadable manifest. An explicit empty array means no declared
capabilities. Proposed validation rejects null, non-arrays, non-string entries, duplicates and unknown
case-sensitive identifiers before assembly loading or consent. Order carries no meaning, but the existing
raw-byte signing and hashing are preserved: even reordering changes the manifest hash. Packer output
should use a stable order, emit the field and schema 2, and validate before touching output.

The field is inside `addin.json`, not an unsigned sidecar or assembly attribute. The publisher signs
those exact bytes, including the declaration and `files[]`; file hashes transitively cover payload files.
Changing a declaration without re-signing fails verification. Re-signing changed bytes produces a new
manifest hash, so `ConsentStore` finds no earlier grant even if id and publisher are unchanged.
Re-verification after consent must still match the hash shown. No id-keyed grants are introduced.

Schema-1 hosts reject schema 2 under #310, including in developer mode, rather than silently ignoring
these declarations. A schema-2 host must explicitly handle the older-schema policy below; merely adding
a deserialized property would leave the omission hole open. A known capability with no host API yet is
still a declaration, not a promise that the feature is implemented.

## Missing declarations and older manifests — alternatives for decision

| Option | Missing field and older-schema behaviour | Consequences |
|---|---|---|
| A: deny all host capabilities (preferred for review) | Schema 2 without the field is incompatible. Schema 1 and older load only after existing trust/consent checks, with no capability grants. A stray capability field on an old schema does not grant anything. | Existing add-ins may initialize but their privileged host calls fail until repacked. No silent inherited full access. Direct in-process access remains possible. Publishers must migrate. |
| B: explicit legacy full trust | Schema 2 still requires the field. Older schemas without a declaration may load only after a separate, prominent legacy full-trust disclosure in the unified flow. | Preserves old add-ins but offers no declared limits. Show that capabilities are unknown, never an empty safe-looking list. Legacy is a compatibility state, not a new capability identifier. |

Under B, a historical hash-only Allow cannot establish that the legacy disclosure was seen. A consent
record migration/version marker must force that decision once while retaining manifest-hash keying;
#460 would own the flow. No automatic legacy grant based on id, signature or an earlier generic Allow.
Jen must choose A or B (or record another policy). The delta intentionally does not settle this branch.
An explicit schema-2 empty list differs from an omitted legacy declaration in either option.

## Host checks and attribution

Propose a facade per loaded add-in, retaining its verified manifest hash and approved declarations.
Check each operation, including legacy overloads, before data is returned, callbacks/factories run or
side effects occur; denied calls must identify the capability and perform no operation. The concrete
exception/result shape remains a review decision so existing APIs cannot silently report success.
Shared services must not accidentally reuse the last add-in's authority. Contribution cleanup remains
available on failure or unload. First-party pre-consent must not automatically fabricate declarations;
Jen must decide migration of bundled packages and unsigned developer packages explicitly.

## Consumers: #460 and #462

#460 can use one immutable verified request containing manifest hash, identity, declaration list and
legacy state for every consent entry path. It displays host-owned localized descriptions, not publisher
prose. Proposed consent is one decision for the exact build's complete declaration, not per-capability
toggles; Jen must approve that granularity. Revocation and enablement remain separate existing controls.

#462 can display declared capabilities separately from observed host-mediated use, associated with that
same build hash. A successful host operation or subscription delivery can report the capability used;
refused attempts must be labelled refused. No observed use means only that none was recorded through
the host, not that the add-in never accessed files or ran code. Retention, reset, event granularity and
presentation belong to #462 and are not decided here. No source text, paths or arguments need be retained
to identify a category's use. Neither consumer should infer new grants or key them by add-in id.

## Validation plan

After approval, add tests proving validation precedes loading, manifest tampering fails, correctly
re-signed declaration changes re-prompt, old hosts refuse schema 2, and every facade route checks the
right add-in's capabilities before returning data or mutating state. Exercise legacy overloads and
cleanup as well as modern methods. Prove each changed-behaviour test fails on the pre-change product.
Live consent and disclosure checks belong to their implementing issues; this draft adds no UI.
