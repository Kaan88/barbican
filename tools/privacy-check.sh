#!/bin/sh
# tools/privacy-check.sh [<rev>|--worktree] [<base>]
# The repository is public: fail when the tree at <rev> (default HEAD; --worktree = the files on
# disk, tracked and untracked, before you commit), or the commits in <base>..<rev> (default:
# commits not on any origin branch), carry personal data. Text files only: scan binaries you add
# (DLLs, images with text) by hand, e.g. for local paths compiled into them.
# Runs from .githooks/pre-push on every push; run it by hand before pushing anything unusual.
# This file is the only tracked file allowed to contain the patterns below.
set -eu

rev=${1:-HEAD}
base=${2:-}
cd "$(git rev-parse --show-toplevel)"
self=":(exclude)tools/privacy-check.sh"
if [ "$rev" = "--worktree" ]; then opt="--untracked"; tree=""; rev=HEAD; else opt=""; tree="$rev"; fi
fail=0

# 1. Hard patterns: the user's accounts, server, local user name, the admin key file.
hard='<private patterns: see .local/privacy-patterns.txt>'
if git grep $opt -I -n -i -E "$hard" $tree -- . "$self"; then
  echo "privacy-check: personal data above (use placeholders; real values go in .local/accounts.md)" >&2
  fail=1
fi

# 2. Every e-mail address must use a fictional or public-service domain.
allow='@([a-z0-9-]+\.)*(example|example\.(org|com|net)|anthropic\.com|users\.noreply\.github\.com|okafor\.studio|stalw\.art|flytap\.com|fastmail\.de|hetzner\.com|thebrowser\.com|github\.com|barbican\.mail|prize-centre\.biz|fischer-arch\.de|studio-north\.eu|domain\.com|bulwarkmail\.org)$'
addrs=$(git grep $opt -I -h -o -i -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}' $tree -- . "$self" \
  | sort -u | grep -v -i -E "$allow" || true)
if [ -n "$addrs" ]; then
  echo "privacy-check: e-mail addresses outside the allowlist:" >&2
  echo "$addrs" >&2
  fail=1
fi

# 3. Commit metadata of the commits being published: authors, committers and messages.
if [ -n "$base" ]; then range="$base..$rev"; else range="$rev --not --remotes=origin"; fi
# shellcheck disable=SC2086
meta=$(git log --format='%an <%ae>%n%cn <%ce>%n%B' $range)
if printf '%s\n' "$meta" | grep -n -i -E "$hard"; then
  echo "privacy-check: personal data in commit metadata above" >&2
  fail=1
fi
# shellcheck disable=SC2086
bad_ids=$(git log --format='%ae%n%ce' $range | sort -u | grep -v -i -E '(users\.noreply\.github\.com|noreply@anthropic\.com)$' || true)
if [ -n "$bad_ids" ]; then
  echo "privacy-check: commit identities must be the GitHub noreply address:" >&2
  echo "$bad_ids" >&2
  echo "  fix: git config user.email <id>+<login>@users.noreply.github.com (see CLAUDE.md)" >&2
  fail=1
fi

[ $fail = 0 ] && echo "privacy-check: clean ($rev)"
exit $fail
