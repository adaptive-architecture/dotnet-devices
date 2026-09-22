# Discovery

Three sources find printers, and each answers a different question.

| Source | Interface | What it needs |
| :--- | :--- | :--- |
| Multicast DNS | `IMdnsPrinterDiscovery` | Nothing. It asks the local link and opens no connection. |
| TCP probe | `INetworkPrinterDiscovery` | A host list. Opt-in, for printers that do not advertise themselves. |
| Operating system spooler | `IPrinterDiscovery` | Nothing. It enumerates Win32 print queues or CUPS destinations, plus any CUPS server the application named. |

Prefer mDNS. Most applications should let [the printer manager](printer-manager.md) run all
three rather than call any of them directly.

## Multicast DNS

`MdnsPrinterDiscovery` sends one multicast DNS query to the local link and collects the
answers for `BrowseTimeout`. It replaces a subnet sweep: no host list, no connection to any
address. It opens one socket for each interface and address family, so a query goes out one
time on each link.

| Service type | Usual port | Reported as |
| :--- | :--- | :--- |
| `_pdl-datastream._tcp` | 9100 | `raw://…` |
| `_ipp._tcp` | 631 | `ipp://…` |
| `_ipps._tcp` | 443 or 631 | `ipps://…` |
| `_printer._tcp` | 515 | **nothing** |

The TXT attributes fill `PrinterInfo`: `ty` → `Name`, `note` → `Location`, `pdl` →
`DriverName`, `UUID` → `Uuid`, `usb_MFG` and `usb_MDL` → `Manufacturer` and `Model`. A key
that appears twice keeps its first value (RFC 6763 §6.4). An address record that points to a
loopback, unspecified or multicast address is ignored, and the endpoint then keeps the target
name of the service.

Four rules are worth knowing:

- **The port is the one the `SRV` record gave, never a default for the service type.** A
  printer that advertises `_ipps._tcp` on 443 is reached on 443.
- **`_ipp` and `_ipps` are two channels and not one.** They are separate services on separate
  ports, and a printer commonly advertises both. Collapsing them would hide the plain channel
  behind a secure one that may not negotiate — an older printer whose certificate has expired
  is the usual case — and a caller could then not reach a printer that answers perfectly well
  on 631.
- **A printer that advertises several protocols gives one channel for each.** They share a
  device key, so the manager puts them on one `PrinterDevice`. A caller needs both: only the
  raw channel sends a payload unchanged, and only the IPP channel has a job queue.
- **LPD is not reported.** No transport in this library writes the LPD message format, so an
  endpoint on port 515 is not a channel a job can take. A printer that advertises only LPD is
  out of reach and is not reported at all.

**The UUID is shared across the services of one instance.** A printer answers the `UUID` query
on its IPP service and usually not on its raw one, so the value found on any service of an
instance is applied to all of them. Without that the raw channel would never join the device it
belongs to. When a UUID is known, every channel takes the identity form and the address stays as
an alias, so an identifier a caller already holds keeps resolving.

`MdnsPrinterDiscoveryOptions` controls the service types, the timeout, the retries, the
interfaces, IPv6 and the record limit.

> [!NOTE]
> The browse socket binds an ephemeral port, not 5353, so it never competes with a responder
> such as avahi or Bonjour: RFC 6762 §5.1 and §6.7 require the printer to answer such a
> "one-shot" querier by unicast. A strict local firewall can discard that unicast answer, and
> the browse then returns nothing even though `avahi-browse` works.

## TCP probe

`TcpNetworkPrinterDiscovery` opens a connection to each host you name and reports the ones that
accept it on the raw print port. A host listed more than once is probed once.

```csharp
INetworkPrinterDiscovery discovery = new TcpNetworkPrinterDiscovery();
NetworkPrinterDiscoveryOptions options = new()
{
    Hosts = ["192.168.1.50", "192.168.1.51"],
    ConnectTimeout = TimeSpan.FromSeconds(1),
};
IReadOnlyList<DiscoveredPrinter> printers =
    await discovery.DiscoverAsync(options, cancellationToken).ConfigureAwait(false);
```

To sweep the subnet the machine is already on, `NetworkPrinterDiscoveryOptions.LocalSubnetHosts`
builds the list. It reads the network adapters only, and opens no connection:

```csharp
NetworkPrinterDiscoveryOptions options = new()
{
    Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts(),
};
```

An adapter counts only when it is up, has an IPv4 gateway, and has a prefix length from 16 to
30. A loopback address and a link-local address are skipped, and the list stops at `maxHosts`,
which is 4096 by default. A prefix shorter than 16 holds too many addresses to probe, and IPv6
is not listed because a subnet there is too large to walk.

**The probe stays opt-in.** The method gives you the list, and you decide to use it. A socket
that accepted a connection also says nothing about identity, so a probed channel is never
grouped with another on the strength of the probe alone.

## The operating system spooler

`SpoolerPrinterDiscovery` enumerates the printers installed in the operating system spooler,
and the queues of every CUPS server the application named in `PrinterManagerOptions.CupsServers`.
Both are one source: an application that turns `IncludeSpooler` off asks about no queue at all,
wherever the queue lives. [Spooler and CUPS](spooler-and-cups.md) covers both.

## Running them together

`IPrinterManager` runs every configured source at once, merges the channels into devices, and
never lets one source failure hide another's answers. That is almost always what you want; see
[Printer manager](printer-manager.md).

```csharp
IReadOnlyList<PrinterDevice> devices = await manager.DiscoverAsync(
    new PrinterManagerOptions
    {
        Probe = new() { Hosts = NetworkPrinterDiscoveryOptions.LocalSubnetHosts() },
        ReadIdentity = true,
    },
    cancellationToken).ConfigureAwait(false);
```
