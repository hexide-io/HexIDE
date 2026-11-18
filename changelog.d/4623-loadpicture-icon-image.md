### Fixed

- **LoadPicture selects the first 32×32 image in a multi-image icon file** (#4623), including files with a larger PNG image. A selected PNG image or a 32-bit image missing its AND mask raises trappable error 481. Multi-image icons without a 32×32 image and missing masks at other depths explicitly refuse as unmeasured; saved pictures and resource icons retain their existing decoding.
