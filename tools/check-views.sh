#!/usr/bin/env bash
set -euo pipefail

# Run from repository root
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "$REPO_ROOT"

paths=("Areas" "Pages" "Views" "wwwroot")

function run_check() {
  local pattern="$1"
  local message="$2"
  local args=("--iglob" "*.cshtml" "--iglob" "*.html" "-n" "$pattern")
  local search_paths=()
  for path in "${paths[@]}"; do
    if [ -d "$path" ]; then
      search_paths+=("$path")
    fi
  done
  if [ ${#search_paths[@]} -eq 0 ]; then
    return 0
  fi
  if out=$(rg "${args[@]}" "${search_paths[@]}" 2>/dev/null); then
    if [ -n "$out" ]; then
      echo "Error: ${message}" >&2
      echo "$out" >&2
      exit 1
    fi
  fi
}

run_check '<script(?:(?!\\bsrc=)[^>])*>' "Inline <script> tags without a src attribute are not allowed."
run_check '\\bon[a-zA-Z]+\\s*=' "Inline event handlers are not allowed."
run_check 'style="' "Inline style attributes are not allowed."

echo "View guardrail checks passed."
