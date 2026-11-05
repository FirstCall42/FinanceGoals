#!/usr/bin/env bash
# Configures the GitHub repository: settings, labels, milestones, ruleset and project board.
# Idempotent: safe to run any number of times; it creates what is missing and updates the rest.
#
# Usage: tools/github/setup-repo.sh <owner>/<repo>
# Requires: gh (signed in with the 'repo' and 'project' scopes), jq is not needed (gh --jq is used).
set -euo pipefail

REPO="${1:?usage: setup-repo.sh <owner>/<repo>}"
OWNER="${REPO%/*}"
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "== Repository settings"
gh repo edit "$REPO" \
  --enable-squash-merge \
  --enable-merge-commit=false \
  --enable-rebase-merge=false \
  --delete-branch-on-merge \
  --enable-auto-merge \
  --enable-issues \
  --enable-wiki=false >/dev/null
gh api -X PATCH "repos/$REPO" \
  -f squash_merge_commit_title=PR_TITLE \
  -f squash_merge_commit_message=PR_BODY >/dev/null
echo "squash merge only, PR title as commit title, branches deleted after merge, auto-merge allowed"

echo "== Labels"
label() {
  gh label create "$1" --repo "$REPO" --color "$2" --description "$3" --force >/dev/null
  echo "label: $1"
}
label "feature"          "0e8a16" "New capability or improvement"
label "bug"              "d73a4a" "Something does not work as it should"
label "chore"            "c5def5" "Tooling, dependencies, refactoring, CI, infrastructure"
label "dependencies"     "0366d6" "Dependency updates (Dependabot)"
label "area: accounts"   "fbca04" "Accounts, balances and net worth"
label "area: currency"   "fbca04" "Currencies, exchange rates and conversion"
label "area: debt"       "fbca04" "Loans, mortgages and payoff"
label "area: goals"      "fbca04" "Savings goals"
label "area: households" "fbca04" "Household profiles, members and sign-in"
label "area: infra"      "fbca04" "Build, CI/CD, hosting, database deploys"
label "human-approved"   "5319e7" "A human reviewed changes to sensitive paths; new pushes remove it"

# GitHub's default labels that overlap with the ones above.
for unused in "enhancement" "documentation" "good first issue" "help wanted" "question"; do
  if gh label delete "$unused" --repo "$REPO" --yes >/dev/null 2>&1; then
    echo "removed default label: $unused"
  fi
done

echo "== Milestones"
milestone() {
  local title="$1" desc="$2" count
  count=$(gh api "repos/$REPO/milestones?state=all&per_page=100" \
    --jq "[.[] | select(.title == \"$title\")] | length")
  if [ "$count" = "0" ]; then
    gh api "repos/$REPO/milestones" -f title="$title" -f description="$desc" >/dev/null
    echo "created: $title"
  else
    echo "exists:  $title"
  fi
}
milestone "Phase 0a: Local and CI"                   "Runs locally with one command; every test level runs in GitHub Actions"
milestone "Phase 0b: Cloud"                          "Azure test and production on free tiers; a one-line change reaches production"
milestone "Phase 1: Households, money and currencies" "Google sign-in, household profiles, Money, exchange rates, conversion"
milestone "Phase 2: Accounts and net worth"          "Accounts by type and currency, value snapshots, net worth history"
milestone "Phase 3: Debt payoff"                     "Loan types, schedules, rate changes, what-if, avalanche vs snowball"
milestone "Phase 4: Savings goals"                   "Targets, progress, required monthly amount, projections"
milestone "Phase 5: Insights"                        "Dashboard, currency effects vs real growth, goal timeline"

echo "== Environment for the PR gates"
# Only workflows running from main may use it, so its secrets never reach PR-controlled code.
gh api -X PUT "repos/$REPO/environments/pr-gates" --input - >/dev/null <<'JSON'
{ "deployment_branch_policy": { "protected_branches": false, "custom_branch_policies": true } }
JSON
if ! gh api "repos/$REPO/environments/pr-gates/deployment-branch-policies" \
       --jq '.branch_policies[].name' | grep -qx main; then
  gh api "repos/$REPO/environments/pr-gates/deployment-branch-policies" -f name=main -f type=branch >/dev/null
fi
echo "environment pr-gates: deployable from main only"

