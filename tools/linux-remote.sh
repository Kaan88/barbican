#!/usr/bin/env bash
# Runs ON the Linux VM, sent by tools/linux.ps1 (not meant to be run on Windows).
#   linux-remote.sh setup
#   linux-remote.sh run <branch> <sha> <build|test|gen-tokens> [tools/build.sh args...]
# One VM job at a time (flock on ~/.barbican-vm.lock), so two agents never fight over the
# shared checkout in ~/barbican. Builds go through tools/build.sh with one slot and -m:2.
set -euo pipefail

repo_url=https://github.com/Kaan88/barbican.git
clone="$HOME/barbican"
mode=${1:-}
[ $# -gt 0 ] && shift

exec 8>"$HOME/.barbican-vm.lock"
if ! flock -n 8; then
  echo "[vm] another Barbican job is running on the VM; waiting..."
  flock 8
fi

case "$mode" in
  setup)
    # .NET SDK under the user only (no system-wide install). Channel 10.0, latest patch.
    if [ ! -x "$HOME/.dotnet/dotnet" ]; then
      curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$HOME/dotnet-install.sh"
      bash "$HOME/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet" --no-path
    fi
    "$HOME/.dotnet/dotnet" --list-sdks
    # Packages .NET needs at runtime. Only reported here: installing them is a system change the
    # user approves (CLAUDE.md). Uno/Skia/WebKitGTK packages are added with spike S1/S2.
    missing=""
    for pkg in ca-certificates git curl; do
      dpkg -s "$pkg" >/dev/null 2>&1 || missing="$missing $pkg"
    done
    # .NET needs the ICU runtime library, not a particular package name (libicu76 on Debian 13).
    ldconfig -p 2>/dev/null | grep -q 'libicuuc\.so' || missing="$missing libicu-dev"
    if [ -n "$missing" ]; then
      echo "[vm] missing packages:$missing"
      if sudo -n true 2>/dev/null; then
        sudo -n apt-get install -y --no-install-recommends $missing
      else
        echo "[vm] run on the VM: sudo apt-get install -y$missing"
      fi
    fi
    [ -d "$clone/.git" ] || git clone -q "$repo_url" "$clone"
    echo "[vm] setup done"
    ;;
  run)
    branch=$1 sha=$2 target=$3
    shift 3
    if [ ! -x "$HOME/.dotnet/dotnet" ]; then
      echo "[vm] no .NET SDK in ~/.dotnet: run tools/linux.ps1 setup first" >&2
      exit 3
    fi
    [ -d "$clone/.git" ] || git clone -q "$repo_url" "$clone"
    cd "$clone"
    git fetch -q origin "+refs/heads/$branch:refs/remotes/origin/$branch"
    git checkout -q --force --detach "$sha"
    echo "[vm] $(git log -1 --format='%h %s') ($branch)"
    BARBICAN_BUILD_SLOTS=1 BARBICAN_MSBUILD_CPU=2 bash tools/build.sh "$target" "$@"
    ;;
  *)
    echo "usage: linux-remote.sh setup | run <branch> <sha> <target> [args]" >&2
    exit 2
    ;;
esac
