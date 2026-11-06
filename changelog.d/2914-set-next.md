### Added

- **Set Next Statement for external debug sessions** (#2914): adapters advertising goto targets receive
  the caret source and line, then the first target on the stopped thread. Refused moves retain inspection
  and report the adapter's message in Immediate, without moving the caret or scrolling the editor.
  Refused continue and step commands also keep the caret and scroll position.

### Fixed

- **External launch breakpoint conditions explain their refusal** (#2914), naming the launch file and
  breakpoint entry in the selected language before starting the adapter.
