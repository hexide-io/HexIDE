### Added

- **Native Wayland is opt-in on Linux** (#868): `--platform auto` tries Wayland with X11 fallback,
  while `--platform wayland` requires it. `HEXIDE_PLATFORM` also works in made programs. X11 remains
  the default.
