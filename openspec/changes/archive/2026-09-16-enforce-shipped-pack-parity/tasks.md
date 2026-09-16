# Tasks

## 1. Enforce it
- [x] 1.1 A test asserting every pack declared in `LanguageManifest.Packs` carries every canonical key.
- [x] 1.2 A test asserting every declared pack has a file, since one without renders wholly in English
      while still appearing in the menu.
- [x] 1.3 A test asserting every full translation on disk is declared, which is the `la`/`eo` lesson and
      is also what keeps `tools/TranslationCoverage` correct, since it infers the shipped set from
      filenames.
- [x] 1.4 Prove each has teeth: remove one key from a pack, hide a declared pack's file, add an undeclared
      neutral. Each must redden its own test and only its own.

## 2. Resolve the contradiction it exposes
- [x] 2.1 Scope `A pack SHALL declare only what it translates` to undeclared packs and region variants.
- [x] 2.2 Add `A declared language SHALL be a complete translation`.
- [x] 2.3 Say what `region` means, because it reads as *dialect* and the maintainer misread it reviewing
      this change.

## 3. Make the documentation true
- [x] 3.1 `CLAUDE.md` names the guard behind "enforced at build".
- [x] 3.2 `tools/TranslationCoverage` no longer says drift never fails a build, and points at the test.

## 4. Close it out
- [x] 4.1 Full `HexIDE.Tests` run, plus `check-tree-hygiene.sh`.
- [x] 4.2 `openspec validate --strict`, archive, and check the merged `## Purpose` survived.
