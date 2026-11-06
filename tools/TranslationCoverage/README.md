# Translation coverage

Run `dotnet run` here to compare every pack with canonical `en.json`. Neutral packs are discovered
from their filenames, including the shipped Catalan pack (`ca`, #2773); there is no separate list to
update in this tool. `LanguageManifest.Packs` in `IDE/HexIDE/Localization/LanguagePack.cs` declares
what ships, and `ShippedPackParityTests` checks that the manifest and pack files agree.

The report counts keys, not translation quality. A complete pack must also preserve placeholders and
follow the access-key conventions in the [language-packs specification](../../openspec/specs/language-packs/spec.md).
