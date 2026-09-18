# A CUPS daemon with a few queues, so the spooler and the cups:// channel run against the
# real thing instead of a hand-written answer. CUPS is an IPP server, which is why one
# container serves both channels.
FROM debian:trixie-slim@sha256:d7e12182ce18b85b93007c1dedf31f2d29e01ccf3182cc4017c709b6259bc132

RUN apt-get update \
    && apt-get install --no-install-recommends --yes cups cups-client cups-ipp-utils \
    && rm -rf /var/lib/apt/lists/*

# The daemon listens on every interface and asks for nothing: this holds print jobs from a
# test suite, and an authentication prompt would only be something to work around.
RUN printf '%s\n' \
    'LogLevel warn' \
    'MaxLogSize 0' \
    'Listen 0.0.0.0:631' \
    'ServerAlias *' \
    'Browsing Off' \
    'DefaultEncryption Never' \
    'WebInterface No' \
    'IdleExitTimeout 0' \
    'PreserveJobHistory Yes' \
    'PreserveJobFiles Yes' \
    '<Location />' \
    '  Order allow,deny' \
    '  Allow all' \
    '</Location>' \
    '<Location /admin>' \
    '  Order allow,deny' \
    '  Allow all' \
    '</Location>' \
    '<Policy default>' \
    '  JobPrivateAccess default' \
    '  JobPrivateValues default' \
    '  SubscriptionPrivateAccess default' \
    '  SubscriptionPrivateValues default' \
    '  <Limit All>' \
    '    Order deny,allow' \
    '  </Limit>' \
    '</Policy>' \
    > /etc/cups/cupsd.conf

# The stock policy asks for authentication on Cancel-Job and Set-Job-Attributes unless the
# caller owns the job. Nothing here has a user account, so the policy above allows every
# operation instead.
# Testcontainers publishes the daemon on a random host port, so every request arrives with a
# Host: field cupsd has never heard of and rejects as a DNS-rebinding attempt. ServerAlias *
# is the setting CUPS documents for exactly that.
# The file: backend is refused by default. The queues below write there, which is how a
# test reads back the exact bytes the spooler passed on.
RUN sed -i 's/^FileDevice No/FileDevice Yes/' /etc/cups/cups-files.conf \
    && grep -q '^FileDevice' /etc/cups/cups-files.conf || echo 'FileDevice Yes' >> /etc/cups/cups-files.conf

COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

EXPOSE 631

ENTRYPOINT ["/entrypoint.sh"]
