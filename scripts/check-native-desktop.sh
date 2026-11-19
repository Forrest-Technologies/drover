#!/usr/bin/env bash
set -euo pipefail

app=${1:-src/Drover.Native/bin/Release/net11.0-desktop/Drover.Native.dll}
scratch=$(mktemp -d)
pid=
cleanup() {
  if [[ -n $pid ]]; then
    kill "$pid" 2>/dev/null || true
    wait "$pid" 2>/dev/null || true
  fi
  rm -rf -- "$scratch"
}
trap cleanup EXIT

# Everything the app or the test keyring writes stays in the scratch folder.
export XDG_CONFIG_HOME="$scratch" XDG_DATA_HOME="$scratch" XDG_CACHE_HOME="$scratch"

check_first_frame() {
  dotnet "$app" >"$scratch/startup.log" 2>&1 &
  pid=$!

  for _ in $(seq 1 30); do
    if ! kill -0 "$pid" 2>/dev/null; then
      break
    fi
    if window=$(timeout 3s xwininfo -name 'Connect to Drover' 2>/dev/null) && [[ $window == *IsViewable* ]]; then
      id=$(awk '/Window id:/ { print $4 }' <<<"$window")
      if colors=$(timeout 3s import -window "$id" -resize 128x -format '%k' info:) &&
        [[ $colors =~ ^[0-9]+$ ]] && (( colors > 8 )); then
        printf 'Linux startup rendered the Connect to Drover screen (%s colors).\n' "$colors"
        return 0
      fi
    fi
    sleep 1
  done

  cat "$scratch/startup.log" >&2
  echo 'Drover did not render the Connect to Drover screen.' >&2
  return 1
}

# The restart step. A sign-in is written to the system credential store, read back by a fresh opening of the store
# that shares nothing with the first but the store, as after a restart, and removed again, as on signing out. The
# store is a test Secret Service: a keyring daemon in a D-Bus session of its own, unlocked with a throwaway password.
check_sign_in_survives_restart() {
  dbus-run-session -- bash -c '
    openssl rand -hex 16 | tr -d "\n" | gnome-keyring-daemon --unlock --components=secrets >/dev/null
    dotnet test --project tests/Drover.Native.Desktop.Tests -c Release
  '
  echo 'The desktop sign-in survived a restart in the test Secret Service and was removed on sign-out.'
}

# A deep link opened while nothing answers at the chosen server shows the unreachable gate and the process
# reports that. Teardown under the virtual display can still crash after the report is written.
check_unreachable_open() {
  local report="$scratch/unreachable-open.txt"
  export DROVER_UNREACHABLE_OPEN_CHECK=1
  export DROVER_UNREACHABLE_OPEN_REPORT="$report"
  set +e
  dotnet "$app" >"$scratch/unreachable.log" 2>&1
  set -e
  cat "$scratch/unreachable.log" >&2
  if [[ -f $report ]] && grep -F 'Unreachable open check passed' "$report"; then
    echo 'A deep link opened while the server was down showed the unreachable gate.'
    return 0
  fi
  echo 'The unreachable open check did not pass.' >&2
  return 1
}

# Email, password, and address fields carry the platform's own hints, and a paste past a limit is cut with no error.
check_field_input() {
  local report="$scratch/field-input.txt"
  export DROVER_FIELD_INPUT_CHECK=1
  export DROVER_FIELD_INPUT_REPORT="$report"
  set +e
  dotnet "$app" >"$scratch/field-input.log" 2>&1
  set -e
  cat "$scratch/field-input.log" >&2
  if [[ -f $report ]] && grep -F 'Field input check passed' "$report"; then
    echo 'Desktop fields carried their input scopes, and a paste past the limit was cut with no error.'
    return 0
  fi
  echo 'The field input check did not pass.' >&2
  return 1
}

check_first_frame
check_sign_in_survives_restart
check_unreachable_open
check_field_input
