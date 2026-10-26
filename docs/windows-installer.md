# Windows installer — Inno Setup

Extracted from `docs/TODO.md` on 2026-08-17 when that file was retired in favour of GitHub Issues.
It is a decision record rather than a task, so it belongs beside [`macos-signing.md`](macos-signing.md)
rather than in the tracker. The work itself is tracked as an issue.


Observed on the live site immediately after v0.1.0: a download hands you a `.zip` you must
extract yourself. That is developer-native and alien to this audience — a VB6 developer's model of
"getting software" is `Setup.exe`, not "unpack an archive". The link targets were fixed separately
(they now download the asset directly instead of opening the releases page), but the extract step
remains.

**Decided 2026-10-25: Inno Setup, with no auto-update for alpha** (part of #77).
`installer/HexIDE.iss` packages the staged self-contained Windows x64 Desktop build, including
its LSP server, standalone runner and any published add-ins. The build supplies the version from
`IDE/Directory.Build.props`; `publish.yml` produces `HexIDE-Setup-<version>.exe` on the Windows
x64 leg and attaches it to the release beside the portable archives.

Installation requires administrator approval and defaults to `{autopf}\HexIDE` (64-bit Program
Files). It creates a common Start-menu shortcut and an uninstaller, with no file associations.
`addins/` remains beneath the installation directory, inheriting Program Files permissions;
the script grants no user write access. Settings remain in the user's existing profile.

The workflow always downloads and installs pinned **Inno Setup 6.7.3**, regardless of the
runner image's compiler. Before executing the installer it checks its size (10592232 bytes)
and SHA-256 (`9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732`),
then requires a `Valid` Authenticode signature whose signer certificate names **Pyrsys B.V.**
as its organization. Any mismatch stops the step before execution. After installation,
`ISCC.exe` must report file version 6.7.3 through its Windows version information before
that compiler builds HexIDE's installer.

To bump the pin, obtain the new asset's digest and size from the publisher's GitHub release API
(`repos/jrsoftware/issrc/releases/tags/<tag>`), then update the URL, filename, digest, size and
compiler version check together with the infrastructure tests. The current digest is the one
GitHub records for the asset the publisher uploaded to release `is-6_7_3`, read 2026-10-25;
it is publisher-hosted, not computed here. Keep the Authenticode status and Pyrsys B.V. signer
check, as documented by [the publisher's verification instructions](https://jrsoftware.org/isdl-verify.php).
Moving to Inno Setup 7 requires a separate evaluation.

**#77 remains open until #314 and #315 land.** Package verification and revocation fixes are
reviewed separately; this installer creates the protected install location they must defend.
Windows compilation and a real install/uninstall must be verified on Windows, including a
standard user's inability to write `addins/`, Start-menu launch, and operation without a
separately installed .NET runtime. WSL infrastructure tests check the script and release wiring,
not the resulting executable or Windows ACLs.

Four options were weighed. Two are blocked by decisions already taken:

| option | verdict |
|---|---|
| **Inno Setup** | Simplest fit. Free, one `.iss` script, one Windows-only build step, produces exactly the `Setup.exe` this audience expects — and is pleasingly period-appropriate. No auto-update. |
| **Velopack** | Best fit *if auto-update matters*. Produces an installer, does delta updates, and uses **GitHub Releases as the update feed**, so no hosting is required. Cross-platform, so it would cover the macOS and Linux downloads too. Needs evaluating rather than assuming. |
| **ClickOnce** | Blocked in practice. Its draw is auto-update, but that needs a hosted deployment directory of versioned folders plus a manifest. Releases serves flat files; Pages would mean committing ~100 MB of binaries per release into the website repo. The zero-infrastructure static site was a deliberate choice, and this reintroduces exactly what it avoided. The browser click-to-install flow also degraded once the old IE integration went. |
| **MSIX** | Blocked outright. An unsigned MSIX does not warn, it **refuses to install**. The alternatives are a trusted certificate, asking users to import a self-signed cert into Trusted Root (an unacceptable ask), or Store distribution — which signs for you but attaches a publisher display name. Containerisation is a second risk: the IDE spawns the LSP server as a child process and loads add-in assemblies at runtime, neither of which suits the package-integrity model. |

None of these dodge SmartScreen — an unsigned installer trips it exactly as an unsigned portable
executable does, so an installer buys familiarity, not fewer warnings. See
[`macos-signing.md`](macos-signing.md) for why signing is deferred on both platforms.

