#!/bin/sh
# tools/test-privacy-check.sh: tests tools/privacy-check.sh and .githooks/pre-push in a throwaway
# repository under the system temp dir (nothing in this checkout or its .local is touched).
# Exit 0 when every case behaves as expected.
set -u
src=$(cd "$(dirname "$0")/.." && pwd)
root=$(mktemp -d "${TMPDIR:-/tmp}/privtest.XXXXXX") || exit 2
trap 'rm -rf "$root"' EXIT
pass=0; bad=0

# Test data is assembled at run time so this file itself holds no token, key or home path.
tok="gh""p_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
key="-----BEGIN ""RSA PRIVATE KEY-----"
winhome='C:\Us''ers\jsmith\x'
unixhome="/ho""me/jsmith/x"
mnthome="/mnt/c/US""ERS/jsmith/x"
gitbashhome="/c/Us""ers/jsmith/x"

check() { # <expected exit> <name> <command...>
  want=$1; name=$2; shift 2
  "$@" > "$root/out" 2>&1; got=$?
  if [ "$got" = "$want" ]; then pass=$((pass+1)); echo "ok   $name"
  else bad=$((bad+1)); echo "FAIL $name (exit $got, wanted $want)"; sed 's/^/     | /' "$root/out"; fi
}
nopush_ok() { # a push that must be refused leaves the ref off the remote
  if git --git-dir="$root/remote.git" rev-parse -q --verify "$1" >/dev/null 2>&1; then return 1; fi
}

# --- a throwaway repo with the hook enabled and a bare "origin" ----------------------------------
git init -q --bare "$root/remote.git"
git init -q -b main "$root/work"
cd "$root/work" || exit 2
git config user.name tester
git config user.email "1+tester@users.noreply.github.com"
git config commit.gpgsign false
git config tag.gpgsign false
git config core.autocrlf false
mkdir tools .githooks .local
cp "$src/tools/privacy-check.sh" tools/
cp "$src/.githooks/pre-push" .githooks/
git config core.hooksPath .githooks
git remote add origin "$root/remote.git"
printf '.local/\n' > .gitignore
pat=.local/privacy-patterns.txt
printf 'zzpriv|fake\\.host\\.invalid\n' > $pat
script="sh tools/privacy-check.sh"
commit() { git add -A && git commit -q -m "${1:-c}" ${2:+-m "$2"}; }
echo base > a.txt; commit base
check 0 "push of a clean branch" git push -q origin main
cp $pat "$root/pat.good"

# --- exit 2: the checker cannot check ------------------------------------------------------------
rm $pat
check 2 "missing pattern file fails closed" $script --worktree
printf '(unclosed\n' > $pat
check 2 "invalid regex exits 2" $script --worktree
printf 'z\0z\0p\0\n\0' > $pat
check 2 "UTF-16 pattern file exits 2" $script --worktree
printf '\357\273\277  zzpriv  \r\n' > $pat
echo "has zzpriv inside" > leak.txt
check 1 "BOM, CRLF and spaces in the pattern line are tolerated" $script --worktree
rm leak.txt
cp "$root/pat.good" $pat

# --- worktree scans (each leak alone must fail, each placeholder must pass) ----------------------
leak() { # <name> <content>
  printf '%s\n' "$2" > leak.txt
  check 1 "worktree: $1" $script --worktree
  rm leak.txt
}
leak "private pattern" "mail zzpriv here"
leak "private pattern, other case" "mail ZZPRIV here"
leak "Windows home path" "$winhome"
leak "Windows home path, forward slashes" "C:/Us""ers/jsmith/x"
leak "Unix home path" "$unixhome"
leak "/mnt/c/Users path, upper case" "$mnthome"
leak "Git Bash /c/Users path" "$gitbashhome"
leak "token" "$tok"
leak "private key block" "$key"
printf '%s\n' 'C:\Us''ers\user\x /ho''me/<name>/ /Us''ers/Public/x /ho''me/a /Home/End' > ok.txt
check 0 "worktree: placeholders and /Home/End pass" $script --worktree
rm ok.txt
printf 'a\0b\n' > data.txt
check 1 "worktree: NUL file off the allowlist" $script --worktree
rm data.txt
printf 'a\0b' > icon.PNG
check 0 "worktree: NUL file with an allowlisted extension" $script --worktree
rm icon.PNG
printf 'z\0z\0p\0\n\0r\0i\0v\0' > u16.txt
check 1 "worktree: UTF-16 file is rejected" $script --worktree
rm u16.txt

