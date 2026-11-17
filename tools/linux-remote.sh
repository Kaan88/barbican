#!/usr/bin/env bash
# Runs ON the Linux VM, sent by tools/linux.ps1 (not meant to be run on Windows).
#   linux-remote.sh setup
#   linux-remote.sh run <branch> <sha> <build|test|gen-tokens> [tools/build.sh args...]
#   linux-remote.sh setup-ui                 packages for running the app head under Xvfb (WebKitGTK)
#   linux-remote.sh xrun <branch> <sha> <project> <out-name> [timeout-s] [configuration] [script]
#        build <project> for net10.0-desktop, run it under its own Xvfb display (never a real
#        screen), results in ~/barbican-out/<out-name> (BARBICAN_SPIKE_OUT). BARBICAN_XVFB=1
#        tells the App it is on a private display (its hidden mode refuses any other display).
#        script: (none) run once; smoke = App --smoke <out>/smoke.json; auto = the Automation
#        build's idle scenario over its loopback port (wshot, probe-start x11, 2 s, probe-stop, close)
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
  setup-ui)
    # Uno Skia X11 + the WebView2 control (a GTK WebKit.WebView) + a virtual display. Installed
    # with apt only when missing (passwordless sudo on the VM); listed in docs/adr/0005.
    missing=""
    for pkg in xvfb xauth libwebkit2gtk-4.1-0 libgtk-3-0 libfontconfig1 fonts-dejavu-core; do
      dpkg -s "$pkg" >/dev/null 2>&1 || missing="$missing $pkg"
    done
    if [ -n "$missing" ]; then
      echo "[vm] installing:$missing"
      sudo -n apt-get install -y --no-install-recommends $missing
    fi
    echo "[vm] setup-ui done"
    ;;
  xrun)
    branch=$1 sha=$2 project=$3 outname=$4 limit=${5:-300} config=${6:-Debug} script=${7:-}
    [ -d "$clone/.git" ] || git clone -q "$repo_url" "$clone"
    cd "$clone"
    git fetch -q origin "+refs/heads/$branch:refs/remotes/origin/$branch"
    git checkout -q --force --detach "$sha"
    echo "[vm] $(git log -1 --format='%h %s') ($branch)"
    BARBICAN_BUILD_SLOTS=1 BARBICAN_MSBUILD_CPU=2 bash tools/build.sh build -c "$config" --project "$project" -- -p:BarbicanHead=desktop
    # The app's entry assembly is the one with a runtimeconfig (AssemblyName may differ from the project).
    cfg=$(ls -t "$(dirname "$project")"/bin/"$config"/net10.0-desktop/*.runtimeconfig.json 2>/dev/null | head -1)
    dll="${cfg%.runtimeconfig.json}.dll"
    [ -n "$cfg" ] && [ -f "$dll" ] || { echo "[vm] no build output for $project" >&2; exit 4; }
    out="$HOME/barbican-out/$outname"
    rm -rf "$out"; mkdir -p "$out"
    disp=:97
    Xvfb $disp -screen 0 1280x800x24 -nolisten tcp >"$out/xvfb.log" 2>&1 &
    xpid=$!
    trap 'kill $xpid 2>/dev/null || true' EXIT
    sleep 1
    export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
    # Uno 6.7's Linux WebView loads unversioned names ("libwebkit2gtk-4.1.so", "libgdk-3.so", ...)
    # that only the -dev packages provide, and falls back to WebKitGTK 4.0 (gone from Debian 13).
    # Private symlinks on LD_LIBRARY_PATH instead of the dev packages (docs/adr/0005).
    shim="$HOME/.barbican-lib"
    mkdir -p "$shim"
    for lib in libwebkit2gtk-4.1 libjavascriptcoregtk-4.1 libgdk-3 libgtk-3 libsoup-3.0 libglib-2.0 libgobject-2.0 libgio-2.0; do
      real=$(ls /usr/lib/*-linux-gnu/$lib.so.0 2>/dev/null | head -1)
      [ -n "$real" ] && ln -sf "$real" "$shim/$lib.so"
    done
    export LD_LIBRARY_PATH="$shim${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
    export DISPLAY=$disp GDK_BACKEND=x11 BARBICAN_XVFB=1 BARBICAN_SPIKE_OUT="$out" BARBICAN_PROFILE="xrun-$outname"
    set +e
    case "$script" in
      smoke)
        timeout "$limit" nice -n 10 dotnet "$dll" --smoke "$out/smoke.json" >"$out/stdout.txt" 2>"$out/stderr.txt"
        code=$?
        echo "[vm] smoke exit $code"
        cat "$out/smoke.json" 2>/dev/null || echo "[vm] no smoke report"
        ;;
      auto|auto-m2)
        port=8811
        # Fable F-08: a per-run token; every connection proves it first ("auth <token>" -> "ok auth")
        token=$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')
        # auto-m2 (M2-plan §8): the sample FakeJmap seed, so menus, a dialog over the body and the drop path have mail to act on
        sample=""; [ "$script" = "auto-m2" ] && sample=1
        BARBICAN_SAMPLE=$sample BARBICAN_AUTOMATION_TOKEN=$token BARBICAN_AUTOMATION_PORT=$port timeout "$limit" nice -n 10 dotnet "$dll" >"$out/stdout.txt" 2>"$out/stderr.txt" &
        app=$!
        send() {
          exec 3<>"/dev/tcp/127.0.0.1/$port" || return 1
          printf 'auth %s\n' "$token" >&3
          IFS= read -r -t 10 authed <&3
          [ "$authed" = "ok auth" ] || { exec 3<&- 3>&-; return 1; }
          printf '%s\n' "$1" >&3
          IFS= read -r -t 60 reply <&3
          exec 3<&- 3>&-
          printf '%s\n' "$reply"
        }
        up=""
        for _ in $(seq 1 120); do
          kill -0 $app 2>/dev/null || break
          if up=$(send title 2>/dev/null) && [ -n "$up" ]; then break; fi
          sleep 0.5
        done
        if [ -z "$up" ]; then
          echo "[vm] automation server did not come up"
        else
          echo "size: $(send size)"
          echo "wshot: $(send "wshot $out/wshot.png")"
          echo "probe-start: $(send 'probe-start x11')"
          sleep 2
          probe=$(send probe-stop)
          printf '%s\n' "$probe" >"$out/idle-probe.json"
          case "$probe" in *'"frames":[]'*) echo "probe-stop: 0 frames in 2 s idle" ;; *) echo "probe-stop: FRAMES while idle: ${probe:0:300}" ;; esac
          if [ "$script" = "auto-m2" ]; then
            # M2 verify: a context menu, a dialog over the body (the X11 stand-in image, ADR 0005), the palette and a simulated drop
            for c in "select #0" "menu-open MENU-MAIL 0" "menu-items" "wshot $out/menu.png" "key Escape" \
                     "dialog-open confirm Verify" "dialog-state" "wshot $out/dialog.png" "dialog-answer cancel" "dialog-state" \
                     "palette" "key Escape" "dnd-sim #1 folder:archive" "toast-last" "act mail.undo" "toast-last" \
                     "search from:carol has:attachment" "chips" "search" "unified on" "unified state" "plugins"; do
              echo "$c: $(send "$c")"
            done
          fi
          echo "popup-guard: $(send popup-guard)"
          echo "close: $(send close)"
        fi
        for _ in $(seq 1 40); do kill -0 $app 2>/dev/null || break; sleep 0.25; done
        kill $app 2>/dev/null
        wait $app
        code=$?
        echo "[vm] app exit $code"
        ;;
      *)
        timeout "$limit" nice -n 10 dotnet "$dll" >"$out/stdout.txt" 2>"$out/stderr.txt"
        code=$?
        echo "[vm] exit $code"
        grep -E '^\[spike\]' "$out/stdout.txt" || tail -20 "$out/stdout.txt"
        ;;
    esac
    set -e
    tail -5 "$out/stderr.txt"
    exit 0
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
    echo "usage: linux-remote.sh setup | setup-ui | run <branch> <sha> <target> [args] | xrun <branch> <sha> <project> <out> [timeout]" >&2
    exit 2
    ;;
esac
