# FinanceGoals

A personal finance app focused on goals: paying down loans and mortgages, savings goals, and multi-currency net worth across countries.

- **Stack:** C# / .NET 10 (ASP.NET Core API), Angular, Azure SQL Database (SQL Server in Docker locally)
- **Plan:** https://claude.ai/artifact/XRtmggwqJuXY5U9zsgMW8H

Status: Phase 0a, walking skeleton.

## Build

Requires the .NET 10 SDK (pinned in `global.json`).

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test          # also writes coverage to TestResults/<project>.cobertura.xml
```

## Layout

| Path | Purpose |
| --- | --- |
| `src/FinanceGoals.Domain` | Entities and value objects; references nothing |
| `src/FinanceGoals.Application` | Use cases; references Domain |
| `src/FinanceGoals.Infrastructure` | EF Core, external services; references Application |
| `src/FinanceGoals.Api` | ASP.NET Core Minimal API; references Application and Infrastructure |
| `src/FinanceGoals.Migrator` | DbUp console app that applies the database scripts in `db/` |
| `db/migrations` | Schema changes, run once each in order (`0001_name.sql`), journaled in `dbo.SchemaVersions`. Never edit one once merged |
| `db/repeatable` | Permissions, views and procedures, run on every deployment (`R001_name.sql`); must be idempotent |
| `db/seed` | Reference data, run once each (`S0001_name.sql`), journaled in `dbo.SeedVersions` |
| `tests/*` | Test projects: MSTest on Microsoft.Testing.Platform, NSubstitute for mocks, Shouldly for assertions |
| `tests/FinanceGoals.Architecture.Tests` | Layer dependency rules (NetArchTest and project references) and the no-primary-constructors check |

Shared build settings live in `Directory.Build.props`, and every NuGet version lives in `Directory.Packages.props`.

## API

```sh
ConnectionStrings__App="Server=...;User ID=financegoals_app;..." dotnet run --project src/FinanceGoals.Api
```

| Endpoint | Meaning |
| --- | --- |
| `GET /health/live` | The process answers. Runs no checks and never touches the database, so a database outage doesn't restart the API |
| `GET /health/ready` | The API can serve requests: it can connect to the database with `ConnectionStrings__App`. `503` otherwise. Wakes the database; see the cost note below |
| `GET /openapi/v1.json` | The OpenAPI document, in Development only |

**Cost: don't probe readiness often.** Every `/health/ready` call opens a database connection. The Azure SQL free offer is serverless, with 100,000 vCore-seconds a month, and it auto-pauses only when idle. A readiness probe every few seconds keeps it awake around the clock (about 1.3 million vCore-seconds a month at the 0.5 vCore minimum), so the allowance runs out in 2–3 days and the database then pauses or bills. In Container Apps, use `/health/live` for the liveness and readiness probes, and `/health/ready` only as the startup probe and for the post-deploy smoke test. Uptime monitors use `/health/live` only (#22).

The build writes the OpenAPI document to `src/FinanceGoals.Api/openapi/FinanceGoals.Api.json`. It is committed, so API changes show in the pull request diff.

Logs are JSON lines on standard output (Serilog), with levels set in the `Serilog` configuration section. Successful health probes are left out of the request log; failed ones are logged as errors.

## Database

The migrator connects as the **schema owner** (a `db_owner` user) and:

1. applies new migrations,
2. creates the **app user** if it is missing and adds it to the `app_data` role, which can read and write data in the `app` schema but cannot change the schema,
3. runs the repeatable scripts,
4. applies new seed scripts.

Every script runs in its own transaction. Running the migrator again changes nothing, except that a password app user's password is set again (so rotation works).

The app user must be a dedicated single user: the migrator refuses `dbo`, `guest`, the schema owner, a role, an Entra group, or a user that has any other role or database permission or owns a schema, object or role.

| Setting (environment variable) | Meaning |
| --- | --- |
| `ConnectionStrings__SchemaOwner` | Schema owner connection string (required) |
| `Migrator__AppUser` | App user to create and grant `app_data` (optional) |
| `Migrator__AppUserKind` | `password` (contained user) or `entra` (Entra ID user, e.g. the API's managed identity); required with `Migrator__AppUser` |
| `Migrator__AppUserPassword` | Its password, for kind `password` only. Set on every run, so rotating it here takes effect. Pass it as an environment variable, not `--Migrator:AppUserPassword=`, which shows in process listings |

Locally the database must be contained (`CONTAINMENT = PARTIAL`) so users can have passwords; Docker Compose (#13) will set that up. The migrator tests do the same against SQL Server in Docker, so Docker must be running for `dotnet test`.

On Azure SQL, creating an `entra` app user requires the migrator to sign in as an Entra principal (not a SQL user), and the server needs permission to look up Entra names. The Bicep templates (#18) and the deploy workflow (#21) cover this.

CI checks every PR's database scripts (the "Database checks" job): merged migration and seed scripts must be unchanged, both on disk and as embedded in the migrator, and a database built by `main`'s migrator and then upgraded by the PR's must match a fresh build. There are no releases yet, so `main` stands in for the last release. To run these checks locally, build `main`'s migrator and point `FINANCEGOALS_BASE_MIGRATOR` at its `FinanceGoals.Migrator.dll`; without it the test reports inconclusive.
