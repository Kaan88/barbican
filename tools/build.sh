#!/usr/bin/env bash
# tools/build.sh: the Linux counterpart of tools/build.ps1 (PORT-PLAN.md §6.3). On the Linux VM it
# is run by tools/linux.ps1; run it by hand only on a Linux machine.
#
#   tools/build.sh [build|test|restore|clean|gen-tokens|rules] [--project P] [-c Debug|Release]
#                  [--class "*Pattern"] [--method "*Pattern"] [--check] [-- extra dotnet args]
#
# Every dotnet call takes one of BARBICAN_BUILD_SLOTS (default 2) machine-wide slots (flock on
# lock files: a crashed build frees its slot automatically), runs under nice -n 10 and uses
# -m:${BARBICAN_MSBUILD_CPU:-2} with the compiler in-process.
set -euo pipefail

target=${1:-build}
[ $# -gt 0 ] && shift
root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
project="$root/Barbican.slnx"
config=Debug
class=""
method=""
check=""
extra=()
while [ $# -gt 0 ]; do
  case "$1" in
    --project) project=$2; shift 2 ;;
    -c|--configuration) config=$2; shift 2 ;;
    --class) class=$2; shift 2 ;;
    --method) method=$2; shift 2 ;;
    --check) check=--check; shift ;;
    --) shift; extra+=("$@"); break ;;
    *) extra+=("$1"); shift ;;
  esac
done
case "$project" in /*) ;; *) project="$root/$project" ;; esac

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_UI_LANGUAGE=en
if [ -x "$HOME/.dotnet/dotnet" ]; then
  export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
  export PATH="$HOME/.dotnet:$PATH"
fi

slots=${BARBICAN_BUILD_SLOTS:-2}
cpu=${BARBICAN_MSBUILD_CPU:-2}
lockdir="${XDG_RUNTIME_DIR:-/tmp}/barbican-build-$(id -u)"
mkdir -p "$lockdir"

# Real input is forbidden on the user's machines (CLAUDE.md); xdotool belongs to throwaway VMs only.
rules() {
  local pattern='\b(SendInput|keybd_event|mouse_event|SetCursorPos|InputInjector)\b|FlaUI\.Core\.Input\.(Mouse|Keyboard)|xdotool'
  if grep -rnE "$pattern" "$root/src" "$root/tests" "$root/tools" \
      --include='*.cs' --include='*.xaml' --include='*.ps1' --include='*.sh' --include='*.py' --include='*.csproj' \
      --exclude=build.ps1 --exclude=build.sh --exclude-dir=bin --exclude-dir=obj; then
    echo "[build.sh] real-input API found (forbidden; see CLAUDE.md)" >&2
    exit 1
  fi
  echo "[build.sh] rules: no real-input APIs"
}

take_slot() {
  local said=0 i fd
  while :; do
    for ((i = 0; i < slots; i++)); do
      exec {fd}>"$lockdir/slot-$i.lock"
      if flock -n "$fd"; then
        echo "[build.sh] build slot $((i + 1))/$slots taken"
        return 0
      fi
      exec {fd}>&-
    done
    if [ $said = 0 ]; then echo "[build.sh] all $slots build slots busy; waiting (BARBICAN_BUILD_SLOTS)..."; said=1; fi
    sleep 2
  done
}

dn() {
  echo "[build.sh] dotnet $*"
  nice -n 10 dotnet "$@"
}

common=(-c "$config" "-m:$cpu" -p:UseSharedCompilation=false)

case "$target" in
  rules) rules; exit 0 ;;
  test) rules ;;
  build|restore|clean|gen-tokens) ;;
  *) echo "unknown target: $target" >&2; exit 2 ;;
esac

take_slot
case "$target" in
  restore) dn restore "$project" "${extra[@]}" ;;
  build) dn build "$project" "${common[@]}" "${extra[@]}" ;;
  clean) dn clean "$project" -c "$config" "${extra[@]}" ;;
  test)
    dn build "$project" "${common[@]}"
    case "$project" in
      *.slnx|*.sln) args=(test --solution "$project") ;;
      *) args=(test --project "$project") ;;
    esac
    args+=(-c "$config" --no-build)
    [ -n "$class" ] && args+=(--filter-class "$class")
    [ -n "$method" ] && args+=(--filter-method "$method")
    dn "${args[@]}" "${extra[@]}"
    ;;
  gen-tokens)
    dn run --project "$root/tools/Barbican.GenTokens/Barbican.GenTokens.csproj" -c "$config" -- --root "$root" $check "${extra[@]}"
    ;;
esac
