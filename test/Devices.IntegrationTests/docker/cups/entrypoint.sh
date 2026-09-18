#!/bin/sh
# lpadmin talks to a running daemon, so the queues cannot be made at image build time. The
# daemon starts in the background, the queues are added, and then it is waited on.
set -e

mkdir -p /var/spool/out
cupsd

tries=0
until lpstat -r >/dev/null 2>&1; do
    tries=$((tries + 1))
    if [ "$tries" -gt 50 ]; then
        echo "cupsd did not come up" >&2
        exit 1
    fi
    sleep 0.2
done

# Passthrough: what the spooler receives is written byte for byte, so a test compares the
# file with the ZPL it sent.
lpadmin -p raw-queue -E -v file:/var/spool/out/raw.prn -m raw \
    -D 'Raw passthrough queue' -L 'Integration tests'

# Stopped on purpose: a job stays queued instead of racing to completion, which is what
# makes the job state, the watch and the cancel assertions deterministic.
lpadmin -p held-queue -E -v file:/var/spool/out/held.prn -m raw \
    -D 'Queue that never prints' -L 'Integration tests'
cupsdisable held-queue

# A second live queue, so the enumeration has more than one thing to find and the
# de-duplication has something to do.
lpadmin -p second-queue -E -v file:/var/spool/out/second.prn -m raw \
    -D 'Second passthrough queue' -L 'Integration tests'

echo "cups-ready"

# cupsd forked; keep the container alive on its process.
while kill -0 "$(cat /run/cups/cupsd.pid 2>/dev/null || echo 0)" 2>/dev/null; do
    sleep 1
done
