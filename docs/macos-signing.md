# macOS signing and notarisation

What HexIDE does today, what Gatekeeper approval would additionally require, and what has to change
in the release pipeline to get there. Written so the work can be picked up cold rather than
re-derived.

The release workflow is [`.github/workflows/publish.yml`](../.github/workflows/publish.yml); its
`Build macOS .app bundles` step is what this document is about.

## What ships today

Nothing is signed with a real identity and nothing is notarised. macOS binaries are **ad-hoc signed**
(`codesign --sign -`), which does exactly one thing: it stops Apple Silicon killing the process.

An arm64 Mach-O with a missing or broken signature is `SIGKILL`ed at exec, unconditionally — that is
a loader rule, not Gatekeeper, and it applies even to a binary the user compiled themselves. Renaming
`HexIDE.Desktop` to `HexIDE` invalidates the linker's own ad-hoc signature, so the bundle step
re-signs every Mach-O inner-out and then asserts that each one verifies.

Two things follow, and they are easy to conflate:

- **Ad-hoc signing is not identity.** There is no certificate and no chain. `codesign -dvvv` reports
  `Signature=adhoc`, and Gatekeeper learns nothing from it.
- **Gatekeeper is avoided, not satisfied.** The download is `.tar.gz` rather than `.zip` because tar
  does not carry the `com.apple.quarantine` extended attribute, so the quarantine check never fires.
  A `.zip` or `.dmg` would fire it and be refused.

That is a deliberate workaround with a known shape: it depends on Apple continuing not to propagate
quarantine through tar, and on users extracting from a terminal. Notarisation replaces the workaround
with an actual guarantee.

## The bundle is not currently sealable

Before anything else: **notarisation requires a properly signed bundle, and the `.app` cannot
currently be sealed at all.** This is a prerequisite, not a detail.

`Contents/MacOS` holds the entire .NET publish payload — four apphost executables (`HexIDE`,
`HexIDE.LspProxy`, `HexIDE.VbLspServer`, `HexIDE.AddinPacker`), a `*.runtimeconfig.json` beside each,
a `standalone/` subdirectory, and per-language satellite assembly directories. codesign rejects that
two different ways depending on flags, and both are the same underlying fact:

| invocation | failure |
|---|---|
| `codesign --sign - "$APP"` | `code object is not signed at all` / `In subcomponent: <a runtimeconfig.json>` |
| `codesign --deep --sign - "$APP"` | `bundle format unrecognized, invalid, or unsuitable` |

Without `--deep` it stops at the first `*.runtimeconfig.json`: data that cannot be signed and cannot
be removed, so signing more file types first only advances the error to the next one. With `--deep`
it reads `standalone/` and the language directories as nested bundles, finds no `Info.plist`, and
rejects the structure outright.

The fix is structural. `Contents/MacOS` is meant to hold executables; the payload belongs in
`Contents/Resources`, with assembly resolution pointed at it. That means either an apphost
configuration change or a small launcher shim, applied to all four executables. Until that is done,
the release gates on **per-binary** signature verification instead — which checks the property users
actually depend on and is achievable against the current layout.

## Prerequisites for notarisation

1. **Apple Developer Program membership.** Roughly $99/year, billed in local currency. There is no
   free tier that issues Developer ID certificates.
2. **A Developer ID Application certificate** — *not* the Mac App Store certificate, which only signs
   for store distribution. Exported as `.p12`, stored base64-encoded in a repository secret,
   imported into a temporary keychain during the run and removed afterwards.
3. **An App Store Connect API key** for `notarytool`. Preferred over an Apple ID plus app-specific
   password: it is scoped, revocable, and does not tie CI to one person's account credentials.

### Enrolment has a consequence worth deciding deliberately

Apple offers two enrolment types and they differ in what becomes public, permanently:

- **Individual** — the team name is the enrolee's **legal name**, and the certificate's common name
  is literally `Developer ID Application: <Legal Name> (TEAMID)`. That string is embedded in every
  signed binary and is readable by anyone who runs `codesign -dvvv` on a download.
- **Organization** — the team name is the registered entity, and enrolment requires a legal entity
  and a D-U-N-S number.

Neither is reversible after the fact for artifacts already published, so this is a decision to take
before the first signed release rather than during it.

## Pipeline changes

Against the current `Build macOS .app bundles` step:

| now | notarised |
|---|---|
| `--sign -` | `--sign "Developer ID Application: … (TEAMID)"` |
| `--timestamp=none` | `--timestamp` — a secure timestamp is mandatory |
| no runtime hardening | `--options runtime` |
| no entitlements | `--entitlements` (see below) |
| per-binary verify gate | plus a bundle seal, once the layout allows it |
| `.tar.gz` to dodge quarantine | `.dmg` or `.zip`, because quarantine *should* now engage |
| — | `xcrun notarytool submit --wait` |
| — | `xcrun stapler staple` |

Stapling matters: it attaches the notarisation ticket to the artifact so first launch works without a
network round-trip to Apple. Without it, a user opening the app offline still sees a refusal.

Order is inner-out, unchanged: sign nested code first, then the bundle, then submit the archive.

### Entitlements

The hardened runtime is required for notarisation and it breaks .NET unless these are granted:

| entitlement | why |
|---|---|
| `com.apple.security.cs.allow-jit` | RyuJIT allocates executable memory; without this the runtime dies at startup |
| `com.apple.security.cs.allow-unsigned-executable-memory` | commonly required alongside the above for .NET's code generation |
| `com.apple.security.cs.disable-library-validation` | the add-in system loads assemblies at runtime that Apple did not sign; library validation refuses them |

That last one is not optional here — it is the same constraint that already rules out
`PublishTrimmed` and AOT for the desktop build, surfacing in a different place.

## Verifying it worked

```sh
codesign -dvvv --entitlements - /Applications/HexIDE.app   # identity, timestamp, entitlements
codesign --verify --deep --strict /Applications/HexIDE.app # the seal
spctl -a -vvv -t install /Applications/HexIDE.app          # what Gatekeeper will decide
xcrun stapler validate /Applications/HexIDE.app            # ticket present for offline launch
```

`spctl` is the one that answers the actual question. `accepted` with
`source=Notarized Developer ID` means a user double-clicking the download sees no warning.

## Windows, for comparison

The Windows story lands in the same place by a different route. Extended Validation certificates no
longer confer instant SmartScreen reputation, so an EV certificate buys a warning that fades with
download volume rather than one that never appears. Azure Trusted Signing is the current
recommendation and requires an organisation with three years of verifiable history.

So both platforms currently ship unsigned, and on both the blocker is an identity/enrolment
question rather than a technical one.

## Status

Deferred. The technical work is understood and written down above; the enrolment decision is the
gate, and the bundle restructure is the prerequisite that has to land first either way.
