# Development

Build on Windows with **.NET SDK 10**. From the repository root:

```powershell
dotnet build Petty.Desktop.slnx -c Release
dotnet run --project src/Petty.App
```

### Checks

```powershell
dotnet test Petty.Desktop.slnx -c Debug
dotnet test Petty.Desktop.slnx -c Release

dotnet run --project scripts/ContentVerification -c Release
dotnet run --project src/Petty.Assets -c Release -- --verify

# Close other Petty instances before running the Windows harness.
dotnet run --project scripts/WindowsReleaseVerification -c Release

# Optional isolated tutorial startup/replay scenarios:
$env:PETTY_VERIFY_TUTORIAL = '1'
dotnet run --project scripts/WindowsReleaseVerification -c Release
Remove-Item Env:PETTY_VERIFY_TUTORIAL
```

### Portable build

```powershell
./scripts/publish-portable.ps1
# Optional ZIP for distribution:
./scripts/publish-portable.ps1 -Zip
```

### Backend development

`Petty.Desktop.slnx` contains the existing Windows app and its tests.
`Petty.Server.slnx` contains only cross-platform .NET 10 server projects and tests:
Domain, Application, Infrastructure and Api. No Desktop artwork or
Windows runtime is required to build or publish the server.

Business persistence uses EF Core, feature-specific repositories and Unit of Work.
`AccountRepository`, `SessionRepository`, `FriendshipRepository` and `LetterRepository`
share the scoped `ServerDbContext`,
which implements `IUnitOfWork`. Identity's user store also defers saving to Unit of
Work. Handlers commit explicitly; session changes lock the account row in a
transaction. A partial unique index enforces one unrevoked session per account.

Use .NET SDK 10 and PostgreSQL 17 or newer. Supply the connection string through
`ConnectionStrings__Petty` (environment variable) or a local secret provider. Never
commit credentials. For example, in PowerShell, replace the placeholders locally:

```powershell
$env:ConnectionStrings__Petty = 'Host=localhost;Port=5432;Database=petty;Username=<user>;Password=<password>;Maximum Pool Size=20;Timeout=5;Command Timeout=10'
# Development key for this shell. Keep a stable key in a secret provider for hosting.
$keyBytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($keyBytes)
$rng.Dispose()
$env:Auth__SigningKey = [Convert]::ToBase64String($keyBytes)
dotnet tool restore
dotnet build Petty.Server.slnx -c Release
dotnet test Petty.Server.slnx -c Release
dotnet ef database update --project src/Petty.Server.Infrastructure --startup-project src/Petty.Server.Api --configuration Release
dotnet run --project src/Petty.Server.Api --configuration Release --no-launch-profile -- --urls http://127.0.0.1:5080
```

On Linux, use `export ConnectionStrings__Petty='...'` and
`export Auth__SigningKey='<base64-encoded random key>'`; the `dotnet` commands are
identical. The signing key must contain at least 32 random bytes before base64
encoding. Optional `Auth__Issuer` and `Auth__Audience` default to `Petty.Server` and
`Petty.Desktop`. Apply migrations explicitly before running the API. The migrations
include an empty baseline, Identity/session, friendship/block and letter tables. Startup never
creates or migrates a database automatically.

- `GET /health/live`: process liveness, without querying PostgreSQL.
- `GET /health/ready`: 200 only when PostgreSQL is reachable and all migrations
  are applied; otherwise 503. Responses contain no database or exception details.

### Account API (development)

| Method | Route | Request / response |
|---|---|---|
| POST | `/api/v1/auth/register` | `{login,password,confirmPassword}` -> 201 `{userId}` |
| POST | `/api/v1/auth/login` | `{login,password}` -> `{accessToken,refreshToken,sessionId,expiresAtUtc}` |
| POST | `/api/v1/auth/refresh` | `{refreshToken}` -> rotated token response |
| POST | `/api/v1/auth/logout` | Bearer access token -> 204; repeat logout also returns 204 while the signed access token is unexpired |
| GET | `/api/v1/me` | Bearer access token -> own login, nullable pet profile and `sessionActive` |
| PUT | `/api/v1/me/pet` | Bearer access token, `{petName,assetId}` -> 204 |

Logins are 3–32 ASCII letters, digits or underscores, trimmed and case-insensitive.
Registration requires matching 12–128-character passwords with uppercase,
lowercase and a digit. Identity hashes passwords. **There is no email or password
recovery in v1.** Pet names follow Desktop's 1–24 UTF-16 code-unit limit after
trimming and reject control characters. Only the 40 official appearance IDs are
accepted; filesystem paths and image URLs are rejected.

