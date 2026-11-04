### Added

- **An add-in can contribute a debug launch for the External Debug Session.** `IHexIdeHost.RegisterDebugLaunch` takes a
  title, which projects it runs and what makes its launch configuration, asked for only when a run starts. **Run > Run
  With** chooses, per project and per user, whether F5 runs the project in the IDE or with the add-in's launch; the IDE
  stays the default, and the choice is kept in the `.user.hexproj` sidecar. **Run > Start With** runs a launch once.
  The session gets the project's saved breakpoints. A chosen launch that is gone leaves F5 to the IDE, with a status
  message.
