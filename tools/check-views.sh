#!/usr/bin/env bash
set -euo pipefail

# View guardrails for the Content-Security-Policy in Program.cs, whose
# script-src has no 'unsafe-inline': inline <script> blocks and on* handler
# attributes are blocked by the browser. Use external files under wwwroot/js and
# the data-* behaviours in wwwroot/js/core/declarative-actions.js instead.
#
# style="" attributes are not checked: the policy allows them through
# style-src-attr 'unsafe-inline' (data-driven widths and CSS custom properties).
#
# Usage: tools/check-views.sh [path...]   (defaults to the application view roots)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "$REPO_ROOT"

if ! command -v rg >/dev/null 2>&1; then
  echo "Error: ripgrep (rg) is required to run the view guardrails." >&2
  exit 2
fi

if [ "$#" -gt 0 ]; then
  paths=("$@")
else
  paths=("Areas" "Pages" "Views" "wwwroot")
fi

search_paths=()
for path in "${paths[@]}"; do
  if [ -e "$path" ]; then
    search_paths+=("$path")
  fi
done
if [ ${#search_paths[@]} -eq 0 ]; then
  echo "Error: none of the view paths exist: ${paths[*]}" >&2
  exit 2
fi

failed=0

function run_check() {
  local pattern="$1"
  local message="$2"
  local out
  local status=0
  out=$(rg --pcre2 --line-number --iglob '*.cshtml' --iglob '*.html' -- "$pattern" "${search_paths[@]}") || status=$?
  case "$status" in
    0)
      echo "Error: ${message}" >&2
      echo "$out" >&2
      failed=1
      ;;
    1)
      ;;
    *)
      echo "Error: ripgrep failed (exit ${status}) while checking: ${message}" >&2
      exit 2
      ;;
  esac
}

# A <script> start tag with neither a src attribute nor a JSON data-block type.
run_check '(?i)<script\b(?![^>]*\bsrc\s*=)(?![^>]*\btype\s*=\s*["'"'"']?application/(?:ld\+)?json\b)[^>]*>' \
  "Inline <script> blocks are not allowed; move the code to a file under wwwroot/js."

# An on* attribute with a quoted or Razor value, e.g. onclick="..." or onchange=@x.
# The lookbehind skips C# identifiers such as "var onTimePercent = (...)".
run_check '(?i)(?<![\w.$@:-])on[a-z]+\s*=\s*["'"'"'@]' \
  "Inline event handler attributes are not allowed; use data-confirm, data-auto-submit, data-print or a page script."

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "View guardrail checks passed."
