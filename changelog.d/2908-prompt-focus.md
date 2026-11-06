### Fixed

- **Language-server questions focus their first action when opened** (#2908, #3095). Enter chooses
  that action without first clicking or tabbing to a button; Escape still dismisses the question.
  External-link consent focuses No instead, so Enter declines.

### Added

- **Language servers can apply checked edits during a command the user started** (#2908).
  Unsolicited or late edits are refused with a reason; accepted edits share rename's all-or-nothing
  checks and one undo step per module, preserving the active tab. External document requests ask every
  time, showing the full URI in a localized question, and launch only after Yes; only web and mail links
  are offered, and anything else is refused.
