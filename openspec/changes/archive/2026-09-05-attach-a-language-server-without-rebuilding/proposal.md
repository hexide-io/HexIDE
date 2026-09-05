# Attach a language server without rebuilding, including our own

## Why

The set of language servers is a hardcoded array in `DISetup.cs`. `LspClientRegistry` accepts
`IEnumerable<LanguageServerRegistration>` and exactly one is ever constructed, with a comment that says what
is missing:

> *The one registration HexIDE ships with. A second language means a second entry here — and, before that is
> worth doing, a way to discover servers on disk.*

So a user who has a language server installed cannot attach it. There is no config, no UI, no discovery —
adding one means editing dependency injection and recompiling.

**The plural machinery underneath is already built and already proven.** The router accepts a list, routes by
language to every claimant, merges results, gates each request on advertised capabilities, and starts servers
lazily. It has been driven end to end against a server this project did not write, which is what found the
URI-matching defect and the capability-shape blackout that silently disabled every language feature. This is
not a missing capability. It is a missing *entry point* to one that works.

The risk in leaving it is that the proof decays. The only exercised path is the hardcoded one; the
foreign-server tests construct their registration in test code against a mocked locator and an environment
variable, a route that does not ship. Every month the untested assumption *"servers are configured the way
ours is"* grows back — which is the exact class of defect the foreign-server work existed to kill.

## What Changes

- **The server list becomes configuration, and the bundled VB6 server is a row in it.** Not a special case
  beside the config: the same shape as any other server, overridable and disablable by the user.

- **Compiled-in defaults, with a user file merged over them by id.** The bundled row is contributed in code;
  `lsp-servers.json` in the per-user directory overrides or adds. Deleting the file restores defaults.

- **A registration declares the file extensions it claims**, not only protocol language identifiers.

- **Routing is keyed on extension, and each server is told the language identifier it declared.** Two servers
  may disagree about what `.py` means and both still be served correctly.

- **Servers get the project's working directory** as both working directory and `rootUri`.

- **Config-file only. No new user-facing strings.**

## What This Overturned

**"The bundled server should stay in code, because a user can break their own VB6 support."** That was the
first recommendation and it was wrong, for a reason that outranks the risk: HexIDE is *today* heavily
dependent on its own server, and the point of the seam is to stop being. A server special-cased in code is
not replaceable — it is merely abstracted. Making it a row is what turns "the backend can be replaced" from
a claim in this capability's Purpose into something a user can actually do. The breakage risk is real and is
answered by layering: the defaults are compiled in, so deleting the file restores them.

**"The user typed the path, so attaching a server needs no trust gate."** Also wrong, and for a sharper
reason. That argument holds exactly while the user is the one who typed it. It fails when anything else
writes the file — an installer, a script, a compromised dependency — after which the IDE silently launches
an attacker's executable on every start, forever. That is a persistence mechanism, and it is the shape the
add-in consent design exists to prevent. Full signing is disproportionate for a tool the user already
installed, but *silent obedience* is not the alternative: a changed command line is shown before it is run.

**Theme and keymap packs look like the precedent and are not.** They appear to be exactly this shape —
declarative JSON, choose from a list, applied live. They are embedded resources, and adding one requires a
new file, an edit to a hardcoded array, and a rebuild. Their specs claim otherwise. Following them would
have produced a mechanism that could not do the one thing being asked for.

## Design Notes

**Why extension, not language identifier, is the routing key.** The obvious model — a global
extension-to-language table, then route by language — is what exists, and plurality breaks it. Two servers
can legitimately disagree about the identifier for the same file, and a document carries exactly one
identifier per `didOpen`. But each server has its **own connection**, and nothing in the protocol requires
two connections be told the same thing. Routing on the extension and telling each server the identifier it
asked for dissolves the conflict rather than picking a winner.

**Why a user's server outranks the bundled one by default.** `Priority` already decides the features that
cannot merge two answers — formatting, rename. If the bundled row sat at the default, a user adding their own
would get a tie broken by registration order: deterministic, but accidental, and it varies with discovery.
The bundled row therefore sits *below* the default, so a user-supplied server wins without anyone having to
know the field exists. This is the same argument as making it a row at all.

**Why restart rather than live reload.** The house line is that data applies live and code does not — themes,
keymaps and languages all swap without restart; add-in enable/disable explicitly takes effect next launch. A
language server is a process. The tempting middle — new rows work at once because servers start lazily, while
changed rows need a restart — is worse than either pole, because "it took effect for that server but not this
one" is indistinguishable from a bug.

## What This Does Not Do

- **No project-level configuration.** A server named by a repository means cloning it and opening it launches
  an executable that repository chose. That needs a consent design of its own, not a field that quietly
  appears in a project file.
- **No UI.** Every field costs a key in the canonical pack plus every shipped language pack, with a build
  gate on a miss. That budget belongs to the connections view, which covers the whole surface at once.
- **No discovery of installed servers.** This attaches one the user already has and can name. Finding servers
  on a machine is a separate problem.
- **No signing, consent store or revocation.** Disproportionate here; see above.
