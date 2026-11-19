#!/bin/sh
# tools/privacy-check.sh [<rev>|--worktree] [<base>]
# The repository is public: fail when the tree at <rev> (default HEAD; --worktree = the files on
# disk, tracked and untracked, before you commit), or the commits in <base>..<rev> (default:
# commits not on any origin branch), carry personal data. Text files only: scan binaries you add
# (DLLs, images with text) by hand, e.g. for local paths compiled into them.
# Runs from .githooks/pre-push on every push; run it by hand before pushing anything unusual.
set -eu

rev=${1:-HEAD}
base=${2:-}
cd "$(git rev-parse --show-toplevel)"
self=":(exclude)tools/privacy-check.sh"
if [ "$rev" = "--worktree" ]; then opt="--untracked"; tree=""; rev=HEAD; else opt=""; tree="$rev"; fi
fail=0

# 1. Hard patterns: the user's accounts, server, local user name, the admin key file.
# The patterns are private: they live in the gitignored .local/privacy-patterns.txt of the main
# checkout (one extended regex), never in this public file. Fail closed when they are missing.
common=$(git rev-parse --path-format=absolute --git-common-dir)
pat="$(dirname "$common")/.local/privacy-patterns.txt"
if [ ! -s "$pat" ]; then
  echo "privacy-check: $pat is missing; ask the user for the pattern file (see CLAUDE.md)" >&2
  exit 2
fi
hard=$(head -n 1 "$pat")

# Required, verbatim public clsx 2.1.1 MIT notice. Its upstream contact is not
# user data. Exempt only this exact path AND reviewed LF-normalized SHA-256;
# any edited notice fails closed and remains subject to every normal scan.
license_path='tools/plugin-runtime-build/third-party-licenses/clsx--license'
license_sha='9a9edad7baae52622bddf3c15b2ef8a33d2c89f2d25408ad13e8a7481c6b0c97'
license_actual=''
if [ -n "$opt" ]; then
  if [ -L "$license_path" ]; then
    license_actual=symlink-not-approved
  elif [ -f "$license_path" ]; then
    license_actual=$(sed 's/\r$//' "$license_path" | sha256sum | cut -d ' ' -f 1)
  fi
elif git cat-file -e "$tree:$license_path" 2>/dev/null; then
  license_actual=$(git show "$tree:$license_path" | sed 's/\r$//' | sha256sum | cut -d ' ' -f 1)
fi
set -- . "$self"
if [ -n "$license_actual" ]; then
  if [ "$license_actual" = "$license_sha" ]; then
    set -- "$@" ":(exclude)$license_path"
  else
    echo "privacy-check: reviewed public license content changed: $license_path" >&2
    fail=1
  fi
fi
if git grep $opt -I -n -i -E "$hard" $tree -- "$@"; then
  echo "privacy-check: personal data above (use placeholders; real values go in .local/accounts.md)" >&2
  fail=1
fi

# 2. Every e-mail address must use a fictional or public-service domain.
allow='@([a-z0-9-]+\.)*(example|example\.(org|com|net)|anthropic\.com|users\.noreply\.github\.com|okafor\.studio|stalw\.art|flytap\.com|fastmail\.de|hetzner\.com|thebrowser\.com|github\.com|barbican\.mail|prize-centre\.biz|fischer-arch\.de|studio-north\.eu|domain\.com|bulwarkmail\.org|yahoo\.com)$'
# This exact upstream copyright notice is required by the bundled font's OFL.
# Keep filenames and line numbers until after this one file-and-address exception.
addrs=$(git grep $opt -I -n -o -i -E '[A-Za-z0-9._%+-]+@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}' $tree -- "$@" \
  | grep -v -i -E '(^|:)src/Barbican[.]App/Assets/Fonts/OpenDyslexic-OFL[.]txt:[0-9]+:support@abbiecod[.]es$' \
  | sed -E 's/^.*:[0-9]+://' | sort -u | grep -v -i -E "$allow" || true)
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
