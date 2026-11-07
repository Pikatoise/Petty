# Local online acceptance

Reusable owner-operated E2E template for Windows 11, .NET SDK 10 and Docker
with Linux containers. Run commands from the repository root in Windows
PowerShell. No production deployment, release packaging or second Petty instance
is needed. Close any running Petty through its tray before starting.

The database is disposable and exposed only on loopback. Use only the test accounts
below. The fixture creates friendships and letters through the real API; it does
not prove that the corresponding desktop UI works. Live hover, DPI, layering,
shell recovery and animations require owner observation.

## 1. Start an isolated backend (terminal A)

```powershell
$ErrorActionPreference = 'Stop'
$dbPassword = [Guid]::NewGuid().ToString('N')
$env:POSTGRES_PASSWORD = $dbPassword
docker run --detach --rm --name petty-e2-postgres `
    -e POSTGRES_PASSWORD -e POSTGRES_USER=petty_e2 -e POSTGRES_DB=petty_test_e2 `
    -p 127.0.0.1::5432 --tmpfs /var/lib/postgresql/data postgres:17
if ($LASTEXITCODE -ne 0) { throw 'Database startup failed; do not reuse another database.' }
Remove-Item Env:POSTGRES_PASSWORD
$dbPort = ((docker port petty-e2-postgres 5432/tcp).Trim() -split ':')[-1]
$env:ConnectionStrings__Petty = "Host=127.0.0.1;Port=$dbPort;Database=petty_test_e2;Username=petty_e2;Password=$dbPassword;GSS Encryption Mode=Disable"
$bytes = New-Object byte[] 32
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$rng.GetBytes($bytes)
$rng.Dispose()
$env:Auth__SigningKey = [Convert]::ToBase64String($bytes)
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5187'
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    docker exec petty-e2-postgres pg_isready -U petty_e2 -d petty_test_e2
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep -Milliseconds 500
}
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw 'Tool restore failed.' }
dotnet ef database update --project src/Petty.Server.Infrastructure `
    --startup-project src/Petty.Server.Api --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
dotnet run --project src/Petty.Server.Api -c Release --no-launch-profile
```

Keep terminal A open. API startup never applies migrations. HTTP is intentionally
limited to local development; this template does not verify public TLS/WSS or VPS
capacity. Port 5187 must be free. Stop an earlier test explicitly rather than
reusing an unidentified process or database.

## 2. Seed and start the client (terminal B)

```powershell
Invoke-RestMethod http://127.0.0.1:5187/health/ready
powershell -NoProfile -ExecutionPolicy Bypass -File tests/E2E/Seed.ps1
if ($LASTEXITCODE -ne 0) { throw 'Fixture setup failed.' }
dotnet build Petty.Desktop.slnx -c Release
if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
$env:PETTY_SERVER_URL = 'http://127.0.0.1:5187'
& '.\src\Petty.App\bin\Release\net10.0-windows10.0.19041.0\Petty.exe' `
    --data-dir '.\artifacts\verification\e2-owner'
```

Create a test pet if prompted. Open **Account and friends** from the tray or
Settings and log in as `e2_owner`, password `E2TestPassword2026`. The other test
logins are `e2_cat` and `e2_dog`, with the same test-only password. All local state
and DPAPI sessions stay under the test data directory; autostart synchronization
is skipped. Existing test pet state can be reused after a fresh database start;
log in again if its previous server session is no longer valid.

`Seed.ps1` requires a fresh database. It stops on errors; it does not silently
reuse accounts, reset quotas or mark letters read. After consuming the initial
letters, add three cat letters and one dog letter without replacing the recipient
session:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tests/E2E/Seed.ps1 -LettersOnly
```

Run this only while the test senders remain friends with the recipient and have
enough quota. An interrupted fixture run may have already sent some letters.
Every send uses a fresh request ID; rerunning adds letters rather than retrying an
uncertain previous send.

## 3. Owner acceptance checklist

| Action | Expected result |
|---|---|
| Start without `PETTY_SERVER_URL` | Creation, feeding, play, settings RU/EN, tray exit and restore work independently of the server |
| Log in as `e2_owner` | Local pet stays active; account shows two prepared friends |
| Wait for delivery | Home pet enters first; one cat visitor arrives carrying three letters |
| Open and close the reading window without expanding letters | Cat remains; letters stay unread |
| Expand one letter | Server-confirmed status becomes Read; no per-letter read button; other letters stay unread |
| Close and reopen the window | Same visit and read status are retained |
| Expand the last unread letter | Window remains open; cat waits while the text is visible |
| Close the fully read window | Cat rises and leaves; only after full departure does the dog arrive |
| Use Read all for the dog | Status updates, but the window remains open until closed; dog then departs |
| Inspect histories | Sender sees confirmed sends; recipient sees only confirmed reads |
| Add letters while a visit window is open | Original snapshot stays fixed; later letters belong to a later visit |
| Stop API with Ctrl+C in terminal A, then expand unread mail | Safe error, no false Read status, local pet continues and guest waits |
| Restart API in terminal A with the same run command | Collapse/reopen the letter or use Read all to retry; unread queue recovers |
| Exit and restart the isolated client with unread mail | Unread delivery resumes from the server without creating extra letters |
| Remove a sender in the friends UI | Guest/window closes appropriately; pending letters stay hidden, not marked read |
| Log out, log in as the sender, invite `e2_owner`; log back in as owner and accept | Pending mail becomes deliverable again; removal did not reset sender quota |
| Block a sender, then unblock and re-establish friendship as needed | Block prevents new sends and delivery; unread data remains intact |
| Log in as a sender and compose letters to owner | Quota is displayed; 10 unread are accepted, #11 rejected; reading one frees exactly one slot |
| While owner is logged in, POST a second owner login from terminal B | Previous client session loses API/Hub access; local pet remains active |
| Check visitor clicks, transparent areas, ordinary app overlap, scaling/display changes and Explorer restart | No loss of click-through, primary-monitor placement or shell attachment |

To trigger session replacement without a second desktop instance:

```powershell
$body = @{ login = 'e2_owner'; password = 'E2TestPassword2026' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri http://127.0.0.1:5187/api/v1/auth/login `
    -ContentType application/json -Body $body | Out-Null
```

Login swaps also revoke old sessions by design. Do not claim that sequential
desktop checks establish simultaneous two-PC behavior. Atomic quota/concurrent
sends and unauthorized reads are covered separately by PostgreSQL integration
tests; do not infer those guarantees solely from this manual checklist.

## 4. Stop and clean up

1. Exit the test Petty through its tray.
2. Press Ctrl+C in terminal A to stop the API.
3. Run `docker stop petty-e2-postgres`. Its `--rm` container and tmpfs database
   are deleted; there is no persistent database volume.
4. Close the test terminals to discard process-scoped configuration and keys.
5. Remove only `artifacts/verification/e2-owner` after acceptance if the test pet
   and encrypted test sessions are no longer needed. Never clean the owner's
   `%LOCALAPPDATA%\Petty` directory.

Verify that port 5187 no longer serves health requests and
`docker ps -a --filter name=petty-e2-postgres` shows no container. Do not save
passwords, tokens, connection strings, owner paths or test logs in tracked files.
