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
git tag -a v1 -m "release for zzpriv"
git push -q origin refs/tags/v1 > "$root/out" 2>&1; rc=$?
if [ $rc -ne 0 ] && nopush_ok refs/tags/v1; then pass=$((pass+1)); echo "ok   hook blocks an annotated tag whose message leaks"
else bad=$((bad+1)); echo "FAIL tag message leak not caught (rc=$rc)"; fi
git tag -d v1 >/dev/null
git tag -a v2 -m "release"
check 0 "clean annotated tag pushes" git push -q origin refs/tags/v2

echo
echo "privacy-check tests: $pass passed, $bad failed"
[ "$bad" = 0 ]
