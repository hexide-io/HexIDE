### Changed

- **Tool-window reopening has bounds coverage for Toolbox and Properties** (#895). Headless Dock tests
  check that default and resized panes return to their column at their previous size over repeated
  close/show cycles, including at 150% scaling. The existing reopening fixes are unchanged.