Access JWTs expire after 15 minutes and validate issuer, audience, signature,
expiry and the current database session on every protected request. Signing in
again atomically revokes the previous session. An unexpired signed token from a
revoked session may only repeat logout for that same session; it cannot access
profiles or refresh. Refresh secrets expire after 30 days of inactivity, rotate
on every successful refresh, and are stored only as SHA-256 hashes. Concurrent
refreshes using one secret have at most one winner. Clients must serialize refresh
requests and persist each replacement before retrying. Responses containing tokens
must be treated as secrets.

Failures use ProblemDetails with a machine-readable `code`; examples include
`INVALID_REQUEST`, `USERNAME_TAKEN`, `INVALID_CREDENTIALS`, `SESSION_REVOKED`,
`INVALID_REFRESH_TOKEN` and `INVALID_PET_PROFILE`. Database connection failures
return `503 DATABASE_UNAVAILABLE`, including during token validation, so clients
can retry without discarding their credentials. Auth routes have an IP-based
fixed-window limit of 20 requests/minute (`Auth__RequestsPerMinute`, 1-1000).
Registration/login also share a limit of 10 requests/minute per trimmed,
case-insensitive login across IPs (`Auth__LoginRequestsPerMinute`, 1-1000).
Both return `429 RATE_LIMITED`; refresh/logout use the IP budget only.

### Friends API (development)

All routes require a valid bearer session. Actor IDs come from validated claims.

