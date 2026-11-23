# Developer notes

## Privacy check (pre-push hook)

This repository is public. `tools/privacy-check.sh` fails a push when the tree or the commits
being published carry personal data. It checks:

- the private patterns (see below), in tracked and untracked text files and in commit authors,
  committers and messages;
- local home paths of real users (`/home/<name>/`, `/Users/<name>/`, `C:\Users\<name>\`);
- private-key blocks and obvious tokens;
- that every commit author and committer is a `users.noreply.github.com` address.

Turn the hook on once per clone:

```sh
git config core.hooksPath .githooks
```

The private patterns are not in the repository. Put them in `.local/privacy-patterns.txt` in the
**main checkout** (the directory that holds the `.git` folder; linked worktrees use that same
file). The first line is one extended regular expression, for example
`name one|name two|mail\.example-host\.net`. `.local/` is git-ignored. The script fails closed
(exit 2) when the file is missing or empty.

Run it by hand before publishing anything unusual:

```sh
sh tools/privacy-check.sh --worktree   # files on disk, tracked and untracked
sh tools/privacy-check.sh              # the tree at HEAD
```

Binary files are not scanned; check those by hand.

## Parity table

`spec/parity.csv` is the one parity table (one row per item). Validate it with
`python tools/parity-check.py`.

Conventions: `G-nnn` ids are the C# port's `gaps.csv` ids (they cover every item in GAPS.md);
`S-<key>` rows come from the C# `settings.csv`. `phase` is the ROADMAP.md phase; track C
(distribution) rows use 7, and 0 marks rows with nothing left to schedule (already implemented).
`rust_status` is `done` only with a verified call site in `src/` or a row that GAPS.md reports
finished; implemented but not checked live is `unverified`.
