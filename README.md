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
| `web` | Angular app; see Web below |
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

## Web

Requires Node.js 24.15 or later (`web/.nvmrc`; CI uses the latest 24).

```sh
cd web
npm ci
npm start            # http://localhost:4200, proxies /health to the API on http://localhost:5208
npm run lint
npm run format:check
npm test             # Vitest
npm run build
```

The home page shows whether the API answers (`/health/live`) and whether it can reach its database (`/health/ready`). Run the API with `ConnectionStrings__App` set to see the database as OK.

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

## Cloud

GitHub Actions signs in to Azure with OpenID Connect, so no Azure password, secret or certificate is stored in GitHub. Each environment has its own identity:

| GitHub environment | Azure identity (in `rg-financegoals-shared`) | Access |
| --- | --- | --- |
| `test` | `id-financegoals-deploy-test` | Contributor on `rg-financegoals-test` only |
| `production` | `id-financegoals-deploy-production` | Contributor on `rg-financegoals-prod` only |

Each identity trusts only jobs running in its GitHub environment. The federated credential subject is `<prefix>:environment:<env>`, where the prefix is read from the repository. This repository uses GitHub immutable subjects, `repo:FirstCall42@<id>/FinanceGoals@<id>`. Both environments are deployable from exactly `main`. The script locks each environment before Azure trusts it, and stops if an environment allows any other branch. Neither identity can change access, touch the other environment, or see the rest of the subscription.

Set it up once, and again whenever the script changes (it is idempotent):

```sh
az login
tools/azure/setup-github-oidc.sh FirstCall42/FinanceGoals --dry-run   # shows what it would change
tools/azure/setup-github-oidc.sh FirstCall42/FinanceGoals             # default region: norwayeast
```

Then run **Actions > Azure sign-in check** from `main`. Each environment signs in, reaches its own resource group, and is refused the other.

Everything the script creates is free: resource groups, managed identities, federated credentials and role assignments.

### Infrastructure

`infra/main.bicep` defines everything one environment runs on, and there is one parameter file per environment. Nothing is configured in the portal.

**The test environment is the live one** until production exists (#86). Production's resource group, deploy identity and GitHub environment exist, but nothing is deployed there, and `infra/production.bicepparam` pins no region yet.

| | test (live) | production (#86) |
| --- | --- | --- |
| Region | `swedencentral` | to be decided |
| Database (free offer, auto-pause at the limit) | `financegoals-test` | `financegoals-prod` |
| Static Web App (Free) | `eastus2` | `eastus2` |

Each environment has a Log Analytics workspace, managed identities for the API and the migrator, an Azure SQL server with Entra-only sign-in (the migrator's identity is its admin), a free-offer database, a Container Apps environment with the API (scales to zero) and the migrator job, and a Static Web App. There is no Key Vault and no password: the API and the migrator sign in to SQL with their managed identities.

```sh
az deployment group what-if -g rg-financegoals-test -p infra/test.bicepparam   # preview
az deployment group create  -g rg-financegoals-test -p infra/test.bicepparam
```

The API runs a placeholder image until the deploy workflow (#21) supplies real images (#20). Probes call `/health/live` only; see docs/free-tier.md for why.

Regions are where this subscription is allowed to create things. In 2026-11 Norway East, West Europe and North Europe refused new SQL servers, and the free SQL offer failed in Germany West Central and France Central, working only in Sweden Central. West Europe refused new Static Web Apps, and the subscription allows one Container Apps environment per region. See docs/free-tier.md. The resource groups themselves stay in `norwayeast`; their location is only metadata.

### Cost guardrails

A monthly budget of 1 (billing currency) on the subscription emails the Owners on any charge:

```sh
tools/azure/setup-budget.sh --email <address> [--email <another>] --dry-run
tools/azure/setup-budget.sh --email <address> [--email <another>]
```

[docs/free-tier.md](docs/free-tier.md) lists the free allowances we rely on, what happens at each limit, what is not free (Key Vault), and how to check usage. Uptime monitors call `/health/live` only, at most once an hour.
