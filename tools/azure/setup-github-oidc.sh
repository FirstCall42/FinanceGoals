#!/usr/bin/env bash
# Lets GitHub Actions sign in to Azure with OpenID Connect: no password, secret or certificate
# is stored in GitHub. Idempotent: safe to run again; it creates what is missing and leaves the rest.
#
# For each environment (test, production), in this order:
#   1. the GitHub environment, deployable from main only. It is locked down before Azure trusts
#      it: GitHub creates a missing environment unrestricted the first time any workflow names it,
#      so Azure must never trust an environment that does not exist yet or allows other branches;
#   2. a user-assigned managed identity, id-financegoals-deploy-<env>, in rg-financegoals-shared;
#   3. a federated credential that trusts only jobs in that GitHub environment. The subject uses
#      the repository's own OIDC subject format, read from GitHub, never assumed (this repository
#      uses GitHub's immutable form, repo:<owner>@<id>/<repo>@<id>:environment:<env>);
#   4. Contributor on that environment's resource group and nothing else (least privilege: test
#      cannot touch production, and neither can change access or see the subscription);
#   5. the IDs as plain environment variables (identifiers, not secrets).
#
# Usage: tools/azure/setup-github-oidc.sh <owner>/<repo> [--location <azure-region>] [--dry-run]
# Requires: gh, signed in as a repository admin; az, signed in (az login) as someone who can create
# resource groups and assign roles on the subscription (Owner, or Contributor plus User Access
# Administrator). A dry run changes nothing anywhere and works without an Azure sign-in.
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

fail() { echo "ERROR: $*" >&2; exit 1; }

# Changes go through run(); with --dry-run they are printed (to stderr, so redirects of the real
# output do not hide them) instead. Reads always run, so a dry run reports what would change.
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

# GET a GitHub API path. Prints the body and returns 0, or returns 4 on an explicit 404. Any other
# failure (auth, rate limit, network, 5xx) stops the script, so an error is never taken to mean
# "missing" and never leads to a create that could overwrite existing settings.
gh_get() {
  local out
  if out=$(gh api "$@" 2>&1); then
    printf '%s' "$out"
    return 0
  fi
  if grep -q "HTTP 404" <<<"$out"; then
    return 4
  fi
  fail "gh api $*: $out"
}

echo "== Preflight"
gh auth status >/dev/null 2>&1 || fail "gh is not signed in: gh auth login"
[ "$(gh_get "repos/$REPO" --jq '.permissions.admin')" = "true" ] \
  || fail "your gh login must be an admin of $REPO to manage its environments"
echo "gh: admin of $REPO"

# A job's token carries the subject <prefix>:environment:<env>. Custom claim templates are not
# supported here; refuse rather than register a subject that would never match.
[ "$(gh_get "repos/$REPO/actions/oidc/customization/sub" --jq '.use_default')" = "true" ] \
  || fail "$REPO uses a custom OIDC subject template; update this script for it first"
SUBJECT_PREFIX=$(gh_get "repos/$REPO/actions/oidc/customization/sub" --jq ".sub_claim_prefix // \"repo:$REPO\"")
echo "oidc subject prefix: $SUBJECT_PREFIX"

AZ_SIGNED_IN=""
if az account show >/dev/null 2>&1; then
  AZ_SIGNED_IN=yes
  SUBSCRIPTION_ID=$(az account show --query id -o tsv)
  TENANT_ID=$(az account show --query tenantId -o tsv)
  echo "azure: $(az account show --query name -o tsv) ($SUBSCRIPTION_ID)"
elif [ -n "$DRY_RUN" ]; then
  SUBSCRIPTION_ID="<subscription-id>"
  TENANT_ID="<tenant-id>"
  echo "azure: not signed in; dry run with placeholders"
else
  fail "sign in to Azure first: az login (then az account set --subscription <id> if you have several)"
fi

