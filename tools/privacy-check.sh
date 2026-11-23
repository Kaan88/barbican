#!/bin/sh
# tools/privacy-check.sh [<rev>|--worktree] [<base>]
# The repository is public: fail when the tree at <rev> (default HEAD; --worktree = the files on
# disk, tracked and untracked, before you commit), or the commits in <base>..<rev> (default:
# commits not on any origin branch), carry personal data. Text files only: scan binaries you add
# by hand, e.g. for local paths compiled into them.
# Runs from .githooks/pre-push on every push; run it by hand before pushing anything unusual.
# Exit codes: 0 clean, 1 findings, 2 pattern file missing.
set -eu

rev=${1:-HEAD}
base=${2:-}
cd "$(git rev-parse --show-toplevel)"
self=":(exclude)tools/privacy-check.sh"
if [ "$rev" = "--worktree" ]; then opt="--untracked"; tree=""; rev=HEAD; else opt=""; tree="$rev"; fi
fail=0

# 1. Private patterns: the user's accounts, server, local user name.
# They live in the gitignored .local/privacy-patterns.txt of the main checkout (first line = one
# extended regex), never in this public file. Fail closed when the file is missing or empty.
common=$(git rev-parse --path-format=absolute --git-common-dir)
pat="$(dirname "$common")/.local/privacy-patterns.txt"
if [ ! -s "$pat" ]; then
  echo "privacy-check: the private pattern file is missing or empty: .local/privacy-patterns.txt in the main checkout" >&2
  echo "  create it (first line: one extended regex of your names, addresses, hosts) or ask the owner; see CONTRIBUTING-DEV.md" >&2
  exit 2
fi
hard=$(head -n 1 "$pat" | tr -d '\r')
if [ -z "$hard" ]; then
  echo "privacy-check: the first line of $pat is empty" >&2
  exit 2
fi

# shellcheck disable=SC2086
if git grep $opt -I -n -i -E -e "$hard" $tree -- . "$self"; then
  echo "privacy-check: personal data above (use placeholders)" >&2
  fail=1
fi

# 2. Local paths of real users: /home/<name>/, /Users/<name>/, C:\Users\<name>\.
# Placeholders and system accounts are fine.
placeholder='^(.|user|users|username|you|yourname|your-name|name|me|public|default|default user|all users|runner|runneradmin|administrator|admin|guest|nobody|root|shared|<[^>]*>|\.\.\.)$'
# shellcheck disable=SC2086
paths=$(git grep $opt -I -n -o -E '([A-Za-z]:[\/]+[Uu]sers[\/]+|/Users/|/home/)[^\/ "'"'"'`<>%$*{}()]+' $tree -- . "$self" \
  | while IFS= read -r line; do
      name=$(printf '%s' "$line" | sed -E 's#^.*([\/])([Uu]sers|home)[\/]+##' | tr 'A-Z' 'a-z')
      printf '%s' "$name" | grep -q -E "$placeholder" || printf '%s\n' "$line"
    done || true)
if [ -n "$paths" ]; then
  echo "privacy-check: local home paths of real users:" >&2
  echo "$paths" >&2
  fail=1
fi

# 3. Private-key blocks and obvious tokens.
secrets='-----BEGIN ([A-Z]+ )?PRIVATE KEY( BLOCK)?-----|gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[0-9A-Z]{16}|sk-ant-[A-Za-z0-9_-]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
# shellcheck disable=SC2086
if git grep $opt -I -n -E -e "$secrets" $tree -- . "$self"; then
  echo "privacy-check: a private key or token above" >&2
  fail=1
fi

# 4. Commit metadata of the commits being published: authors, committers and messages.
if [ -n "$base" ]; then range="$base..$rev"; else range="$rev --not --remotes=origin"; fi
# shellcheck disable=SC2086
meta=$(git log --format='%an <%ae>%n%cn <%ce>%n%B' $range)
if printf '%s\n' "$meta" | grep -n -i -E "$hard"; then
  echo "privacy-check: personal data in commit metadata above" >&2
  fail=1
fi
# shellcheck disable=SC2086
bad_ids=$(git log --format='%ae%n%ce' $range | sort -u | grep -v -E '^$' | grep -v -i -E '(@users\.noreply\.github\.com|^noreply@anthropic\.com)$' || true)
if [ -n "$bad_ids" ]; then
  echo "privacy-check: commit identities must be a GitHub noreply address:" >&2
  echo "$bad_ids" >&2
  echo "  fix: git config user.email <id>+<login>@users.noreply.github.com" >&2
  fail=1
fi

[ $fail = 0 ] && echo "privacy-check: clean ($rev)"
exit $fail
