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
  if command -v brew >/dev/null 2>&1; then
    # postgresql@N formulae are keg-only, so their tools are not on PATH. The
    # helper supports PostgreSQL 16 or later, so accept any installed version.
    local formula
    for formula in postgresql@16 postgresql@17 postgresql@18; do
      candidate="$(brew --prefix "$formula" 2>/dev/null || true)"
      if [ -x "$candidate/bin/pg_ctl" ]; then
        echo "$candidate/bin"
        return
      fi
    done
  fi
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
      as_postgres "env LC_ALL=C LANG=C '$BIN/initdb' -D '$DATA/data' -A trust -U postgres -E UTF8 >/dev/null"
    fi
    # The superuser is trusted so the fixture needs no password, but Drover's own roles must give theirs, as on
    # a real server: a test that a wrong password is refused means nothing where every password is accepted.
    RULE="host all drover_app,drover_owner 127.0.0.1/32 scram-sha-256"
    if ! grep -qxF "$RULE" "$DATA/data/pg_hba.conf"; then
      as_postgres "{ printf '%s\n' '$RULE'; cat '$DATA/data/pg_hba.conf'; } > '$DATA/data/pg_hba.conf.new' && mv '$DATA/data/pg_hba.conf.new' '$DATA/data/pg_hba.conf'"
      as_postgres "env LC_ALL=C LANG=C '$BIN/pg_ctl' -D '$DATA/data' reload >/dev/null 2>&1" || true
    fi
    if ! as_postgres "env LC_ALL=C LANG=C '$BIN/pg_ctl' -D '$DATA/data' status >/dev/null 2>&1"; then
      as_postgres "env LC_ALL=C LANG=C '$BIN/pg_ctl' -D '$DATA/data' -o \"-p $PORT -k '$DATA' -c listen_addresses=127.0.0.1\" -l '$DATA/log' -w start >/dev/null"
    fi
    echo "export DROVER_TEST_POSTGRES='Host=127.0.0.1;Port=$PORT;Username=postgres'"
    ;;
  stop)
    if [ -d "$DATA/data" ]; then
      as_postgres "env LC_ALL=C LANG=C '$BIN/pg_ctl' -D '$DATA/data' -m fast stop >/dev/null 2>&1" || true
      rm -rf "$DATA"
    fi
    echo "stopped"
    ;;
  *)
    echo "usage: $0 start|stop" >&2
    exit 2
    ;;
esac
