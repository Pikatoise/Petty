# Optimization measurements

Measured on 2026-11-06 after the online functionality fixes: Windows x64,
Release/.NET 10.0.14, 12 logical processors, PostgreSQL 17 in Docker.
All runs used disposable data and a local backend. CPU below is the percentage
of one logical processor; divide by 12 for this machine's total CPU percentage.
Memory values are MiB. Working set, private commit and live managed memory overlap.

## Desktop

Six scenarios ran for 120 seconds each, excluding warmup. Tools collected process
and GC counters, managed stack/allocation samples, heap snapshots and callback timings.

| Scenario | CPU | Working set range | Private commit at end | Live managed memory after GC | Allocated MiB/s |
|---|---:|---:|---:|---:|---:|
| Idle | 4.91% | 123.6–133.7 | 52.7 | 6.26 | 0.073 |
| Walking | 6.30% | 121.8–127.7 | 50.4 | 6.30 | 0.125 |
| Sleeping | 3.02% | 123.9–127.1 | 51.6 | 6.32 | 0.066 |
| Playing | 10.39% | 125.1–129.8 | 55.8 | 6.33 | 0.127 |
| Home pet and waiting guest | 6.72% | 128.8–131.3 | 55.6 | 6.53 | 0.082 |
| Repeated online-window use | 11.61% | 147.7–194.8 | 108.8 | 7.13 | 2.470 |

- Nineteen online-window cycles loaded/scrolled history and closed 38 windows;
  all 38 were collected. At most five history rows were realized.
- A separate 1000-letter RU/EN check realized only 3–4 rows and verified scrolling,
  pagination, deduplication, reset and text wrapping.
- A 90.6-second production-app run with external sampling only averaged 6.90% CPU
  and 113.9–120.9 working set. Most time was spent running; only three seconds were
  stable idle, too little for a reliable idle comparison with the controlled run.
- Simulation/hover callbacks allocated essentially nothing. Frame presentation
  accounted for more callback work; native WPF/graphics CPU was not fully attributed.
- A resident-page snapshot classified 126.31 total: 35.73 private and 90.57
  Windows-shared. Images, runtime/WPF metadata, strings and arrays explain much of
  the measured footprint; private native heap/JIT/stack ownership was not separated.

## Backend

A 720.5-second run covered idle, reads, mail with two SignalR sockets, password
logins and recovery. Steady process figures exclude startup and trace/forced-GC intervals.

| Scenario | API CPU | Mean working set | Mean private commit | Allocated MiB/s | PostgreSQL CPU |
|---|---:|---:|---:|---:|---:|
| Idle | 1.17% | 130.2 | 44.1 | 0.06 | 0.04% |
| Reads | 27.78% | 157.2 | 58.8 | 14.49 | 17.51% |
| Mail | 17.08% | 157.5 | 52.6 | 6.28 | 13.70% |
| Authentication | 51.54% | 164.4 | 58.2 | 1.33 | 2.80% |
| Recovery | 0.76% | 165.1 | 58.9 | 0.13 | 0.09% |

- 28,529 successful HTTP requests, 1,528 letters sent/read, 3,056 SignalR wakeups
  and 801 logins; no HTTP errors. Read traffic averaged about 120 requests/s.
- Login p95: 124.8 ms; sending/reading p95: 45.3/41.5 ms.
- Live managed memory grew from 6.34 to 7.84 after additional routes warmed up.
  No `Letter` or `UserSession` entities remained in the captured heaps. Two
  contexts and two hub contexts matched the two open sockets.
- Total GC pauses: approximately 0.84 seconds. Read allocation was about
  126 KiB/request; samples identified strings, scoped DI caches and EF setup.
  Authentication samples identified expected PBKDF2 password-verification work.
- Resident-page snapshot: 165.17 total, 48.05 private and 117.12 Windows-shared.
  The disposable database contained 809 session rows after repeated logins.

## Conclusions

- Fixed lost friends updates during loading, duplicate refresh requests/row
  reconstruction, nonvirtualized history and unchanged native window placement.
- No critical remaining inefficiency or unbounded live-memory growth was observed.
  High working set/private commitment alone did not indicate retained-object leaks.
- First optimization candidate: benchmark bounded DbContext pooling while retaining
  scoped repositories, Unit of Work, transaction/reset and session-security invariants.
- Further candidates: measure frequent window setup independently of fixture JSON,
  paginate/virtualize large friendship lists, and define expired-session retention.
  Benefits of these changes have not yet been established by comparative benchmarks.
- Preserve password strength, immediate revocation and correct notification routing.

Desktop Debug/Release tests, 40 PostgreSQL-backed server tests, content/asset checks,
hidden-window placement checks, simulated Windows lifecycle checks and formatting passed.
Profiling included diagnostic overhead and shared-machine activity; online-window
stress used fake HTTP, including fixture serialization. Stack samples include waits
and are not native CPU percentages. Twelve-minute runs do not exclude rare multi-hour
leaks or establish live hover/DPI/z-order/shell/logoff acceptance.
