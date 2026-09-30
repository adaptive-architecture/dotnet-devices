#!/bin/sh
# Create, or remove, the two throwaway queues test/Devices.CupsHostTests runs against on the
# CUPS daemon of this machine. The macos CI job runs it; so can a developer on macOS or Linux.
#
#   sh ./pipeline/cups-host-queues.sh up     # prints the variables that turn the tests on
#   sh ./pipeline/cups-host-queues.sh down
#
# adaptarch-host-held       a stopped queue to socket://127.0.0.1:9, so every job stays in the
#                           queue where a test reads it back, and nothing is ever delivered
# adaptarch-host-forward    an IPP Everywhere queue to an ippeveprinter on localhost:8631, so
#                           a printer-language job is forwarded over IPP and refused
#
# ippeveprinter never ends its own copy of a job it refused, and answers every later job with
# server-error-busy, so "up" restarts it: run "down" and "up" again before a second run.
#
# Nothing prints on paper. "up" fails, rather than letting the tests skip, when a queue did not
# take or the held queue did not stop.

set -eu

held=adaptarch-host-held
forward=adaptarch-host-forward
port=8631
state="${TMPDIR:-/tmp}/adaptarch-cups-host"

fail() { echo "error: $*" >&2; exit 2; }

# lpadmin and cupsdisable need a member of the CUPS administrators: _lpadmin or admin on
# macOS, lpadmin on most Linux distributions. Without that, try sudo that asks nothing.
admin() {
    "$@" 2>/dev/null || sudo -n "$@"
}

down() {
    cancel -a "$held" 2>/dev/null || true
    cancel -a "$forward" 2>/dev/null || true
    admin lpadmin -x "$held" 2>/dev/null || true
    admin lpadmin -x "$forward" 2>/dev/null || true
    if [ -f "$state/ippeveprinter.pid" ]; then
        kill "$(cat "$state/ippeveprinter.pid")" 2>/dev/null || true
    fi
    rm -rf "$state"
}

up() {
    for tool in lpadmin cupsdisable lpstat ippeveprinter; do
        command -v "$tool" >/dev/null 2>&1 || fail "$tool not found (Linux: cups-client and cups-ipp-utils)"
    done
    down

    # lpstat wakes a daemon that is started on demand, as launchd does on macOS.
    lpstat -r >/dev/null || fail "the CUPS daemon did not answer"

    mkdir -p "$state/spool"
    ippeveprinter -n localhost -p "$port" -k -d "$state/spool" \
        -f application/pdf,image/urf,image/pwg-raster "AdaptArch Host Forward" > "$state/ippeveprinter.log" 2>&1 &
    echo $! > "$state/ippeveprinter.pid"

    tries=0
    until nc -z localhost "$port" 2>/dev/null; do
        tries=$((tries + 1))
        [ "$tries" -le 20 ] || fail "ippeveprinter did not listen on port $port; see $state/ippeveprinter.log"
        sleep 0.5
    done

    admin lpadmin -p "$held" -E -v socket://127.0.0.1:9 || fail "could not create $held"
    admin cupsdisable "$held" || fail "could not stop $held"
    # IPP Everywhere reads the printer's attributes to write the PPD, which is why ippeveprinter
    # must answer first.
    admin lpadmin -p "$forward" -E -v "ipp://localhost:$port/ipp/print" -m everywhere 2>/dev/null \
        || fail "could not create $forward"

    lpstat -p "$held" | grep -q disabled || fail "$held did not stop: $(lpstat -p "$held")"
    lpstat -v "$forward" >/dev/null || fail "$forward was not created"

    echo "DEVICES_CUPS_HELD_QUEUE=$held"
    echo "DEVICES_CUPS_FORWARDING_QUEUE=$forward"
}

case "${1:-}" in
    up) up ;;
    down) down ;;
    *) fail "usage: sh $0 up|down" ;;
esac
