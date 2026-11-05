# FinanceGoals

A personal finance app for a household: paying down loans and mortgages, savings goals, and multi-currency net worth (NOK main currency, also USD, RON and EUR). ASP.NET Core API on .NET 10, Angular web app, SQL Server (Azure SQL in the cloud, Docker locally).

The backlog lives in GitHub issues, grouped by phase milestones. `tools/github/backlog.txt` is the source the issues were seeded from.

## Commands

```sh
dotnet build
dotnet format --verify-no-changes
dotnet test

cd web
npm ci
npm run lint
npm run format:check   # npm run format to fix
npm test
npm run build
```

## Layout

- `src/FinanceGoals.Domain`: entities and value objects. References nothing.
- `src/FinanceGoals.Application`: use cases. References Domain only.
- `src/FinanceGoals.Infrastructure`: EF Core and external services. References Application.
- `src/FinanceGoals.Api`: Minimal API. References Application and Infrastructure.
- `src/FinanceGoals.Migrator`: DbUp console app that applies the database scripts. Stands alone.
- `tests/`: one test project per layer.
- `infra/`: Bicep for one environment (`main.bicep`, `modules/`) and one parameter file per environment. Deploy with `az deployment group create -g rg-financegoals-<env> -p infra/<env>.bicepparam`.
- `web/`: Angular app (standalone components, zoneless, signals). Vitest for unit tests, ESLint and Prettier.

## C# conventions

- File-scoped namespaces.
- Private fields, including static, readonly and const, are `_camelCase`.
- No primary constructors on classes or structs. Write explicit constructors that assign `_fields`.
- Nullable is on and warnings are errors. Fix warnings; don't suppress them without a comment saying why.
- NuGet versions go in `Directory.Packages.props`, never in a `.csproj`. Settings shared by all projects go in `Directory.Build.props`, and shared test settings in `tests/Directory.Build.props`.

`.editorconfig` enforces the namespace and naming rules in the build. Primary constructors have no built-in rule, so check for them yourself.

## Web conventions

- Standalone components with signals; no NgModules, no zone.js.
- Prettier formats everything in `web/`; `npm run format:check` runs in CI.
- The app calls the API with relative paths. `ng serve` proxies them to the local API (`web/proxy.conf.json`).
- PRs that change the UI need the owner to review screenshots or a preview. Tick the PR template item only after the owner says so.

## Tests

- MSTest on Microsoft.Testing.Platform. Use NSubstitute for mocks and Shouldly for assertions. Don't use xUnit, NUnit, Moq or FluentAssertions.
- `tests/FinanceGoals.Architecture.Tests` enforces the layer rules and the no-primary-constructors rule. CI (`.github/workflows/ci.yml`) runs build, format and tests on every PR as the required "Build and test" check, and the database scripts through the required "Database checks" (see Database).
- Test method names use underscores, e.g. `Add_rejects_different_currencies`.
- Integration tests run against SQL Server in Testcontainers, migrated by the real DbUp scripts. Never use an Azure database in tests.

## Database

- Schema changes are T-SQL scripts applied by DbUp, never EF Core migrations.
- Every script is guarded and idempotent, so a second run changes nothing.
- Name every constraint (primary key, unique, check, default, foreign key). SQL Server names unnamed ones randomly, so databases would differ and later scripts couldn't refer to them. A test enforces this.
- Seed data must be deterministic: no `NEWID()`, current dates or `rowversion` in seeded tables, or an upgraded database can never match a fresh build.
- Never edit, rename or delete a migration or seed script once it is merged. Add a new script instead. Repeatable scripts (`db/repeatable`) may change; they run every time.
- CI enforces this: the "Database checks" job fails on a changed merged script, and requires a database upgraded from main to match a fresh build. Tests must also re-run every script without the journal (see `Migrations_and_seeds_are_guarded_against_running_again`).

## Money

- Amounts always carry their ISO 4217 currency. Never add amounts in different currencies without an explicit conversion.
- If an exchange rate is missing, show a warning. Never fall back to a rate of 1.0.

## Cloud

Test and production run on Azure free tiers, and the monthly cost must stay at $0. Flag anything that could incur a charge.

