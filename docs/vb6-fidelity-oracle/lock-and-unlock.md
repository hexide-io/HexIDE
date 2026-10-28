## Lock and Unlock (2026-10-28, #2553)

Measured on vb6.exe SP6, every probe built native and as p-code, which logged the same lines. Each probe is a Standard EXE `Sub Main` under `On Error Resume Next`, logging each statement with `Err.Number`; `Show(v)` logs a value as `[value] TypeName/VarType`.

**What the rows say:**
- `Lock #n` with no range locks the whole file. Another file number open on the same file then gets **70** (Permission denied) on a `Get` or `Put` in a locked range; the number that locked it reads its own range without error.
- A Binary file's records are bytes, and a Random file's are its records of `Len`. `Lock #1, 2 To 4` covers 2, 3 and 4. `Lock #1, To 4` covers 1 to 4.
- Locking a range already locked is 70. **`Unlock` must name exactly a range locked:** unlocking one not locked is 70, and so is `Unlock #1, 2 To 3` after `Lock #1, 2 To 4`.
- Record 0, a negative record, and a start above the end are **63**. A record past the end of the file can be locked, and reading it through the other number is then 70.
- On a **sequential** file a range locks the whole file: after `Lock #1, 2`, `Line Input #2` is 70.
- Closing the number releases its locks.
- A file number not open is 52.
- Two `Open`s of one file without `Shared`, in one program, raise nothing.

Every row is within one program, so an in-process lock table answers all of them on every platform, where an OS lock would not: on Linux a lock is advisory and a read through the other number would succeed.

**A Binary file of the ten bytes `0123456789`, open as `#1` and `#2`, both Shared:**

```
Lock #1 err 0
Get #2 at 5 under a whole lock err 70 b 0
Lock #1 again err 70
Unlock #1 err 0
Unlock #1 again err 70
Get #2 at 5 unlocked err 0 b 52
Lock #1, 5 err 0
Get #2 at 5 err 70 b 0
Get #2 at 4 err 0 b 51
Get #2 at 6 err 0 b 53
Get #1 at 5, its own lock, err 0 b 52
Put #2 at 5 err 70
Unlock #1, 5 err 0
Lock #1, 2 To 4 err 0
Get #2 at 2 err 70 b 0
Get #2 at 4 err 70 b 0
Get #2 at 5 err 0 b 52
Unlock #1, 2 To 3, not the range locked err 70
Unlock #1, 2 To 4 err 0
Lock #1, 0 err 63
Lock #1, -1 err 63
Lock #1, 5 To 2 err 63
Unlock #1, 5 To 2 err 63
Lock #1, 20, past the end err 0
Get #2 at 20 err 70 b 0
Unlock #1, 20 err 0
Lock #9, not open err 52
Unlock #9, not open err 52
Lock #1, 3 then Close #1 err 0
Get #2 at 3 after #1 closed err 0 b 50
lock-binary: done
```

**`To 4`:**

```
Lock #1, To 4 err 0
Get #2 at 1 err 70 b 0
Get #2 at 4 err 70 b 0
Get #2 at 5 err 0 b 52
Unlock #1, To 4 err 0
lock-to-end: done
```

**A Random file, `Len = 4`, records 10, 20, 30, 40:**

```
Lock #1, 2 err 0
Get #2 record 1 err 0 n 10
Get #2 record 2 err 70 n 0
Get #2 record 3 err 0 n 30
Unlock #1, 2 err 0
Lock #1, 2 To 3 err 0
Get #2 record 3 err 70 n 0
Get #2 record 4 err 0 n 40
Unlock #1, 2 To 3 err 0
lock-random: done
```

**A sequential file, both numbers Input Shared:**

```
Lock #1, 2 on a sequential file err 0
Line Input #2 err 70 []
Unlock #1, 2 err 0
Line Input #2 after unlock err 0 [line one]
lock-sequential: done
```

**Neither Open Shared:**

```
a second Open, neither Shared, err 0
Lock #1 err 0
Unlock #1 err 0
lock-not-shared: done
```
