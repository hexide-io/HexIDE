### Fixed

- **The remaining measured coordinate cases run** (#4693). A UserControl saved in User mode without extents
  uses twips. Its scale members through an Object holding Me or the extender raise 438, and Set of the UserControl base-class
  name reports the measured compile error. Invalid ScaleX/ScaleY argument counts report compile errors
  early-bound and error 450 late-bound. A centimetre-loaded PictureBox Line reads and writes its whole-twip
  endpoints in the container’s current scale, its origin included.
