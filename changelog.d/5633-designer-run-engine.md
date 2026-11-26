### Changed

- **The form designer previews a form, and labels the controls it only carries, by asking the run engine** (#5633). Preview Form is shown by the registered engine, the Basic provider's interpreter, and a carried control's box says it's view-only when that engine runs it. With no engine, Preview Form shows nothing and every carried control's box says it's unsupported. An engine's previews close when the engine is withdrawn, and the boxes say again what the engine runs once one comes or goes.