# --- the checker must never read the hook's stdin (remaining ref lines) ---------------------------
# A checker that reads stdin must not swallow the next pushed ref's line. The stub makes the
# consumption visible; the real checker does not read stdin, so the fix is the hook's </dev/null.
cp tools/privacy-check.sh "$root/privacy-check.real"
cat > tools/privacy-check.sh <<EOF
#!/bin/sh
cat >/dev/null
printf '%s %s\n' "\$1" "\$2" >> '$root/stub.log'
exit 0
EOF
: > "$root/stub.log"
zero=0000000000000000000000000000000000000000
printf 'refs/heads/one aaaa refs/heads/one %s\nrefs/heads/two cccc refs/heads/two %s\n' "$zero" "$zero" > "$root/refs"
sh .githooks/pre-push origin < "$root/refs" > "$root/out" 2>&1; rc=$?
cp "$root/privacy-check.real" tools/privacy-check.sh
if [ "$rc" = 0 ] && [ "$(grep -c . "$root/stub.log")" = 2 ] && grep -q '^aaaa ' "$root/stub.log" && grep -q '^cccc ' "$root/stub.log"; then
  pass=$((pass+1)); echo "ok   hook gives the checker an empty stdin (both refs still checked)"
else bad=$((bad+1)); echo "FAIL hook lost a ref line (rc=$rc)"; sed 's/^/     | /' "$root/stub.log"; fi

# --- the pre-push hook: real pushes ---------------------------------------------------------------
echo more > b.txt; commit "clean change"
check 0 "push of a clean update (base = remote sha)" git push -q origin main

echo "$tok" > t.txt; commit "add token"
git rm -q t.txt; commit "remove token again"
check 1 "add then delete in a later commit is still caught" $script HEAD "$(git rev-parse origin/main)"
before=$(git --git-dir="$root/remote.git" rev-parse refs/heads/main)
git push -q origin main > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && [ "$before" = "$(git --git-dir="$root/remote.git" rev-parse refs/heads/main)" ]; then pass=$((pass+1)); echo "ok   hook blocks the push of add-then-delete"
else bad=$((bad+1)); echo "FAIL hook let add-then-delete through (rc=$rc)"; fi
git reset -q --hard origin/main

echo "x" > c.txt; commit "x" "Co-Authored-By: Eve <eve@real.test>"
git push -q origin main > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ]; then pass=$((pass+1)); echo "ok   hook blocks a non-noreply trailer e-mail"
else bad=$((bad+1)); echo "FAIL trailer e-mail not caught"; fi
git reset -q --hard origin/main

echo "x" > c.txt; commit "message with $tok"
git push -q origin main > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ]; then pass=$((pass+1)); echo "ok   hook blocks a token in a commit message"
else bad=$((bad+1)); echo "FAIL message token not caught"; fi
git reset -q --hard origin/main

echo "x" > c.txt; git add -A; git -c user.email=eve@real.test commit -q -m "wrong author"
git push -q origin main > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ]; then pass=$((pass+1)); echo "ok   hook blocks a non-noreply author"
else bad=$((bad+1)); echo "FAIL author not caught"; fi
git reset -q --hard origin/main

echo "mail zzpriv" > c.txt; commit "leak"
git push -q origin HEAD:refs/heads/newbranch > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/heads/newbranch; then pass=$((pass+1)); echo "ok   hook blocks a leak on a NEW branch"
else bad=$((bad+1)); echo "FAIL leak reached the new branch (rc=$rc)"; fi
# the leak is now only local; a clean sibling branch from origin/main must still push
git checkout -q -b clean origin/main; echo ok > d.txt; commit "clean sibling"
check 0 "clean new branch pushes (range = not on the remote)" git push -q origin clean
git checkout -q main
check 0 "deleting a remote branch is not scanned" git push -q origin :refs/heads/clean

