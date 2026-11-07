## 1. Arguments override

- [x] 1.1 Read a top-level `arguments` map from `lsp-servers.json`
- [x] 1.2 Add each entry's override to its own arguments, in the same form, after layering, without changing the default instance
- [x] 1.3 Report an override for an unknown id, for an entry that starts no process, and in a different form from the entry's own
- [x] 1.4 Apply overrides before the unseen-command notice, so added arguments count in a command's identity
- [x] 1.5 Document it in `docs/language-servers.md`
