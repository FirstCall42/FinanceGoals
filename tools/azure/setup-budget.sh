#!/usr/bin/env bash
# A monthly budget of 1 (in the subscription's billing currency) on the whole subscription, so any
# charge is noticed. Idempotent: safe to run again; it updates the budget in place and keeps its
# start date.
#
# Alerts, by email to the subscription's Owners and to each --email:
#   - actual cost above 1% of the budget: any real charge at all (0.01 in billing currency);
#   - actual cost above 100%;
#   - forecast cost above 100%.
# Cost data reaches Cost Management about once a day, and a new subscription can take up to 48
# hours before budgets evaluate, so "immediately" means within roughly a day. A budget only
# alerts; it never stops resources. The free-offer settings (docs/free-tier.md) are what cap cost.
#
# Usage: tools/azure/setup-budget.sh --email <address> [--email <address> ...] [--dry-run]
# Addresses are passed at run time and never committed. Requires: az, signed in as an Owner
# (or Cost Management Contributor) of the subscription. Cost: budgets are free.
set -euo pipefail

# Git Bash on Windows rewrites arguments that start with / into Windows paths; turn that off.
export MSYS_NO_PATHCONV=1

EMAILS=()
DRY_RUN=""
while [ $# -gt 0 ]; do
  case "$1" in
    --email) EMAILS+=("${2:?--email needs an address}"); shift 2 ;;
    --dry-run) DRY_RUN=yes; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done
[ ${#EMAILS[@]} -gt 0 ] || { echo "usage: setup-budget.sh --email <address> [--email <address> ...] [--dry-run]" >&2; exit 2; }
for address in "${EMAILS[@]}"; do
  [[ "$address" =~ ^[^@[:space:]\"]+@[^@[:space:]\"]+$ ]] || { echo "not an email address: $address" >&2; exit 2; }
done
CONTACT_EMAILS=$(printf '"%s",' "${EMAILS[@]}"); CONTACT_EMAILS="[${CONTACT_EMAILS%,}]"

NAME="budget-financegoals"
API="api-version=2023-11-01"

fail() { echo "ERROR: $*" >&2; exit 1; }

az account show >/dev/null 2>&1 || fail "sign in to Azure first: az login"
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
URL="https://management.azure.com/subscriptions/$SUBSCRIPTION_ID/providers/Microsoft.Consumption/budgets/$NAME?$API"
echo "subscription: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID)"

# Keep an existing budget's start date; a new one starts this month (it must be a month's first day).
start=$(az rest --method get --url "$URL" --query "properties.timePeriod.startDate" -o tsv 2>/dev/null || true)
if [ -n "$start" ]; then
  echo "budget exists: $NAME (since $start); updating"
else
  start="$(date -u +%Y-%m-01)T00:00:00Z"
  echo "budget missing: $NAME; creating from $start"
fi

notification() { # threshold, type
  cat <<JSON
{ "enabled": true, "operator": "GreaterThan", "threshold": $1, "thresholdType": "$2",
  "contactEmails": $CONTACT_EMAILS, "contactRoles": ["Owner"], "locale": "en-us" }
JSON
}

body=$(cat <<JSON
{
  "properties": {
    "category": "Cost",
    "amount": 1,
    "timeGrain": "Monthly",
    "timePeriod": { "startDate": "$start" },
    "notifications": {
      "actual-any-charge": $(notification 1 Actual),
      "actual-over-budget": $(notification 100 Actual),
      "forecast-over-budget": $(notification 100 Forecasted)
    }
  }
}
JSON
)

if [ -n "$DRY_RUN" ]; then
  echo "  + az rest --method put --url $URL --body <below>" >&2
  echo "$body" >&2
  exit 0
fi

az rest --method put --url "$URL" --body "$body" --output none
az rest --method get --url "$URL" \
  --query "{amount: properties.amount, currentSpend: properties.currentSpend, alerts: keys(properties.notifications)}" -o json
echo "Done. Spend so far this month is shown above (it lags by about a day)."
