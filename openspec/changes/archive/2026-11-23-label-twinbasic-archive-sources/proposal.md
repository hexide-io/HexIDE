# Label twinBASIC archive sources

## Why
A previewed twinBASIC source with a VB6 extension can reach a VB6 server and receive misleading syntax diagnostics (#1122).

## What Changes
- Label `.twin` and VB6 source extensions in twinBASIC containers `twinbasic`.
- Offer labelled previews only to servers claiming the extension and declaring that language.
- Preserve labels on reload, reopen and workspace replay; retain extension routing for other entries and Office routing.

## Impact
- Archive browsing and the LSP open contract carry the label; each server's configured wire identifier is unchanged.
- No server is bundled. Greying is tracked separately by #1691. Part of #5138; fixes #1122.
