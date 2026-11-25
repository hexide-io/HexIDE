## 1. The hourly check

- [x] 1.1 `RevocationStore` fetches conditionally, keeps a validator only from a list that verified, and says whether a newer list arrived.
- [x] 1.2 `RevocationMonitor` checks about hourly against the wall clock, with a random offset, after startup has loaded the add-ins.

## 2. Withdrawal mid-session

- [x] 2.1 A loaded add-in a newer list revokes is marked Revoked, and what it registered is taken out.
- [x] 2.2 Its commands, menu items and event handlers stop running, and later registrations are refused.
