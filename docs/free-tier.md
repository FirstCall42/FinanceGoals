# Free tiers and cost guardrails

Test and production must cost $0 a month. This page lists the free allowances FinanceGoals relies on, what happens at each limit, and how to check usage. Limits change, so check the linked Microsoft pages before relying on a number. They were last checked on 2026-11-05.

## Guardrails

1. **Budget alert.** `tools/azure/setup-budget.sh --email <address>` sets a monthly budget of 1 in the billing currency on the whole subscription. It emails the subscription's Owners and the given address when:
   - actual cost goes above 1% of the budget, which means any real charge;
   - actual cost goes above 100%;
   - the forecast goes above 100%.

   Cost data arrives about once a day, and a new subscription can take up to 48 hours before budgets evaluate. A budget only alerts; it never stops anything.
2. **Free offers set to stop, not bill.** Wherever a service lets us choose, it stops at the free limit instead of billing overage. The Azure SQL free offer is set to auto-pause until the next month.
3. **Scale to zero.** Container Apps run with zero minimum replicas, so an idle app uses nothing.
4. **Uptime pings use liveness only.** An external uptime monitor may call `GET /health/live` only, never `/health/ready`, which opens a database connection and keeps the free database awake. It must also ping rarely. Every request wakes a scaled-to-zero Container App for its cooldown, about 5 minutes. A ping every 5 minutes therefore keeps the app running all month, about 2.6 million seconds, while the free grant at 0.25 vCPU covers about 720,000 seconds (200 hours). Ping at most once an hour, or not at all and rely on the post-deploy smoke test.
5. **Subscription type.** The subscription is currently a Free Trial with a spending limit, so nothing can be charged; Azure stops resources instead. The trial ends after 30 days or when its credit runs out. Upgrading to Pay-As-You-Go keeps the always-free offers below but removes the spending limit. From then on, the budget alert and the settings above are what keep the cost at $0.

## Free allowances

| Service | Free allowance | At the limit | Check usage |
| --- | --- | --- | --- |
| [Azure SQL Database free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer) | Per database, per month, for the life of the subscription: 100,000 vCore-seconds of serverless compute, 32 GB data, 32 GB backup. Up to 10 such databases per subscription | We choose auto-pause: no charge, but the database is offline until the next month. The other choice, continue and bill, can't be undone | Portal: database **Overview**, "Free amount remaining" |
| [Container Apps](https://azure.microsoft.com/pricing/details/container-apps/) (Consumption) | Per subscription, per month: 180,000 vCPU-seconds, 360,000 GiB-seconds, 2 million requests | Billed per second beyond the grant. Nothing is used while scaled to zero | Portal: Container App **Metrics** (Replica count, Requests); Cost Management |
| [Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/quotas) (Free plan) | 100 GB bandwidth a month, 3 preview environments, 2 custom domains, 250 MB per environment | No overage on the Free plan: traffic beyond 100 GB is refused | Portal: Static Web App **Metrics** (Data out) |
| [Log Analytics](https://azure.microsoft.com/pricing/details/monitor/) (Analytics Logs) | 5 GB ingestion a month free (per billing account, by Microsoft's answers; verify on the pricing page); 31 days retention included | Billed per GB beyond 5 GB | Workspace **Usage and estimated costs**; keep request logging lean (health probes are not logged) |
| Resource groups, managed identities, federated credentials, role assignments, budgets | Free | n/a | n/a |
| GitHub Container Registry (public repository) | Free | n/a | n/a |

## Not free: avoid or decide explicitly

- **[Key Vault](https://azure.microsoft.com/pricing/details/key-vault/)** has no free tier. Secrets operations cost about $0.03 per 10,000, which is cents, not $0. Prefer managed identities, so there are no secrets to store. The API can sign in to Azure SQL with its managed identity, and the migrator already supports Entra users. If #18 still needs a vault, flag it as a cost.
- **Anything with a fixed monthly price** (a provisioned SQL tier, a dedicated Container Apps workload profile, Static Web Apps Standard, a custom domain certificate bought through Azure) is out unless the owner decides otherwise.

## How to check usage

```sh
# This month's spend against the budget (lags by about a day)
az rest --method get \
  --url "https://management.azure.com/subscriptions/$(az account show --query id -o tsv)/providers/Microsoft.Consumption/budgets/budget-financegoals?api-version=2023-11-01" \
  --query "{amount: properties.amount, currentSpend: properties.currentSpend, forecast: properties.forecastSpend}"
```

In the portal, go to **Cost Management > Cost analysis**, scope it to the subscription, and group by resource. Before running the `az` command in Git Bash on Windows, set `MSYS_NO_PATHCONV=1`.