- Images are built once per commit on main and tagged with the commit SHA (`ghcr.io/firstcall42/financegoals-<api|migrator|web>:<sha>`). Deploy and promote by SHA; never rebuild per environment, never use `latest`, and never bake environment settings into an image.
- Deploys happen only through the Release workflow (push to main, or a manual run on main). Never deploy by hand except to recover; if you must, say so in the PR or issue.
- Never bake environment settings into the web build: the deploy writes `config.json` (the API URL). The API allows cross-origin requests only from `Cors:AllowedOrigins`.
- Every Azure resource is defined in `infra/`; never configure anything in the portal. CI compiles and lints the templates (the "Infra" check).
- No Key Vault and no stored secrets: workloads sign in to Azure SQL and other services with their managed identities.
- The test environment (`swedencentral`) is the live one until production exists (#86). Don't deploy to production before then.
- Keep each environment in one region (traffic between regions is billed). The subscription allows one Container Apps environment per region, and on the Free Trial the free SQL offer only works in `swedencentral`.
- `docs/free-tier.md` lists the free allowances, the limit behaviour and what is not free. Update it when adding an Azure service. Where a service lets you choose, set it to stop at the free limit, not to bill overage.
- Uptime monitors call `GET /health/live` only, at most once an hour. Never point a monitor at `/health/ready`, which wakes the database.

- GitHub Actions signs in to Azure with OpenID Connect (`azure/login` with `vars.AZURE_CLIENT_ID`, `vars.AZURE_TENANT_ID` and `vars.AZURE_SUBSCRIPTION_ID`). Never store an Azure password, secret or certificate in GitHub.
- A job reaches Azure only through the `test` or `production` GitHub environment, and gets only `id-token: write` on top of what it already needs. Only workflows triggered by `push` to main or `workflow_dispatch` may use these environments. Never use them from `pull_request_target` or `workflow_run`: those runs count as main but handle code or input from a pull request. Each environment identity is Contributor on its own resource group and nothing more. Grant anything extra in `tools/azure/setup-github-oidc.sh`, with a reason, never in the portal.

## Working agreement

- The owner defines goals and features (issues with a "Done when" list) and sets priorities. Claude takes the next issue, builds and tests it, opens the PR, and lets auto-merge land it when the checks pass.
- Claude decides technical details itself and keeps solutions lean: no extra guards, exhaustive docs or long PR descriptions unless they earn their place. Each PR gets a short summary.
- Only stop to ask the owner about: product and UI decisions; anything that could cost money; deleting or changing live data or cloud resources; and the `human-approved` label for PRs that change the review system. Batch questions into one.

## Workflow

- One issue per branch: `<type>/<issue number>-<short-name>`, e.g. `chore/5-solution-structure`. Branch from an up-to-date `main`.
- PR titles follow Conventional Commits with a scope, e.g. `feat(debt): annuity schedule`.
- Each PR description contains `Closes #<n>` and fills in the PR template. Each PR gets exactly one type label: `feature`, `bug` or `chore`. The `PR rules` workflow checks all three.
- PRs are squash-merged and their branches are deleted, so check whether a PR is already merged before pushing more commits to its branch.
- If review feedback states a general rule, add it to this file.

## Review and merge

- Before opening a PR, run the `pr-reviewer` agent (`.claude/agents/pr-reviewer.md`) locally for database scripts, security, money logic, infrastructure or larger changes, and fix its blocking findings. Skip it for small or docs-only changes. The AI review check runs on every PR either way.
- After opening a PR, turn on auto-merge: `gh pr merge <n> --auto --squash`. GitHub merges once every required check passes. Never merge by hand or bypass a check.
- The `PR gates` workflow posts two required statuses through a dedicated GitHub App. The ruleset accepts them only from that app. **AI review** is the same reviewer, run from main's copy of the workflow. **Human approval** fails when a PR touches the review system itself (`.github/`, `tools/github/`, and at any depth `.claude/`, `CLAUDE.md` or `.gitattributes`) until the owner adds the `human-approved` label. Database scripts, infrastructure and build settings rely on AI review alone, so hold them to the highest standard and call out data-loss or cost risk in the PR description.
- Never add a workflow or step that posts commit statuses or touches the `pr-gates` environment.
- Never add the `human-approved` label yourself, even though your `gh` login could. That label is the owner's decision.
- When AI review requests changes, fix the findings, push, and resolve the review threads (the ruleset requires resolved threads). If you disagree with a finding, reply on the thread with the concrete reason and leave it for the owner.
