#!/usr/bin/env bash
set -euo pipefail

# View guardrails for the Content-Security-Policy in Program.cs, whose
# script-src has no 'unsafe-inline': inline <script> blocks, on* handler
# attributes and javascript: links are blocked by the browser. Use external files
# under wwwroot/js and the data-* behaviours in wwwroot/js/core/declarative-actions.js.
# <style> elements are blocked too (style-src-elem allows them only in Development and
# on a few routes); put styles in a file under wwwroot/css.
#
# Limits:
#   - style="" attributes are not checked: the policy allows them (style-src-attr 'unsafe-inline').
#   - Unquoted handler values (onclick=foo()) are not detected; they are indistinguishable
#     from C# assignments in Razor code blocks.
#   - Markup inside HTML or Razor comments is checked like any other markup, so keep
#     examples out of comments.
#
# Usage: tools/check-views.sh [path...]   (defaults to the application view roots)
# Relative paths are resolved from the current directory. Exit codes: 0 clean,
# 1 violations found, 2 the check could not run (missing path; ripgrep missing, without
# PCRE2, or failing).

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

if ! command -v rg >/dev/null 2>&1; then
  echo "Error: ripgrep (rg) is required to run the view guardrails." >&2
  exit 2
fi

if ! rg --pcre2-version >/dev/null 2>&1; then
  echo "Error: this ripgrep was built without PCRE2 support (rg --pcre2), which the view guardrails need." >&2
  exit 2
fi

search_paths=()
if [ "$#" -gt 0 ]; then
  for path in "$@"; do
    if [ ! -e "$path" ]; then
      echo "Error: view path does not exist: ${path}" >&2
      exit 2
    fi
    search_paths+=("$(cd "$(dirname "$path")" && pwd)/$(basename "$path")")
  done
  cd "$REPO_ROOT"
else
  cd "$REPO_ROOT"
  for path in "Areas" "Pages" "Views" "wwwroot"; do
    if [ -d "$path" ]; then
      search_paths+=("$path")
    fi
  done
  if [ ${#search_paths[@]} -eq 0 ]; then
    echo "Error: no view directories found under ${REPO_ROOT}" >&2
    exit 2
  fi
fi

failed=0

function run_check() {
  local pattern="$1"
  local message="$2"
  local out
  local status=0
  # --multiline: tags and attributes in views often span several lines.
  out=$(rg --pcre2 --multiline --line-number --iglob '*.cshtml' --iglob '*.html' -- "$pattern" "${search_paths[@]}") || status=$?
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

# A <script> start tag (possibly spanning lines) with no src / asp-src-include attribute and
# no data-block type (JSON or client-side templates, which the browser never executes).
# Attribute names must follow whitespace so data-src="..." does not count as src.
run_check '(?i)<script\b(?![^>]*\s(?:src|asp-src-include)\s*=)(?![^>]*\stype\s*=\s*["'"'"']?(?:application/(?:ld\+)?json|text/(?:template|x-template|html))\b)[^>]*>' \
  "Inline <script> blocks are not allowed; move the code to a file under wwwroot/js."

# An on* attribute with a quoted or Razor value inside a tag, e.g. <button onclick="...">
# or <select\n  onchange=@x>. Matching only inside a tag keeps C# such as
# new { online = "y" } or var onTrack = "..." out of it.
run_check '(?i)<[a-z][a-z0-9:-]*\b[^<>]*?\son[a-z]+\s*=\s*["'"'"'@]' \
  "Inline event handler attributes are not allowed; use data-confirm, data-auto-submit, data-print or a page script."

run_check '(?i)\s(?:href|action|formaction|src)\s*=\s*["'"'"']\s*javascript:' \
  "javascript: URLs are not allowed; use a button with a page script or a data-* behaviour."

run_check '(?i)<style\b[^>]*>' \
  "Inline <style> elements are not allowed; move the rules to a file under wwwroot/css (inside <noscript>, link the file)."

if [ "$failed" -ne 0 ]; then
  exit 1
fi

echo "View guardrail checks passed."
