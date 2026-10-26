## 1. Launch and handshake

- [x] 1.1 Start the compiler as `bin\STUB.exe --compiler=0` and read the port and key from its endpoint record
- [x] 1.2 Choose the stub from the project's `Settings` (`project.depAware`, `project.largeAddressAware`)
- [x] 1.3 Hold the compiler in a kill-on-close job object, built as a shared helper (`ChildProcessJob`)
- [x] 1.4 Send the shutdown request on the root's next request id, and wait for the exit
- [x] 1.5 Live test on BETA 1024: initialization, preparation with its package requests, the channels and shutdown

## 2. Remaining on #1689

- [x] 2.1 Record step 1's exchange with the #1218 recorder, redacted, as the first BETA 1024 recording. Root
  initialization and shutdown only: a project load would carry package contents, which are not ours to keep
- [x] 2.2 Compare the BETA 1024 op codes with #1204's BETA1011 run, and note any difference in the specs
