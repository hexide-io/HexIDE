## Why

A user entry that shares a default's id replaces it wholesale, and the bundled server's command is found at run time, because its path differs between a dev build and a publish. So a user could replace the bundled server, but not give it one more argument without writing an absolute path they cannot know (#262).

## What Changes

- `lsp-servers.json` gains a top-level `arguments` object, mapping entry ids to arguments to add to that entry's own. It applies after the user's entries are layered over the defaults, so it follows the id whichever entry holds it.
- The arguments are added in the entry's own form: lists join, and command lines join with a space. Mixed forms are reported and left out, rather than quoted or split by a guess. An entry with no arguments takes the override as written.
- An id that names no entry, or an entry HexIDE starts no process for, is reported. The added arguments count in the command identity that the "command you have not run before" notice reads.

Rejected: a placeholder for the found command, which brings a template language into a file that has none; and a field-level merge for defaults only, which makes an entry behave differently depending on whether its id is a default's.

## Impact

- `LanguageServerConfigFile.Arguments` and `LanguageServerConfigLoader.ApplyArgumentOverrides`.
- `docs/language-servers.md` documents it.
