### Fixed

- **Bare ProgressBar types follow project library order** (#2265): `Reference=` and `Object=` lines now
  form one ordered list, so an earlier component beats a later reference. Form-header order does not
  override project order; `TypeOf` and Set use the same binding, with error 13 for the other library.
