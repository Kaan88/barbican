#!/bin/sh
# tools/privacy-check.sh [<rev>|--worktree] [<base>]
# The repository is public: fail when what is about to be published carries personal data.
#   <rev>        a commit or tag (default HEAD): its tree, the lines and blobs added by every
#                commit of the range, and the commit/tag metadata are scanned
#   --worktree   scan the files on disk (tracked and untracked) instead of a tree; the range is
#                still HEAD's
#   <base>       the range is <base>..<rev>; default: commits not on any branch of the remote
#                (PRIVACY_REMOTE, default origin)
# An annotated tag is peeled through the whole tag chain; every tag object is scanned.
# Scans: private patterns, home paths of real users, private keys and tokens, NUL-byte files off a
# binary allowlist, and the noreply rule for authors, committers, taggers and every e-mail address
# in a commit message body or tag message.
# Merge commits of any parent count are scanned as combined diffs, and NUL bytes are tested on the
# bytes themselves (never through git grep -I, which follows the diff attribute).
# Runs from .githooks/pre-push (once per pushed ref); run it by hand before anything unusual.
# Exit codes: 0 clean, 1 findings, 2 cannot check (pattern file or regex bad, git/grep error).
set -eu

rev=${1:-HEAD}
base=${2:-}
remote=${PRIVACY_REMOTE:-origin}
wt=0
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
# The whole address must be a GitHub noreply address: a suffix match would accept
# eve@example.test+1@users.noreply.github.com.
ok_mail='^([0-9]+\+)?[A-Za-z0-9-]+@users\.noreply\.github\.com$|^noreply@anthropic\.com$'
email_re='[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'

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

# true when the file holds a NUL byte; the bytes are tested directly so a git attribute cannot hide
# a UTF-16 file from the check
has_nul() { # file
  sz=$(wc -c < "$1") || die "cannot read $1"
  txt=$(tr -d '\000' < "$1" | wc -c) || die "cannot read $1"
  [ "$sz" -ne "$txt" ]
}

