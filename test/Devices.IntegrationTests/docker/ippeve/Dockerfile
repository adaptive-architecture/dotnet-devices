# An IPP Everywhere printer, from the CUPS project's own test server. It is the closest
# thing to a real IPP device that fits in a container: ippeveprinter answers
# Get-Printer-Attributes, Print-Job, Get-Jobs, Get-Job-Attributes and Cancel-Job the way a
# printer does, and -k keeps every job it accepts so a test can read what really arrived.
FROM debian:trixie-slim@sha256:d7e12182ce18b85b93007c1dedf31f2d29e01ccf3182cc4017c709b6259bc132

# avahi and dbus are not optional: ippeveprinter registers itself over DNS-SD and exits with
# "Unable to initialize DNS-SD" when nothing answers. The tests connect by address and never
# browse, so the responder only has to exist.
RUN apt-get update \
    && apt-get install --no-install-recommends --yes cups-ipp-utils avahi-daemon dbus \
    && rm -rf /var/lib/apt/lists/*

# The formats the printer advertises in document-format-supported. A test overrides this to
# make the same printer read a PDF or refuse one, which is the branch that decides whether
# the library converts the job to PWG Raster.
ENV IPPEVE_FORMATS="application/pdf,image/pwg-raster,image/jpeg"
ENV IPPEVE_NAME="Integration Test Printer"

COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh && mkdir -p /spool

EXPOSE 8631

ENTRYPOINT ["/entrypoint.sh"]
