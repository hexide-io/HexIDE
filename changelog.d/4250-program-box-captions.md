### Fixed

- **A program's MsgBox and InputBox buttons follow the platform, independently of the IDE's language**
  (#4250). Windows supplies its own captions and access keys; other platforms use English. IDE prompts
  retain their localized captions and styling. A program with no project uses the host's distribution
  name when it omits the title.
