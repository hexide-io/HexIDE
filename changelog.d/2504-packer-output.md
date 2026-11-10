### Fixed

- **Add-in packing preserves unrelated output folders and failed builds** (#2504). `pack` refuses a folder
  unless it is empty or its signed inventory and supplied signing chain identify unchanged package output.
  Replacements are built in a unique staging directory before the previous package is moved aside and replaced.
  Linked ancestors are allowed; links at or inside the output are refused with an explanation and recovery advice.
