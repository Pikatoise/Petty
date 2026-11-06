# Production hosting

The commands below assume the repository root unless a step explicitly says
to run from `deploy/`. Copy the deployment templates to the server as described
below; see [development instructions](../DEVELOPMENT.md) for local setup.

The templates in `deploy/` run one API, PostgreSQL 17 and Caddy. Only Caddy
publishes ports 80/443; neither PostgreSQL nor Kestrel has a host port. The database
network is internal and the proxy cannot join it. API runs as the .NET image's
non-root user, with a read-only filesystem and temporary `/tmp`. One process is
intentional: there is no SignalR backplane or automatic horizontal scaling.

Build the Linux runtime image on a build machine, not by cloning Desktop onto VPS:

```sh
docker build -f deploy/Dockerfile -t petty-server:<reviewed-commit> .
```

The allowlist in `.dockerignore` and explicit Dockerfile copies include only the
four server projects. The final image contains ASP.NET runtime, backend publish
files and an EF migration bundle; no SDK, WPF, sprites, owner data or source tree.
The Dockerfile supports Linux amd64/arm64; verify the image on the actual VPS
architecture before rollout. Base image tags follow .NET 10/PostgreSQL 17/Caddy 2
patches: rebuild and test for security updates, then deploy the reviewed image by
immutable tag or digest. Server image revisions are commit IDs, independent of
Desktop's project version; REST stays `/api/v1`.

### First deployment (explicit operator action)

Copy only `deploy/` templates and the approved image to a Linux VPS with Docker
Engine/Compose, DNS for the API domain, and inbound TCP 80/443. Administrative SSH
should be restricted separately. Load a CI image with `docker load -i
petty-server-image.tar.gz`; verify its checksum first. Choose an image tag/digest
and domain in `deploy/.env` (copy `.env.example`). Check that the private subnet
and both fixed addresses do not overlap another Docker/host network.

From `deploy/`, as the host operator:

```sh
cp .env.example .env
# Edit .env with the domain, certificate contact and reviewed image tag/digest.
sh init-secrets.sh
docker compose config --quiet
docker compose up -d --wait db
docker compose --profile maintenance run --rm migrations
docker compose exec -T db psql -v ON_ERROR_STOP=1 -U petty_owner -d petty < grants.sql
docker compose up -d api proxy
curl --fail https://api.example.com/health/ready
```

`init-secrets.sh` requires OpenSSL and refuses to replace existing secrets. It
creates random database passwords and an independent JWT signing key under
`deploy/secrets/`, ignored by Git and Docker build context. The top directory is
mode 0700 on the Linux host. Its mounted child files are readable by the non-root
API, while other host users cannot traverse the parent. Docker administrators can
read container secrets and must be trusted. Keep backups of signing material in
protected storage; never print secrets, use `set -x`, or attach `.env`, secret
files or database dumps to a release.

API mounts only its app-user connection string and JWT key. Migration jobs mount
only the owner connection string. PostgreSQL initialization creates `petty_app`
without schema-creation privileges. After each migration, `grants.sql` grants
business-table DML plus read-only EF history access; it grants no role-management,
DDL, database-creation or superuser rights. API startup never applies migrations.
Initialization scripts/password files are used on a **fresh** database volume;
editing them does not rotate existing database roles. Rotate roles explicitly and
update matching secret files together. Signing-key rotation invalidates existing
access JWTs; plan reauthentication and never change the key accidentally on deploy.

Caddy obtains/renews public certificates and proxies WebSockets. Preserve its
certificate volume. The API trusts one forwarded hop from the configured proxy
address only, forwards scheme/client IP (not Host), and requires an explicit
AllowedHosts domain. HTTPS mode rejects plaintext API/Hub requests rather than
redirecting credentials; only `/health/live` is exempt for local probes. Do not set
`ASPNETCORE_FORWARDEDHEADERS_ENABLED` or trust arbitrary networks. CORS remains off.
If another proxy/CDN is added, revise and test the trust chain before enabling it.

REST requests have a 15-second cancellation deadline (1-120 seconds via
`Hosting__RequestTimeoutSeconds`); SignalR connections are not subject to that
REST deadline. Kestrel caps bodies at 16 KiB, waits 15 seconds for headers and limits
ordinary/upgraded connections separately (`Hosting__MaxConnections`, default 256;
`Hosting__MaxWebSockets`, default 128). Existing 4 KiB Hub messages and token/session
validation still apply. Configure every client through its HTTPS origin using
`PETTY_SERVER_URL`; there is no built-in public server URL.

