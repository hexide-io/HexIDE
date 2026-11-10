### Fixed

- **MCP member writes preserve the files they replace** (#2504). `set_file_content`, `set_control_property`
  and `add_control` refuse destinations outside the owning project's folder, parent traversal and linked
  paths before editing, including armed first-save destinations and binary companions. Existing bytes are
  preserved in unique recovery files, with their paths and restore instructions in the reply.
