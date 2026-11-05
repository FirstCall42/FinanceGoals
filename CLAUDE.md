# FinanceGoals

A personal finance app for a household: paying down loans and mortgages, savings goals, and multi-currency net worth (NOK main currency, also USD, RON and EUR). ASP.NET Core API on .NET 10, Angular web app, SQL Server (Azure SQL in the cloud, Docker locally).

The backlog lives in GitHub issues, grouped by phase milestones. `tools/github/backlog.txt` is the source the issues were seeded from.

## Commands

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test
```

## Layout

- `src/FinanceGoals.Domain`: entities and value objects. References nothing.
- `src/FinanceGoals.Application`: use cases. References Domain only.
- `src/FinanceGoals.Infrastructure`: EF Core and external services. References Application.
- `src/FinanceGoals.Api`: Minimal API. References Application and Infrastructure.
- `src/FinanceGoals.Migrator`: DbUp console app that applies the database scripts. Stands alone.
- `tests/`: one test project per layer.

## C# conventions

- File-scoped namespaces.
- Private fields, including static, readonly and const, are `_camelCase`.
- No primary constructors on classes or structs. Write explicit constructors that assign `_fields`.
- Nullable is on and warnings are errors. Fix warnings; don't suppress them without a comment saying why.
- NuGet versions go in `Directory.Packages.props`, never in a `.csproj`. Settings shared by all projects go in `Directory.Build.props`, and shared test settings in `tests/Directory.Build.props`.

`.editorconfig` enforces the namespace and naming rules in the build. Primary constructors have no built-in rule, so check for them yourself.

## Tests

- MSTest on Microsoft.Testing.Platform. Use NSubstitute for mocks. Don't use xUnit, NUnit or Moq.
- Test method names use underscores, e.g. `Add_rejects_different_currencies`.
- Integration tests run against SQL Server in Testcontainers, migrated by the real DbUp scripts. Never use an Azure database in tests.

## Database

- Schema changes are T-SQL scripts applied by DbUp, never EF Core migrations.
- Every script is guarded and idempotent, so a second run changes nothing.
- Never edit a migration script once it is merged. Add a new script instead.

## Money

- Amounts always carry their ISO 4217 currency. Never add amounts in different currencies without an explicit conversion.
- If an exchange rate is missing, show a warning. Never fall back to a rate of 1.0.

## Cloud

Test and production run on Azure free tiers, and the monthly cost must stay at $0. Flag anything that could incur a charge.

## Workflow

- One issue per branch: `<type>/<issue number>-<short-name>`, e.g. `chore/5-solution-structure`. Branch from an up-to-date `main`.
- PR titles follow Conventional Commits with a scope, e.g. `feat(debt): annuity schedule`.
- Each PR description contains `Closes #<n>` and fills in the PR template. Each PR gets exactly one type label: `feature`, `bug` or `chore`. The `PR rules` workflow checks all three.
- PRs are squash-merged and their branches are deleted, so check whether a PR is already merged before pushing more commits to its branch.
- If review feedback states a general rule, add it to this file.

## Review and merge

- Before opening a PR, run the `pr-reviewer` agent (`.claude/agents/pr-reviewer.md`) on the branch and fix its blocking findings.
- After opening a PR, turn on auto-merge: `gh pr merge <n> --auto --squash`. GitHub merges once every required check passes. Never merge by hand or bypass a check.
- The `PR gates` workflow posts two required statuses through a dedicated GitHub App. The ruleset accepts them only from that app. **AI review** is the same reviewer, run from main's copy of the workflow. **Human approval** fails when a PR touches the review system itself (`.github/`, `tools/github/`, and at any depth `.claude/`, `CLAUDE.md` or `.gitattributes`) until the owner adds the `human-approved` label. Database scripts, infrastructure and build settings rely on AI review alone, so hold them to the highest standard and call out data-loss or cost risk in the PR description.
- Never add a workflow or step that posts commit statuses or touches the `pr-gates` environment.
- Never add the `human-approved` label yourself, even though your `gh` login could. That label is the owner's decision.
- When AI review requests changes, fix the findings, push, and resolve the review threads (the ruleset requires resolved threads). If you disagree with a finding, reply on the thread with the concrete reason and leave it for the owner.