# true when any blob named in the list (one object name per line) holds a NUL byte. One streaming
# read of all blobs: a well-formed `cat-file --batch` stream has a known size (header + content +
# newline per blob), so a shorter stripped stream means a NUL byte somewhere. The caller names the
# offending files only in that (rare) case, so the common case costs no per-file forks.
batch_has_nul() { # file of blob object names
  git cat-file --batch-check < "$1" > "$tmp/bcheck" || die "git cat-file --batch-check failed"
  exp=$(awk '
    NF != 3 || $2 != "blob" || $3 !~ /^[0-9]+$/ {bad = 1}
    {n += length($1) + length($2) + length($3) + $3 + 4}
    END {if (bad) exit 1; printf "%d\n", n}' "$tmp/bcheck") || die "cannot read the blob sizes of $1"
  rc=0
  got=$(git cat-file --batch < "$1" | tr -d '\000' | wc -c) || rc=$?
  [ "$rc" -eq 0 ] || die "git cat-file --batch failed"
  [ "$got" -le "$exp" ] || die "git cat-file --batch output does not match its object sizes"
  [ "$got" -ne "$exp" ]
}

# --- what to scan ----------------------------------------------------------------------------------
: > "$tmp/bodies"          # commit messages and tag objects, checked for non-noreply e-mails
if [ "$rev" = "--worktree" ]; then
  opt="--untracked"; tree=""; rev=HEAD; shown="worktree"; wt=1
else
  opt=""
  first=$rev
  type=$(git cat-file -t "$rev" 2>/dev/null) || die "unknown revision $rev"
  depth=0
  while [ "$type" = tag ]; do   # annotated tag: every object of the chain is published too
    depth=$((depth + 1))
    [ "$depth" -le 20 ] || die "annotated tag chain of $first is too deep"
    git cat-file tag "$rev" > "$tmp/tag" || die "cannot read tag $rev"
    scan_text "$tmp/tag" "tag $rev"
    if has_nul "$tmp/tag"; then   # a UTF-16 tag message hides its text from the scan above
      tr -d '\000' < "$tmp/tag" > "$tmp/tagtxt" || die "cannot decode tag $rev"
      scan_text "$tmp/tagtxt" "tag $rev (NUL bytes stripped)"
      cat "$tmp/tagtxt" >> "$tmp/bodies" || die "cannot save tag $rev"
    else
      cat "$tmp/tag" >> "$tmp/bodies" || die "cannot save tag $rev"
    fi
    sed -n 's/^tagger .*<\(.*\)>.*/\1/p' "$tmp/tag" > "$tmp/tagmail" || die "cannot read the tagger of $rev"
    rc=0; grep -v -i -E "$ok_mail" "$tmp/tagmail" > "$tmp/tagmailbad" || rc=$?
    [ "$rc" -le 1 ] || die "grep failed ($rc)"
    tagmail=$(cat "$tmp/tagmailbad")
    [ -z "$tagmail" ] || report "tagger identity of $rev must be a GitHub noreply address" "$tagmail"
    rev=$(sed -n 's/^object \([0-9a-f][0-9a-f]*\)$/\1/p' "$tmp/tag" | head -n 1)
    [ -n "$rev" ] || die "tag $first has no object line"
    type=$(git cat-file -t "$rev" 2>/dev/null) || die "tag $first points at a missing object"
  done
  rev=$(git rev-parse --verify "$rev^{commit}") || die "$first does not resolve to a commit"
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
# git grep -I is not used: it follows the diff attribute (a `*.txt diff` line in .gitattributes
# marks even a UTF-16 file as text). Test the bytes of every tracked file directly.
: > "$tmp/nulall"
if [ "$wt" = 1 ]; then
  git ls-files -z -c -o --exclude-standard > "$tmp/list0" || die "git ls-files failed"
  tr '\000' '\n' < "$tmp/list0" > "$tmp/list1" || die "cannot list the worktree files"
  rc=0; grep -v -i -E "\\.($binext)\$" "$tmp/list1" > "$tmp/list" || rc=$?
  [ "$rc" -le 1 ] || die "grep failed ($rc)"
  while IFS= read -r f; do
    [ -f "$f" ] || continue
    if has_nul "$f"; then printf '%s\n' "$f" >> "$tmp/nulall"; fi
  done < "$tmp/list"
else
  git ls-tree -r -z "$tree" > "$tmp/list0" || die "git ls-tree failed"
  tr '\000' '\n' < "$tmp/list0" > "$tmp/list1" || die "cannot list the tree"
  awk '
    /^[0-7]+ blob [0-9a-f]+	/ {
      sha=$3; sub(/^[^\t]*\t/, ""); print sha "\t" $0
    }' "$tmp/list1" > "$tmp/list2" || die "cannot list the tree"
  rc=0; grep -v -i -E "\\.($binext)\$" "$tmp/list2" > "$tmp/list" || rc=$?
  [ "$rc" -le 1 ] || die "grep failed ($rc)"
  cut -f1 "$tmp/list" | sort -u > "$tmp/shalist" || die "cannot list the tree blobs"
  if batch_has_nul "$tmp/shalist"; then   # name the files only when the batch found a NUL
    while IFS='	' read -r sha path; do
      git cat-file blob "$sha" > "$tmp/blobbuf" || die "cannot read blob $sha"
      if has_nul "$tmp/blobbuf"; then printf '%s\n' "$path" >> "$tmp/nulall"; fi
    done < "$tmp/list"
  fi
fi
[ ! -s "$tmp/nulall" ] || report "files with NUL bytes that are not on the binary allowlist (UTF-16 text? scan by hand)" "$(cat "$tmp/nulall")"

# 2. every line added by the commits being published (add-then-delete must not slip through)
# Color is forced off: with color.diff=always the added lines start with an escape sequence and the
# /^\+/ rule below would miss them. --root shows a root commit even with log.showRoot=false, and
# --cc shows the content a merge commit introduces (a plain `git log -p` prints no merge diff).
# shellcheck disable=SC2086
git -c color.ui=never -c color.diff=never -c log.showRoot=true \
    log -p --no-color --root --cc --no-renames --text --no-ext-diff --no-textconv --format='@@@%h' $range > "$tmp/log" \
  || die "git log failed for $range"
# (this script and its test spell out home-path patterns: they are exempt from the home-path group only)
: > "$tmp/added"; : > "$tmp/added_self"
awk -v main="$tmp/added" -v self="$tmp/added_self" '
     /^@@@[0-9a-f]+$/ {c=substr($0,4); hdr=0; next}
     /^@+ / {match($0, /^@+/); n=RLENGTH-1; comb=1; hdr=0; next}   # one '@' more than the parents
     /^diff --git a\/tools\/(test-)?privacy-check\.sh b\/tools\/(test-)?privacy-check\.sh$/ {hdr=1; comb=0; out=self; next}
     /^diff --(cc|combined) (a\/)?tools\/(test-)?privacy-check\.sh$/ {hdr=1; comb=1; out=self; next}
     /^diff --git / {hdr=1; comb=0; out=main; next}
     /^diff --(cc|combined) / {hdr=1; comb=1; out=main; next}
     hdr {next}
     comb {
       pre=substr($0,1,n)                 # one prefix character per parent
       if (pre ~ /^[+]+$/) print c ": " substr($0,n+1) > out
       next
     }
     /^\+/ {print c ": " substr($0,2) > out}' "$tmp/log"
scan_text "$tmp/added" "lines added by commits in $range"
scan_text "$tmp/added_self" "lines added to the privacy scripts in $range" nopath

# 2b. every blob the range adds: a UTF-16 file added and removed inside the range only leaves
# NUL-interleaved lines in the log above, where no pattern can match. Strip the NULs and rescan.
# (`--diff-merges=separate` keeps `--raw` listing a merge's blobs even when log.diffMerges is set.)
# shellcheck disable=SC2086
git -c log.showRoot=true log -m --diff-merges=separate --raw --no-abbrev --no-renames --diff-filter=d --root \
    --format= $range > "$tmp/raw" || die "git log --raw failed for $range"
awk -F'\t' '
     /^::/ {bad = 1}
     /^:[0-7]+ [0-7]+ [0-9a-f]+ [0-9a-f]+ / {
       split($1, a, " ")
       if (a[2] ~ /^(100644|100755|120000)$/ && a[4] !~ /^0+$/) print a[4] "\t" $2
     }
     END {if (bad) exit 1}' "$tmp/raw" > "$tmp/rawlist" || die "cannot list the blobs of $range"
sort -u "$tmp/rawlist" > "$tmp/bloblist" || die "cannot sort the blob list"
cut -f1 "$tmp/bloblist" | sort -u > "$tmp/blobshas" || die "cannot list the blobs of $range"
if [ -s "$tmp/blobshas" ] && batch_has_nul "$tmp/blobshas"; then
  while IFS='	' read -r sha path; do
    [ -n "$sha" ] || continue
    git cat-file blob "$sha" > "$tmp/blobbuf" || die "cannot read blob $sha ($path)"
    has_nul "$tmp/blobbuf" || continue
    tr -d '\000' < "$tmp/blobbuf" > "$tmp/blobtxt" || die "cannot decode blob $sha ($path)"
    nopath=""
    case $path in tools/privacy-check.sh|tools/test-privacy-check.sh) nopath=1 ;; esac
    scan_text "$tmp/blobtxt" "blob $path added by commits in $range" $nopath
  done < "$tmp/bloblist"