### Updates, backup, recovery and monitoring

Before every schema/image update, pause writes by stopping API, record the current
image and EF migration ID, and take a database backup. From `deploy/`:

```sh
umask 077
mkdir -p backups
chmod 700 backups
docker compose stop api
docker compose exec -T db pg_dump -U petty_owner -d petty -Fc > backups/petty.dump
# Only after reviewing/testing the new migrations and selecting the new image in .env:
docker compose --profile maintenance run --rm migrations
docker compose exec -T db psql -v ON_ERROR_STOP=1 -U petty_owner -d petty < grants.sql
docker compose up -d api
curl --fail https://api.example.com/health/ready
```

Use a unique timestamped backup name operationally. Copy dumps, role credentials
and signing material to separate protected/encrypted storage; set retention and
verify backups regularly. Dumps contain private account/password hashes and letter
text. A database dump alone does not recreate cluster roles or signing keys.
Regular backups on a single VPS do not replace off-host backups.

Rehearse restoration into a new database with the same roles on an isolated host:

```sh
docker compose exec -T db createdb -U petty_owner petty_restore
cat backups/petty.dump | docker compose exec -T db pg_restore -U petty_owner --no-owner --exit-on-error -d petty_restore
```

Check migrated schema, row counts, login and letter retrieval through an isolated
API pointed at that database. Never overwrite the live database as a restore test.
For rollback, use the old image only if it is compatible with the resulting schema.
Otherwise keep API stopped, restore the pre-update dump to a replacement database,
switch connection strings deliberately, then start the old image and verify it.
A backward migration is a reviewed/tested operation; no automatic `Down` migration
or destructive volume removal is part of deployment. Restoring a dump loses writes
after that backup; stopping API during the update bounds this loss window.

Container logs use Docker rotation (three files of 10 MiB per service). API log defaults avoid
HTTP/EF payload logging; Caddy access logging is disabled. PostgreSQL statement and
error-parameter logging are suppressed. Do not enable sensitive-data logging or
log Authorization headers, passwords, tokens or letter bodies. Monitor process
liveness and authenticated service behavior externally; `/health/ready` returns
503 until the database is reachable and all migrations are applied. Readiness does
not expose database diagnostics. `unless-stopped` restarts services after process
failure/host reboot; it does not repair a bad image, missing schema or full disk.

Compose memory ceilings (API/database 256 MiB each, proxy 128 MiB), database pool
limits and connection limits are initial safeguards, **not measured capacity**.
Tune from actual VPS memory, CPU, disk/WAL growth, response latency and WebSocket
counts. Test expected load, reconnect storms, API restart and backup restoration
on that VPS. No minimum capacity or supported user count has been measured.

Before opening public registration, the operator must publish a privacy policy,
contact and account/letter deletion process, including backup retention/deletion,
and finish the Windows two-account acceptance matrix. These policies and public
DNS/TLS, host firewall, production backup schedule and load results are operator
inputs; local container tests do not establish them.

### CI and release gates

`.github/workflows/server-build.yml` builds/tests the server on Linux with a
**disposable** PostgreSQL database, then uploads a checksummed runtime-image archive
for seven days. It runs for server-related PRs/pushes to `master` or manual dispatch.
The CI password belongs only to that ephemeral service. No production credentials,
SSH command or automatic VPS deployment is present. Promote a reviewed commit's
image using the explicit migration/backup procedure above.

`.github/workflows/desktop-release.yml` is manual and restricted to `master`. It
checks Desktop only, requires an unused matching version tag, builds the existing
single `Petty.exe`, atomically creates that tag at the built commit, and creates a
**draft** GitHub Release. Configure the `desktop-release`
GitHub environment with required reviewers and a `master` branch restriction before
using it; review/publication of the draft remains explicit. No ZIP is generated.
Actions are pinned to reviewed commit SHAs; maintain those pins intentionally.

Implementation references: [ASP.NET Core proxy trust](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0),
[request timeouts](https://learn.microsoft.com/en-us/aspnet/core/performance/timeouts?view=aspnetcore-10.0),
[EF migration bundles](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying#bundles)
and [Caddy reverse proxy](https://caddyserver.com/docs/caddyfile/directives/reverse_proxy).
