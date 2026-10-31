#!/usr/bin/env sh
# Sensitive-content guard for the public repo.
# Prints only file:line locations, never the matched text.
set -u

root=$(git rev-parse --show-toplevel 2>/dev/null) || { echo "not a git repository"; exit 0; }
cd "$root" || exit 0

fail=0
# The guard itself contains the pattern text, so it must not scan itself.
EXCLUDE_PATHSPEC=":(exclude).github/scripts/sensitive-guard.sh"

scan() { # label, path scope, extended regex
  label=$1; scope=$2; re=$3
  out=$(git grep -n -I -E "$re" -- $scope "$EXCLUDE_PATHSPEC" 2>/dev/null | cut -d: -f1,2 || true)
  if [ -n "$out" ]; then
    echo "sensitive-guard: $label"
    echo "$out" | head -20
    fail=1
  fi
}

scan "activity-package-name" "." 'CMEX[0-9]{2}_'
scan "internal-ip" "." '(^|[^0-9])(192\.168\.|10\.[0-9]{1,3}\.|172\.(1[6-9]|2[0-9]|3[01])\.)'
scan "windows-drive-path" "." '(^|[^A-Za-z0-9])[A-Za-z]:[\\/]'
scan "user-home" "." '(C:\\Users|/c/Users/)'
scan "secret-shape" "." '(sk-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16})'
scan "private-key" "." 'BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY'
scan "internal-doc-ref" "src test" '(方案 ?§|指示 ?[0-9]+|P[0-9]-[0-9]|\.codebuddy/|§[0-9])'

DENY_FILE=""
if [ -n "${AUTOCMEX_DENYLIST-}" ]; then DENY_FILE="$AUTOCMEX_DENYLIST"; else DENY_FILE="$HOME/.autocmex-guard/denylist.txt"; fi
if [ -f "$DENY_FILE" ]; then
  out=$(git grep -n -I -F -f "$DENY_FILE" -- . "$EXCLUDE_PATHSPEC" 2>/dev/null | cut -d: -f1,2 || true)
  if [ -n "$out" ]; then
    echo "sensitive-guard: exact-denylist"
    echo "$out" | head -20
    fail=1
  fi
fi

if [ "$fail" -ne 0 ]; then
  echo "sensitive-guard: FAILED"
  exit 1
fi
echo "sensitive-guard: OK"