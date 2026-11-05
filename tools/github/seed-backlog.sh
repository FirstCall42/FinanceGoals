#!/usr/bin/env bash
# Creates the issues in backlog.txt, links sub-issues to their parents and adds everything
# to the project board. Idempotent: issues are matched by exact title, existing ones are left
# alone, and existing sub-issue links and board items are not duplicated.
#
# Usage: tools/github/seed-backlog.sh <owner>/<repo> [project number]
set -euo pipefail

REPO="${1:?usage: seed-backlog.sh <owner>/<repo> [project number]}"
PROJECT="${2:-1}"
OWNER="${REPO%/*}"
HERE="$(cd "$(dirname "$0")" && pwd)"

declare -A MILESTONE=(
  [0a]="Phase 0a: Local and CI"
  [0b]="Phase 0b: Cloud"
  [1]="Phase 1: Households, money and currencies"
  [2]="Phase 2: Accounts and net worth"
  [3]="Phase 3: Debt payoff"
  [4]="Phase 4: Savings goals"
  [5]="Phase 5: Insights"
)

# Existing issues by title -> number (issues only, any state).
declare -A BY_TITLE=()
while IFS=$'\t' read -r number title; do
  [ -n "$number" ] && BY_TITLE["$title"]="$number"
done < <(gh issue list --repo "$REPO" --state all --limit 1000 --json number,title \
           --jq '.[] | "\(.number)\t\(.title)"')

declare -A BY_KEY=()
created=0; existing=0; linked=0

while IFS= read -r line; do
  line="${line%$'\r'}"
  case "$line" in ''|'#'*) continue ;; esac
  IFS='|' read -r key parent type area ms title summary criteria <<< "$line"

  if [ -n "${BY_TITLE[$title]:-}" ]; then
    number="${BY_TITLE[$title]}"
    existing=$((existing + 1))
  else
    body="$summary"$'\n\n'"### Done when"$'\n'
    IFS=';' read -ra items <<< "$criteria"
    for item in "${items[@]}"; do body+=$'\n'"- [ ] $item"; done

    args=(--repo "$REPO" --title "$title" --body "$body" --label "$type" --label "area: $area")
    if [ "$ms" != "-" ]; then args+=(--milestone "${MILESTONE[$ms]}"); fi
    url=$(gh issue create "${args[@]}")
    number="${url##*/}"
    BY_TITLE["$title"]="$number"
    created=$((created + 1))
    echo "created #$number $title"
  fi
  BY_KEY["$key"]="$number"

  if [ -n "$parent" ]; then
    parent_number="${BY_KEY[$parent]:?parent key '$parent' must appear before '$key'}"
    if ! gh api "repos/$REPO/issues/$parent_number/sub_issues" --paginate --jq '.[].number' \
         | grep -qx "$number"; then
      child_id=$(gh api "repos/$REPO/issues/$number" --jq '.id')
      gh api -X POST "repos/$REPO/issues/$parent_number/sub_issues" -F sub_issue_id="$child_id" >/dev/null
      linked=$((linked + 1))
    fi
  fi

  # The board's auto-add workflow may already have added it; that is fine.
  if ! out=$(gh project item-add "$PROJECT" --owner "$OWNER" \
               --url "https://github.com/$REPO/issues/$number" 2>&1 >/dev/null); then
    case "$out" in *"already exists"*) ;; *) echo "$out" >&2; exit 1 ;; esac
  fi
done < "$HERE/backlog.txt"

echo "issues created: $created, already existed: $existing, sub-issue links added: $linked"
