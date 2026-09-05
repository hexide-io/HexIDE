# The foreign language servers the tests drive

HexIDE's test suite drives real, third-party language servers. This note explains why, which ones, how
they are obtained, and — because one of them is GPL-licensed — exactly why that is consistent with a
100%-MIT tree.

## Why third-party servers at all

A client and a server written by the same hand converge on their shared assumptions rather than on the
specification. They work together, and are wrong in matching ways.

HexIDE's own server once advertised no capabilities while HexIDE's own client called every method
unconditionally. Each was "correct" only because the other was wrong to match. Three defects hid in that
gap and surfaced within hours of pointing the client at a server nobody here had written.

So the value is precisely that the far end does not accommodate us. Writing a second server ourselves
would prove nothing.

## Which ones, and why each earns its place

| Server | Language | Licence | What it exercises that nothing else does |
|---|---|---|---|
| rumdl | Markdown | MIT | The baseline foreign path; publishes on open, change **and save**, which is what proves save notifications reach a server that acts on them. |
| texlab | LaTeX | **GPL-3.0** | A different author, a different release convention, a different licence — and it claims `.cls`, which is a LaTeX class file *and* a VB6 class module. |

**A third should earn its place by exercising a protocol shape neither of these does**, not by being
another server. The shapes nothing real currently exercises are the `pipe` and `websocket` transports —
both supported, both tested only against fakes — and a server that genuinely defers its analysis to save.
Count is not the goal; independence and shape are.

## The GPL question

texlab is GPL-3.0. HexIDE is MIT and guards that with `scripts/check-no-gpl.sh` on every push. These are
consistent, for reasons worth stating explicitly rather than leaving to be re-derived:

- **It is never distributed.** It is downloaded at test time into `artifacts/`, which is gitignored, and
  no release artefact contains it.
- **It is never linked.** It is a separate process reached over stdio. Nothing of it is compiled into,
  statically bound to, or derived from anything here.
- **Running a GPL program does not make your program GPL.** The obligations attach to conveying the work
  or a derivative of it. Driving an unmodified binary across a protocol boundary is neither.

**And the guarantee is enforced, not merely intended.** `check-no-gpl.sh` fails if any downloaded server
becomes a tracked file. That check exists because the whole argument above rests on the binary staying out
of the tree, and a `git add -f` or a re-scoped ignore rule would otherwise defeat it silently.

One caveat worth keeping in view: this project has deliberately said elsewhere that the out-of-process
server design is for crash isolation and a replaceable backend, **not** for licensing reasons. That
remains true. Nothing here relies on the process boundary to launder a licence — the shipped product
contains no GPL code at all, and this is test tooling that is fetched, run, and never shipped.

## How they are obtained

Downloaded on demand, once, at a pinned version, with a SHA-256 verified before anything is executed, into
`artifacts/foreign-lsp/`. Not committed: several megabytes per platform against a repository whose whole
history is a fraction of that, and every version bump would add the same again permanently.

Linux uses musl builds where offered, so one binary runs on any distribution.

### Digest provenance is recorded, because it differs

- **rumdl** publishes a checksum file beside each asset. Its digests are taken from those — they attest
  that the bytes are the ones the publisher intended.
- **texlab** publishes none, so its digests were computed here from a download. That pins *what was tested
  against*: it still catches a corrupted transfer, a replaced asset, or an unannounced rebuild under the
  same tag. It does **not** attest provenance. If the release had already been tampered with when it was
  first fetched, the digest records that faithfully.

That distinction is trust-on-first-use versus attestation, and the code names it per server rather than
letting a row of hex imply they are the same thing.

### Environment variables

| Variable | Effect |
|---|---|
| `HEXIDE_MARKDOWN_LSP`, `HEXIDE_LATEX_LSP` | Point at your own build. Checked before the download, so an explicit choice is never silently replaced. |
| `HEXIDE_FOREIGN_LSP_DOWNLOAD=0` | Stay off the network. The affected tests then skip, visibly. |
| `HEXIDE_REQUIRE_FOREIGN_LSP=1` | Turn "unavailable" into a failure. **CI sets this**, because a silently skipped proof is the failure mode this whole fixture exists to prevent. |

## Bumping a version

Edit the version and the five digests in `ForeignServerAcquisition.cs`. Take each digest from the
publisher's own checksum file where one exists — never from a file you downloaded yourself, which verifies
nothing beyond that the download completed. Where the publisher offers none, compute it and leave the
provenance marked as computed here.
