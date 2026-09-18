#!/bin/sh
set -e

mkdir -p /run/dbus /spool
rm -f /run/dbus/pid /run/avahi-daemon/pid
dbus-daemon --system --fork
avahi-daemon --daemonize --no-chroot

# -d spool directory, -k keep the job files, -f the supported formats, -v verbose so a
# failed start says why in the container log.
#
# -c matters more than it looks. Without a print command ippeveprinter simulates printing,
# which takes seconds per job and answers server-error-busy to everything that arrives
# meanwhile; tests sharing one printer would then race the sleep. /bin/true finishes at
# once, and -k still keeps the document exactly as it arrived, under <job>-<name>.<ext>.
# The empty .prn beside it is the command's own output.
exec ippeveprinter -v -p 8631 -d /spool -k -c /bin/true -f "$IPPEVE_FORMATS" "$IPPEVE_NAME"
