#!/bin/sh
# tools/privacy-check.sh [<rev>|--worktree] [<base>]
# The repository is public: fail when what is about to be published carries personal data.
#   <rev>        a commit or tag (default HEAD): its tree, the lines added by every commit of the
#                range, and the commit/tag metadata are scanned
#   --worktree   scan the files on disk (tracked and untracked) instead of a tree; the range is
#                still HEAD's
#   <base>       the range is <base>..<rev>; default: commits not on any branch of the remote
#                (PRIVACY_REMOTE, default origin)
# Scans: private patterns, home paths of real users, private keys and tokens, NUL-byte files off a
# binary allowlist, and the noreply rule for authors, committers and trailer e-mails.
# Runs from .githooks/pre-push (once per pushed ref); run it by hand before anything unusual.
# Exit codes: 0 clean, 1 findings, 2 cannot check (pattern file or regex bad, git/grep error).
set -eu

rev=${1:-HEAD}
base=${2:-}
remote=${PRIVACY_REMOTE:-origin}
fail=0
die() { echo "privacy-check: $*" >&2; exit 2; }
cd "$(git rev-parse --show-toplevel)" || die "not in a git work tree"
tmp=$(mktemp -d "${TMPDIR:-/tmp}/privchk.XXXXXX") || die "mktemp failed"
trap 'rm -rf "$tmp"' EXIT

# --- the private pattern: first line of .local/privacy-patterns.txt in the MAIN checkout ----------
common=$(git rev-parse --path-format=absolute --git-common-dir) || die "git-common-dir failed"
pat="$(dirname "$common")/.local/privacy-patterns.txt"
if [ ! -s "$pat" ]; then
  echo "privacy-check: the private pattern file is missing or empty: .local/privacy-patterns.txt in the main checkout" >&2
  echo "  create it (first line: one extended regex of your names, addresses, hosts); see CONTRIBUTING-DEV.md" >&2
  exit 2