git reset -q --hard origin/main

# --- colored diffs, UTF-16 range blobs, merge commits, orphan roots -------------------------------
git config color.diff always
echo "mail zzpriv" > col.txt; commit "colored add"
git rm -q col.txt; commit "colored remove"
check 1 "color.diff=always does not hide an added-then-deleted pattern" $script HEAD "$(git rev-parse origin/main)"
git config --unset color.diff
git reset -q --hard origin/main

printf 'z\0z\0p\0r\0i\0v\0\n\0' > u16.txt; commit "utf-16 add"
git rm -q u16.txt; commit "utf-16 remove"
check 1 "UTF-16 file added then removed in the range" $script HEAD "$(git rev-parse origin/main)"
git reset -q --hard origin/main

printf '*.txt diff\n' > .gitattributes
printf 'h\0e\0l\0l\0o\0\n\0' > notes.txt
commit "UTF-16 notes.txt with a diff attribute"
check 1 "NUL bytes are tested despite '*.txt diff' in .gitattributes" $script HEAD "$(git rev-parse origin/main)"
git reset -q --hard origin/main

git config log.showRoot false
o=$(printf 'clean\n' | git hash-object -w --stdin)
leak=$(printf 'mail zzpriv\n' | git hash-object -w --stdin)
t1=$(printf '100644 blob %s\to.txt\n100644 blob %s\tleak.txt\n' "$o" "$leak" | git mktree)
c1=$(git commit-tree "$t1" -m "orphan root with the leak")
t2=$(printf '100644 blob %s\to.txt\n' "$o" | git mktree)
c2=$(git commit-tree "$t2" -p "$c1" -m "orphan cleanup")
git push -q origin "$c2:refs/heads/orphan-leak" > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/heads/orphan-leak; then pass=$((pass+1)); echo "ok   orphan root diff is scanned with log.showRoot=false"
else bad=$((bad+1)); echo "FAIL orphan root leak not caught (rc=$rc)"; fi
git config --unset log.showRoot

git checkout -q -b merge-side
echo side > s.txt; commit "merge side"
git checkout -q main
echo main2 > m.txt; commit "main before merge"
git merge -q --no-commit merge-side >/dev/null 2>&1 || true
printf 'mail zzpriv\n' > evil.txt
git add -A; git commit -q -m "evil merge"
git rm -q evil.txt; commit "remove the evil file again"
before=$(git --git-dir="$root/remote.git" rev-parse refs/heads/main)
git push -q origin main > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && [ "$before" = "$(git --git-dir="$root/remote.git" rev-parse refs/heads/main)" ]; then pass=$((pass+1)); echo "ok   content introduced by a merge commit is caught"
else bad=$((bad+1)); echo "FAIL evil merge content not caught (rc=$rc)"; fi
git reset -q --hard origin/main
git branch -D merge-side >/dev/null

echo x > e.txt; commit "trailer" "Reported-by: eve@real.test"
check 1 "trailer e-mail without angle brackets" $script HEAD "$(git rev-parse origin/main)"
git reset -q --hard origin/main

# --- octopus merges, log.diffMerges, renamed scripts, message-body e-mails -------------------------
p1=$(git rev-parse HEAD)
p2=$(git commit-tree "$(git rev-parse HEAD^{tree})" -p "$p1" -m "octopus side 2")
p3=$(git commit-tree "$(git rev-parse HEAD^{tree})" -p "$p1" -m "octopus side 3")
leak=$(printf 'mail zzpriv\n' | git hash-object -w --stdin)
{ git ls-tree HEAD; printf '100644 blob %s\tleak.txt\n' "$leak"; } | git mktree > "$root/octotree"
merge=$(git commit-tree "$(cat "$root/octotree")" -p "$p1" -p "$p2" -p "$p3" -m "octopus merge with leak")
clean=$(git commit-tree "$(git rev-parse HEAD^{tree})" -p "$merge" -m "drop the octopus leak")
git push -q origin "$clean:refs/heads/octopus-leak" > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/heads/octopus-leak; then pass=$((pass+1)); echo "ok   content added by a 3-parent (octopus) merge is caught"
else bad=$((bad+1)); echo "FAIL octopus merge leak not caught (rc=$rc)"; fi