# Creates the environment if missing, or checks an existing one, so that only main can deploy to
# it. An existing environment is never rewritten (protection rules such as #21's production
# approval stay as they are); if it allows anything other than exactly main, the script stops.
lock_environment() {
  local env="$1" status=0 policy branches
  gh_get "repos/$REPO/environments/$env" >/dev/null || status=$?
  if [ "$status" -eq 4 ]; then
    run gh api -X PUT "repos/$REPO/environments/$env" --input - >/dev/null <<'JSON'
{ "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true } }
JSON
    echo "$(created) github environment: $env"
    if [ -n "$DRY_RUN" ]; then
      run gh api "repos/$REPO/environments/$env/deployment-branch-policies" -f name=main -f type=branch
      echo "deployable from: main"
      return
    fi
  else
    echo "github environment exists: $env"
  fi

  policy=$(gh_get "repos/$REPO/environments/$env" \
    --jq '.deployment_branch_policy | if . == null then "none" else "protected=\(.protected_branches),custom=\(.custom_branch_policies)" end')
  [ "$policy" = "protected=false,custom=true" ] \
    || fail "environment $env must be limited to selected branches (it is: $policy). In Settings > Environments, set it to 'Selected branches and tags' with only main, then run again."

  branches=$(gh_get "repos/$REPO/environments/$env/deployment-branch-policies" \
    --jq '[.branch_policies[] | "\(.type // "branch"):\(.name)"] | sort | join(",")')
  if [ -z "$branches" ]; then
    run gh api "repos/$REPO/environments/$env/deployment-branch-policies" -f name=main -f type=branch >/dev/null
  elif [ "$branches" != "branch:main" ]; then
    fail "environment $env may deploy from '$branches'; it must be exactly main. Remove the others in Settings > Environments, then run again."
  fi
  echo "deployable from: main"
}

echo "== GitHub environments (locked to main before Azure trusts them)"
for env in "${ENVIRONMENTS[@]}"; do
  lock_environment "$env"
done

echo "== Resource providers"
# A new subscription has managed identities switched off until the provider is registered (free).
for namespace in Microsoft.ManagedIdentity; do
  state="NotRegistered"
  if [ -n "$AZ_SIGNED_IN" ]; then
    state=$(az provider show --namespace "$namespace" --query registrationState -o tsv)
  fi
  if [ "$state" = "Registered" ]; then
    echo "registered: $namespace"
  else
    run az provider register --namespace "$namespace" --wait --output none
    echo "$( [ -n "$DRY_RUN" ] && echo "would register" || echo "registered"): $namespace"
  fi
done

echo "== Resource groups ($LOCATION)"
for rg in "$SHARED_RG" "${RESOURCE_GROUP[test]}" "${RESOURCE_GROUP[production]}"; do
  if [ -n "$AZ_SIGNED_IN" ] && [ "$(az group exists --name "$rg")" = "true" ]; then
    echo "exists:  $rg"
  else
    run az group create --name "$rg" --location "$LOCATION" --tags "${TAGS[@]}" --output none
    echo "$(created): $rg"
  fi
done

for env in "${ENVIRONMENTS[@]}"; do
  identity="id-$APP-deploy-$env"
  scope="/subscriptions/$SUBSCRIPTION_ID/resourceGroups/${RESOURCE_GROUP[$env]}"
  subject="$SUBJECT_PREFIX:environment:$env"
  echo "== $env: $identity"

  # Re-running create is a PUT: the identity keeps its client and principal IDs.
  run az identity create --resource-group "$SHARED_RG" --name "$identity" --location "$LOCATION" \
    --tags "${TAGS[@]}" --output none
  if [ -n "$AZ_SIGNED_IN" ] && az identity show -g "$SHARED_RG" -n "$identity" >/dev/null 2>&1; then
    client_id=$(az identity show --resource-group "$SHARED_RG" --name "$identity" --query clientId -o tsv)
    principal_id=$(az identity show --resource-group "$SHARED_RG" --name "$identity" --query principalId -o tsv)
  else
    client_id="<client-id-of-$identity>"
    principal_id="<principal-id-of-$identity>"
  fi

  run az identity federated-credential create --resource-group "$SHARED_RG" --identity-name "$identity" \
    --name "github-$env" --issuer "$ISSUER" --subject "$subject" --audiences "$AUDIENCE" --output none
  echo "federated credential: $subject"

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
      while IFS= read -r line; do echo "           $line"; done <<<"$extra"
    fi
  fi

  run gh variable set AZURE_CLIENT_ID --repo "$REPO" --env "$env" --body "$client_id"
  run gh variable set AZURE_TENANT_ID --repo "$REPO" --env "$env" --body "$TENANT_ID"
  run gh variable set AZURE_SUBSCRIPTION_ID --repo "$REPO" --env "$env" --body "$SUBSCRIPTION_ID"
  run gh variable set AZURE_RESOURCE_GROUP --repo "$REPO" --env "$env" --body "${RESOURCE_GROUP[$env]}"
  echo "variables: AZURE_CLIENT_ID, AZURE_TENANT_ID, AZURE_SUBSCRIPTION_ID, AZURE_RESOURCE_GROUP"
done

echo
echo "Done. Check it from main: Actions > Azure sign-in check > Run workflow."
