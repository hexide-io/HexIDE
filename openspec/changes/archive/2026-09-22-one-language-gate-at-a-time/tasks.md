# Tasks

## 1. The guard

- [x] 1.1 `LanguageSwitchService.PendingLanguage`, set before the language is applied and cleared when its gate
  closes, whether kept, reverted or failed.
- [x] 1.2 `SwitchWithGateAsync` refuses while it is set: nothing applied, no gate, `false` returned, a warning
  logged.
- [x] 1.3 `set_ide_language` checks and starts the switch in one step on the UI thread, and a refusal names the
  open gate's language.

## 2. Tests

- [x] 2.1 A second switch while a gate is open is refused and changes nothing; the first gate still reverts to
  the original language.
- [x] 2.2 Once a gate closes, the next switch opens its own.
- [x] 2.3 A gate that throws does not leave the next switch refused.
