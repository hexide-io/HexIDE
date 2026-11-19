### Added

- **GoSub, Return and computed On GoTo/GoSub run for procedure-level labels** (#4726), including nested calls, recursion and the measured error-handler flows. Runaway GoSub raises trappable error 28 at HexIDE's 2,000-level cap; unmeasured selector overflow and Resume across GoSub passes refuse explicitly.