git config log.diffMerges combined
u16=$(printf 'z\0z\0p\0r\0i\0v\0\n\0' | git hash-object -w --stdin)
{ git ls-tree HEAD; printf '100644 blob %s\tu16.txt\n' "$u16"; } | git mktree > "$root/u16tree"
merge=$(git commit-tree "$(cat "$root/u16tree")" -p "$p1" -p "$p2" -m "merge adds a UTF-16 file")
clean=$(git commit-tree "$(git rev-parse HEAD^{tree})" -p "$merge" -m "drop the UTF-16 file")
git push -q origin "$clean:refs/heads/utf16-merge" > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/heads/utf16-merge; then pass=$((pass+1)); echo "ok   merge-added UTF-16 blob is caught with log.diffMerges=combined"
else bad=$((bad+1)); echo "FAIL merge-added UTF-16 blob not caught (rc=$rc)"; fi
git config --unset log.diffMerges

git mv tools/privacy-check.sh tools/renamed.sh
printf 'x %s\n' "$unixhome" >> tools/renamed.sh
commit "rename the privacy script"
git rm -q tools/renamed.sh; commit "drop the renamed script"
mkdir -p tools
git show origin/main:tools/privacy-check.sh > tools/privacy-check.sh || exit 2
check 1 "a renamed privacy script does not inherit the home-path exemption" $script HEAD "$(git rev-parse origin/main)"
rm -f tools/privacy-check.sh
git reset -q --hard origin/main

echo x > nr.txt; git add -A
git -c user.email='eve@example.test+1@users.noreply.github.com' commit -q -m "suffix-only author"
check 1 "noreply rule anchors the whole address (author/committer)" $script HEAD "$(git rev-parse origin/main)"
git reset -q --hard origin/main

echo x > body.txt; commit "note" "contact eve@example.test for details"
check 1 "e-mail anywhere in a commit message body" $script HEAD "$(git rev-parse origin/main)"
git reset -q --hard origin/main

git tag -a v1 -m "release for zzpriv"
git push -q origin refs/tags/v1 > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/tags/v1; then pass=$((pass+1)); echo "ok   hook blocks an annotated tag whose message leaks"
else bad=$((bad+1)); echo "FAIL tag message leak not caught (rc=$rc)"; fi
git tag -d v1 >/dev/null
git tag -a v2 -m "release"
check 0 "clean annotated tag pushes" git push -q origin refs/tags/v2

git tag -a inner -m "inner release for zzpriv"
git -c advice.nestedTag=false tag -a outer -m "outer release" inner
git push -q origin refs/tags/outer > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/tags/outer; then pass=$((pass+1)); echo "ok   pattern in an inner tag object is caught"
else bad=$((bad+1)); echo "FAIL inner tag leak not caught (rc=$rc)"; fi
git tag -d outer inner >/dev/null

c=$(git rev-parse HEAD)
tagobj=$({ printf 'object %s\ntype commit\ntag u16tag\ntagger tester <1+tester@users.noreply.github.com> 0 +0000\n\n' "$c"
           printf 'r\0e\0l\0e\0a\0s\0e\0 \0z\0z\0p\0r\0i\0v\0\n\0'; } | git hash-object -t tag -w --stdin --literally)
check 1 "tag object with a UTF-16 (NUL-byte) message" $script "$tagobj"

git tag -a mailtag -m "questions to eve@example.test"
check 1 "e-mail in a tag message" $script mailtag
git tag -d mailtag >/dev/null

git -c user.email='eve@example.test+1@users.noreply.github.com' tag -a suffix-tag -m "release"
check 1 "noreply rule anchors the whole address (tagger)" $script suffix-tag
git tag -d suffix-tag >/dev/null

echo
echo "privacy-check tests: $pass passed, $bad failed"
[ "$bad" = 0 ]