# The "AI review" and "Human approval" statuses are required only once everything that posts
# them exists. Requiring them earlier would block every PR, including the one that fixes it.
app_id=$(gh variable get GATES_APP_ID --repo "$REPO" 2>/dev/null || true)
env_secrets=$(gh secret list --repo "$REPO" --env pr-gates --json name --jq '.[].name' 2>/dev/null || true)
gates_on_main=""
if gh api "repos/$REPO/contents/.github/workflows/pr-gates.yml" >/dev/null 2>&1; then gates_on_main=yes; fi
missing=()
[ -n "$app_id" ] || missing+=("repo variable GATES_APP_ID")
grep -qx GATES_APP_PRIVATE_KEY <<<"$env_secrets" || missing+=("pr-gates secret GATES_APP_PRIVATE_KEY")
grep -qx CLAUDE_CODE_OAUTH_TOKEN <<<"$env_secrets" || missing+=("pr-gates secret CLAUDE_CODE_OAUTH_TOKEN")
[ -n "$gates_on_main" ] || missing+=("pr-gates.yml merged to main")

echo "== Ruleset for main"
ruleset=$(mktemp)
if [ ${#missing[@]} -eq 0 ]; then
  sed "s/\"__GATES_APP_ID__\"/$app_id/g" "$HERE/ruleset-main.json" > "$ruleset"
  echo "PR gates required, accepted only from app $app_id"
else
  sed '/__GATES_APP_ID__/d; s/{ "context": "Type label" },/{ "context": "Type label" }/' \
    "$HERE/ruleset-main.json" > "$ruleset"
  echo "WARNING: PR gates NOT required yet. Missing:"
  printf '           - %s\n' "${missing[@]}"
fi
existing=""
if listed=$(gh api "repos/$REPO/rulesets" --jq '.[] | select(.name == "main") | .id' 2>/dev/null); then
  existing="$listed"
fi
if [ -n "$existing" ]; then
  method=PUT; path="repos/$REPO/rulesets/$existing"
else
  method=POST; path="repos/$REPO/rulesets"
fi
if gh api -X "$method" "$path" --input "$ruleset" >/dev/null 2>"$HERE/.ruleset-error.log"; then
  echo "ruleset 'main' applied ($method)"
  rm -f "$HERE/.ruleset-error.log"
else
  echo "WARNING: ruleset not applied. On GitHub Free, rulesets are not available for private repositories."
  echo "         See $HERE/.ruleset-error.log. The PR checks still run; they are just not enforced."
fi

echo "== Project board"
number=$(gh project list --owner "$OWNER" --format json \
  --jq '.projects[] | select(.title == "FinanceGoals") | .number' | head -n1)
if [ -z "$number" ]; then
  number=$(gh project create --owner "$OWNER" --title "FinanceGoals" --format json --jq '.number')
  echo "created project #$number"
else
  echo "exists:  project #$number"
fi
gh project link "$number" --owner "$OWNER" --repo "$REPO" >/dev/null 2>&1 || true
echo "project #$number linked to $REPO"

echo
echo "Done. Manual steps that GitHub's API does not cover (one time, in the browser):"
echo "  1. Project > Settings > Status field: Backlog, Ready, In progress, In review, Done"
echo "  2. Project > Workflows: enable 'Auto-add to project' (this repo), 'Item closed' -> Done,"
echo "     'Pull request merged' -> Done, 'Pull request linked to issue' -> In review"
echo "  3. Project > New view > Roadmap, grouped by Milestone"
echo
echo "PR gates (one time, in this order; then run this script again):"
echo "  1. Create a GitHub App: Settings > Developer settings > GitHub Apps > New. No webhook."
echo "     Repository permissions: Commit statuses: Read and write. Install it on this repo only."
echo "     gh variable set GATES_APP_ID --repo $REPO --body <App ID>"
echo "     gh secret set GATES_APP_PRIVATE_KEY --repo $REPO --env pr-gates < <downloaded .pem>"
echo "  2. Install the Claude GitHub App on this repo: https://github.com/apps/claude"
echo "  3. claude setup-token, then: gh secret set CLAUDE_CODE_OAUTH_TOKEN --repo $REPO --env pr-gates"
echo "  4. Merge the PR that adds .github/workflows/pr-gates.yml."