fi

# 3. commit metadata: names, e-mails and messages. The whole address of every author, committer
#    and tagger, and of every e-mail address found anywhere in a commit message body or a tag
#    message (trailer or not), must be a noreply address. Each extraction is checked so a tool
#    failure cannot hide a finding.
# shellcheck disable=SC2086
git log --format='%an <%ae>%n%cn <%ce>%n%B' $range > "$tmp/meta" || die "git log failed for $range"
scan_text "$tmp/meta" "commit metadata"
# shellcheck disable=SC2086
git log --format='%B' $range > "$tmp/logbody" || die "git log failed for $range"
cat "$tmp/logbody" >> "$tmp/bodies" || die "cannot save the commit messages"

# shellcheck disable=SC2086
git log --format='%ae%n%ce' $range > "$tmp/ids" || die "git log failed for $range"
sort -u "$tmp/ids" > "$tmp/idsu" || die "cannot sort the identities"
rc=0; grep -v '^$' "$tmp/idsu" > "$tmp/idsn" || rc=$?
[ "$rc" -le 1 ] || die "grep failed ($rc)"
rc=0; grep -v -i -E "$ok_mail" "$tmp/idsn" > "$tmp/badids" || rc=$?
[ "$rc" -le 1 ] || die "grep failed ($rc)"
bad_ids=$(cat "$tmp/badids")
rc=0; grep -a -o -E "$email_re" "$tmp/bodies" > "$tmp/mails" || rc=$?
[ "$rc" -le 1 ] || die "grep failed ($rc)"
sort -u "$tmp/mails" > "$tmp/mailsu" || die "cannot sort the e-mail addresses"
rc=0; grep -v -i -E "$ok_mail" "$tmp/mailsu" > "$tmp/badmails" || rc=$?
[ "$rc" -le 1 ] || die "grep failed ($rc)"
bad_mails=$(cat "$tmp/badmails")
if [ -n "$bad_ids" ]; then
  report "commit identities must be ([0-9]+\\+)?<login>@users.noreply.github.com" "$bad_ids"
  echo "  fix: git config user.email <id>+<login>@users.noreply.github.com" >&2
fi
if [ -n "$bad_mails" ]; then
  report "e-mail addresses in commit and tag messages must be noreply addresses" "$bad_mails"
fi

[ "$fail" = 0 ] && echo "privacy-check: clean ($shown)"
exit "$fail"
