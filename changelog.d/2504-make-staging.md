### Fixed

- **Make asks before replacing its derived project archive** (#2504). The question names the existing
  `.dll`; declining leaves both the archive and executable untouched. Saves use unique staging names,
  preserving user files at the former `.tmp` and `.hexide-tmp` paths.
