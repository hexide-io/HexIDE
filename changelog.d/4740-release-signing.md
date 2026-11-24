### Security

- **A release signs its first-party add-ins in a job that builds nothing** (#4740). The signing key used to sit on every
  release build runner while each package's build-time code ran. Now only a separate signing job receives it, scoped to
  a GitHub environment. That job runs the add-in packer, built elsewhere without the key, and the build runners copy in
  the signed packages. The release downloads are unchanged.
