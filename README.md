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
dotnet test
```

## Layout

| Path | Purpose |
| --- | --- |
| `src/FinanceGoals.Domain` | Entities and value objects; references nothing |
| `src/FinanceGoals.Application` | Use cases; references Domain |
| `src/FinanceGoals.Infrastructure` | EF Core, external services; references Application |
| `src/FinanceGoals.Api` | ASP.NET Core Minimal API; references Application and Infrastructure |
| `src/FinanceGoals.Migrator` | DbUp console app that applies the database scripts |
| `tests/*` | Test projects: MSTest on Microsoft.Testing.Platform, NSubstitute for mocks |

Shared build settings live in `Directory.Build.props`, and every NuGet version lives in `Directory.Packages.props`.

<!-- PR gates smoke test: this PR is closed without merging. -->
