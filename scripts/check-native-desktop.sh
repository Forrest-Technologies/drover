#!/usr/bin/env bash
set -euo pipefail

app=${1:-src/Drover.Native/bin/Release/net11.0-desktop/Drover.Native.dll}
scratch=$(mktemp -d)
XDG_CONFIG_HOME="$scratch" XDG_DATA_HOME="$scratch" XDG_CACHE_HOME="$scratch" \
  dotnet "$app" >"$scratch/startup.log" 2>&1 &
pid=$!
cleanup() {
  kill "$pid" 2>/dev/null || true
  wait "$pid" 2>/dev/null || true
  rm -rf -- "$scratch"
}
trap cleanup EXIT

for _ in $(seq 1 30); do
  if ! kill -0 "$pid" 2>/dev/null; then
    break
  fi
  if window=$(timeout 3s xwininfo -name 'Connect to Drover' 2>/dev/null) && [[ $window == *IsViewable* ]]; then
    id=$(awk '/Window id:/ { print $4 }' <<<"$window")
    if colors=$(timeout 3s import -window "$id" -resize 128x -format '%k' info:) &&
      [[ $colors =~ ^[0-9]+$ ]] && (( colors > 8 )); then
      printf 'Linux startup rendered the Connect to Drover screen (%s colors).\n' "$colors"
      exit 0
    fi
  fi
  sleep 1
done

cat "$scratch/startup.log" >&2
echo 'Drover did not render the Connect to Drover screen.' >&2
exit 1
