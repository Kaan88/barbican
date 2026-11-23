# Developer notes

## Privacy check (pre-push hook)

This repository is public. `tools/privacy-check.sh` fails a push when what is being published
carries personal data. For each pushed commit or annotated tag it checks:

- the tree, and **every line and every blob added by every commit in the range** (a leak added and
  removed in a later commit still fails, including a UTF-16 file whose NUL bytes are stripped and
  rescanned), for the private patterns (see below), local home paths of real users
  (`/home/<name>/`, `/Users/<name>/`, `C:\Users\<name>\`, `/c/Users/...`, `/mnt/c/Users/...`),
  private-key blocks and obvious tokens; merge commits are scanned as combined diffs (with any
  number of parents, octopus included), so content introduced while resolving a merge is caught
  too;
- tracked files with NUL bytes (UTF-16 text hides data), unless the extension is on the binary
  allowlist in the script. The bytes are tested directly, so a `*.txt diff` line in
  `.gitattributes` cannot make a UTF-16 file pass as text;
- commit and tag metadata: names, messages and every e-mail address in them. The whole address of
  an author, committer, tagger, or of any e-mail written in a commit message body or a tag message
  must be `([0-9]+\+)?<login>@users.noreply.github.com` (or `noreply@anthropic.com`); a mere
  suffix match does not count. An annotated tag is peeled through its whole chain of tag objects,
  and each object's message (also when it is UTF-16) and tagger are checked.

Turn the hook on once per clone (the hook files must keep their exec bit):

```sh
git config core.hooksPath .githooks
```

The hook reads git's `local_ref local_sha remote_ref remote_sha` lines. Deletions are skipped. A
new ref is checked against what the remote already has; an existing ref from its old sha.

The private patterns are not in the repository. Put them in `.local/privacy-patterns.txt` in the
**main checkout** (the directory that holds the `.git` folder; linked worktrees use that same
file). The first line is one extended regular expression, for example
`name one|name two|mail\.example-host\.net`, saved as UTF-8 (a BOM and CRLF are tolerated).
Matching is case-insensitive for ASCII only, so list every spelling and case variant of a name that
must be caught (for example the Turkish dotted/dotless i forms).
`.local/` is git-ignored. The script exits 2 (never "clean") when the file is missing, empty or
UTF-16, when the regex is invalid, or when git or grep fails.

Run it by hand before publishing anything unusual:

```sh
sh tools/privacy-check.sh --worktree   # files on disk, tracked and untracked
sh tools/privacy-check.sh              # HEAD, plus commits not on any origin branch
sh tools/privacy-check.sh <rev> <base> # the tree at <rev> and the commits <base>..<rev>
sh tools/test-privacy-check.sh         # self-test in a throwaway repo under the temp dir
```

Binary files are not searched as text: a NUL-containing blob added in a push is only rescanned
after its NUL bytes are stripped, which does not decode compressed formats. Check binaries by hand.

## Parity table

`spec/parity.csv` is the one parity table (one row per item). Validate it with
`python tools/parity-check.py`.

Conventions: `G-nnn` ids are the C# port's `gaps.csv` ids (they cover every item in GAPS.md);
`S-<key>` rows come from the C# `settings.csv`. `phase` is the ROADMAP.md phase; track C
(distribution) rows use 7, and 0 marks rows with nothing left to schedule (already implemented).
`rust_status` is `done` only with a call site in `src/` that really reads the setting or runs the
feature (not a doc comment or a defaults table); implemented but not checked live is `unverified`.
