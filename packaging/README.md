# packaging/

Carried over from the Rust app (PORT-PLAN.md §2.2) for the end-of-project packages (docs/adr/0003):

- `icons/`: the app icon (SVG source, PNG sizes, ICO).
- `linux/barbican.desktop`, `linux/debian/postinst`, `linux/debian/postrm`: Linux desktop entry and
  Debian maintainer scripts.
- `linux/install-kind`, `msi/install-kind`: markers that tell the updater whether it may update
  itself (self-updating builds) or must only notify (package-managed installs).
- `wix/main.wxs`: the Rust app's WiX MSI source, as reference for the per-machine MSI in M8.

Until M8 the only package is a Windows portable zip.
