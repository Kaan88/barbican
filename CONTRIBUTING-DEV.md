# Developer notes

## Privacy check (pre-push hook)

This repository is public. `tools/privacy-check.sh` fails a push when what is being published
carries personal data. For each pushed commit or annotated tag it checks:

- the tree, and **every line added by every commit in the range** (a leak added and removed in a
  later commit still fails), for the private patterns (see below), local home paths of real users
  (`/home/<name>/`, `/Users/<name>/`, `C:\Users\<name>\`, `/c/Users/...`, `/mnt/c/Users/...`),
  private-key blocks and obvious tokens;
- tracked files with NUL bytes (UTF-16 text hides data), unless the extension is on the binary
  allowlist in the script;
- commit and tag metadata: names, messages, trailers. Authors, committers, taggers and trailer
  e-mails must be `users.noreply.github.com` addresses (or `noreply@anthropic.com`).

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
`.local/` is git-ignored. The script exits 2 (never "clean") when the file is missing, empty or
UTF-16, when the regex is invalid, or when git or grep fails.

Run it by hand before publishing anything unusual:

```sh
sh tools/privacy-check.sh --worktree   # files on disk, tracked and untracked
sh tools/privacy-check.sh              # HEAD, plus commits not on any origin branch
sh tools/privacy-check.sh <rev> <base> # the tree at <rev> and the commits <base>..<rev>
sh tools/test-privacy-check.sh         # self-test in a throwaway repo under the temp dir
```

Binary files are not scanned; check those by hand.

## Parity table

`spec/parity.csv` is the one parity table (one row per item). Validate it with
`python tools/parity-check.py`.

Conventions: `G-nnn` ids are the C# port's `gaps.csv` ids (they cover every item in GAPS.md);
`S-<key>` rows come from the C# `settings.csv`. `phase` is the ROADMAP.md phase; track C
(distribution) rows use 7, and 0 marks rows with nothing left to schedule (already implemented).
`rust_status` is `done` only with a call site in `src/` that really reads the setting or runs the
feature (not a doc comment or a defaults table); implemented but not checked live is `unverified`.
