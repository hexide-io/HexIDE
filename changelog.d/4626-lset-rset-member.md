### Fixed

- **LSet and RSet recognise member targets inside With** (#4626). A project class instance's member now raises VB6's "Variable required - can't assign to this expression" error when the statement runs, including a named target outside With.