fi
[ "$(wc -c < "$pat")" -eq "$(tr -d '\000' < "$pat" | wc -c)" ] || die "$pat contains NUL bytes (UTF-16?); save it as UTF-8"
bom=$(printf '\357\273\277')
hard=$(head -n 1 "$pat" | tr -d '\r')
hard=${hard#"$bom"}
hard=$(printf '%s' "$hard" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')
[ -n "$hard" ] || die "the first line of $pat is empty"
vrc=0
printf 'x\n' | grep -E -e "$hard" >/dev/null 2>&1 || vrc=$?
[ "$vrc" -le 1 ] || die "the first line of $pat is not a valid extended regex"

# --- generic patterns ------------------------------------------------------------------------------
# Home paths of real users: C:\Users\<n>, /Users/<n>, /c/Users/<n>, /mnt/c/Users/<n>, /home/<n>.
pathre='([A-Za-z]:[\\/]+[Uu][Ss][Ee][Rr][Ss][\\/]+|/[Uu][Ss][Ee][Rr][Ss]/|/home/)[^\\/ "'"'"'`<>%$*{}()]+'
placeholder='^([a-z0-9]|user|users|username|you|yourname|your-name|name|me|public|default|default user|all users|runner|runneradmin|administrator|admin|guest|nobody|root|shared|<[^>]*>|\.\.\.)$'
secrets='-----BEGIN ([A-Z]+ )?PRIVATE KEY( BLOCK)?-----|gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[0-9A-Z]{16}|sk-ant-[A-Za-z0-9_-]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}|AIza[0-9A-Za-z_-]{35}|eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}'
# Files that may legitimately hold NUL bytes (anything else with NULs fails: UTF-16 text hides data).
binext='png|jpe?g|gif|ico|icns|bmp|webp|zip|gz|tgz|tar|7z|xz|jar|exe|dll|so|dylib|lib|a|o|rlib|pdb|wasm|node|msi|deb|ttf|otf|woff2?|pdf|der|bin'

# keep only matches whose last path component is not a placeholder (reads grep -o output)
filter_paths() {
  while IFS= read -r line; do
    [ -n "$line" ] || continue
    name=$(printf '%s' "$line" | sed 's#^.*[/\\]##' | tr 'A-Z' 'a-z')
    printf '%s' "$name" | grep -q -E -e "$placeholder" || printf '%s\n' "$line"
  done
}
report() { # label, text
  echo "privacy-check: $1:" >&2
  printf '%s\n' "$2" | head -n 25 >&2
  fail=1
}
# run grep / git grep; $rc is the exit status (0 match, 1 none, >1 error = fatal), $out the output
grep_file() { rc=0; out=$(grep -a "$@") || rc=$?; [ "$rc" -le 1 ] || die "grep failed ($rc)"; }
git_grep() { rc=0; out=$(git grep --text "$@") || rc=$?; [ "$rc" -le 1 ] || die "git grep failed ($rc)"; }

scan_text() { # file label [nopath]: all three groups on one text file (nopath: skip the home-path group)
  grep_file -n -i -E -e "$hard" "$1"
  [ "$rc" -ne 0 ] || report "personal data in $2" "$out"
  grep_file -n -o -E -e "$pathre" "$1"
  if [ "$rc" -eq 0 ] && [ -z "${3:-}" ]; then
    bad=$(printf '%s\n' "$out" | filter_paths)
    [ -z "$bad" ] || report "local home paths of real users in $2" "$bad"
  fi
  grep_file -n -E -e "$secrets" "$1"
  [ "$rc" -ne 0 ] || report "private key or token in $2" "$out"
}

# --- what to scan ----------------------------------------------------------------------------------
if [ "$rev" = "--worktree" ]; then
  opt="--untracked"; tree=""; rev=HEAD; shown="worktree"
else
  opt=""
  type=$(git cat-file -t "$rev" 2>/dev/null) || die "unknown revision $rev"
  if [ "$type" = tag ]; then   # annotated tag: its tagger and message are published too
    git cat-file tag "$rev" > "$tmp/tag"
    scan_text "$tmp/tag" "tag $rev"
    tagmail=$(sed -n 's/^tagger .*<\(.*\)>.*/\1/p' "$tmp/tag" | grep -v -i -E '(@users\.noreply\.github\.com|^noreply@anthropic\.com)$' || true)
    [ -z "$tagmail" ] || report "tagger identity must be a GitHub noreply address" "$tagmail"
  fi
  rev=$(git rev-parse --verify "$rev^{commit}") || die "$rev is not a commit"
  tree=$rev; shown=$rev
fi
if [ -n "$base" ]; then range="$base..$rev"; else range="$rev --not --remotes=$remote"; fi

# 1. the tree
# shellcheck disable=SC2086
git_grep $opt -n -i -E -e "$hard" $tree -- .
[ "$rc" -ne 0 ] || report "personal data in the tree" "$out"
# shellcheck disable=SC2086
git_grep $opt -n -o -E -e "$pathre" $tree -- . ":(exclude)tools/privacy-check.sh" ":(exclude)tools/test-privacy-check.sh"
if [ "$rc" -eq 0 ]; then
  bad=$(printf '%s\n' "$out" | filter_paths)
  [ -z "$bad" ] || report "local home paths of real users" "$bad"
fi
# shellcheck disable=SC2086
git_grep $opt -n -E -e "$secrets" $tree -- .
[ "$rc" -ne 0 ] || report "private key or token in the tree" "$out"
# NUL-byte files that are not on the binary allowlist
# shellcheck disable=SC2086
git_grep $opt -l -e '' $tree -- .
printf '%s\n' "$out" | sort > "$tmp/all"
# shellcheck disable=SC2086
git_grep $opt -I -l -e '' $tree -- .
printf '%s\n' "$out" | sort > "$tmp/txt"
nul=$(comm -23 "$tmp/all" "$tmp/txt" | sed "s#^$tree:##" | grep -v -i -E "\\.($binext)\$" | grep -v '^$' || true)
[ -z "$nul" ] || report "files with NUL bytes that are not on the binary allowlist (UTF-16 text? scan by hand)" "$nul"

# 2. every line added by the commits being published (add-then-delete must not slip through)
# shellcheck disable=SC2086
git log -p --text --no-ext-diff --format='@@@%h' $range > "$tmp/log" || die "git log failed for $range"
# (this script and its test spell out home-path patterns: they are exempt from the home-path group only)
: > "$tmp/added"; : > "$tmp/added_self"
awk -v main="$tmp/added" -v self="$tmp/added_self" '
     /^@@@/ {c=substr($0,4); hdr=0; next}
     /^diff --git/ {hdr=1; out=main; if ($0 ~ /^diff --git a\/tools\/(test-)?privacy-check\.sh /) out=self; next}
     /^@@ / {hdr=0; next}
     hdr {next}
     /^\+/ {print c ": " substr($0,2) > out}' "$tmp/log"
scan_text "$tmp/added" "lines added by commits in $range"
scan_text "$tmp/added_self" "lines added to the privacy scripts in $range" nopath

# 3. commit metadata: names, e-mails and messages (including trailers)
# shellcheck disable=SC2086
git log --format='%an <%ae>%n%cn <%ce>%n%B' $range > "$tmp/meta" || die "git log failed for $range"
scan_text "$tmp/meta" "commit metadata"
ok_mail='(@users\.noreply\.github\.com|^noreply@anthropic\.com)$'
# shellcheck disable=SC2086
bad_ids=$(git log --format='%ae%n%ce' $range | sort -u | grep -v '^$' | grep -v -i -E "$ok_mail" || true)
trailer_ids=$(grep -a -i -E '^[A-Za-z][A-Za-z-]*:.*<' "$tmp/meta" | grep -a -o -E '<[^<> ]+@[^<> ]+>' | tr -d '<>' | grep -v -i -E "$ok_mail" || true)
if [ -n "$bad_ids$trailer_ids" ]; then
  report "commit identities and trailer e-mails must be GitHub noreply addresses" "$bad_ids
$trailer_ids"
  echo "  fix: git config user.email <id>+<login>@users.noreply.github.com" >&2
fi

[ "$fail" = 0 ] && echo "privacy-check: clean ($shown)"
exit "$fail"
