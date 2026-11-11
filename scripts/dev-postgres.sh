#!/usr/bin/env bash
# Starts a throwaway PostgreSQL for tests on machines without Docker
# (for example cloud coding sessions). With Docker available you do not
# need this: the test fixture starts its own container.
#
#   scripts/dev-postgres.sh start    # prints the line to export
#   scripts/dev-postgres.sh stop     # stops it and deletes its data
#
# The data test fixture uses DROVER_TEST_POSTGRES when it is set and
# falls back to a container when it is not.
set -euo pipefail

PORT="${DROVER_PG_PORT:-54329}"
DATA="${DROVER_PG_DIR:-/tmp/drover-dev-postgres}"

find_bin() {
  if command -v pg_ctl >/dev/null 2>&1; then
    dirname "$(command -v pg_ctl)"
    return
  fi
  local candidate
  candidate="$(ls -d /usr/lib/postgresql/*/bin 2>/dev/null | sort -V | tail -1 || true)"
  if [ -z "$candidate" ]; then
    echo "PostgreSQL server binaries were not found. Install PostgreSQL 16 or later, or use Docker." >&2
    exit 1
  fi
  echo "$candidate"
}

# PostgreSQL refuses to run as root, so hand off to the postgres user when needed.
as_postgres() {
  if [ "$(id -u)" -eq 0 ]; then
    su postgres -s /bin/bash -c "$1"
  else
    bash -c "$1"
  fi
}

BIN="$(find_bin)"

case "${1:-}" in
  start)
    if [ ! -d "$DATA/data" ]; then
      mkdir -p "$DATA"
      if [ "$(id -u)" -eq 0 ]; then
        chown postgres "$DATA"
      fi
      as_postgres "'$BIN/initdb' -D '$DATA/data' -A trust -U postgres >/dev/null"
    fi
    if ! as_postgres "'$BIN/pg_ctl' -D '$DATA/data' status >/dev/null 2>&1"; then
      as_postgres "'$BIN/pg_ctl' -D '$DATA/data' -o \"-p $PORT -k '$DATA' -c listen_addresses=127.0.0.1\" -l '$DATA/log' -w start >/dev/null"
    fi
    echo "export DROVER_TEST_POSTGRES='Host=127.0.0.1;Port=$PORT;Username=postgres'"
    ;;
  stop)
    if [ -d "$DATA/data" ]; then
      as_postgres "'$BIN/pg_ctl' -D '$DATA/data' -m fast stop >/dev/null 2>&1" || true
      rm -rf "$DATA"
    fi
    echo "stopped"
    ;;
  *)
    echo "usage: $0 start|stop" >&2
    exit 2
    ;;
esac
