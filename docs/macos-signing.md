# macOS signing and notarisation

What HexIDE does today, what Gatekeeper approval would additionally require, and what has to change
in the release pipeline to get there. Written so the work can be picked up cold rather than
re-derived.

The release workflow is [`.github/workflows/publish.yml`](../.github/workflows/publish.yml); its
`Build macOS .app bundles` step is what this document is about.

## What ships today

The bundle is sealed with an **ad-hoc signature**, without a signing identity or notarisation.
The release pipeline retains its per-Mach-O verification and also requires
`codesign --verify --strict --deep` on the assembled bundle.

A downloaded archive can carry `com.apple.quarantine`, including through tar extraction.
The measurements on [#4507](https://github.com/hexide-io/HexIDE/issues/4507) correct the earlier claim
that tar avoided Gatekeeper. macOS says it cannot verify HexIDE because the build is not notarised.
The user then chooses Done, and Open Anyway in System Settings → Privacy & Security, which asks for an
administrator's password and opens the app. That was measured on macOS 15.5 with a bundle sealed this way.
On 27.2.1 the warning is the same, and Open Anyway wasn't checked. Before the bundle was sealed, macOS
called the download "damaged" and offered no Open Anyway at all (#4507).
Notarisation is still needed to remove that warning.

## How the bundle is sealed

[`scripts/macos-bundle.sh`](../scripts/macos-bundle.sh) implements
[#4495](https://github.com/hexide-io/HexIDE/issues/4495). It copies the staged payload into
`Contents/MacOS`, moves `addins/` to `Contents/Resources/addins`, and writes the icon and property list.
It attempts ad-hoc signing on every regular file under `Contents/MacOS`, then signs the bundle last,
without `--deep`, and requires strict deep verification to succeed. The main executable's path resolves
to the bundle, so that final step signs it. Language folders and `standalone/` stay in place.

The dotted add-in folders were the structural obstacle: codesign treated them as nested bundles.
The measurements recorded on #4507 sealed this layout without moving the rest of the .NET payload
or changing apphost assembly resolution. Both add-in lookup sites use the Resources location in a
macOS bundle, and the usual `BaseDirectory/addins` elsewhere.

The repack tool accepts sealed bundles and re-seals the rewritten bundle ad hoc on macOS, then
requires the same strict deep verification. No signing key is used. CI assembles a self-contained
bundle and repacks it on macOS, checking both seals.

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
| per-binary and strict deep bundle verification | retain both gates |
| `.tar.gz` (can carry quarantine) | submit a supported archive for notarisation |
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

So both platforms currently ship without a signing identity, and on both the blocker is an identity/enrolment
question rather than a technical one.

## Status

Deferred. The technical work is understood and written down above; the enrolment decision is the
gate. The ad-hoc bundle seal is implemented; notarisation remains deferred.
