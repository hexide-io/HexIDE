# Composite workspaces — review draft (#1207)

This is a proposal for [#1207](https://github.com/hexide-io/HexIDE/issues/1207), supporting
[#1206](https://github.com/hexide-io/HexIDE/issues/1206). The supervisor's 2026-10-29 comment asks
for a draft with open questions, not accepted decisions or an implementation. Nothing here changes
current behaviour. Recommendations inherited from the issue are identified below; the alternatives
remain available for review. No implementation slice should treat the illustrative JSON as a settled format.

## Motivation and scope

Open Folder should discover a composite workspace without requiring somebody to reproduce project
membership in a second file. The motivating cases are project roots (#261), a VB6 project with LaTeX
documentation (#279), and the related folder workflows (#455 and #462), as identified by the brief.
Their full issue histories were not supplied; this draft makes no additional claims about their requirements.
A small, shared descriptor would override discovery when a repository needs information markers cannot convey.

The proposed deliverable after review is an OpenSpec change named `composite-workspace`: a proposal,
design, schema, examples, tasks and capability deltas. This draft deliberately does not add live requirements,
select disputed policies, implement discovery, launch providers or change persistence.

## Current contracts and intended spec boundaries

The [LSP client specification](../../../openspec/specs/lsp-client/spec.md) currently routes extension
claims to multiple servers and uses priority for features requiring one answer. Its working-directory
requirement restarts servers when the project directory moves and reoffers **every** open editor's latest
text before changes. Its later outside-source requirement supplies additional initial workspace folders
and folder-change notifications when advertised. A composite design must preserve those guarantees rather
than regress them to startup-project-only routing.

The [Project Explorer specification](../../../openspec/specs/project-explorer/spec.md) describes project
members and carried files, including members outside a project's directory. The
[state persistence specification](../../../openspec/specs/ide-state-persistence/spec.md) describes per-user
geometry and layout, not a workspace-root contract. There is currently no `project-system` capability in
`openspec/specs/`. The issue's proposed relocation therefore needs an explicit review of which requirements
actually move; it must not silently move layout preferences into a repository descriptor.

| Proposed delta | Content to draft after the questions below are resolved |
|---|---|
| New `workspace-descriptor` | Discovery, cascade, declarative fields, validation and trust boundary |
| `project-explorer` | Composite root, discovered sub-workspaces, shared-file conflict indication |
| `lsp-client` | Ownership eligibility, primary selection, contributor aggregation and document transitions |
| New `project-system` | Root identity and lifecycle, with cross-references from LSP and persistence where needed |

Each eventual live requirement belongs inside `## Requirements`. This document is not a spec delta and
must not be archived as if the described behaviour already shipped.

## Discovery and adapter markers

**Issue direction:** discovery works without a descriptor; adapters register marker kinds, and the
descriptor overrides rather than enables discovery.

**Questions:** What is the initial marker set? A conservative candidate is `.vbg` and `.vbp` using the
existing VB6 project readers. Adding language markers such as a LaTeX root declaration improves folder
workflows but needs an adapter and an ambiguity policy. An extension such as `.tex` alone indicates a
language, not necessarily a project root. Which additional markers have an adapter ready to own them?

What registration contract should an adapter expose? A declarative marker pattern plus adapter id is
simple and inspectable; a discovery callback can validate content and derive membership but requires
execution and trust handling. Should discovery run recursively, lazily as folders expand, or with a bounded
scan? Recursion finds nested projects immediately but costs time and risks traversing generated trees.
How are links, overlapping markers, duplicate projects and inaccessible directories treated? Following links
finds external projects; refusing traversal is simpler but can hide intentional layouts. Neither is selected.

## Cascade and globs

**Issue direction:** nearest descriptor wins per setting; `root: true` stops traversal. Otherwise traversal
ends at the repository root, or at the opened folder when no repository exists. Glob sections are local to
one file rather than assembled from multiple files.

**Questions:** Does repository root mean the worktree root, including linked worktrees and submodules?
May ancestors above the opened folder participate when it lies inside a repository? What happens when an
opened folder spans nested repositories? A repository boundary is predictable; independent nested boundaries
better isolate configuration but complicate the composite model.

Are paths and globs relative to the descriptor containing them? This is easy to relocate; opened-folder
relative paths are easier to display centrally but make inherited descriptors context-dependent. Which glob
grammar, case rules, negation and within-file tie order apply? First match is easy to explain; last match
allows later exceptions; most-specific match needs an exact definition. Does `null` reset an inherited scalar,
or is resetting explicit? Omission can inherit under either choice.

List policies must be approved **per field**, not by a universal merge function:

| Candidate field | Open question and trade-offs |
|---|---|
| `ignores` | Append (issue suggestion) preserves parent exclusions; replace allows recovery of an excluded subtree; explicit negation could combine both but needs ordering rules. |
| `ownership` | Replace (issue suggestion) prevents accidental mixing of ordered rules; append permits small overrides but makes precedence across files harder to explain. |
| `roots` | Replace gives a complete local answer; union preserves discovered/ancestor roots but needs deduplication and removal semantics. Does this field add to or replace discovery? |
| `recommendedProviders` | Union accumulates advice; replace lets a subtree retract unsuitable recommendations. Neither authorizes execution. |

Should invalid descriptors fail only their overrides and preserve discovery, or block the affected subtree?
Fallback keeps files usable but risks acting on unintended settings; blocking is explicit but disruptive.
Should unknown fields be warnings or errors? Warnings support forward compatibility; errors catch misspellings.

## Ownership, primary and contributors

**Issue recommendation:** project membership, then nearest descriptor rule, marker inference, and finally
`lsp-servers.json`. For membership in projects of two systems, keep both providers as contributors, show a
conflict in Project Explorer and allow a descriptor rule to choose the primary.

**Questions:** Is that precedence ordering accepted? How does a descriptor settle a primary when membership
otherwise outranks it? One candidate separates contributor eligibility (membership retains both) from primary
selection (an explicit rule resolves the conflict). Another lets a rule override membership entirely, which
is simpler but may remove useful diagnostics. Which files constitute a conflict: two systems, two projects
of one system, or only incompatible primary claims? Can a rule remove a contributor explicitly?

| Feature | Issue direction or question |
|---|---|
| Formatting, rename, go to definition, code actions that edit | Issue proposes one primary; how does unavailable capability fall back, and can users select another provider? |
| Diagnostics, hover, completion | Issue proposes merged contributions; how are duplicate results, ordering, provenance and stale results handled? |
| Folding, document symbols, signature help, semantic tokens and other existing requests | Preserve current per-feature rules until reviewed; does primary replace priority for all of these or only the named operations? |

Today's global `priority` orders claimants, with user registrations ahead of defaults. Should workspace
primary outrank that priority only within its scope, or should priority remain the final selector among
eligible contributors? Scoped selection expresses project intent; global priority preserves existing user
preferences. What breaks a tie, and what does the inspector say when a provider loses? Edit-producing code
actions need one selected authority or an explicit user choice; combining unrelated edits risks conflicts.

## Roots and open documents

**Issue direction:** a provider instance receives its sub-workspace roots as `workspaceFolders`; a provider
without multiple-folder support gets a process per root.

**Questions:** What identifies an instance: configured provider id plus roots, or id plus settings as well?
Sharing saves processes; separate instances isolate incompatible project settings. How does a single-root
server's launch directory and `rootUri` relate to its folder? What happens before capabilities are known,
and how are servers that advertise folders but cannot change them handled? Restarting is predictable;
notifications avoid churn but require explicit support.

For a composite edit, candidate transition semantics are: compute the new ownership/root snapshot, stop
routing to removed connections, close documents leaving surviving connections, update folders where
supported (restart otherwise), and open each newly eligible document with its latest editor text before
sending changes. Preserve edits, dirty state, URI and editor tabs independently of server lifetime. Review
this against #469 and #1051 rather than assuming those issue histories are settled by this draft.

Should requests be cancelled during a transition or finish against a versioned snapshot? Cancellation
avoids stale edits; snapshot completion reduces disruption but requires rejecting stale responses. How are
diagnostics withdrawn when ownership changes? How are unsaved documents assigned before a path exists?
What happens on Save As, root deletion, changed project membership or a descriptor edited into an invalid
state? Retaining the last valid model avoids churn; immediate fallback makes disk state authoritative.
These lifecycle cases need accepted scenarios before implementing the roots slice.

## Trust and configuration boundaries

**Issue direction:** descriptors are declarative and name already configured providers. They never supply
commands, arguments or environment variables. Recommendations are advisory. Trust gates anything that runs;
a trust decision is stored per user, never in the folder.

**Questions:** Is trust keyed by canonical folder, repository identity or both? Paths are simple but clones
move; repository identity travels but must not transfer trust to an unrelated checkout. Does a changed
provider configuration or widened root invalidate consent? What can happen before trust: static marker
inspection only, or in-process adapters too? Are externally configured providers automatically allowed once
a folder is trusted, or is provider-specific consent separate? The latter is more precise but adds prompts.
Existing workspace-artefact consent must not accidentally become blanket execution trust.

**Issue's settings test:** true for everyone who clones the folder. The candidate shared surface is
ownership, ignores, roots, ANSI code page and personality. Honour `.gitignore` and `.editorconfig` rather
than duplicating their contents. Theme, keymap and layout remain personal and are refused here.

**Questions:** How do descriptor ignores interact with `.gitignore`, especially tracked or explicit project
members? Does `.editorconfig` win over overlapping editor settings? Which code-page values are supported,
what scope do they cover, and how are decode failures and unsaved edits handled on a change? The example
uses the issue's requested 932; it asserts no new VB6 measurement. What personality vocabulary and profile
migration apply? Should a user override shared defaults for a session, with a visible explanation? Which
external roots are allowed, and do they require separate trust? A declarative path can still widen what a
provider reads even though it is not an executable command.

## Name, format and illustrative schema

**Issue recommendation:** `.hexide.json`, JSON and a published schema. JSON offers existing validation and
editor support; `.folder` is less explicit about ownership and format. A generic name could aid interoperability
but would need a separately agreed format. JSON comments/trailing commas are convenient for humans but
would require a deliberate JSON-with-comments contract. Versioning could be an explicit integer or tied to
the schema URI; an integer supports migration even when tools ignore `$schema`, at the cost of two version cues.

**Questions:** Accept the name and strict JSON? Where will the schema be published, how is its stable URI
versioned, and what is the unsupported-version policy? Do properties use the spellings below? No publication
URL is invented here. The local [draft schema](descriptor.schema.json) is only a review aid. Its list shapes,
field names and validation constraints are provisional, not accepted policies. It cannot validate discovery,
merge semantics, registered provider identities, trust, glob meaning or filesystem confinement.

[Three examples](examples.json) each carry `$schema` pointing to that local draft. They illustrate VB6 plus
LaTeX documentation, a group with separate project roots, and a Japanese codebase using code page 932.
The first hypothetical folder contains `App.vbp` and `docs/manual.tex`; project discovery supplies the VB6
membership while the descriptor identifies the documentation provider. The second contains `Suite.vbg`,
`Client/Client.vbp` and `Service/Service.vbp`; the listed roots illustrate separate analysis directories
for the group's projects. The third contains `App.vbp` and Japanese source files.
Provider ids are placeholders for already configured entries, not installation instructions. Paths describe
hypothetical repositories. The examples select no answer about merging, matching or execution.

## Review exit criteria

Resolve the questions above explicitly before producing executable slices. Convert accepted answers into
OpenSpec requirements and scenarios, including list-specific merge tests, conflicting membership, a server
with one root, a root change with two dirty open editors, untrusted discovery and invalid descriptors.
Keep any unresolved question visible in the change's design and tasks. Publish the schema only once its
syntax and version policy are accepted. This draft leaves implementation and all unresolved choices open.