| Method | Route | Request / response |
|---|---|---|
| POST | `/api/v1/friends/requests` | `{login}` -> 201 `{requestId}` |
| GET | `/api/v1/friends` | Accepted friends: `[{userId,login,petName,petAssetId}]` |
| GET | `/api/v1/friends/requests` | `{incoming:[...],outgoing:[...]}`; each item has `requestId`, `user` (the other participant's public profile) and `requestedAtUtc` |
| POST | `/api/v1/friends/requests/{requestId}/accept` | Recipient only -> 204; repeated acceptance of the same accepted request also returns 204 |
| POST | `/api/v1/friends/requests/{requestId}/decline` | Recipient only -> 204; repeated decline is idempotent |
| DELETE | `/api/v1/friends/{friendUserId}` | Remove an accepted friendship -> 204; repeated removal is idempotent |
| POST | `/api/v1/users/{userId}/block` | Add own directional block and remove the relationship/pending request -> 204 |
| DELETE | `/api/v1/users/{userId}/block` | Remove only own block -> 204 |

Friend requests use exact, case-insensitive login matching with the same normalization
as authentication. There is no user directory, prefix search or online status.
Only the recipient can accept or decline; crossed/duplicate pending requests return
`409 FRIEND_REQUEST_EXISTS` and never implicitly accept a friendship.

A block in either direction prevents new requests and excludes the relationship
from friend/request lists. Unblocking does not restore friendship; both people must
agree to a new request. Mutual blocks are independent. Requesting a blocked person
and requesting an unavailable login both return `404 USER_NOT_AVAILABLE`, without
identifying who blocked whom. Unrelated request IDs return
`404 FRIEND_REQUEST_NOT_AVAILABLE`; an attempt to accept one's own outgoing request
returns `403 FRIEND_REQUEST_RECIPIENT_REQUIRED`.

Each new request has a fresh `requestId`, so an old accept URL cannot approve a
later request. One canonical friendship row per pair is retained across decline,
removal, blocking and re-friending. Every relationship mutation acquires that row's
lock in a Unit of Work transaction; the repository creates a removed placeholder
when no pair exists yet. Concurrent requests/blocking use the same lock, including
the first interaction between two accounts. Mail send/read operations reuse these
stable rows for atomic friendship/quota checks.

Friend mutations have a separate per-account fixed-window limit of 20 requests/minute,
configurable with `Friends__RequestsPerMinute` (1–1000). Read routes do not consume
this budget. Accepted friends are ordered by normalized login then user ID; pending
requests are ordered by request time then request ID. Public profiles contain no
password hashes, session details, email addresses or block flags.

### Letters API (development)

All routes require a valid bearer session and return private, non-cacheable data.

| Method | Route | Request / response |
|---|---|---|
| POST | `/api/v1/letters` | `{recipientUserId,body,clientRequestId}` -> 201 `{letterId,sentAtUtc,unreadCount,limit:10}` |
| GET | `/api/v1/letters/quota/{recipientUserId}` | Own directional `{unreadCount,limit:10}`, even after friendship removal/blocking |
| GET | `/api/v1/letters/outgoing?cursor=&limit=50` | `{items,nextCursor}` for all own sent letters |
| GET | `/api/v1/letters/incoming?cursor=&limit=50` | `{items,nextCursor}` for own **read** received letters |
| POST | `/api/v1/letters/{letterId}/read` | Confirm one received letter -> `{letters:[{letterId,readAtUtc}],unreadCount,limit:10}` |
| POST | `/api/v1/letters/read` | `{letterIds:[...]}` -> the same response shape, confirming exactly those IDs atomically |

Sending creates one immutable text letter. There are no edit, cancel, attachment
or delete APIs. The sender comes from validated claims. New sends require accepted
friendship without a block in either direction, checked under the same pair lock
as relationship changes. The maximum is 10 unread letters **per ordered
sender/recipient pair**. At the cap, `409 UNREAD_MAIL_LIMIT_REACHED` includes
`unreadCount` and `limit`. Reading one letter releases exactly one slot. Removing
or blocking a friend retains the letters and their quota; re-friending allows
unread letters to be read again. Read history remains available after removal.

Use a fresh UUID `clientRequestId` for each deliberate send and keep it on network
retry. Identical retries return the existing letter and a fresh quota count, even
if friendship was removed after the original successful send. Reusing the key
with a different recipient or body returns `409 IDEMPOTENCY_KEY_REUSED`.
The database uniquely indexes `(senderId,clientRequestId)`, including concurrent
attempts targeting different friends. Different senders have independent keys.

Bodies contain 1–500 Unicode scalar values and must contain non-whitespace text.
An emoji counts as one scalar; combining marks count separately. Newlines and
tabs are accepted, other control characters and malformed UTF-16 are rejected.
Leading/trailing whitespace and markup-like characters are preserved as plain
text. The API does not interpret HTML/Markdown or log letter bodies.

Read lists contain 1–10 distinct, nonempty IDs from one sender, all owned by the
authenticated recipient. Missing/foreign IDs return `404 LETTER_NOT_AVAILABLE`;
mixed senders or duplicate IDs are rejected without partial changes. Current
friendship and block status are checked for read operations, including retries.
Repeated successful reads keep their original `readAtUtc`. Bulk read confirms
only the supplied snapshot IDs; later letters stay unread. No transaction remains
open while a person reads a letter. Unread bodies are absent from received history;
the mailbox endpoint exposes them to their recipient as a visit snapshot.

History items contain `id`, `senderId`, `recipientId`, `body`, `sentAtUtc`,
`readAtUtc` and `otherUser` (the correspondent's safe public profile). Page limits
are 1–100, default 50. Outgoing order is `(sentAtUtc DESC,id DESC)`; incoming order
is `(readAtUtc DESC,id DESC)`. Opaque `nextCursor` values use keyset pagination,
including ID tie-breaking, and are null on the last page. Invalid cursors/limits
return 400. The caller's identity always scopes a history query.

New sends and retries have a separate per-account limit of 30 requests/minute,
configurable with `Letters__RequestsPerMinute` (1–1000). Quota, history and read
requests do not consume that budget.

### Mailbox and realtime

`GET /api/v1/mailbox/next` requires a valid bearer session. It returns 204 when no
deliverable unread mail exists, or a non-cacheable 200 response:

```json
{
  "sender": { "userId": "...", "login": "...", "petName": null, "petAssetId": null },
  "letters": [{ "id": "...", "body": "...", "sentAtUtc": "..." }]
}
```

Among letters whose sender is an accepted, unblocked friend, the earliest
`(sentAtUtc ASC,id ASC)` selects the sender. All available unread letters from
that sender, up to 10, form the ordered snapshot. Selection and body loading use
one SQL statement and one database snapshot. Fetching a visit never marks letters
read, reserves a guest or creates a persistent batch/visit table. API restarts and
reconnects therefore reconstruct the queue from PostgreSQL.

The Desktop client must keep the original snapshot while that guest is active.
Later letters are excluded from its read-all request. After all original IDs are
confirmed, the reading window is closed and the exit animation finishes, fetch
the next visit again. A refresh
signal during a visit must not replace its snapshot or spawn another guest.
Relationship changes require rechecking that the current sender remains a friend;
if unavailable, close the visit without marking the remaining letters read.

`/hubs/mail` is an authorized ASP.NET Core SignalR hub. The .NET client supplies a
bearer access token in the Authorization header. The hub exposes no application
commands, public presence, arbitrary group joins or letter payloads. All server
events have **zero arguments**:

| Event | Trigger / audience |
|---|---|
| `MailboxChanged` | After a new send or read commit, to the recipient's current session; also after accepted friendship restores delivery availability |
| `FriendshipsChanged` | After friend request/response, removal or block/unblock commits, to both affected accounts' current sessions |
| `SessionRevoked` | To the replaced/logged-out session, after the revocation commits |

SignalR user routing is keyed by server session ID rather than account ID.
Normal wakeups resolve the current active session under the same account lock as
login/logout, preventing a routing decision from racing revocation. Each dispatch
is bounded by a two-second deadline. Push failures log only event/failure types,
do not expose bodies or tokens, and do not change the successful REST result or
roll back committed data. Existing-send/read retries do not create another wakeup
when nothing changed.

Connections validate session state again on connect. A hub filter checks current
database state before invocations, so a cached SignalR principal cannot authorize
a revoked session. `CloseOnAuthenticationExpiration` closes the connection when
the access JWT expires. On .NET 10 the client refreshes through REST and reconnects
with the replacement access token. A revoked connection may stay physically open
until expiry if its client ignores `SessionRevoked`; it receives no normal wakeups
and cannot obtain private data through the hub or REST. Multiple connections of
the same valid session can receive the same zero-payload signals.

REST remains the source of truth. Client startup, successful login, reconnect and
guest completion must fetch the mailbox. Coalesce wakeups; after a lost signal,
recover with a low-frequency reconciliation roughly every 10–15 minutes with
jitter. The client and postman share these synchronization hooks. There is no
five-second polling loop, transactional outbox, broker or multi-server backplane.
Delivery is best effort, with recovery from REST.

### Desktop online infrastructure

`Petty.Online.Client` is a platform-neutral .NET 10 library, independent of server,
WPF and the local pet model. It provides typed REST operations for accounts,
profiles, friends, blocks, letters, quota, histories and mailbox snapshots.
`OnlineClient` owns its HTTP client with a 12-second timeout and cancellation.
Session changes, HTTP calls and refresh rotation are serialized. Expiring access
tokens refresh before use; an authentication challenge gets at most one refresh
and replay. Revoked/invalid refresh sessions clear local credentials.

GETs and idempotent letter sends retry a transient transport failure/502/503/504
once with jitter. Other mutations and refresh/login are not automatically retried.
The caller supplies `SendLetter.ClientRequestId` and retains it across uncertain
responses and explicit retries. Success requires the server's response. Exceptions
expose a machine code and quota fields rather than echoing server diagnostics.
Offline logout clears local credentials even if server revocation fails; callers
must surface that failure rather than claim a confirmed remote logout.

Until production hosting is configured, online startup is opt-in through
`PETTY_SERVER_URL`, an HTTPS origin without credentials, path or query. HTTP is
accepted only for loopback development. Without this variable Petty starts locally
without creating an online client. There is no built-in public service address.
Open **Account and friends** from Settings or the tray menu. Registration is
optional, without email or password recovery. The account window provides friends,
letter composition and paginated sent/read-received histories. Signed-in users can
also open composition through the pet's Letter action. Network failures leave the
local pet running. Online windows close when their session is replaced or revoked.

Composition shows the recipient's server quota and rechecks it before the first
send. After an uncertain send, the text, recipient and request ID remain fixed;
Retry replays that same letter, including when a lost success response filled the
quota. A success message requires the server acknowledgement. Closing a draft
discards its in-memory retry key; sent letters remain in server history.

The incoming window retains the original visit snapshot across closing/reopening,
acknowledges expansion/read-all only after server success, and never reads later
arrivals implicitly. Click the waiting visitor to open its letters. Closing the
window leaves the visitor waiting if any original letters remain unread. Opening
a letter acknowledges it after server success; there is no separate read button.
After all original IDs are confirmed, the window stays open until the user closes
it; only then does the visitor depart. After a network failure, collapse and reopen
the letter or use Read all to retry. Later arrivals belong to a subsequent visit.

`Petty.App.Online.OnlineCoordinator` starts after the local pet is restored, sends
pet profile changes after login/create/rename, and publishes account/friends/mailbox
updates through the WPF Dispatcher. The coordinator owns the connection/retry and
shutdown tasks; networking never participates in local simulation or state saves.
It coalesces server wakeups, fetches REST on startup/login/reconnect/visit completion,
and reconciles every randomized 10–15 minutes. Connection failures retry with capped
exponential backoff and jitter. The three SignalR events carry no letter contents.
`SessionRevoked` clears only the matching session, preserving a newer login.
Mailbox wakeups refresh only the mailbox. Friendship wakeups refresh friends,
pending requests and the mailbox; the friends window consumes that published snapshot
without issuing duplicate reads. Account/friend events fire only when their data
changes, so a mailbox-only update retains existing friend rows, including during a
pending mutation. Invalidations arriving during a fetch remain queued for another pass.
Letter history uses WPF recycling virtualization: loaded letter data remains available
while the window is open, but visual rows are realized only around the viewport.

On Windows, `ProtectedTokenStore` encrypts session tokens with DPAPI CurrentUser in
an origin-specific `online-session-*.bin` under the resolved Petty data directory.
Isolated `--data-dir`/`PETTY_DATA_DIR` runs use that directory too. Writes replace an
encrypted temporary file atomically; corrupt/unreadable DPAPI payloads are discarded.
Passwords are never persisted. Pet-state schema v1 stays unchanged, and letters
have no filesystem cache. DPAPI requires the same Windows user; it is not a portable
account backup.

`Petty.App.Delivery.PostmanCoordinator` owns one visitor and its original letter
snapshot. It waits for the home pet's entry to finish and requests a fresh mailbox
snapshot from the online coordinator. The guest walks in from a primary-monitor edge away from the bowl,
sits and waits without a timeout. It reuses `PetWindow` alpha hit-testing and desktop
placement, with no second local pet, needs, props or persistent visitor state.
After all snapshot IDs are confirmed and the reading window is closed, the guest
rises and walks fully off screen;
only then is the next visit fetched. Refresh signals never replace an active visit.
A mailbox request generation prevents an older in-flight response from starting
the next visit after departure. The postman does not issue a second mailbox read.
Unchanged rounded pet-window geometry skips native placement; display, DPI,
external geometry changes and shell reattachment invalidate the cached placement.

Removing/blocking the sender closes its reading window and starts departure without
acknowledging remaining letters. Logout, session replacement, pet recreation and
shutdown immediately dispose the guest. Offline reading failures leave it waiting;
restarting reconstructs unread visits from the server. Shell recovery reattaches the
visitor; display/taskbar changes reuse the existing primary-monitor geometry and
eased floor. No local pet save or autostart setting depends on online delivery.

All 40 prebuilt mail appearances are embedded. Unknown or missing sender appearances
use the standard British Shorthair mail atlas; no remote images are downloaded.
Sprite format and regeneration instructions are in `assets/sprites/README.md`.
Live click-through, DPI, layering and shell/lifecycle acceptance remains an owner
check; headless scene tests do not establish these Windows behaviors.
The repeatable local backend/client setup and owner acceptance checklist are in
[`tests/E2E/README.md`](tests/E2E/README.md).

`dotnet test tests/Petty.Online.Tests -c Release` checks refresh serialization,
idempotent retry, revocation, cancellation, coordinator shutdown and isolated Windows
DPAPI storage. Server integration tests additionally exercise the typed client
against PostgreSQL and SignalR transport reconnects. DPAPI verification requires
Windows; real Desktop window/lifecycle acceptance remains an owner check.

### PostgreSQL integration tests

The normal test command runs composition tests and explicitly skips PostgreSQL
tests when `PETTY_TEST_POSTGRES` is absent. To exercise registration, rotation,
revocation, concurrent login/refresh, profile ownership, token validation,
friend consent, crossed requests, block races, stale-request rejection, mail quota,
send retries, snapshot reads, history pagination, queue ordering/restart recovery,
WebSocket routing, revocation/expiry and failed-push recovery, point
it at a **disposable** database whose name starts with `petty_test_`:

```powershell
$env:PETTY_TEST_POSTGRES = 'Host=localhost;Database=petty_test_auth;Username=<test-user>;Password=<test-password>'
dotnet test Petty.Server.slnx -c Release
Remove-Item Env:PETTY_TEST_POSTGRES
```

These tests apply migrations and insert isolated test accounts. They never use
`ConnectionStrings__Petty` as a fallback. The database-name guard helps prevent
accidental use of the app's database; dispose of the test database afterward.

Local HTTP binding is for development. Public hosting will require TLS/WSS and
explicit `AllowedHosts` configuration for the server domain; the default allows
only localhost. Production templates and operator steps are in [deploy/README.md](deploy/README.md). To publish backend
runtime files, use `dotnet publish src/Petty.Server.Api -c Release -o artifacts/server`.

See also [local E2E acceptance](tests/E2E/README.md) and
[production hosting](deploy/README.md).
