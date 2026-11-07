# Tree hygiene guard

Run `bash scripts/check-tree-hygiene.sh` before staging or pushing. It scans tracked and untracked
files; `bash scripts/check-tree-hygiene.selftest.sh` plants violations and checks the findings, counts
and exit status, using a private index for its staged probes.

The C1 scan matches Unicode code points U+0080–U+009F in UTF-8, with PCRE2's `(*UTF)` mode explicit.
A byte pattern is unsafe here: Git for Windows uses UTF-mode matching even under `LC_ALL=C`, so a
pattern for the two UTF-8 bytes misses the control (#1881). Conversely, matching the range without
UTF mode on Linux would flag continuation bytes in ordinary punctuation.

## Verification

- **WSL Ubuntu, Git 2.53.0 (2026-10-25):** the explicit UTF pattern detects C1 controls, including
  after an invalid UTF-8 byte, and leaves em dashes and curly quotes alone. The 78-assertion selftest forces UTF
  mode for its untracked and staged violation runs, reproducing Windows matching on Linux;
  its clean and punctuation runs retain native matching.
- **Git Bash, Git for Windows 2.55.0.windows.5 (issue #1881, 2026-10-25):** the original byte pattern
  missed the planted C1 file (four of 78 selftest assertions failed). The issue's probe matched
  U+0085 with `(*UTF)\x{85}`. The revised guard's full selftest still needs a Windows run; Linux
  emulation of UTF mode does not establish a Windows result.
