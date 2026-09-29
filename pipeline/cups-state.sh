#!/bin/sh
# Report the state of the local CUPS spooler without waking it up.
# Only the last check (-w) talks to the daemon, which starts it if it is idle.

sock=""
for p in /private/var/run/cupsd /run/cups/cups.sock /var/run/cups/cups.sock; do
    [ -S "$p" ] && { sock=$p; break; }
done

echo "socket:   ${sock:-none found}"

if [ "$(uname)" = Darwin ]; then
    state=$(launchctl print system/org.cups.cupsd 2>/dev/null | awk -F' = ' '/^\tstate =/ {print $2; exit}')
    echo "launchd:  ${state:-unknown}"
elif command -v systemctl >/dev/null 2>&1; then
    echo "systemd:  cups.service $(systemctl is-active cups.service), cups.socket $(systemctl is-active cups.socket)"
fi

if pgrep -x cupsd >/dev/null; then
    echo "process:  cupsd running (pid $(pgrep -x cupsd | tr '\n' ' '))"
else
    echo "process:  cupsd not running"
fi

# A connect probe, not lsof: lsof cannot see root's sockets without sudo. Only the
# daemon itself listens here, so the probe never starts it.
if nc -z -w 1 localhost 631 2>/dev/null; then
    echo "tcp 631:  listening"
else
    echo "tcp 631:  not listening"
fi

if [ "$1" = "-w" ]; then
    echo "--- waking the daemon through the socket ---"
    lpstat -r
    lpstat -p 2>/dev/null
fi
