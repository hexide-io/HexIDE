## 1. Fetch for first-party packages

- [x] 1.1 `AddinRegistry` fetches before loading whenever a discovered package has a manifest, first-party included.
- [x] 1.2 `StartupRevocationTests` expects a fetch for a first-party-only install, and none with no manifest or an empty URL.
- [x] 1.3 The settings documentation and `docs/MISSING_FEATURES.md` describe the new rule.
