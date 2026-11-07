# Petty Backend - agent instructions

Read the [project instructions](../../AGENTS.md) first. This document covers every
`Petty.Server.*` project, server tests and deployment configuration.

## Architecture and persistence

- Use ASP.NET Core Controllers, PostgreSQL/EF Core and scoped Mediator. Domain is
  platform-free; Application owns use cases; Infrastructure owns storage; Api owns
  transport and composition. Keep business decisions out of controllers.
- Business persistence uses feature-specific repositories and `IUnitOfWork`.
  Repositories share the scoped `ServerDbContext`; handlers commit explicitly
  through `IUnitOfWork`. Identity's user store also defers saving to that unit.
- Preserve transaction and row-lock boundaries for session rotation, friendship
  changes and letter/quota checks. Idempotent retries must preserve ownership,
  friendship and concurrency guarantees.
- Startup never creates or migrates the database. Apply migrations explicitly.
  Keep credentials in environment variables or a secret provider, outside source
  control and desktop pet-state. Development HTTP is loopback-only; public hosting
  requires TLS/WSS and explicit host/proxy configuration.

## Authentication and delivery

- Validate the current database session on protected requests. Preserve refresh
  rotation and immediate revocation when a session is replaced or logged out.
  Keep password hashing strength and avoid logging tokens or letter bodies.
- Actor IDs come from validated claims. Preserve friendship consent, directional
  blocks, atomic unread quotas and sender-owned letter idempotency keys.
- REST is the source of truth. SignalR events carry zero arguments and route by
  session ID. Notify after commits; push failure must not undo a successful REST
  mutation. Preserve account-lock routing checks and bounded notification deadlines.
- Follow the existing ProblemDetails error codes and rate limits. Read
  [API and concurrency documentation](../../DEVELOPMENT.md) when changing routes,
  authorization, transactions or delivery semantics. Coordinate contract changes
  with the [Desktop instructions](../Petty.App/AGENTS.md) and shared online client.
- Use [deployment documentation](../../deploy/README.md) for hosting and
  [local E2E acceptance](../../tests/E2E/README.md) for backend/client verification.

## Validation

Run from the repository root with .NET SDK 10; server builds support Windows/Linux:

```powershell
dotnet build Petty.Server.slnx -c Release
dotnet test Petty.Server.slnx -c Release
```

PostgreSQL integration tests are skipped unless `PETTY_TEST_POSTGRES` points to a
disposable database whose name starts with `petty_test_`. For persistence, auth,
friendship, letters or SignalR changes, run those tests with PostgreSQL configured;
report any skips explicitly. Tests apply migrations and create test data; never
use the owner's database. Remove the disposable database/container afterward.
Setup and migration commands are in `DEVELOPMENT.md`.
