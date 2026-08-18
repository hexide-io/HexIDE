# Windows installer — options weighed

Extracted from `docs/TODO.md` on 2026-08-17 when that file was retired in favour of GitHub Issues.
It is a decision record rather than a task, so it belongs beside [`macos-signing.md`](macos-signing.md)
rather than in the tracker. The work itself is tracked as an issue.


Observed on the live site immediately after v0.1.0: a download hands you a `.zip` you must
extract yourself. That is developer-native and alien to this audience — a VB6 developer's model of
"getting software" is `Setup.exe`, not "unpack an archive". The link targets were fixed separately
(they now download the asset directly instead of opening the releases page), but the extract step
remains.

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

