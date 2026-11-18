### Fixed

- **LoadPicture loads JPEG and GIF pictures and raises trappable file errors** (#4597). PNG, empty and unknown content raise 481; missing files, missing folders and directory paths raise 53, 76 and 75, allowing VB6 error handlers to run their fallback. Exported JPEG and GIF pictures use their matching extensions. Unmeasured metafiles, cursors, decoding failures and other I/O errors retain explicit refusals.
