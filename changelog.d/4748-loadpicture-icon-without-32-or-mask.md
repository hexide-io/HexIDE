### Fixed

- **LoadPicture selects the first icon image when none is 32×32** (#4748), preserving its dimensions. A selected image without its AND mask raises trappable error 481 at every colour depth; unusable later images do not affect it.
