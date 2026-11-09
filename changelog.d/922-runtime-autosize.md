### Added

- **PictureBox AutoSize at runtime** (#922): assigning a bitmap fits the picture plus its measured border, and clearing it with `LoadPicture()` retains the size. Saved dimensions survive loading and an explicit Width holds until the next picture assignment. Flat bordered and non-bitmap sizing refuse as not implemented; Label AutoSize remains pending.
