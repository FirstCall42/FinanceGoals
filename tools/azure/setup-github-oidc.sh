#!/usr/bin/env bash
# Lets GitHub Actions sign in to Azure with OpenID Connect: no password, secret or certificate
# is stored in GitHub. Idempotent: safe to run again; it creates what is missing and leaves the rest.
#
# For each environment (test, production) it sets up:
#   - a user-assigned managed identity, id-financegoals-deploy-<env>, in rg-financegoals-shared;
#   - a federated credential that trusts only GitHub jobs running in that repository's
#     environment (subject repo:<owner>/<repo>:environment:<env>);
#   - Contributor on that environment's resource group and nothing else (least privilege:
#     test cannot touch production, and neither can change access or the subscription);
#   - the GitHub environment, deployable from main only, with the IDs as plain variables.
#
# Usage: tools/azure/setup-github-oidc.sh <owner>/<repo> [--location <azure-region>] [--dry-run]
# Requires: az, signed in (az login) as someone who can create resource groups and assign roles
# on the subscription (Owner, or Contributor plus User Access Administrator); gh, signed in.
# Cost: resource groups, managed identities, federated credentials and role assignments are free.
set -euo pipefail

REPO=""
LOCATION="norwayeast"
DRY_RUN=""
while [ $# -gt 0 ]; do
  case "$1" in
    --location) LOCATION="${2:?--location needs a region}"; shift 2 ;;
    --dry-run) DRY_RUN=yes; shift ;;
    -*) echo "unknown option: $1" >&2; exit 2 ;;
    *) REPO="$1"; shift ;;
  esac
done
[ -n "$REPO" ] || { echo "usage: setup-github-oidc.sh <owner>/<repo> [--location <region>] [--dry-run]" >&2; exit 2; }

APP="financegoals"
SHARED_RG="rg-$APP-shared"
ENVIRONMENTS=(test production)
declare -A RESOURCE_GROUP=([test]="rg-$APP-test" [production]="rg-$APP-prod")
ISSUER="https://token.actions.githubusercontent.com"
AUDIENCE="api://AzureADTokenExchange"
TAGS=("app=$APP" "managed-by=tools/azure/setup-github-oidc.sh")

# Changes go through run(); with --dry-run they are printed (to stderr, so redirects of the
# real output do not hide them) instead. Reads always run, except that a dry run without an
# az sign-in uses placeholders.
run() {
  if [ -n "$DRY_RUN" ]; then
    echo "  + $*" >&2
  else
    "$@"
  fi
}

# "created", or "would create" in a dry run.
created() {
  if [ -n "$DRY_RUN" ]; then echo "would create"; else echo "created"; fi
}

if az account show >/dev/null 2>&1; then
  SUBSCRIPTION_ID=$(az account show --query id -o tsv)
  TENANT_ID=$(az account show --query tenantId -o tsv)
  echo "Subscription: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID)"
elif [ -n "$DRY_RUN" ]; then
  SUBSCRIPTION_ID="<subscription-id>"
  TENANT_ID="<tenant-id>"
  echo "Not signed in to Azure; dry run with placeholders."
else
  echo "Sign in first: az login (then az account set --subscription <id> if you have several)." >&2
  exit 1
fi

echo "== Resource groups ($LOCATION)"
for rg in "$SHARED_RG" "${RESOURCE_GROUP[test]}" "${RESOURCE_GROUP[production]}"; do
  if [ -z "$DRY_RUN" ] && [ "$(az group exists --name "$rg")" = "true" ]; then
    echo "exists:  $rg"
  else
    run az group create --name "$rg" --location "$LOCATION" --tags "${TAGS[@]}" --output none
    echo "$(created): $rg"
  fi
done

for env in "${ENVIRONMENTS[@]}"; do
  identity="id-$APP-deploy-$env"
  scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/${RESOURCE_GROUP[$env]}"
  echo "== $env: $identity"

  run az identity create --resource-group "$SHARED_RG" --name "$identity" --location "$LOCATION" \
    --tags "${TAGS[@]}" --output none
  if [ -n "$DRY_RUN" ] && ! az identity show -g "$SHARED_RG" -n "$identity" >/dev/null 2>&1; then
    client_id="<client-id-of-$identity>"
    principal_id="<principal-id-of-$identity>"
  else
    client_id=$(az identity show --resource-group "$SHARED_RG" --name "$identity" --query clientId -o tsv)
    principal_id=$(az identity show --resource-group "$SHARED_RG" --name "$identity" --query principalId -o tsv)
  fi

  # Trusts only jobs that run in this GitHub environment, which only main may use (below).
  run az identity federated-credential create --resource-group "$SHARED_RG" --identity-name "$identity" \
    --name "github-$env" --issuer "$ISSUER" --subject "repo:$REPO:environment:$env" \
    --audiences "$AUDIENCE" --output none
  echo "federated credential: repo:$REPO:environment:$env"

  # --assignee-principal-type avoids a directory lookup that fails while a new identity replicates.
  run az role assignment create --assignee-object-id "$principal_id" --assignee-principal-type ServicePrincipal \
    --role Contributor --scope "$scope" --output none
  echo "role: Contributor on ${RESOURCE_GROUP[$env]}"

  # Least privilege: report anything else this identity holds, so drift is noticed.
  if [ -z "$DRY_RUN" ]; then
    extra=$(az role assignment list --assignee "$principal_id" --all \
      --query "[?!(roleDefinitionName=='Contributor' && scope=='$scope')].[roleDefinitionName, scope]" -o tsv)
    if [ -n "$extra" ]; then
      echo "WARNING: $identity has other role assignments; remove them unless a later issue added them on purpose:"
      echo "$extra" | sed 's/^/           /'
    fi
  fi

  # The GitHub environment. Created only if missing, so protection rules added later (#21,
  # production approval) are never overwritten. Only main may deploy to it: otherwise any
  # branch could run a job in this environment and get its Azure token.
  if gh api "repos/$REPO/environments/$env" >/dev/null 2>&1; then
    echo "github environment exists: $env"
    if [ "$(gh api "repos/$REPO/environments/$env" --jq '.deployment_branch_policy.custom_branch_policies // false')" != "true" ]; then
      echo "WARNING: environment $env is not limited to selected branches; set it to main only." >&2
    fi
  else
    run gh api -X PUT "repos/$REPO/environments/$env" --input - >/dev/null <<'JSON'
{ "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true } }
JSON
    echo "$(created) github environment: $env"
  fi
  if [ -n "$DRY_RUN" ] || ! gh api "repos/$REPO/environments/$env/deployment-branch-policies" \
       --jq '.branch_policies[].name' 2>/dev/null | grep -qx main; then
    run gh api "repos/$REPO/environments/$env/deployment-branch-policies" -f name=main -f type=branch >/dev/null
  fi
  echo "deployable from: main"

  # Identifiers, not secrets: a token can only be issued to a job in this environment.
  run gh variable set AZURE_CLIENT_ID --repo "$REPO" --env "$env" --body "$client_id"
  run gh variable set AZURE_TENANT_ID --repo "$REPO" --env "$env" --body "$TENANT_ID"
  run gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --env "$env" --body "$SUBSCRIPTION_ID"
  run gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --env "$env" --body "${RESOURCE_GROUP[$env]}"
  echo "variables: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, AZURE_RESOURCE_GROUP"
done

echo
echo "Done. Check it from main: Actions > Azure sign-in check > Run workflow."
