# Changelog fragments

Each change records its changelog entry here, in a file of its own, instead of in `CHANGELOG.md`. At
release the fragments are folded into `CHANGELOG.md`'s `[Unreleased]` section and deleted.

**Why.** Every pull request used to add its entry at the top of the same section of `CHANGELOG.md`, and a
branch must be up to date before it merges, so each merge put every other open pull request in conflict on
that one file ([#1375](https://github.com/hexide-io/HexIDE/issues/1375)). One file per change means no two
pull requests touch the same lines.

## Writing one

Name it after the issue the change closes: `1375.md`. Where one issue gets several changes, add a word:
`698-forms.md`. A change with no issue takes a short name of its own, in lower case: `release-notes.md`.

Start with the section the entry belongs in, as `CHANGELOG.md` names them (Keep a Changelog's), then write
the entry exactly as it would read there:

```markdown
### Fixed

- **A carried `.cls` goes to the VB6 server only if it is a VB6 class file** (#279). Its first non-blank
  line must be `VERSION 1.0 CLASS`; anything else goes to whichever other server claims the extension.
```

The sections are `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` and `Security`. A fragment may hold
more than one, each under its own heading. `ChangelogFragmentTests` checks every fragment on each build.

## Gathering them

Before tagging a release, run `scripts/changelog-gather.sh`. It puts each fragment's entries at the top of
their section under `[Unreleased]`, newest issue first, and deletes the fragments. `--dry-run` prints the
result without changing anything, and `--check` only validates. The release notes link to `CHANGELOG.md`
at the tag, so a fragment left here when the tag is made is missing from them.
