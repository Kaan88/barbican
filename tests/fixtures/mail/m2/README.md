# M2 mail fixtures

The Rust tests ported in M2 carry their inputs inline (headers, mbox text, ICS text, JSON), so no file fixture was needed in stage F. The synthetic
TNEF (winmail.dat) is built in code by `tests/Barbican.FakeJmap/FakeTnef.cs`, never stored as a binary. A stream that needs a file fixture adds it here
(`example.org` / `.example` addresses and sample text only; scan any binary for local paths before committing).
